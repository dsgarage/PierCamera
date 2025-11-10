#!/usr/bin/env python3
"""
MCP Client for Unity - Setup RuntimeAvatarLoader
Uses standard library only (socket + ssl for WebSocket)
"""
import json
import socket
import base64
import hashlib
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
        key = base64.b64encode(b"unity-mcp-client-key").decode()
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
        elif "result" in response:
            print(f"  ✓ Success")

        return response

    def close(self):
        """Close the connection"""
        if self.sock:
            self.sock.close()
            print("\nConnection closed")

def main():
    """Setup RuntimeAvatarLoader in Unity scene via MCP"""
    client = SimpleWSClient()

    try:
        client.connect()

        # Step 1: List scene objects
        print("\n=== Finding Scene Objects ===")
        response = client.send_request("unity.scene.list", {"includeInactive": False})

        if "result" not in response:
            print("Failed to list scene objects")
            return

        objects = response["result"].get("objects", [])
        appmgr = None
        xr_origin = None

        for obj in objects:
            if obj["name"] == "AppMgr":
                appmgr = obj
                print(f"  Found AppMgr (ID: {obj['instanceId']})")
            if obj["name"] == "XR Origin":
                xr_origin = obj
                print(f"  Found XR Origin (ID: {obj['instanceId']})")

        if not appmgr or not xr_origin:
            print("  ✗ Required objects not found")
            return

        # Step 2: Add RuntimeAvatarLoader component
        print("\n=== Adding RuntimeAvatarLoader Component ===")
        response = client.send_request("unity.component.add", {
            "instanceId": appmgr["instanceId"],
            "componentType": "AICam.VRM.RuntimeAvatarLoader"
        })

        # Step 3: Find AnimatorController assets
        print("\n=== Finding AnimatorController Assets ===")
        response = client.send_request("unity.asset.find", {
            "filter": "KyokoAnimatorController t:RuntimeAnimatorController"
        })
        kyoko_path = None
        if "result" in response and response["result"].get("assets"):
            kyoko_path = response["result"]["assets"][0]["path"]
            print(f"  Found: {kyoko_path}")

        response = client.send_request("unity.asset.find", {
            "filter": "UnityChanLocomotions t:RuntimeAnimatorController"
        })
        unitychan_path = None
        if "result" in response and response["result"].get("assets"):
            unitychan_path = response["result"]["assets"][0]["path"]
            print(f"  Found: {unitychan_path}")

        # Step 4: Set references
        print("\n=== Setting References ===")

        # Set arPlacementTarget to XR Origin
        client.send_request("unity.component.setReference", {
            "instanceId": appmgr["instanceId"],
            "componentType": "AICam.VRM.RuntimeAvatarLoader",
            "fieldName": "arPlacementTarget",
            "referenceInstanceId": xr_origin["instanceId"]
        })

        # Set vrmAnimatorController
        if kyoko_path:
            client.send_request("unity.component.setReference", {
                "instanceId": appmgr["instanceId"],
                "componentType": "AICam.VRM.RuntimeAvatarLoader",
                "fieldName": "vrmAnimatorController",
                "assetPath": kyoko_path
            })

        # Set fbxAnimatorController
        if unitychan_path:
            client.send_request("unity.component.setReference", {
                "instanceId": appmgr["instanceId"],
                "componentType": "AICam.VRM.RuntimeAvatarLoader",
                "fieldName": "fbxAnimatorController",
                "assetPath": unitychan_path
            })

        # Set initialStateName
        client.send_request("unity.component.set", {
            "instanceId": appmgr["instanceId"],
            "componentType": "AICam.VRM.RuntimeAvatarLoader",
            "fieldName": "initialStateName",
            "value": "Idle"
        })

        # Step 5: Connect PlaceAvatarOnPlaneOnly
        print("\n=== Connecting PlaceAvatarOnPlaneOnly ===")
        client.send_request("unity.component.setReference", {
            "instanceId": appmgr["instanceId"],
            "componentType": "PlaceAvatarOnPlaneOnly",
            "fieldName": "avatarLoader",
            "referenceInstanceId": appmgr["instanceId"],
            "referenceComponentType": "AICam.VRM.RuntimeAvatarLoader"
        })

        # Step 6: Save scene
        print("\n=== Saving Scene ===")
        client.send_request("unity.scene.save", {})

        print("\n✅ Setup Complete!")

    except Exception as e:
        print(f"\n✗ Error: {e}")
        import traceback
        traceback.print_exc()
    finally:
        client.close()

if __name__ == "__main__":
    main()
