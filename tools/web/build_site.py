#!/usr/bin/env python3
"""Builds the static RESQ website into web/app (plain HTML + one stylesheet + a few lines of JS; no build step).

Design: "Briefing Room" (DECISIONS.md, 29 Oct 2026). The site is built from RESQ's own interface: glass panels,
objective checklists, HUD chips, warning toasts and the tool tray, in the game's own colours (UiKit.cs). Navigation
is the tool tray, docked at the bottom like the tray at the near edge of the board; the Menu tool opens a glass
sheet. Each world is a mission screen on its own colour table: the valley as a cut-out object
(tools/web/capture_objects.sh), a briefing card, the first mission's real threat timings, loadout, residents,
supply and its two reference plans, all read from the mission contracts and the world bible, so the site says
exactly what the game contains. Every image is labelled as an Editor render; panels are the game's interface rebuilt
for the web. Nothing is AI-generated, and nothing is claimed about headsets until device testing.
    python3 tools/web/build_site.py
"""
import hashlib, html, json, pathlib, struct

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "web/app"
MEDIA_DIR = OUT / "media"
BRIEF = json.loads((ROOT / "content/briefings.json").read_text())
CONTRACTS = {m["id"]: m for m in json.loads((ROOT / "specification/content/mission_contracts.json").read_text())["missions"]}
COST = {e["id"]: e["supply_cost"] for e in json.loads((ROOT / "specification/content/equipment_rules.json").read_text())["equipment"]}
WORLDS = ["W1", "W2", "W3", "W4", "W5"]
SLUG = {"W1": "alder-valley", "W2": "emberwood", "W3": "frostline", "W4": "tidemark", "W5": "cinder-reach"}
HAZARD = {"W1": "Floods", "W2": "Wildfire", "W3": "Snow and avalanches", "W4": "Tides and storm power", "W5": "Lava and heat"}
# the table each valley sits on, and the text tone on it (tools/web/check_contrast.py checks every pair)
SLAB = {"W1": ("#2357C4", "light"), "W2": ("#B8361B", "light"), "W3": ("#DCEAF2", "dark"),
        "W4": ("#F2BE22", "dark"), "W5": ("#1C1A19", "light")}
TOOL = {"barrier": "Flood Barrier", "bridge": "Emergency Bridge", "skiff": "Rescue Boat", "pump": "Pump",
        "generator": "Generator", "beacon": "Beacon", "firebreak": "Firebreak", "sprinkler": "Sprinkler",
        "crawler": "Crawler", "brace": "Brace", "winch": "Cable Winch", "relay": "Power Relay",
        "cooler": "Lava Cooler", "shelter": "Heat Shelter"}   # the in-game names (HudView.ToolName)

STATUS = "In development for the Meta VR Start Developer Competition 2026. Not yet released."
RENDER = "Editor render"

DETAIL = {
    "W1": "A group gathers by the bus at the crossing.",
    "W2": "Residents reach the landing, where the rescue crew waits.",
    "W3": "Arms up at the Frostline landing.",
    "W4": "Safe at the harbour landing.",
    "W5": "A Cinder Reach group, one resident in a wheelchair.",
}
OBJ_ALT = {
    "W1": "Alder Valley as a miniature on a wooden board: river channels, cedar houses, a church and a bus by the crossing.",
    "W2": "Emberwood as a miniature: a meadow inside a pine forest, a lookout tower, cabins and a burning fire line.",
    "W3": "Frostline as a miniature: snowfields ringed by peaks, a cable station, cabins and a red snowcat.",
    "W4": "Tidemark as a miniature: a harbour basin, two islands of houses, a causeway and a lighthouse.",
    "W5": "Cinder Reach as a miniature: dark basalt terraces split by glowing lava channels.",
}
PLATES = {  # 16:9 captures with the game's own UI, shown uncropped (tools/web/capture.sh)
    "plan.jpg": ("A tool hovering over the board shows a forecast: green cells stay dry, red cells would flood.", "Forecast while placing a barrier"),
    "act.jpg": ("A rescue boat crosses the river while residents wait at a dock.", "Routing a boat to a waiting group"),
    "rescue.jpg": ("A glass card above the board reads Rescue complete, with each goal marked Done.", "Results after a rescue"),
    "menu.jpg": ("A glass card above the board lists the missions for one world.", "Choosing a mission"),
}

