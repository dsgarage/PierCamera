#!/usr/bin/env python3
"""
FBXLoader Scene Setup via HTTP API
"""
import urllib.request
import urllib.error
import json
import time

MCP_URL = "http://localhost:5051/api/mcp"

def send_request(method, params=None):
    """Send JSON-RPC request via HTTP"""
    request = {
        "jsonrpc": "2.0",
        "method": method,
        "params": params or {},
        "id": int(time.time() * 1000)
    }

    print(f"\n→ {method}")

    try:
        data = json.dumps(request).encode('utf-8')
        req = urllib.request.Request(
            MCP_URL,
            data=data,
            headers={'Content-Type': 'application/json'}
        )

        with urllib.request.urlopen(req, timeout=30) as response:
            result = json.loads(response.read().decode('utf-8'))

        if "error" in result:
            print(f"  ✗ Error: {result['error'].get('message', 'Unknown error')}")
            return result
        elif "result" in result:
            print(f"  ✓ Success")
            return result
        else:
            print(f"  ? Unexpected response: {result}")
            return result
    except Exception as e:
        print(f"  ✗ Exception: {e}")
        return {"error": {"message": str(e)}}

def setup_fbxloader_scene():
    """Setup FBXLoader scene"""
    print("=== FBXLoader Scene Setup via HTTP API ===\n")

    # Step 1: Check current scene
    print("Step 1: Checking current scene...")
    response = send_request("unity.scene.list", {
        "includeInactive": False
    })

    if "error" in response:
        print("Failed to get scene info")
        return

    scene_name = response["result"].get("sceneName", "")
    print(f"  Current scene: {scene_name}")

    root_objects = response["result"].get("rootObjects", [])
    ui_document_exists = any(obj["name"] == "UI_Document" for obj in root_objects)
    runtime_manager_exists = any(obj["name"] == "RuntimeManager" for obj in root_objects)

    print(f"  UI_Document exists: {ui_document_exists}")
    print(f"  RuntimeManager exists: {runtime_manager_exists}")

    # Step 2: Create UI_Document if it doesn't exist
    if not ui_document_exists:
        print("\nStep 2: Creating UI_Document...")
        response = send_request("unity.create", {
            "name": "UI_Document"
        })

        if "error" in response:
            print("Failed to create UI_Document")
            return

        ui_doc_path = response["result"].get("path", "UI_Document")
        print(f"  Created: {ui_doc_path}")

        # Add UIDocument component
        print("  Adding UIDocument component...")
        send_request("unity.component.add", {
            "gameObjectPath": ui_doc_path,
            "componentType": "UnityEngine.UIElements.UIDocument"
        })

        # Add FileBrowserUIController component
        print("  Adding FileBrowserUIController component...")
        send_request("unity.component.add", {
            "gameObjectPath": ui_doc_path,
            "componentType": "AICam.FBXLoader.FileBrowserUIController"
        })

        # Set UXML visualTreeAsset
        print("  Setting UXML asset...")
        send_request("unity.component.setReference", {
            "gameObjectPath": ui_doc_path,
            "componentType": "UnityEngine.UIElements.UIDocument",
            "fieldName": "m_VisualTreeAsset",
            "referenceType": "asset",
            "referencePath": "Assets/UI/RuntimeFBXLoaderWithFileBrowser/RuntimeFBXLoaderWithFileBrowser.uxml"
        })

        # Set PanelSettings
        print("  Setting PanelSettings...")
        send_request("unity.component.setReference", {
            "gameObjectPath": ui_doc_path,
            "componentType": "UnityEngine.UIElements.UIDocument",
            "fieldName": "m_PanelSettings",
            "referenceType": "asset",
            "referencePath": "Assets/UI/PanelSettings.asset"
        })

        # Set uiDocument reference in FileBrowserUIController
        print("  Setting UIDocument reference...")
        send_request("unity.component.setReference", {
            "gameObjectPath": ui_doc_path,
            "componentType": "AICam.FBXLoader.FileBrowserUIController",
            "fieldName": "uiDocument",
            "referenceType": "component",
            "referenceGameObjectPath": ui_doc_path,
            "referenceComponentType": "UnityEngine.UIElements.UIDocument"
        })
    else:
        print("\nStep 2: UI_Document already exists, skipping")

    # Step 3: Create RuntimeManager if it doesn't exist
    if not runtime_manager_exists:
        print("\nStep 3: Creating RuntimeManager...")
        response = send_request("unity.create", {
            "name": "RuntimeManager"
        })

        if "error" in response:
            print("Failed to create RuntimeManager")
            return

        runtime_mgr_path = response["result"].get("path", "RuntimeManager")
        print(f"  Created: {runtime_mgr_path}")

        # Add FileBrowserController component
        print("  Adding FileBrowserController component...")
        send_request("unity.component.add", {
            "gameObjectPath": runtime_mgr_path,
            "componentType": "AICam.FBXLoader.FileBrowserController"
        })

        # Add RuntimeFBXLoaderBridge component
        print("  Adding RuntimeFBXLoaderBridge component...")
        send_request("unity.component.add", {
            "gameObjectPath": runtime_mgr_path,
            "componentType": "AICam.FBXLoader.RuntimeFBXLoaderBridge"
        })

        # Set browser reference in RuntimeFBXLoaderBridge
        print("  Setting browser reference...")
        send_request("unity.component.setReference", {
            "gameObjectPath": runtime_mgr_path,
            "componentType": "AICam.FBXLoader.RuntimeFBXLoaderBridge",
            "fieldName": "browser",
            "referenceType": "component",
            "referenceGameObjectPath": runtime_mgr_path,
            "referenceComponentType": "AICam.FBXLoader.FileBrowserController"
        })
    else:
        print("\nStep 3: RuntimeManager already exists, skipping")

    # Step 4: Save scene
    print("\nStep 4: Saving scene...")
    response = send_request("unity.scene.save", {})

    if "error" not in response:
        print("\n✅ FBXLoader Scene Setup Complete!")
        print("\nNext steps:")
        print("1. Press Play button in Unity Editor")
        print("2. Click 'ファイルを選択' to select a VRM file")
        print("3. Click 'ロード開始' to load the model")
    else:
        print("\n⚠ Setup completed but scene save failed")

if __name__ == "__main__":
    setup_fbxloader_scene()
