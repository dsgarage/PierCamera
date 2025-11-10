#!/usr/bin/env python3
"""
MCP Client for Unity WebSocket Communication
"""
import json
import sys
import websocket
import time

class MCPClient:
    def __init__(self, url="ws://localhost:5050"):
        self.url = url
        self.ws = None
        self.request_id = 1

    def connect(self):
        """Connect to the MCP WebSocket server"""
        print(f"Connecting to {self.url}...")
        self.ws = websocket.create_connection(self.url)
        print("Connected!")

    def send_request(self, method, params=None):
        """Send a JSON-RPC request to the server"""
        request = {
            "jsonrpc": "2.0",
            "id": self.request_id,
            "method": method,
            "params": params or {}
        }
        self.request_id += 1

        request_json = json.dumps(request)
        print(f"\n→ Sending: {request_json}")
        self.ws.send(request_json)

        # Wait for response
        response_json = self.ws.recv()
        print(f"← Received: {response_json}")

        response = json.loads(response_json)
        return response

    def close(self):
        """Close the WebSocket connection"""
        if self.ws:
            self.ws.close()
            print("Connection closed")

def setup_runtime_avatar_loader():
    """Setup RuntimeAvatarLoader component in the scene"""
    client = MCPClient()

    try:
        client.connect()

        # 1. List scene objects to find AppMgr
        print("\n=== Step 1: Finding AppMgr in scene ===")
        response = client.send_request("unity.scene.list", {
            "includeInactive": False
        })

        if "result" in response:
            objects = response["result"].get("objects", [])
            appmgr = None
            xr_origin = None

            for obj in objects:
                if obj["name"] == "AppMgr":
                    appmgr = obj
                    print(f"✓ Found AppMgr: {obj}")
                if obj["name"] == "XR Origin":
                    xr_origin = obj
                    print(f"✓ Found XR Origin: {obj}")

            if not appmgr:
                print("✗ AppMgr not found in scene")
                return

            if not xr_origin:
                print("✗ XR Origin not found in scene")
                return
        else:
            print(f"✗ Error listing scene: {response.get('error')}")
            return

        # 2. Add RuntimeAvatarLoader component to AppMgr
        print("\n=== Step 2: Adding RuntimeAvatarLoader component ===")
        response = client.send_request("unity.component.add", {
            "instanceId": appmgr["instanceId"],
            "componentType": "AICam.VRM.RuntimeAvatarLoader"
        })

        if "result" in response:
            print(f"✓ Component added: {response['result']}")
        else:
            error = response.get("error", {})
            # Check if component already exists
            if "already has" in error.get("message", ""):
                print("✓ Component already exists")
            else:
                print(f"✗ Error adding component: {error}")
                return

        # 3. Find KyokoAnimatorController asset
        print("\n=== Step 3: Finding AnimatorController assets ===")
        response = client.send_request("unity.asset.find", {
            "filter": "KyokoAnimatorController t:RuntimeAnimatorController"
        })

        kyoko_controller = None
        if "result" in response and response["result"].get("assets"):
            kyoko_controller = response["result"]["assets"][0]
            print(f"✓ Found Kyoko controller: {kyoko_controller}")
        else:
            print(f"⚠ Kyoko controller not found, will set later")

        # 4. Find UnityChan AnimatorController asset
        response = client.send_request("unity.asset.find", {
            "filter": "UnityChanLocomotions t:RuntimeAnimatorController"
        })

        unitychan_controller = None
        if "result" in response and response["result"].get("assets"):
            unitychan_controller = response["result"]["assets"][0]
            print(f"✓ Found UnityChan controller: {unitychan_controller}")
        else:
            print(f"⚠ UnityChan controller not found, will set later")

        # 5. Set references on RuntimeAvatarLoader
        print("\n=== Step 4: Setting references on RuntimeAvatarLoader ===")

        # Set arPlacementTarget to XR Origin
        response = client.send_request("unity.component.setReference", {
            "instanceId": appmgr["instanceId"],
            "componentType": "AICam.VRM.RuntimeAvatarLoader",
            "fieldName": "arPlacementTarget",
            "referenceInstanceId": xr_origin["instanceId"]
        })

        if "result" in response:
            print(f"✓ Set arPlacementTarget to XR Origin")
        else:
            print(f"✗ Error setting arPlacementTarget: {response.get('error')}")

        # Set vrmAnimatorController
        if kyoko_controller:
            response = client.send_request("unity.component.setReference", {
                "instanceId": appmgr["instanceId"],
                "componentType": "AICam.VRM.RuntimeAvatarLoader",
                "fieldName": "vrmAnimatorController",
                "assetPath": kyoko_controller["path"]
            })

            if "result" in response:
                print(f"✓ Set vrmAnimatorController to Kyoko controller")
            else:
                print(f"✗ Error setting vrmAnimatorController: {response.get('error')}")

        # Set fbxAnimatorController
        if unitychan_controller:
            response = client.send_request("unity.component.setReference", {
                "instanceId": appmgr["instanceId"],
                "componentType": "AICam.VRM.RuntimeAvatarLoader",
                "fieldName": "fbxAnimatorController",
                "assetPath": unitychan_controller["path"]
            })

            if "result" in response:
                print(f"✓ Set fbxAnimatorController to UnityChan controller")
            else:
                print(f"✗ Error setting fbxAnimatorController: {response.get('error')}")

        # 6. Set PlaceAvatarOnPlaneOnly's avatarLoader reference
        print("\n=== Step 5: Connecting PlaceAvatarOnPlaneOnly to RuntimeAvatarLoader ===")
        response = client.send_request("unity.component.setReference", {
            "instanceId": appmgr["instanceId"],
            "componentType": "PlaceAvatarOnPlaneOnly",
            "fieldName": "avatarLoader",
            "referenceInstanceId": appmgr["instanceId"],
            "referenceComponentType": "AICam.VRM.RuntimeAvatarLoader"
        })

        if "result" in response:
            print(f"✓ Set avatarLoader reference on PlaceAvatarOnPlaneOnly")
        else:
            print(f"✗ Error setting avatarLoader: {response.get('error')}")

        # 7. Save scene
        print("\n=== Step 6: Saving scene ===")
        response = client.send_request("unity.scene.save", {})

        if "result" in response:
            print(f"✓ Scene saved successfully")
        else:
            print(f"✗ Error saving scene: {response.get('error')}")

        print("\n=== Setup Complete! ===")

    except Exception as e:
        print(f"✗ Error: {e}")
        import traceback
        traceback.print_exc()
    finally:
        client.close()

if __name__ == "__main__":
    setup_runtime_avatar_loader()
