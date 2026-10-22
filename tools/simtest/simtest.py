#!/usr/bin/env python3
"""Headless C# build-and-test runner for RESQ's pure simulation code.

Uses the .NET 6 runtime and Roslyn compiler bundled inside the installed Unity
Editor, so no extra SDK is needed and the Unity Editor can stay closed (or busy).

Usage:
  python3 tools/simtest/simtest.py sim [FILTER]      # Sim v2 runtime + tests
  python3 tools/simtest/simtest.py legacy            # compile the legacy oracle only
  python3 tools/simtest/simtest.py golden [FILTER]   # Sim v2 vs legacy oracle tests
  python3 tools/simtest/simtest.py validate ARGS...  # mission validator (later phases)

Exit code: 0 = all selected tests passed, 1 = test failures, 2 = build error.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UNITY = Path(os.environ.get(
    "RESQ_UNITY_APP", "/Applications/Unity/Unity-6000.3.24f1/Unity.app"))
SCRIPTING = UNITY / "Contents/Resources/Scripting"
DOTNET = SCRIPTING / "NetCoreRuntime/dotnet"
CSC = SCRIPTING / "DotNetSdkRoslyn/csc.dll"
FX_DIR = SCRIPTING / "NetCoreRuntime/shared/Microsoft.NETCore.App"
CACHE = ROOT / "tools/.cache/simtest"

SIM_RUNTIME = ROOT / "game/Assets/Resq/Sim/Runtime"
SIM_TESTS = ROOT / "game/Assets/Resq/Sim/Tests"
LEGACY = ROOT / "reference/legacy-sim/Resq"
HARNESS = ROOT / "tools/simtest/Harness"


def fx_version_dir() -> Path:
    versions = sorted(p for p in FX_DIR.iterdir() if p.is_dir())
    if not versions:
        raise SystemExit(f"No .NET runtime found under {FX_DIR}")
    return versions[-1]


def references() -> list[str]:
    fx = fx_version_dir()
    refs = []
    for dll in sorted(fx.glob("*.dll")):
        name = dll.name
        if name.startswith(("System.", "Microsoft.CSharp", "Microsoft.Win32.Primitives")) or name in (
                "netstandard.dll", "mscorlib.dll"):
            refs.append(f"-r:{dll}")
    return refs


def sources(*roots: Path, exclude_parts: tuple[str, ...] = ()) -> list[str]:
    files: list[str] = []
    for root in roots:
        if not root.exists():
            continue
        for path in sorted(root.rglob("*.cs")):
            rel_parts = path.relative_to(root).parts
            if any(part in exclude_parts for part in rel_parts):
                continue
            files.append(str(path))
    return files


def compile_cs(out: Path, srcs: list[str], target: str, extra_refs: list[Path] = ()) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    rsp = out.with_suffix(".rsp")
    args = [
        "-nologo", "-noconfig", "-nostdlib+", "-langversion:9.0", "-optimize+", "-deterministic",
        "-warn:4", "-nowarn:1591,0649,0169,0414", f"-target:{target}", f"-out:{out}",
        "-define:RESQ_HEADLESS",
        *references(),
        *(f"-r:{p}" for p in extra_refs),
        *srcs,
    ]
    rsp.write_text("\n".join(f'"{a}"' if " " in a else a for a in args))
    started = time.time()
    proc = subprocess.run([str(DOTNET), str(CSC), f"@{rsp}"], capture_output=True, text=True)
    elapsed = time.time() - started
    output = (proc.stdout + proc.stderr).strip()
    errors = [line for line in output.splitlines() if ": error " in line]
    if proc.returncode != 0:
        print(output[-8000:])
        print(f"BUILD FAILED: {out.name} ({len(errors)} errors, {elapsed:.1f}s)")
        raise SystemExit(2)
    warnings = [line for line in output.splitlines() if ": warning " in line]
    print(f"built {out.name} from {len(srcs)} files in {elapsed:.1f}s ({len(warnings)} warnings)")


def write_runtimeconfig(dll: Path) -> Path:
    fx = fx_version_dir()
    cfg = dll.with_suffix(".runtimeconfig.json")
    cfg.write_text(json.dumps({
        "runtimeOptions": {
            "tfm": "net6.0",
            "framework": {"name": "Microsoft.NETCore.App", "version": fx.name},
            "configProperties": {"System.GC.Server": False},
        }
    }, indent=2))
    return cfg


def run(dll: Path, args: list[str]) -> int:
    cfg = write_runtimeconfig(dll)
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_ROLL_FORWARD="Major")
    proc = subprocess.run([str(DOTNET), "exec", "--runtimeconfig", str(cfg), str(dll), *args], env=env)
    return proc.returncode


def build_legacy() -> Path:
    out = CACHE / "Resq.Legacy.dll"
    srcs = sources(LEGACY / "Runtime", LEGACY / "Content", exclude_parts=("Probe", "Editor", "Tests"))
    compile_cs(out, srcs, "library")
    return out


def main(argv: list[str]) -> int:
    if not DOTNET.exists() or not CSC.exists():
        print(f"Unity-bundled .NET not found at {SCRIPTING}")
        return 2
    suite = argv[1] if len(argv) > 1 else "sim"
    rest = argv[2:]
    if suite == "legacy":
        build_legacy()
        return 0
    if suite == "sim":
        out = CACHE / "Resq.Sim.Tests.exe.dll"
        compile_cs(out, sources(SIM_RUNTIME) + sources(SIM_TESTS, exclude_parts=("NUnit",)) + sources(HARNESS), "exe")
        return run(out, rest)
    if suite == "lab":
        out = CACHE / "Resq.Sim.Tests.exe.dll"
        compile_cs(out, sources(SIM_RUNTIME) + sources(SIM_TESTS, exclude_parts=("NUnit",)) + sources(HARNESS), "exe")
        return run(out, ["--lab", *rest])
    if suite == "golden":
        legacy = build_legacy()
        out = CACHE / "Resq.Golden.Tests.exe.dll"
        compile_cs(out, sources(SIM_RUNTIME) + sources(SIM_TESTS, exclude_parts=("NUnit",)) + sources(HARNESS)
                   + sources(ROOT / "tools/simtest/Golden"), "exe", extra_refs=[legacy])
        import shutil
        shutil.copy2(legacy, out.parent / legacy.name)
        return run(out, ["--golden", *rest])
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