# ---------- icons: simple line glyphs in the game's hologram cyan (currentColor) ----------
def ico(name):
    p = {
        "worlds": '<rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/>',
        "plays": '<path d="M8 13V5.5a1.5 1.5 0 0 1 3 0V12m0-1.5a1.5 1.5 0 0 1 3 0V12m0-.5a1.5 1.5 0 0 1 3 0v4.5a6 6 0 0 1-6 6h-.5a6 6 0 0 1-4.9-2.6L4 16.5a1.5 1.5 0 0 1 2.4-1.8L8 16"/>',
        "access": '<circle cx="12" cy="4.5" r="2"/><path d="M5 8.5l7 1.5 7-1.5M12 10v4.5m0 0l-3.5 6.5M12 14.5l3.5 6.5"/>',
        "press": '<rect x="3" y="7" width="18" height="13" rx="1.5"/><circle cx="12" cy="13.5" r="3.5"/><path d="M8.5 7l1.5-3h4l1.5 3"/>',
        "menu": '<path d="M4 7h16M4 12h16M4 17h16"/>',
        "people": '<circle cx="9" cy="7" r="3"/><path d="M3 20v-1a6 6 0 0 1 12 0v1"/><circle cx="17" cy="9" r="2.5"/><path d="M15.5 14.2A5 5 0 0 1 21 19v1"/>',
        "supply": '<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3zM4 7.5l8 4.5 8-4.5M12 12v9"/>',
        "clock": '<circle cx="12" cy="12" r="8.5"/><path d="M12 7v5l3 2"/>',
        "warn": '<path d="M12 3.5l9.5 16.5h-19L12 3.5z"/><path d="M12 10v4.5"/><circle cx="12" cy="17.3" r=".6"/>',
        "open": '<circle cx="12" cy="12" r="8"/>',
        "star": '<path d="M12 3.5l2.6 5.4 5.9.8-4.3 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.3-4.1 5.9-.8L12 3.5z"/>',
        "step": '<circle cx="9" cy="5" r="2"/><path d="M9 8v6h6l2 5M9 11h5"/><circle cx="10" cy="17" r="4"/>',
        "barrier": '<path d="M3 17h18M5 17V9h14v8M5 13h14"/>',
        "bridge": '<path d="M2 16h20M4 16c2-6 14-6 16 0M8 12.5V16m8-3.5V16"/>',
        "skiff": '<path d="M3 13h18l-3 5H6l-3-5zM8 13V7l6 3-6 0"/>',
        "beacon": '<path d="M12 3l6 9-6 9-6-9 6-9z"/>',
        "pump": '<circle cx="9" cy="14" r="5"/><path d="M14 14h7M18 11l3 3-3 3"/>',
        "generator": '<path d="M13 3L5 13h6l-1 8 8-10h-6l1-8z"/>',
        "firebreak": '<path d="M3 12h3m3 0h3m3 0h3m3 0h0M4 7l2-3 2 3M16 20l2-3 2 3"/>',
        "sprinkler": '<path d="M12 21v-7M8 14h8M7 9c0 1.5 1 2 1 2M12 7c0 1.5 1 2 1 2M17 9c0 1.5 1 2 1 2"/>',
        "crawler": '<rect x="3" y="12" width="18" height="6" rx="3"/><path d="M7 12V8h8l3 4"/>',
        "brace": '<path d="M5 20L19 4M5 4l14 16M3 20h18"/>',
        "winch": '<path d="M2 4h20M8 4v9M16 4v5"/><rect x="5" y="13" width="6" height="5"/><rect x="13" y="9" width="6" height="5"/>',
        "relay": '<path d="M3 12h4l3-6 4 12 3-6h4"/>',
        "cooler": '<path d="M12 3v18M4.2 7.5l15.6 9M4.2 16.5l15.6-9"/>',
        "shelter": '<path d="M3 11l9-7 9 7v9H3v-9zM9 20v-5h6v5"/>',
    }[name]
    return f'<svg class="i" viewBox="0 0 24 24" aria-hidden="true">{p}</svg>'


RING = ('<svg class="ring" viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="13" fill="#FF6B35"/>'
        '<circle cx="16" cy="16" r="7" fill="#fff"/><g fill="#fff"><rect x="14" y="2" width="4" height="7"/>'
        '<rect x="14" y="23" width="4" height="7"/><rect x="2" y="14" width="7" height="4"/><rect x="23" y="14" width="7" height="4"/></g></svg>')

NAV = [("worlds.html", "Worlds"), ("how-to-play.html", "How it plays"), ("accessibility.html", "Access"), ("press.html", "Press")]
TRAY = [("worlds.html", "worlds", "Worlds", "Worlds", "5 valleys"), ("how-to-play.html", "plays", "How it plays", "Play", "4 steps"),
        ("accessibility.html", "access", "Access", "Access", "8 options"), ("press.html", "press", "Press", "Press", "Fact sheet")]
SHEET = [("index.html", "Home"), ("worlds.html", "Worlds"), ("how-to-play.html", "How it plays"),
         ("accessibility.html", "Accessibility"), ("press.html", "Press"), ("support.html", "Support"), ("privacy.html", "Privacy")]


