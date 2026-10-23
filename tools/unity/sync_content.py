#!/usr/bin/env python3
"""Headless twin of the Editor menu RESQ/Content/Sync Mission Content (Editor/Setup/ContentSync.cs).

Copies the frozen specification inputs and every authored mission (content/wN/mM) into the Unity project's
Resources so the player build carries exactly the data the headless validator proved. Unity imports the
files on its next refresh. Stale mission folders are removed.
"""
from __future__ import annotations

import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "game/Assets/Resq/Content/Resources/ResqContent"


def main() -> int:
    DEST.mkdir(parents=True, exist_ok=True)
    for name in ("mission_contracts", "equipment_rules", "modes_and_challenges"):
        shutil.copyfile(ROOT / f"specification/content/{name}.json", DEST / f"{name}.json")
    shutil.copyfile(ROOT / "content/briefings.json", DEST / "briefings.json")   # tools/content/briefings.py
    missions = DEST / "missions"
    missions.mkdir(exist_ok=True)
    seen = set()
    for map_json in sorted((ROOT / "content").glob("w*/m*/map.json")):
        mission_dir = map_json.parent
        mid = f"{mission_dir.parent.name.upper()}-{mission_dir.name.upper()}"
        seen.add(mid)
        out = missions / mid
        out.mkdir(exist_ok=True)
        shutil.copyfile(map_json, out / "map.json")
        ov = mission_dir / "contract_overrides.json"
        if ov.exists():
            shutil.copyfile(ov, out / "contract_overrides.json")
        for s in sorted((mission_dir / "strategies").glob("*.json")):
            shutil.copyfile(s, out / f"strategy_{s.name}")
    for stale in missions.iterdir():
        if stale.is_dir() and stale.name not in seen:
            shutil.rmtree(stale)
            meta = stale.with_suffix(".meta")
            if meta.exists():
                meta.unlink()
    print(f"synced {len(seen)} missions into {DEST.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
