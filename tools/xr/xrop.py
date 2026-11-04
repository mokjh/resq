#!/usr/bin/env python3
"""Small MCP-over-SSE client for the Meta XR Operator layer (http://127.0.0.1:8720), standard library only.
    xrop.py list | schema TOOL | call TOOL '{json}'
Other scripts: from xrop import call, save_images."""
import base64, json, queue, sys, threading, time, urllib.request

BASE = "http://127.0.0.1:8720"
_events, _endpoint, _next = queue.Queue(), {}, [0]

def _reader():
    with urllib.request.urlopen(urllib.request.Request(BASE + "/sse", headers={"Accept": "text/event-stream"}), timeout=3600) as r:
        kind = None
        for raw in r:
            line = raw.decode("utf-8").rstrip("\n")
            if line.startswith("event:"): kind = line[6:].strip()
            elif line.startswith("data:"):
                data = line[5:].strip()
                if kind == "endpoint": _endpoint["url"] = data if data.startswith("http") else BASE + data
                else: _events.put(data)

def _post(msg):
    urllib.request.urlopen(urllib.request.Request(_endpoint["url"], data=json.dumps(msg).encode(), headers={"Content-Type": "application/json"}), timeout=60).read()

def rpc(method, params=None):
    _next[0] += 1
    _post({"jsonrpc": "2.0", "id": _next[0], "method": method, "params": params or {}})
    end = time.time() + 60
    while time.time() < end:
        try: d = json.loads(_events.get(timeout=60))
        except ValueError: continue
        if isinstance(d, dict) and d.get("id") == _next[0]: return d
    raise TimeoutError(method)

def connect():
    threading.Thread(target=_reader, daemon=True).start()
    for _ in range(200):
        if "url" in _endpoint: break
        time.sleep(0.05)
    rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "resq", "version": "0.1"}})
    _post({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}})

def call(name, args=None):
    r = rpc("tools/call", {"name": name, "arguments": args or {}})
    return r.get("result", r)

def save_images(result, path):
    n = 0
    for c in result.get("content", []):
        if c.get("type") == "image":
            with open(path if n == 0 else path.replace(".png", f"_{n}.png"), "wb") as f: f.write(base64.b64decode(c["data"]))
            n += 1
    return n

connect()
if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "list":
        for t in rpc("tools/list")["result"]["tools"]: print(t["name"], "-", (t.get("description") or "")[:120].replace("\n", " "))
    elif cmd == "schema":
        for t in rpc("tools/list")["result"]["tools"]:
            if t["name"] == sys.argv[2]: print(json.dumps(t, indent=1)[:3500])
    elif cmd == "call":
        print(json.dumps(call(sys.argv[2], json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}), indent=1)[:4000])