def e(s):
    return html.escape(s, quote=True)


def clock(sec):
    return f"{sec // 60}:{sec % 60:02d}"


def size(file):
    """Pixel size of a media file (WebP or JPEG), so every <img> reserves its space before it loads."""
    b = (MEDIA_DIR / file).read_bytes()
    if b[:4] == b"RIFF":
        kind = b[12:16]
        if kind == b"VP8X":
            return 1 + int.from_bytes(b[24:27], "little"), 1 + int.from_bytes(b[27:30], "little")
        if kind == b"VP8 ":
            w, h = struct.unpack("<HH", b[26:30]); return w & 0x3FFF, h & 0x3FFF
        if kind == b"VP8L":
            v = int.from_bytes(b[21:25], "little"); return 1 + (v & 0x3FFF), 1 + ((v >> 14) & 0x3FFF)
    i = 2
    while i < len(b):
        marker, length = b[i + 1], struct.unpack(">H", b[i + 2:i + 4])[0]
        if marker in (0xC0, 0xC2):
            h, w = struct.unpack(">HH", b[i + 5:i + 9]); return w, h
        i += 2 + length
    raise ValueError(file)


def img(file, alt, cls="", eager=False):
    w, h = size(file)
    return (f'<img class="{cls}" src="media/{file}" alt="{e(alt)}" width="{w}" height="{h}" '
            f'{"fetchpriority=\"high\"" if eager else "loading=\"lazy\""} decoding="async">')


def tag(text):
    return f'<span class="tag">{e(text)}</span>'


def page(name, title, body, desc, cls=""):
    css_v = hashlib.sha1((OUT / "site.css").read_bytes()).hexdigest()[:8]   # new URL whenever the styles change
    js_v = hashlib.sha1((OUT / "js/valley.js").read_bytes()).hexdigest()[:8]
    links = "".join(f'<li><a href="{h}"{" aria-current=\"page\"" if h == name else ""}>{e(t)}</a></li>' for h, t in NAV)
    every = "".join(f'<li><a href="{h}"{" aria-current=\"page\"" if h == name else ""}>{e(t)}</a></li>' for h, t in SHEET)
    doc = f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>{e(title)}</title>
<meta name="description" content="{e(desc)}">
<meta name="theme-color" content="#E8EDEE">
<link rel="icon" href="media/icon.png">
<link rel="preload" href="fonts/AtkinsonHyperlegible-Bold.ttf" as="font" type="font/ttf" crossorigin>
<link rel="stylesheet" href="site.css?v={css_v}">
<script>document.documentElement.classList.add("js")</script>
</head>
<body class="{cls}">
<a class="skip" href="#main">Skip to content</a>
<header class="top">
  <a class="mark" href="index.html" aria-label="RESQ home">{RING}<span>RESQ</span></a>
  <nav aria-label="Main"><ul class="links">{links}</ul></nav>
  <details class="menu"><summary aria-label="Menu"><span>Menu</span></summary>
    <div class="menu-sheet"><ul>{every}</ul><p class="menu-note">{e(STATUS)}</p></div></details>
</header>
<main id="main">
{body}
</main>
<footer class="foot">
  <div class="shelf" aria-hidden="true">{"".join(f'<canvas data-valley="{w}-M1" data-delay="{i * 140}" data-lift="2.6"></canvas>' for i, w in enumerate(WORLDS))}</div>
  <p class="foot-word" aria-hidden="true">RESQ</p>
  <div class="foot-grid">
    <p class="foot-status">{e(STATUS)}</p>
    <ul class="foot-links">{every}</ul>
    <p class="foot-note">The valleys on this site are drawn from RESQ's own mission files, and the flood replays the game's deterministic simulation. Photographs are the development build rendered in the Unity Editor (October 2026), labelled “{RENDER}”. None of it is headset footage, and none of it is AI-generated. Made by Amir Lotfy.</p>
  </div>
