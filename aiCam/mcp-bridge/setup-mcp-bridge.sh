#!/bin/bash

# Unity MCP Bridge Setup Script
# This script sets up the MCP bridge in your Unity project directory
# The bridge will be placed at the same level as the Assets/ folder

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== Unity MCP Bridge Setup ===${NC}"
echo ""

# Get the Unity project root directory
UNITY_PROJECT_ROOT=""

# Check if we're in a Unity project
if [ -d "Assets" ] && [ -d "ProjectSettings" ]; then
    UNITY_PROJECT_ROOT="$(pwd)"
    echo -e "${GREEN}✓ Detected Unity project: ${UNITY_PROJECT_ROOT}${NC}"
elif [ -d "../Assets" ] && [ -d "../ProjectSettings" ]; then
    UNITY_PROJECT_ROOT="$(cd .. && pwd)"
    echo -e "${GREEN}✓ Detected Unity project: ${UNITY_PROJECT_ROOT}${NC}"
else
    echo -e "${YELLOW}Unity project not auto-detected.${NC}"
    echo -e "Please enter the full path to your Unity project root (where Assets/ folder is):"
    read -r UNITY_PROJECT_ROOT

    if [ ! -d "$UNITY_PROJECT_ROOT/Assets" ]; then
        echo -e "${RED}Error: Assets/ folder not found in ${UNITY_PROJECT_ROOT}${NC}"
        exit 1
    fi
fi

# Target directory for mcp-bridge
MCP_BRIDGE_DIR="${UNITY_PROJECT_ROOT}/mcp-bridge"

echo ""
echo -e "MCP Bridge will be installed at:"
echo -e "${GREEN}${MCP_BRIDGE_DIR}${NC}"
echo ""

# Check if mcp-bridge already exists
if [ -d "$MCP_BRIDGE_DIR" ]; then
    echo -e "${YELLOW}Warning: mcp-bridge directory already exists.${NC}"
    echo -e "Do you want to overwrite it? (y/N): "
    read -r response
    if [[ ! "$response" =~ ^[Yy]$ ]]; then
        echo -e "${RED}Setup cancelled.${NC}"
        exit 0
    fi
    rm -rf "$MCP_BRIDGE_DIR"
fi

# Create mcp-bridge directory
mkdir -p "$MCP_BRIDGE_DIR"

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Copy template files
echo -e "${GREEN}Copying template files...${NC}"
cp "$SCRIPT_DIR/index.js" "$MCP_BRIDGE_DIR/"
cp "$SCRIPT_DIR/package.json" "$MCP_BRIDGE_DIR/"

# Install npm dependencies
echo ""
echo -e "${GREEN}Installing npm dependencies...${NC}"
cd "$MCP_BRIDGE_DIR"

if ! command -v npm &> /dev/null; then
    echo -e "${RED}Error: npm is not installed.${NC}"
    echo -e "Please install Node.js and npm from: https://nodejs.org/"
    exit 1
fi

npm install

echo ""
echo -e "${GREEN}=== Setup Complete! ===${NC}"
echo ""
echo -e "MCP Bridge installed at:"
echo -e "  ${GREEN}${MCP_BRIDGE_DIR}${NC}"
echo ""
echo -e "Next steps:"
echo -e "1. Add the following to your Claude Code MCP configuration:"
echo -e "   ${YELLOW}~/.config/claude/claude_desktop_config.json${NC}"
echo ""
echo -e '   {'
echo -e '     "mcpServers": {'
echo -e '       "unity": {'
echo -e '         "command": "node",'
echo -e "         \"args\": [\"${MCP_BRIDGE_DIR}/index.js\"]"
echo -e '       }'
echo -e '     }'
echo -e '   }'
echo ""
echo -e "2. Restart Claude Code"
echo -e "3. Open your Unity project with the Unity MCP Server package installed"
echo -e "4. Start using Unity APIs through Claude Code!"
echo ""
