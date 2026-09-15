#!/usr/bin/env python3
"""Simuliert Lantronix-Geräte auf UDP-Port 30718 (nur für Demo/Screenshot).
Simulates Lantronix devices on UDP port 30718 (demo/screenshot only)."""
import socket

PORT = 30718
DEVICES = {  # Absender-IP (Loopback) -> MAC
    "127.0.0.11": "00-80-A3-BE-2F-1A",
    "127.0.0.12": "00-80-A3-BE-2F-2B",
    "127.0.0.13": "00-80-A3-BE-2F-3C",
    "127.0.0.14": "00-80-A3-BE-2F-4D",
}

rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
rx.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
rx.bind(("0.0.0.0", PORT))

senders = []
for ip, mac in DEVICES.items():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind((ip, 0))
    # Antwort 0xF7, MAC ab Byte 24 / reply 0xF7, MAC at byte offset 24
    reply = bytes([0, 0, 0, 0xF7]) + bytes(20) + bytes.fromhex(mac.replace("-", ""))
    senders.append((s, reply))

print(f"Warte auf Broadcasts auf Port {PORT} ...")
while True:
    data, addr = rx.recvfrom(1024)
    if data[:4] == b"\x00\x00\x00\xf6":
        for s, reply in senders:
            s.sendto(reply, addr)
