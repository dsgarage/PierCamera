#!/bin/bash

# RuntimeAvatarLoader Setup Script with Face/Body AnimatorController Creation via MCP
# Version 2: Creates controllers directly and handles errors gracefully

set -e

API_URL="http://localhost:5051/api/mcp"

echo "=== RuntimeAvatarLoader Setup Script (Face/Body AnimatorControllers) v2 ==="

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

# Step 1: Create AnimatorControllers (will skip if already exist)
echo -e "\n[1/5] Creating Face/Body AnimatorControllers..."

# Create Face AnimatorController
echo "Creating Face AnimatorController..."
face_create=$(mcp_call "unity.animator.create" '{"name":"Face_AnimatorController","outputPath":"Assets/Animations/Face_AnimatorController.controller"}' 11)

if echo "$face_create" | grep -q '"status":"success"'; then
    echo "✓ Face AnimatorController created"

    # Add Neutral state (default for face)
    echo "  Adding Neutral state..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Neutral","layerIndex":0}' 12 > /dev/null

    # Add common expression states
    echo "  Adding expression states (Happy, Sad, Angry, Surprised)..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Happy","layerIndex":0}' 13 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Sad","layerIndex":0}' 14 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Angry","layerIndex":0}' 15 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Surprised","layerIndex":0}' 16 > /dev/null
elif echo "$face_create" | grep -q 'error'; then
    echo "⚠ Face AnimatorController already exists or error occurred"
    echo "$face_create" | python3 -m json.tool
fi

# Create Body AnimatorController
echo -e "\nCreating Body AnimatorController..."
body_create=$(mcp_call "unity.animator.create" '{"name":"Body_AnimatorController","outputPath":"Assets/Animations/Body_AnimatorController.controller"}' 21)

if echo "$body_create" | grep -q '"status":"success"'; then
    echo "✓ Body AnimatorController created"

    # Add Idle state (default for body)
    echo "  Adding Idle state..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Idle","layerIndex":0}' 22 > /dev/null

    # Add common motion states
    echo "  Adding motion states (Walk, Run, Jump)..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Walk","layerIndex":0}' 23 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Run","layerIndex":0}' 24 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Jump","layerIndex":0}' 25 > /dev/null
elif echo "$body_create" | grep -q 'error'; then
    echo "⚠ Body AnimatorController already exists or error occurred"
    echo "$body_create" | python3 -m json.tool
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

echo -e "\n✅ Setup Complete!"
echo -e "\nAnimatorControllers:"
echo "  - Assets/Animations/Face_AnimatorController.controller"
echo "    表情用: Neutral(default), Happy, Sad, Angry, Surprised"
echo "  - Assets/Animations/Body_AnimatorController.controller"
echo "    ポーズ用: Idle(default), Walk, Run, Jump"
echo -e "\nNote:"
echo "  - Face AnimatorController: BlendShape制御で表情を変更"
echo "  - Body AnimatorController: ボーンTransform制御で体のポーズを変更"
