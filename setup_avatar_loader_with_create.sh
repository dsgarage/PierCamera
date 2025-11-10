#!/bin/bash

# RuntimeAvatarLoader Setup Script with AnimatorController Creation via MCP

set -e

API_URL="http://localhost:5051/api/mcp"

echo "=== RuntimeAvatarLoader Setup Script with AnimatorController Creation ==="

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

# Step 1: Check if AnimatorControllers exist, if not create them
echo -e "\n[1/5] Checking and creating AnimatorControllers if needed..."

# Check for VRM AnimatorController
echo "Checking for VRM AnimatorController..."
vrm_check=$(mcp_call "unity.asset.list" '{"assetType":"RuntimeAnimatorController","searchFilter":"VRM_AnimatorController"}' 10)
vrm_exists=$(echo "$vrm_check" | python3 -c "import json,sys; data=json.load(sys.stdin); print('true' if data.get('result',{}).get('assets') else 'false')")

if [ "$vrm_exists" = "false" ]; then
    echo "Creating VRM AnimatorController..."
    vrm_create=$(mcp_call "unity.animator.create" '{"name":"VRM_AnimatorController","outputPath":"Assets/Animations/VRM_AnimatorController.controller"}' 11)
    echo "$vrm_create" | python3 -m json.tool

    # Add Idle state
    echo "Adding Idle state to VRM AnimatorController..."
    vrm_state=$(mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/VRM_AnimatorController.controller","stateName":"Idle","layerIndex":0}' 12)
    echo "$vrm_state" | python3 -m json.tool
else
    echo "✓ VRM AnimatorController already exists"
fi

# Check for FBX AnimatorController
echo -e "\nChecking for FBX AnimatorController..."
fbx_check=$(mcp_call "unity.asset.list" '{"assetType":"RuntimeAnimatorController","searchFilter":"FBX_AnimatorController"}' 13)
fbx_exists=$(echo "$fbx_check" | python3 -c "import json,sys; data=json.load(sys.stdin); print('true' if data.get('result',{}).get('assets') else 'false')")

if [ "$fbx_exists" = "false" ]; then
    echo "Creating FBX AnimatorController..."
    fbx_create=$(mcp_call "unity.animator.create" '{"name":"FBX_AnimatorController","outputPath":"Assets/Animations/FBX_AnimatorController.controller"}' 14)
    echo "$fbx_create" | python3 -m json.tool

    # Add Idle state
    echo "Adding Idle state to FBX AnimatorController..."
    fbx_state=$(mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/FBX_AnimatorController.controller","stateName":"Idle","layerIndex":0}' 15)
    echo "$fbx_state" | python3 -m json.tool
else
    echo "✓ FBX AnimatorController already exists"
fi

# Step 2: Find AppMgr and get its details
echo -e "\n[2/5] Finding AppMgr in scene..."
scene_data=$(mcp_call "unity.scene.list" '{"includeInactive":false}' 20)

# Step 3: Add RuntimeAvatarLoader component via menu
echo -e "\n[3/5] Adding RuntimeAvatarLoader component via menu..."
response=$(mcp_call "unity.editor.executeMenuItem" '{"menuPath":"AICam/Setup/Add RuntimeAvatarLoader to Scene"}' 30)
echo "$response" | python3 -m json.tool

# Step 4: Set references via MCP (if needed - the menu already does this)
echo -e "\n[4/5] References are set by the menu command..."

# Step 5: Save scene
echo -e "\n[5/5] Saving scene..."
response=$(mcp_call "unity.scene.save" '{}' 40)
echo "$response" | python3 -m json.tool

echo -e "\n✅ Setup Complete with AnimatorController Creation!"
echo -e "\nCreated AnimatorControllers:"
echo "  - Assets/Animations/VRM_AnimatorController.controller"
echo "  - Assets/Animations/FBX_AnimatorController.controller"