</footer>
<script src="js/valley.js?v={js_v}" defer></script>
</body>
</html>
"""
    (OUT / name).write_text(doc)


# ---------- game components ----------
def objectives(items):
    rows = "".join(f'<li class="obj-row{" bonus" if bonus else ""}">{ico("star" if bonus else "open")}<span>{e(t)}</span></li>'
                   for t, bonus in items)
    return f'<ul class="objectives">{rows}</ul>'


def toasts(w):
    rows = "".join(f'<li class="glass toast">{ico("warn")}<span class="t-time">{clock(t["at"])}</span><span>{e(t["text"])}</span></li>'
                   for t in BRIEF["missions"][f"{w}-M1"]["threats"])
    return f'<ol class="toasts" aria-label="What happens in the first mission">{rows}</ol>'


def loadout(w):
    lo = CONTRACTS[f"{w}-M1"]["default_loadout"]
    cards = "".join(f'<li class="glass tool-card">{ico(k)}<span class="tool-name">{e(TOOL[k])}</span>'
                    f'<span class="tool-sub">×{n} · {COST[k]} supply</span></li>' for k, n in lo.items())
    return f'<ul class="loadout" aria-label="Tools in the first mission">{cards}</ul>'


def mission_chips(w):
    c = CONTRACTS[f"{w}-M1"]
    step = sum(g["mobility_counts"].get("step_free", 0) for g in c["groups"])
    return (f'<ul class="chips on-table"><li class="chip">{ico("people")}{c["total_residents"]} residents</li>'
            f'<li class="chip">{ico("step")}{step} step-free</li><li class="chip">{ico("supply")}{c["supply_budget"]} supply</li>'
            f'<li class="chip">{ico("clock")}{clock(c["duration_scenario_ticks"] // 10)}</li></ul>')


def plans(w):
    c = CONTRACTS[f"{w}-M1"]["reference_strategy_briefs"]
    return (f'<div class="glass plans"><p class="card-kicker">Two ways to win</p><dl>'
            f'<div><dt>Plan A</dt><dd>{e(c["A"])}</dd></div><div><dt>Plan B</dt><dd>{e(c["B"])}</dd></div></dl></div>')


def mission_tiles(w, full=False):
    tiles = ""
    for m in range(1, 6):
        mid = f"{w}-M{m}"; mb = BRIEF["missions"][mid]
        tiles += (f'<li class="glass tile"><span class="tile-id">M{m}</span><span class="tile-title">{e(mb["title"])}</span>'
                  + (f'<span class="tile-sub">{e(mb["decision"])}</span>' if full else "") + "</li>")
    return f'<ol class="tiles{" full" if full else ""}" aria-label="Missions">{tiles}</ol>'


def mission_screen(w, i, full=False):
    b = BRIEF["worlds"][w]; c = CONTRACTS[f"{w}-M1"]; mb = BRIEF["missions"][f"{w}-M1"]
    bg, tone = SLAB[w]
    learn = (f'<dl class="more-facts"><div><dt>What you learn</dt><dd>{e(b["thesis"])}</dd></div>'
             f'<div><dt>Afterwards</dt><dd>{e(b["aftercare"])}</dd></div></dl>') if full else ""
    return f"""
<section class="table {tone}" id="{SLUG[w]}" style="--slab:{bg}" aria-labelledby="h-{SLUG[w]}">
  <div class="screen">
    <div class="glass brief">
      <p class="card-kicker"><span class="tag-num">0{i + 1}/05</span><span>{e(HAZARD[w])}</span></p>
      <h2 id="h-{SLUG[w]}">{e(b["name"])}</h2>
      <p class="brief-text">{e(b["identity"])}</p>
      <div class="first"><p class="first-label">First mission</p><p class="first-title">{e(mb["title"])}</p><p class="first-text">{e(mb["decision"])}</p></div>
      {learn}
    </div>
    <div class="board">
      {img(f"obj_{w.lower()}.webp", OBJ_ALT[w], "obj")}
      {toasts(w)}
      <figure class="pip glass">{img(f"det_{w.lower()}.webp", DETAIL[w], "det")}<figcaption>{tag("Camera")}<span>{e(DETAIL[w])}</span></figcaption></figure>
      <p class="render-tag">{e(b["name"])} · {e(c["title"])} · {RENDER}</p>
    </div>
    {mission_chips(w)}
    {loadout(w)}
    {plans(w) if full else ""}
    {mission_tiles(w, full)}
  </div>
