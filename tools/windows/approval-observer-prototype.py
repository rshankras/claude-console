# approval-observer-prototype.py — proof-of-concept for approval lighting on Windows.
#
# Codex's hook runner spawns nothing on Windows (docs/spike-windows-codex-hooks.md), and no file
# codex writes records a pending approval — but its app-server BROADCASTS the state to every
# connected client: thread/status/changed with activeFlags ["waitingOnApproval"]. This script is
# that second client. It starts `codex app-server --listen ws://127.0.0.1:18742`, observes, and
# writes the SAME PermissionRequest/UserPromptSubmit envelopes the hook would have written into
# the plugin's IPC tree — so the keypad's bell/amber/red light with no plugin change.
#
# HOW TO TRY IT (three steps):
#   1. In one terminal:                py tools/windows/approval-observer-prototype.py
#   2. In a NEW Windows Terminal tab:  codex --remote ws://127.0.0.1:18742
#      (a plain `codex` will NOT be observed — the --remote flag is what routes the session
#       through the shared server this script watches)
#   3. In that codex, type:  create a file on my Desktop called bell-test.txt
#      When codex asks for approval, the session key and Yes/No on the keypad should light.
#      Answer with the keypad's Yes/No or in the terminal; the lighting should clear.
#
# Ctrl+C stops the script and the server it started.

import base64
import json
import os
import re
import socket
import struct
import subprocess
import threading
import time

PORT = 18742
IPC_SESSIONS = os.path.join(os.environ["TEMP"], "codex-console", "sessions")
IPC_REGISTRY = os.path.join(os.environ["TEMP"], "codex-console", "registry.json")


def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


class WsClient:
    """Minimal RFC6455 client — text frames only, replies to pings."""

    def __init__(self, port):
        self.lines = []
        self.sock = socket.create_connection(("127.0.0.1", port), timeout=10)
        key = base64.b64encode(os.urandom(16)).decode()
        self.sock.sendall(
            (f"GET / HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n"
             "Upgrade: websocket\r\nConnection: Upgrade\r\n"
             f"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode())
        resp = b""
        while b"\r\n\r\n" not in resp:
            resp += self.sock.recv(4096)
        if b" 101" not in resp.split(b"\r\n", 1)[0]:
            raise RuntimeError("websocket handshake refused")
        self.sock.settimeout(None)
        self._buf = b""
        threading.Thread(target=self._read, daemon=True).start()

    def _recv_exact(self, n):
        while len(self._buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError("closed")
            self._buf += chunk
        out, self._buf = self._buf[:n], self._buf[n:]
        return out

    def _read(self):
        try:
            while True:
                b1, b2 = self._recv_exact(2)
                opcode, length = b1 & 0x0F, b2 & 0x7F
                if length == 126:
                    length = struct.unpack(">H", self._recv_exact(2))[0]
                elif length == 127:
                    length = struct.unpack(">Q", self._recv_exact(8))[0]
                if b2 & 0x80:
                    mask = self._recv_exact(4)
                    payload = bytes(c ^ mask[i % 4] for i, c in enumerate(self._recv_exact(length)))
                else:
                    payload = self._recv_exact(length)
                if opcode == 9:
                    self._send_frame(10, payload)
                elif opcode == 1:
                    self.lines.append(payload.decode("utf-8", "replace"))
                elif opcode == 8:
                    break
        except Exception:
            pass

    def _send_frame(self, opcode, payload):
        mask = os.urandom(4)
        n = len(payload)
        header = bytes([0x80 | opcode])
        if n < 126:
            header += bytes([0x80 | n])
        elif n < 65536:
            header += bytes([0x80 | 126]) + struct.pack(">H", n)
        else:
            header += bytes([0x80 | 127]) + struct.pack(">Q", n)
        self.sock.sendall(header + mask + bytes(c ^ mask[i % 4] for i, c in enumerate(payload)))

    def send(self, obj):
        self._send_frame(1, json.dumps(obj).encode())


def write_envelope(event, payload):
    """The same envelope the hook writes — one per live session key, plus the shared fallback."""
    envelope = json.dumps({
        "schema": 1, "agent": "codex-cli", "event": event,
        "ts": int(time.time()), "payload": payload,
    }) + "\n"
    os.makedirs(IPC_SESSIONS, exist_ok=True)
    targets = ["shared"]
    try:
        with open(IPC_REGISTRY, encoding="utf-8") as f:
            targets += list(json.load(f).get("slots", {}).values())
    except Exception:
        pass
    for name in targets:
        path = os.path.join(IPC_SESSIONS, name + ".json")
        tmp = path + f".{os.getpid()}.tmp"
        try:
            with open(tmp, "w", encoding="utf-8") as f:
                f.write(envelope)
            os.replace(tmp, path)
        except Exception:
            pass


def pending_command(client, thread_id, req_id):
    """Best effort: the command awaiting approval, for the risk classifier's red grading."""
    client.send({"jsonrpc": "2.0", "id": req_id, "method": "thread/items/list",
                 "params": {"threadId": thread_id}})
    end = time.time() + 5
    seen = 0
    while time.time() < end:
        while seen < len(client.lines):
            line = client.lines[seen]
            seen += 1
            if f'"id":{req_id}' in line:
                commands = re.findall(r'"command":"((?:[^"\\]|\\.)*)"', line)
                return json.loads(f'"{commands[-1]}"') if commands else None
        time.sleep(0.1)
    return None


def main():
    server = subprocess.Popen(
        ["codex", "app-server", "--listen", f"ws://127.0.0.1:{PORT}"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    log(f"codex app-server listening on ws://127.0.0.1:{PORT}")
    time.sleep(4)

    try:
        client = WsClient(PORT)
        client.send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "clientInfo": {"name": "vizhi-approval-observer", "title": "observer", "version": "0"}}})
        log("observer connected — launch sessions with:  codex --remote ws://127.0.0.1:" + str(PORT))

        waiting = set()
        req_id = 100
        seen = 0
        while True:
            while seen < len(client.lines):
                line = client.lines[seen]
                seen += 1
                if '"thread/status/changed"' not in line:
                    continue
                try:
                    params = json.loads(line)["params"]
                    thread_id = params["threadId"]
                    flags = params.get("status", {}).get("activeFlags") or []
                except Exception:
                    continue
                if "waitingOnApproval" in flags and thread_id not in waiting:
                    waiting.add(thread_id)
                    req_id += 1
                    command = pending_command(client, thread_id, req_id)
                    payload = {"tool_name": "exec", "tool_input": {"command": command}} if command else None
                    write_envelope("PermissionRequest", payload)
                    log(f"APPROVAL PENDING on {thread_id[:8]}…  command={command!r}  → bell/amber written")
                elif "waitingOnApproval" not in flags and thread_id in waiting:
                    waiting.discard(thread_id)
                    write_envelope("UserPromptSubmit", None)
                    log(f"approval resolved on {thread_id[:8]}… → back to busy")
            time.sleep(0.2)
    except KeyboardInterrupt:
        log("stopping")
    finally:
        server.kill()


if __name__ == "__main__":
    main()
