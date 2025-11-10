#!/usr/bin/env python3
import socket
import base64

try:
    print("Testing MCP WebSocket connection...")

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(5)
    sock.connect(("localhost", 5050))
    print("✓ Socket connected")

    # WebSocket handshake
    key = base64.b64encode(b"test-key-12345678901").decode()
    handshake = (
        f"GET / HTTP/1.1\r\n"
        f"Host: localhost:5050\r\n"
        f"Upgrade: websocket\r\n"
        f"Connection: Upgrade\r\n"
        f"Sec-WebSocket-Key: {key}\r\n"
        f"Sec-WebSocket-Version: 13\r\n"
        f"\r\n"
    )
    sock.send(handshake.encode())
    print("✓ Sent handshake")

    response = sock.recv(4096)
    print(f"✓ Received: {response[:200]}")

    if b"101" in response:
        print("✓ WebSocket handshake successful!")
    else:
        print("✗ Handshake failed")

    sock.close()

except Exception as e:
    print(f"✗ Error: {e}")
    import traceback
    traceback.print_exc()
