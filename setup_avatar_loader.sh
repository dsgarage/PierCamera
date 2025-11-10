#!/bin/bash

# RuntimeAvatarLoader Setup Script using MCP HTTP API

set -e

API_URL="http://localhost:5051/api/mcp"

echo "=== RuntimeAvatarLoader Setup Script ==="

# Function to call MCP API
mcp_call() {
    local method=$1
    local params=$2
    local id=$3

    local request="{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"$method\",\"params\":$params}"
    curl -s -X POST "$API_URL" \
        -H "Content-Type: application/json" \
        -d "$request"
}

# Step 1: Execute SetupRuntimeAvatarLoader menu item
echo -e "\n[1/2] Adding RuntimeAvatarLoader component via menu..."
response=$(mcp_call "unity.editor.executeMenuItem" '{"menuPath":"AICam/Setup/Add RuntimeAvatarLoader to Scene"}' 1)
echo "$response" | python3 -m json.tool

# Step 2: Save scene
echo -e "\n[2/2] Saving scene..."
response=$(mcp_call "unity.scene.save" '{}' 2)
echo "$response" | python3 -m json.tool

echo -e "\n✅ Setup Complete!"