</section>"""


def plate(file, n):
    alt, cap = PLATES[file]
    return f'<figure class="plate">{img(file, alt)}<figcaption class="glass">{tag(f"{n:02d}")}<span>{e(cap)}</span><span class="r">{RENDER}</span></figcaption></figure>'


def settings_card(rows, title="Settings"):
    items = "".join(f'<li class="set-row"><span class="set-name">{e(a)}</span><span class="set-vals">'
                    + "".join(f'<span class="seg{" on" if j == 0 else ""}">{e(v)}</span>' for j, v in enumerate(vals))
                    + f'</span><span class="set-note">{e(note)}</span></li>' for a, vals, note in rows)
    return f'<div class="glass settings"><p class="card-kicker">{e(title)}</p><ul>{items}</ul></div>'


SETTINGS = [
    ("Controls", ["Two hands", "One hand", "Hands-free"], "Every mission finishes with either hand alone, or with no hands: look at a control until it fills."),
    ("Text size", ["100%", "125%", "150%"], "Menus, warnings, the tool tray and the run controls all grow."),
    ("Pace", ["Standard", "Relaxed", "Command"], "Relaxed slows the hazards. Command steps time forward 1 or 5 seconds at a time."),
    ("Contrast", ["Glass", "High contrast"], "High contrast puts solid panels behind all text."),
    ("Effects", ["Full", "Reduced"], "Reduced stops flicker and pulsing without hiding any hazard."),
    ("Missions", ["In order", "Open play"], "Open play unlocks everything. Each world’s first mission is always open."),
]


def index():
    places = '{"low_dock":"Low Dock","east_crossing":"East crossing","west_crossing":"West crossing","road_junction":"Road junction","depot":"Depot"}'
    minis = ""
    for i, w in enumerate(WORLDS):
        b = BRIEF["worlds"][w]
        cells = "".join(f'<li><canvas data-valley="{w}-M{m}" data-delay="{(m - 1) * 120}" role="img" aria-label="{e(BRIEF["missions"][f"{w}-M{m}"]["title"])}: map of the mission"></canvas>'
                        f'<p class="mini-id">M{m}</p><p class="mini-title">{e(BRIEF["missions"][f"{w}-M{m}"]["title"])}</p></li>' for m in range(1, 6))
        minis += (f'<section class="world-row reveal" aria-labelledby="row-{SLUG[w]}"><header><p class="label">0{i + 1} · {e(HAZARD[w])}</p>'
                  f'<h3 id="row-{SLUG[w]}"><a href="worlds.html#{SLUG[w]}">{e(b["name"])}</a></h3></header><ol class="minis">{cells}</ol></section>')
    body = f"""
<section class="story" data-story="data/flood_w1m1.json" data-places='{places}' aria-labelledby="h-hero">
  <div class="stage">
    <canvas role="img" aria-label="First Crossing, the first mission, drawn as a model from its map file: river, reservoir, roads, depot and high landing, with the simulated flood."></canvas>
    <div class="beats">
      <div class="beat" data-step><p class="label">A mixed-reality rescue strategy game for Meta Quest 3 and 3S</p>
        <h1 id="h-hero">Small worlds.<br>Big saves.</h1>
        <p class="lede">A miniature valley appears on your real table. Scroll, and you're playing its first minutes.</p></div>
      <div class="beat" data-step><p class="label">1 · Read what's coming</p><h2>The reservoir is rising.</h2>
        <p class="lede">Every danger is announced before it arrives. Nothing is random, so a plan that works will work again.</p></div>
      <div class="beat" data-step><p class="label">2 · Place the plan</p><h2>One flood barrier. Three places it could go.</h2>
        <p class="lede">Pinch it from the tray and drop it on a glowing spot. Here: spot 3.</p></div>
      <div class="beat" data-step><p class="label">3 · Watch it play out</p><h2>Green stays dry because of you.</h2>
        <p class="lede">Red would flood instead. In the game, the forecast shows you this before you commit.</p></div>
    </div>
    <div class="hud" aria-live="polite"><p class="hud-clock"><span class="label">First Crossing</span><output data-clock>0:00</output></p><ul class="status" data-status></ul></div>
    <p class="scroll-cue" aria-hidden="true">Scroll to play</p>
  </div>
</section>
<section class="flood reveal" aria-labelledby="h-flood" data-flood="data/flood_w1m1.json" data-places='{places}' data-start="none">
  <div class="flood-head">
    <p class="label">Your turn</p>
    <h2 id="h-flood">Put the barrier somewhere else.</h2>
    <p>Every frame here is the output of RESQ's deterministic simulation for that choice (recorded with <code>lab webflood W1-M1</code>), sampled every 4 seconds. Nothing is animated by hand.</p>
  </div>
  <div class="flood-stage">
    <canvas role="img" aria-label="First Crossing with the simulated flood for the chosen barrier spot."></canvas>
    <div class="flood-ui">
      <div class="choices" role="group" aria-label="Where the barrier goes">
        <button type="button" data-scenario="none" aria-pressed="true">No barrier</button>
        <button type="button" data-scenario="barrier:01" aria-pressed="false">Spot 1</button>
        <button type="button" data-scenario="barrier:02" aria-pressed="false">Spot 2</button>
        <button type="button" data-scenario="barrier:03" aria-pressed="false">Spot 3</button>
      </div>
      <div class="timeline"><button type="button" class="play" data-play>Play</button>
        <label class="sr" for="scrub">Time</label><input id="scrub" type="range" min="0" value="0"><output data-clock>0:00</output></div>
      <ul class="status" data-status aria-live="polite"></ul>
      <p class="key"><span class="kv"><span class="k kept"></span>Kept dry by your barrier</span><span class="kv"><span class="k newly"></span>Flooded because of it</span></p>
    </div>
  </div>
