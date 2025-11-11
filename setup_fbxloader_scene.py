#!/usr/bin/env python3
"""
FBXLoader Scene Setup via MCP WebSocket
"""
import json
import socket
import base64
import struct
import sys

class SimpleWSClient:
    def __init__(self, host="localhost", port=5050):
        self.host = host
        self.port = port
        self.sock = None
        self.request_id = 1

    def connect(self):
        """Establish WebSocket connection"""
        print(f"Connecting to ws://{self.host}:{self.port}...")
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.connect((self.host, self.port))

        # Perform WebSocket handshake
        key = base64.b64encode(b"fbxloader-setup-key").decode()
        handshake = (
            f"GET / HTTP/1.1\r\n"
            f"Host: {self.host}:{self.port}\r\n"
            f"Upgrade: websocket\r\n"
            f"Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            f"Sec-WebSocket-Version: 13\r\n"
            f"\r\n"
        )
        self.sock.send(handshake.encode())

        # Read handshake response
        response = b""
        while b"\r\n\r\n" not in response:
            response += self.sock.recv(1024)

        if b"101" not in response:
            raise Exception(f"WebSocket handshake failed: {response.decode()}")

        print("✓ Connected!")

    def send_frame(self, payload):
        """Send a WebSocket text frame"""
        payload_bytes = payload.encode('utf-8')
        payload_len = len(payload_bytes)

        # Build frame header
        frame = bytearray()
        frame.append(0x81)  # FIN + text frame

        # Payload length
        if payload_len <= 125:
            frame.append(0x80 | payload_len)  # MASK bit + length
        elif payload_len <= 65535:
            frame.append(0x80 | 126)
            frame.extend(struct.pack('>H', payload_len))
        else:
            frame.append(0x80 | 127)
            frame.extend(struct.pack('>Q', payload_len))

        # Masking key
        mask_key = b'\x00\x00\x00\x00'
        frame.extend(mask_key)

        # Masked payload
        frame.extend(payload_bytes)

        self.sock.send(bytes(frame))

    def recv_frame(self):
        """Receive a WebSocket frame"""
        # Read first 2 bytes
        header = self.sock.recv(2)
        if len(header) < 2:
            return None

        payload_len = header[1] & 0x7F

        # Read extended payload length if needed
        if payload_len == 126:
            ext_len = self.sock.recv(2)
            payload_len = struct.unpack('>H', ext_len)[0]
        elif payload_len == 127:
            ext_len = self.sock.recv(8)
            payload_len = struct.unpack('>Q', ext_len)[0]

        # Read payload
        payload = b""
        while len(payload) < payload_len:
            chunk = self.sock.recv(payload_len - len(payload))
            if not chunk:
                break
            payload += chunk

        return payload.decode('utf-8')

    def send_request(self, method, params=None):
        """Send JSON-RPC request and receive response"""
        request = {
            "jsonrpc": "2.0",
            "id": self.request_id,
            "method": method,
            "params": params or {}
        }
        self.request_id += 1

        request_json = json.dumps(request)
        print(f"\n→ {method}")
        self.send_frame(request_json)

        response_json = self.recv_frame()
        response = json.loads(response_json)

        if "error" in response:
            print(f"  ✗ Error: {response['error'].get('message', 'Unknown error')}")
            return response
        elif "result" in response:
            print(f"  ✓ Success")
            return response

    def close(self):
        """Close the connection"""
        if self.sock:
            self.sock.close()
            print("\nConnection closed")

