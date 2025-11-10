#!/bin/bash

# RuntimeAvatarLoader Setup Script with Face/Body AnimatorController Creation via MCP

set -e

API_URL="http://localhost:5051/api/mcp"

echo "=== RuntimeAvatarLoader Setup Script (Face/Body AnimatorControllers) ==="

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

# Check for Face AnimatorController
echo "Checking for Face AnimatorController..."
face_check=$(mcp_call "unity.asset.list" '{"assetType":"RuntimeAnimatorController","searchFilter":"Face_AnimatorController"}' 10)
face_exists=$(echo "$face_check" | python3 -c "import json,sys; data=json.load(sys.stdin); print('true' if data.get('result',{}).get('assets') else 'false')")

if [ "$face_exists" = "false" ]; then
    echo "Creating Face AnimatorController..."
    face_create=$(mcp_call "unity.animator.create" '{"name":"Face_AnimatorController","outputPath":"Assets/Animations/Face_AnimatorController.controller"}' 11)
    echo "$face_create" | python3 -m json.tool

    # Add Neutral state (default for face)
    echo "Adding Neutral state to Face AnimatorController..."
    face_state=$(mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Neutral","layerIndex":0}' 12)
    echo "$face_state" | python3 -m json.tool

    # Add common expression states
    echo "Adding expression states..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Happy","layerIndex":0}' 13 | python3 -m json.tool
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Sad","layerIndex":0}' 14 | python3 -m json.tool
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Angry","layerIndex":0}' 15 | python3 -m json.tool
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Surprised","layerIndex":0}' 16 | python3 -m json.tool
else
    echo "✓ Face AnimatorController already exists"
fi

# Check for Body AnimatorController
echo -e "\nChecking for Body AnimatorController..."
body_check=$(mcp_call "unity.asset.list" '{"assetType":"RuntimeAnimatorController","searchFilter":"Body_AnimatorController"}' 20)
body_exists=$(echo "$body_check" | python3 -c "import json,sys; data=json.load(sys.stdin); print('true' if data.get('result',{}).get('assets') else 'false')")

if [ "$body_exists" = "false" ]; then
    echo "Creating Body AnimatorController..."
    body_create=$(mcp_call "unity.animator.create" '{"name":"Body_AnimatorController","outputPath":"Assets/Animations/Body_AnimatorController.controller"}' 21)
    echo "$body_create" | python3 -m json.tool

    # Add Idle state (default for body)
    echo "Adding Idle state to Body AnimatorController..."
    body_state=$(mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Idle","layerIndex":0}' 22)
    echo "$body_state" | python3 -m json.tool

    # Add common motion states
    echo "Adding motion states..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Walk","layerIndex":0}' 23 | python3 -m json.tool
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Run","layerIndex":0}' 24 | python3 -m json.tool
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Jump","layerIndex":0}' 25 | python3 -m json.tool
else
    echo "✓ Body AnimatorController already exists"
fi

# Step 2: Find AppMgr and get its details
echo -e "\n[2/5] Finding AppMgr in scene..."
scene_data=$(mcp_call "unity.scene.list" '{"includeInactive":false}' 30)

# Step 3: Add RuntimeAvatarLoader component via menu
echo -e "\n[3/5] Adding RuntimeAvatarLoader component via menu..."
response=$(mcp_call "unity.editor.executeMenuItem" '{"menuPath":"AICam/Setup/Add RuntimeAvatarLoader to Scene"}' 40)
echo "$response" | python3 -m json.tool

# Step 4: Set references via MCP (if needed - the menu already does this)
echo -e "\n[4/5] References are set by the menu command..."

# Step 5: Save scene
echo -e "\n[5/5] Saving scene..."
response=$(mcp_call "unity.scene.save" '{}' 50)
echo "$response" | python3 -m json.tool

echo -e "\n✅ Setup Complete with Face/Body AnimatorController Creation!"
echo -e "\nCreated AnimatorControllers:"
echo "  - Assets/Animations/Face_AnimatorController.controller (表情用: Neutral, Happy, Sad, Angry, Surprised)"
echo "  - Assets/Animations/Body_AnimatorController.controller (ポーズ用: Idle, Walk, Run, Jump)"
echo -e "\nNote: Face AnimatorController controls BlendShapes for facial expressions."
echo "      Body AnimatorController controls bone transforms for body motions."