</section>
<section class="campaign" aria-labelledby="h-camp">
  <div class="section-intro reveal"><p class="label">The whole campaign</p>
  <h2 id="h-camp">25 missions, drawn from the game's own map files.</h2>
  <p>Five valleys, five dangers. Each world's first mission is always open; Open play unlocks the rest.</p></div>
  {minis}
</section>
<section class="photos" aria-labelledby="h-photos">
  <div class="section-intro reveal"><p class="label">On your table</p><h2 id="h-photos">Small enough to lean over. Detailed enough to care about.</h2></div>
  <div class="photo-row">
    <figure class="ph wide reveal">{img("obj_hero.webp", OBJ_ALT["W1"], "", False)}<figcaption>Alder Valley as it sits on your table · {RENDER}</figcaption></figure>
    <figure class="ph reveal d1">{img("det_w2.webp", DETAIL["W2"])}<figcaption>{e(DETAIL["W2"])} · {RENDER}</figcaption></figure>
    <figure class="ph reveal d2">{img("det_w1.webp", DETAIL["W1"])}<figcaption>{e(DETAIL["W1"])} · {RENDER}</figcaption></figure>
  </div>
</section>
<section class="hands" aria-labelledby="h-hands">
  <div class="section-intro reveal"><p class="label">Hands first</p><h2 id="h-hands">Two hands, one hand, or none at all.</h2></div>
  <ul class="ways reveal">
    <li><b>Pinch and place.</b> Tools come from a tray at the near edge of the board; drop them on glowing spots.</li>
    <li><b>Either hand alone.</b> Every mission can be finished one-handed, left or right.</li>
    <li><b>Hands-free.</b> Look at a control until it fills, and step time forward one or five seconds at a time.</li>
    <li><b>Seated, two-foot reach.</b> Text to 150%, high contrast, calmer visuals and a relaxed pace are in Settings.</li>
  </ul>
  <p><a class="btn" href="how-to-play.html">How it plays</a> <a class="btn ghost" href="accessibility.html">Accessibility</a></p>
</section>"""
    page("index.html", "RESQ: small worlds, big saves", body,
         "RESQ is a mixed-reality rescue strategy game for Meta Quest 3 and 3S: a miniature valley on your real table, and every resident to bring home.")


def room_head(title, text, kicker):
    return f'<section class="room page-top"><div class="room-head glass"><p class="card-kicker">{e(kicker)}</p><h1>{e(title)}</h1><p class="room-text">{e(text)}</p></div></section>'


def worlds():
    jump = "".join(f'<li><a href="#{SLUG[w]}">0{i + 1} {e(BRIEF["worlds"][w]["name"])}</a></li>' for i, w in enumerate(WORLDS))
    chapters = ""
    for i, w in enumerate(WORLDS):
        b = BRIEF["worlds"][w]; c = CONTRACTS[f"{w}-M1"]; mb = BRIEF["missions"][f"{w}-M1"]
        lo = ", ".join(f"{TOOL[k]} ×{n}" for k, n in c["default_loadout"].items())
        threats = "".join(f'<li><span class="t">{clock(t["at"])}</span>{e(t["text"])}</li>' for t in mb["threats"])
        minis = "".join(f'<li><canvas data-valley="{w}-M{m}" role="img" aria-label="Map of {e(BRIEF["missions"][f"{w}-M{m}"]["title"])}"></canvas>'
                        f'<p class="mini-id">M{m}</p><p class="mini-title">{e(BRIEF["missions"][f"{w}-M{m}"]["title"])}</p>'
                        f'<p class="mini-sub">{e(BRIEF["missions"][f"{w}-M{m}"]["decision"])}</p></li>' for m in range(1, 6))
        chapters += f"""
