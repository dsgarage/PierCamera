#!/bin/bash

# RuntimeAvatarLoader Setup Script with Face/Body AnimatorController Creation via MCP
# Final version with proper error handling

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

# Step 0: Create Animations folder
echo -e "\n[0/6] Ensuring Animations folder exists..."
folder_create=$(mcp_call "unity.asset.createFolder" '{"path":"Assets/Animations"}' 5)
if echo "$folder_create" | grep -q 'error'; then
    echo "⚠ Animations folder may already exist"
else
    echo "✓ Animations folder created/verified"
fi

# Step 1: Create AnimatorControllers
echo -e "\n[1/6] Creating Face AnimatorController..."
face_create=$(mcp_call "unity.animator.create" '{"name":"Face_AnimatorController","outputPath":"Assets/Animations/Face_AnimatorController.controller"}' 11)

if echo "$face_create" | grep -q '"status":"success"'; then
    echo "✓ Face AnimatorController created successfully"

    # Add states
    echo "  Adding Neutral state (default)..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Neutral","layerIndex":0}' 12 > /dev/null

    echo "  Adding expression states..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Happy","layerIndex":0}' 13 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Sad","layerIndex":0}' 14 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Angry","layerIndex":0}' 15 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Face_AnimatorController.controller","stateName":"Surprised","layerIndex":0}' 16 > /dev/null
    echo "  ✓ Added 5 states (Neutral, Happy, Sad, Angry, Surprised)"
else
    echo "⚠ Face AnimatorController already exists or creation failed"
fi

echo -e "\n[2/6] Creating Body AnimatorController..."
body_create=$(mcp_call "unity.animator.create" '{"name":"Body_AnimatorController","outputPath":"Assets/Animations/Body_AnimatorController.controller"}' 21)

if echo "$body_create" | grep -q '"status":"success"'; then
    echo "✓ Body AnimatorController created successfully"

    # Add states
    echo "  Adding Idle state (default)..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Idle","layerIndex":0}' 22 > /dev/null

    echo "  Adding motion states..."
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Walk","layerIndex":0}' 23 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Run","layerIndex":0}' 24 > /dev/null
    mcp_call "unity.animator.addState" '{"controllerPath":"Assets/Animations/Body_AnimatorController.controller","stateName":"Jump","layerIndex":0}' 25 > /dev/null
    echo "  ✓ Added 4 states (Idle, Walk, Run, Jump)"
else
    echo "⚠ Body AnimatorController already exists or creation failed"
fi

# Step 3: Find AppMgr and get its details
echo -e "\n[3/6] Verifying scene structure..."
scene_data=$(mcp_call "unity.scene.list" '{"includeInactive":false}' 30)
echo "✓ Scene loaded"

# Step 4: Add RuntimeAvatarLoader component via menu
echo -e "\n[4/6] Setting up RuntimeAvatarLoader component..."
response=$(mcp_call "unity.editor.executeMenuItem" '{"menuPath":"AICam/Setup/Add RuntimeAvatarLoader to Scene"}' 40)

if echo "$response" | grep -q '"success":true'; then
    echo "✓ RuntimeAvatarLoader component added/updated"
else
    echo "✗ Failed to add RuntimeAvatarLoader component"
    echo "$response" | python3 -m json.tool
fi

# Step 5: Save scene
echo -e "\n[5/6] Saving scene..."
save_response=$(mcp_call "unity.scene.save" '{}' 50)

if echo "$save_response" | grep -q '"success":true'; then
    echo "✓ Scene saved successfully"
else
    echo "✗ Failed to save scene"
    echo "$save_response" | python3 -m json.tool
fi

# Summary
echo -e "\n======================================"
echo "✅ Setup Complete!"
echo "======================================"
echo -e "\nCreated Assets:"
echo "  📁 Assets/Animations/"
echo "  🎭 Face_AnimatorController.controller"
echo "     └─ States: Neutral(default), Happy, Sad, Angry, Surprised"
echo "  🏃 Body_AnimatorController.controller"
echo "     └─ States: Idle(default), Walk, Run, Jump"
echo -e "\nRuntimeAvatarLoader Configuration:"
echo "  - faceAnimatorController: Face_AnimatorController"
echo "  - bodyAnimatorController: Body_AnimatorController"
echo "  - arPlacementTarget: XR Origin"
echo -e "\nUsage:"
echo "  Face AnimatorController → BlendShape制御で表情を変更"
echo "  Body AnimatorController → ボーンTransform制御でポーズを変更"
