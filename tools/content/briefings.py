#!/usr/bin/env python3
"""Extract player-facing briefing text from the world bible into content/briefings.json.

The game's briefing (U08), debrief lessons (U19) and aftercare (U20) read this file; ContentSync copies it into
Resources. Source: Resq_Production_Pack/docs/03_WORLD_MISSION_BIBLE.md (kept byte-for-byte).
    python3 tools/content/briefings.py
"""
import json, re, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
BIBLE = ROOT / "Resq_Production_Pack/docs/03_WORLD_MISSION_BIBLE.md"
OUT = ROOT / "content/briefings.json"

def field(block, name):
    m = re.search(r"\*\*" + re.escape(name) + r":\*\*\s*(.+)", block)
    return m.group(1).strip() if m else ""

def threats(text):
    # "60s: Reservoir inflow increases; highlight the fork forecast.; 140s: ..." -> [{"at": 60, "text": ...}]
    out = []
    for m in re.finditer(r"(\d+)s:\s*(.*?)(?=;\s*\d+s:|$)", text):
        t = m.group(2).strip().rstrip(";").strip()
        t = re.sub(r"\.;?$", "", t).split(";")[0].strip().rstrip(".")
        out.append({"at": int(m.group(1)), "text": t})
    return out

def main():
    src = BIBLE.read_text()
    worlds, missions = {}, {}
    for wm in re.finditer(r"^## (W\d) — (.+?)\n(.*?)(?=^## |\Z)", src, re.S | re.M):
        wid, name, body = wm.group(1), wm.group(2).strip(), wm.group(3)
        head = body.split("\n### ")[0]
        worlds[wid] = {
            "name": name,
            "identity": field(head, "Identity"),
            "thesis": field(head, "Mechanical thesis"),
            "aftercare": field(head, "Aftercare"),
        }
        for mm in re.finditer(r"^### (W\d-M\d) — (.+?)\n(.*?)(?=^### |\Z)", body, re.S | re.M):
            mid, title, b = mm.group(1), mm.group(2).strip(), mm.group(3)
            missions[mid] = {
                "title": title,
                "world": wid,
                "decision": field(b, "Decision"),
                "layout": field(b, "Layout"),
                "threats": threats(field(b, "Threat sequence")),
                "required": field(b, "Required outcome"),
                "mastery": field(b, "Three-star mastery"),
                "failure": field(b, "Failure to communicate"),
                "payoff": field(b, "Memorable payoff"),
            }
    assert len(worlds) == 5 and len(missions) == 25, (len(worlds), len(missions))
    for mid, m in missions.items():
        assert m["decision"] and m["layout"] and m["required"] and m["threats"], mid
    OUT.write_text(json.dumps({"source": str(BIBLE.relative_to(ROOT)), "worlds": worlds, "missions": missions}, indent=1, ensure_ascii=False) + "\n")
    print(f"briefings: {len(worlds)} worlds, {len(missions)} missions -> {OUT.relative_to(ROOT)}")

if __name__ == "__main__":
    main()