<section class="chapter" id="{SLUG[w]}" aria-labelledby="h-{SLUG[w]}" style="--accent:{SLAB[w][0]}">
  <div class="chapter-head">
    <p class="label">0{i + 1} / 05 · {e(HAZARD[w])}</p>
    <h2 id="h-{SLUG[w]}">{e(b["name"])}</h2>
    <p class="lede">{e(b["identity"])}</p>
  </div>
  <figure class="ph chapter-photo">{img(f"obj_{w.lower()}.webp", OBJ_ALT[w])}<figcaption>{e(b["name"])} · {RENDER}</figcaption></figure>
  <div class="chapter-facts">
    <div><h3 class="label">First mission · {e(mb["title"])} · {clock(c["duration_scenario_ticks"] // 10)}</h3>
      <ol class="threats">{threats}</ol>
      <p class="fact"><b>{c["total_residents"]} residents</b>, {sum(g["mobility_counts"].get("step_free", 0) for g in c["groups"])} step-free · <b>{c["supply_budget"]} supply</b> · {e(lo)}</p></div>
    <div><h3 class="label">Two ways to win</h3><p class="plan"><b>A</b> {e(c["reference_strategy_briefs"]["A"])}</p><p class="plan"><b>B</b> {e(c["reference_strategy_briefs"]["B"])}</p></div>
    <div><h3 class="label">What you learn</h3><p>{e(b["thesis"])}</p><h3 class="label">Afterwards</h3><p>{e(b["aftercare"])}</p></div>
  </div>
  <ol class="minis big">{minis}</ol>
</section>"""
    body = f"""
<section class="intro small"><p class="label">Mission select</p><h1>Five worlds</h1>
  <p class="lede">Each valley teaches one kind of danger, and later missions combine them. Every map, number and timing below comes from the game's mission files.</p>
  <ul class="jump">{jump}</ul></section>
{chapters}"""
    page("worlds.html", "Worlds · RESQ", body, "The five valleys of RESQ and their 25 missions.")


def how_to_play():
    steps = [
        ("plan.jpg", "Read the warnings, test an idea", "The card behind the board lists who needs help and what’s coming. Pinch a tool and hover it over a glowing spot: the forecast shows what that placement changes over the next minute. The clock doesn’t start until you press Start."),
        ("act.jpg", "Place, route, adjust", "Pinch or poke a tool card, then a spot on the board; or pinch, drag and let go. To move people, pick a group and then a destination. For a boat, bus, crawler or winch, pick the vehicle, the group, then the drop-off. Tap a placed pump, generator or sprinkler to switch it."),
        ("rescue.jpg", "See what happened", "Results show who reached safety and which goals you met. The debrief names the moment that mattered, and you can try the other plan."),
        ("menu.jpg", "Pause any time", "The Menu control is always on the run controls, and pausing freezes everything. In hands-free play, look at a control until it fills. If your hands leave view mid-rescue, the game pauses and waits."),
    ]
    items = "".join(f'<li>{plate(f, n + 1)}<div class="plate-text"><h3>{e(t)}</h3><p>{e(p)}</p></div></li>' for n, (f, t, p) in enumerate(steps))
    body = (room_head("How it plays", "RESQ finds a real table in your room and fits the board to it: wide on a big table, compact on a small one, or a virtual table if there’s none. It stays where you left it next time.", "Briefing")
            + f'<section class="room"><ol class="plates">{items}</ol></section>')
    page("how-to-play.html", "How it plays · RESQ", body, "Placing the board, reading warnings, placing equipment and routing residents in RESQ.")


def accessibility():
    extra = [("Hands-free details", "Quest 3 and 3S have no eye tracking, so hands-free follows your head, not your eyes. Hold your gaze 0.5 to 1.5 seconds, your choice."),
             ("Sound", "Warnings, blocked actions and backup calls have their own volume, so they stay clear when music is low. Every warning is also shown as text; there’s no spoken dialogue to miss."),
             ("Seated", "Designed for seated play at a table. No movement around the room is needed. Restart and leaving a mission always ask first.")]
    rows = "".join(f'<div><dt>{e(a)}</dt><dd>{e(b)}</dd></div>' for a, b in extra)
    body = (room_head("Nobody needs two steady hands to bring everyone home.", "These options are in the game’s Settings card and are remembered between sessions.", "Access")
            + f'<section class="room dark-room">{settings_card(SETTINGS)}<div class="glass notes"><dl>{rows}</dl>'
            f'<p class="fine">{e(STATUS)} These features are tested automatically in the development build. Testing with people and on headsets happens in November; this page will say what that finds.</p></div></section>')
    page("accessibility.html", "Accessibility · RESQ", body, "One-hand, hands-free, text size, contrast, pace and other accessibility options in RESQ.")


def press():
    facts = [("Title", "RESQ"), ("Developer", "Amir Lotfy"), ("Platform", "Meta Quest 3 and Quest 3S (mixed reality)"),
             ("Genre", "Rescue strategy / puzzle"), ("Status", STATUS), ("Content", "5 worlds, 25 missions, 30 challenge variants"),
             ("Input", "Hand tracking (two hands or one), head-gaze dwell; no controllers needed"),
             ("Made with", "Unity 6, Meta XR SDK (Core, Interaction, MR Utility Kit), Blender")]
    rows = "".join(f"<div><dt>{e(a)}</dt><dd>{e(b)}</dd></div>" for a, b in facts)
    files = ([(f"obj_{w.lower()}.webp", BRIEF["worlds"][w]["name"] + ", cut-out") for w in WORLDS]
             + [(f"det_{w.lower()}.webp", BRIEF["worlds"][w]["name"] + ", detail") for w in WORLDS] + [(f, PLATES[f][1]) for f in PLATES])
    tiles = "".join(f'<li><a class="glass press-tile" href="media/{f}" download>{img(f, c, "press-img")}<span>{e(c)}</span></a></li>' for f, c in files)
    body = (room_head("Press", "RESQ turns your table into a small world in trouble. Each valley faces one danger it can see coming: a flood, a wildfire, an avalanche, a turning tide, a lava flow. You place barriers, bridges, pumps and boats with your hands, route each group to safety, and watch the plan play out. Residents can’t be picked up and carried; the only way to save them is a good plan.", "Fact sheet")
            + f'<section class="room"><div class="glass notes"><dl>{rows}</dl></div>'
            f'<h2 class="room-title">Images</h2><p class="room-text">Renders of the development build in the Unity Editor. Cut-outs have transparent backgrounds. A headset trailer follows after device testing.</p>'
            f'<ul class="press-grid">{tiles}</ul></section>')
    page("press.html", "Press · RESQ", body, "Fact sheet and images for RESQ.")


def doc_page(name, title, intro, sections, desc):
    toc = "".join(f'<li><a class="btn ghost" href="#{i}">{e(t)}</a></li>' for i, t, _ in sections)
    arts = "".join(f'<section class="glass doc-card" id="{i}"><h2>{e(t)}</h2>{b}</section>' for i, t, b in sections)
    body = (room_head(title, intro, "Help").replace("</div></section>", f'<ul class="jump">{toc}</ul></div></section>')
            + f'<section class="room docs">{arts}<p class="fine">{e(STATUS)}</p></section>')
    page(name, f"{title} · RESQ", body, desc)


def support():
    doc_page("support.html", "Support", "Setting up the board, controls, saves, and what to do when a mission feels impossible.", [
        ("setup", "Setting up the board", "<p>RESQ asks to use your room’s surfaces only so it can place the board on a table. If you say no, or there’s no table that fits, choose the virtual table: the missions are the same. To move the board later, open the Menu and choose Move the board.</p><p>If the board seems to drift or jump, the headset may have lost track of the room. RESQ pauses and offers to find the table again or switch to the virtual table.</p>"),
        ("inputs", "Hands, one hand and hands-free", "<p>Pinch with your thumb and index finger, or poke a card with your fingertip. Hands-free: in Settings, set Controls to hands-free; look at a control until it fills. If your hands leave view during a rescue, the game pauses until you press Resume.</p>"),
        ("saves", "Progress and saves", "<p>Progress and settings are saved on the headset after every mission. If the save can’t be read, RESQ offers your last good backup and never erases anything without asking. Reset progress in Settings clears stars and unlocks but keeps your settings.</p>"),
        ("trouble", "Something went wrong", "<p>If a mission feels impossible, open the briefing again: it lists who needs help and what’s coming. After a failed run, the debrief shows the moment that mattered. Every mission has been won by two different reference plans on both board sizes.</p>"),
    ], "Help with setting up RESQ, inputs, saves and troubleshooting.")


def privacy():
    doc_page("privacy.html", "Privacy", "RESQ keeps everything on your headset and has no internet access.", [
        ("data", "What stays on the headset", "<p>RESQ saves your progress, settings and any postcards you take in its own storage on the headset. There are no accounts, no sign-in and no analytics.</p>"),
        ("room", "Your room", "<p>If you allow it, RESQ reads where tables are in your room (from your headset’s Space Setup) to place the board, and saves one spatial anchor so the board returns to the same spot. It never records or stores camera images.</p>"),
        ("network", "Network", "<p>The app has no internet permission: it can’t send anything anywhere. This website uses no cookies, trackers or analytics.</p>"),
    ], "RESQ keeps your data on the headset and has no internet access.")


def not_found():
    body = """
<section class="intro small lost"><p class="label">404</p><h1>This page is under water.</h1>
  <p class="lede">It may have moved, or it never made it out of the valley.</p>
  <p><a class="btn" href="index.html">Back to RESQ</a></p>
  <canvas class="lost-map" data-valley="W1-M1" role="img" aria-label="Map of First Crossing"></canvas></section>"""
    page("404.html", "Not found · RESQ", body, "Page not found.")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    index(); worlds(); how_to_play(); accessibility(); press(); support(); privacy(); not_found()
    used = {f"obj_{w.lower()}.webp" for w in WORLDS} | {f"det_{w.lower()}.webp" for w in WORLDS} | {"obj_hero.webp", "icon.png"} | set(PLATES)
    missing = sorted(f for f in used if not (MEDIA_DIR / f).exists())
    unused = sorted(p.name for p in MEDIA_DIR.iterdir() if p.name not in used)
    print("site written to", OUT, "| missing media:", ", ".join(missing) or "none", "| unused media:", ", ".join(unused) or "none")
