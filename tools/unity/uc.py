#!/usr/bin/env python3
"""Thin wrapper over `unity command` for the RESQ Editor: prints the result payload readably.

Usage: python3 tools/unity/uc.py <command> [--flag value ...]
Waits for the Editor bridge to be reachable (domain reloads) before sending.
"""
import json, subprocess, sys, time

PROJECT = "/Users/dev/Desktop/Resq/game"

def call(args):
    cmd = ["unity", "command", *args, "--project-path", PROJECT, "--format", "json", "--no-banner"]
    for _ in range(120):
        p = subprocess.run(cmd, capture_output=True, text=True)
        out = p.stdout.strip()
        if "Cannot connect" in (p.stderr + out):
            time.sleep(3); continue
        try:
            d = json.loads(out)
        except Exception:
            return p.returncode, out or p.stderr
        return (0 if d.get("success", True) else 1), d
    return 1, "editor unreachable"

if __name__ == "__main__":
    code, d = call(sys.argv[1:])
    if isinstance(d, dict):
        data = d.get("data", d)
        if isinstance(data, list) and len(data) == 1: data = data[0]
        res = data.get("result", data) if isinstance(data, dict) else data
        if isinstance(res, dict) and "result" in res and isinstance(res["result"], str):
            print(res["result"])
        else:
            print(json.dumps(res, indent=1)[:20000])
        if not d.get("success", True): print("ERRORS:", json.dumps(d.get("errors"))[:3000])
    else:
        print(d)
    sys.exit(code)