def setup_fbxloader_scene():
    """Setup FBXLoader scene via MCP"""
    client = SimpleWSClient()

    try:
        client.connect()

        # Step 1: Open FBXLoader scene
        print("\n=== Step 1: Opening FBXLoader Scene ===")
        response = client.send_request("unity.scene.open", {
            "scenePath": "Assets/Scenes/FBXLoader.unity"
        })

        if "error" in response:
            print(f"Failed to open scene: {response['error']}")
            return

        # Step 2: List current scene objects
        print("\n=== Step 2: Checking Existing Objects ===")
        response = client.send_request("unity.scene.list", {"includeInactive": False})

        if "result" not in response:
            print("Failed to list scene objects")
            return

        objects = response["result"].get("objects", [])
        ui_document_exists = any(obj["name"] == "UI_Document" for obj in objects)
        runtime_manager_exists = any(obj["name"] == "RuntimeManager" for obj in objects)

        # Step 3: Create UI_Document if it doesn't exist
        if not ui_document_exists:
            print("\n=== Step 3: Creating UI_Document ===")
            response = client.send_request("unity.gameobject.create", {
                "name": "UI_Document"
            })

            if "result" not in response:
                print("Failed to create UI_Document")
                return

            ui_doc_id = response["result"]["instanceId"]
            print(f"  Created UI_Document (ID: {ui_doc_id})")

            # Add UIDocument component
            response = client.send_request("unity.component.add", {
                "instanceId": ui_doc_id,
                "componentType": "UnityEngine.UIElements.UIDocument"
            })

            # Add FileBrowserUIController component
            response = client.send_request("unity.component.add", {
                "instanceId": ui_doc_id,
                "componentType": "AICam.FBXLoader.FileBrowserUIController"
            })

            # Set UXML asset
            response = client.send_request("unity.component.setReference", {
                "instanceId": ui_doc_id,
                "componentType": "UnityEngine.UIElements.UIDocument",
                "fieldName": "m_VisualTreeAsset",
                "assetPath": "Assets/UI/RuntimeFBXLoaderWithFileBrowser/RuntimeFBXLoaderWithFileBrowser.uxml"
            })

            # Set PanelSettings
            response = client.send_request("unity.component.setReference", {
                "instanceId": ui_doc_id,
                "componentType": "UnityEngine.UIElements.UIDocument",
                "fieldName": "m_PanelSettings",
                "assetPath": "Assets/UI/PanelSettings.asset"
            })
        else:
            print("\n=== Step 3: UI_Document already exists ===")
            ui_doc = next(obj for obj in objects if obj["name"] == "UI_Document")
            ui_doc_id = ui_doc["instanceId"]

        # Step 4: Create RuntimeManager if it doesn't exist
        if not runtime_manager_exists:
            print("\n=== Step 4: Creating RuntimeManager ===")
            response = client.send_request("unity.gameobject.create", {
                "name": "RuntimeManager"
            })

            if "result" not in response:
                print("Failed to create RuntimeManager")
                return

            runtime_mgr_id = response["result"]["instanceId"]
            print(f"  Created RuntimeManager (ID: {runtime_mgr_id})")

            # Add FileBrowserController component
            response = client.send_request("unity.component.add", {
                "instanceId": runtime_mgr_id,
                "componentType": "AICam.FBXLoader.FileBrowserController"
            })

            # Add RuntimeFBXLoaderBridge component
            response = client.send_request("unity.component.add", {
                "instanceId": runtime_mgr_id,
                "componentType": "AICam.FBXLoader.RuntimeFBXLoaderBridge"
            })

            # Set browser reference
            response = client.send_request("unity.component.setReference", {
                "instanceId": runtime_mgr_id,
                "componentType": "AICam.FBXLoader.RuntimeFBXLoaderBridge",
                "fieldName": "browser",
                "referenceInstanceId": runtime_mgr_id,
                "referenceComponentType": "AICam.FBXLoader.FileBrowserController"
            })
        else:
            print("\n=== Step 4: RuntimeManager already exists ===")
            runtime_mgr = next(obj for obj in objects if obj["name"] == "RuntimeManager")
            runtime_mgr_id = runtime_mgr["instanceId"]

        # Step 5: Set UI Document reference in FileBrowserUIController
        print("\n=== Step 5: Connecting UI References ===")
        response = client.send_request("unity.component.setReference", {
            "instanceId": ui_doc_id,
            "componentType": "AICam.FBXLoader.FileBrowserUIController",
            "fieldName": "uiDocument",
            "referenceInstanceId": ui_doc_id,
            "referenceComponentType": "UnityEngine.UIElements.UIDocument"
        })

        # Step 6: Save scene
        print("\n=== Step 6: Saving Scene ===")
        response = client.send_request("unity.scene.save", {})

        print("\n✅ FBXLoader Scene Setup Complete!")
        print("\nNext steps:")
        print("1. Press Play button in Unity Editor")
        print("2. Click 'ファイルを選択' to select a VRM file")
        print("3. Click 'ロード開始' to load the model")

    except Exception as e:
        print(f"\n✗ Error: {e}")
        import traceback
        traceback.print_exc()
    finally:
        client.close()

if __name__ == "__main__":
    setup_fbxloader_scene()
