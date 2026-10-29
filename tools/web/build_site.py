#!/usr/bin/env python3
"""Builds the static RESQ website into web/app (plain HTML + one stylesheet; no framework, no build step to deploy).

Design: "Collectible Worlds" (DECISIONS.md, 29 Oct 2026). Each valley is shown as an object (a cut-out render of the
running game, tools/web/capture_objects.sh) on its own full-bleed colour slab; the object breaks up through the top
edge of its slab. One family (Atkinson Hyperlegible), square edges, flat surfaces, one pill CTA in RESQ orange,
yellow pills for status only. Real UI appears as uncropped 16:9 plates. Every image carries a provenance label.

World and mission text comes from content/briefings.json (the world bible), so the site says exactly what the game
contains; nothing is AI-generated. Status lines are conservative: in development, validated by automated replays,
nothing claimed about headsets until device testing has happened.
    python3 tools/web/build_site.py
"""
import hashlib, html, json, pathlib, struct

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "web/app"
MEDIA_DIR = OUT / "media"
BRIEF = json.loads((ROOT / "content/briefings.json").read_text())
WORLDS = ["W1", "W2", "W3", "W4", "W5"]
SLUG = {"W1": "alder-valley", "W2": "emberwood", "W3": "frostline", "W4": "tidemark", "W5": "cinder-reach"}
HAZARD = {"W1": "Floods", "W2": "Wildfire", "W3": "Snow and avalanches", "W4": "Tides and storm power", "W5": "Lava and heat"}
# slab colour and its text tone (contrast checked by tools/web/check_contrast.py)
SLAB = {"W1": ("#2357C4", "light"), "W2": ("#B8361B", "light"), "W3": ("#DCEAF2", "dark"),
        "W4": ("#F2BE22", "dark"), "W5": ("#1C1A19", "light")}

# Status is a single source of truth for every page: change it here when the evidence changes.
STATUS = "In development for the Meta VR Start Developer Competition 2026. Not yet released."
STATUS_PILL = "In development · Meta Quest 3 and 3S"
RENDER = "Editor render"   # every image is the development build rendered in the Unity Editor, not headset footage

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

NAV = [("worlds.html", "Worlds"), ("how-to-play.html", "How it plays"), ("accessibility.html", "Accessibility"), ("press.html", "Press")]
FOOT = [("worlds.html", "Worlds"), ("how-to-play.html", "How it plays"), ("accessibility.html", "Accessibility"),
        ("press.html", "Press"), ("support.html", "Support"), ("privacy.html", "Privacy")]

RING = ('<svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="13" fill="#FF6B35"/>'
        '<circle cx="16" cy="16" r="7" fill="#fff"/><g fill="#fff"><rect x="14" y="2" width="4" height="7"/>'
        '<rect x="14" y="23" width="4" height="7"/><rect x="2" y="14" width="7" height="4"/><rect x="23" y="14" width="7" height="4"/></g></svg>')


def e(s):
    return html.escape(s, quote=True)


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


def label(*parts):
    return '<p class="plate">' + "<span>" + "</span><span>".join(e(p) for p in parts) + "</span></p>"


def page(name, title, body, desc, theme=""):
    CSS_V = hashlib.sha1((OUT / "site.css").read_bytes()).hexdigest()[:8]   # new URL whenever the styles change
    links = "".join(f'<li><a href="{h}"{" aria-current=\"page\"" if h == name else ""}>{e(t)}</a></li>' for h, t in NAV)
    foot = "".join(f'<li><a href="{h}">{e(t)}</a></li>' for h, t in FOOT)
    doc = f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{e(title)}</title>
<meta name="description" content="{e(desc)}">
<meta name="theme-color" content="#ffffff">
<link rel="icon" href="media/icon.png">
<link rel="preload" href="fonts/AtkinsonHyperlegible-Bold.ttf" as="font" type="font/ttf" crossorigin>
<link rel="stylesheet" href="site.css?v={CSS_V}">
</head>
<body class="{theme}">
<a class="skip" href="#main">Skip to content</a>
<header class="bar">
  <a class="mark" href="index.html" aria-label="RESQ home">{RING}<span>RESQ</span></a>
  <nav aria-label="Main"><ul class="links">{links}</ul></nav>
  <a class="pill cta" href="worlds.html">Meet the worlds</a>
  <details class="menu"><summary>Menu</summary><ul>{links}<li><a href="worlds.html">Meet the worlds</a></li></ul></details>
</header>
<main id="main">
{body}
</main>
<footer class="foot">
  <div class="foot-mark">{RING}<span>RESQ</span></div>
  <p class="foot-status">{e(STATUS)}</p>
  <nav aria-label="Footer"><ul>{foot}</ul></nav>
  <p class="foot-note">Every image on this site is the development build rendered in the Unity Editor (October 2026), labelled “{RENDER}”. None of it is headset footage, and none of it is AI-generated. Made by Frameless.</p>
</footer>
</body>
</html>
"""
    (OUT / name).write_text(doc)


def mission_list(w, detail=False):
    rows = ""
    for m in range(1, 6):
        mid = f"{w}-M{m}"
        mb = BRIEF["missions"][mid]
        rows += (f'<li><span class="mid">{mid}</span><span class="mt">{e(mb["title"])}</span>'
                 + (f'<span class="md">{e(mb["decision"])}</span>' if detail else "") + "</li>")
    return f'<ol class="missions{" full" if detail else ""}">{rows}</ol>'


def world_slab(w, i, full=False):
    b = BRIEF["worlds"][w]
    bg, tone = SLAB[w]
    extra = ""
    if full:
        extra = f"""
      <dl class="facts-inline">
        <div><dt>What you learn</dt><dd>{e(b["thesis"])}</dd></div>
        <div><dt>Afterwards</dt><dd>{e(b["aftercare"])}</dd></div>
      </dl>
      <h3 class="small-caps">Missions</h3>{mission_list(w, True)}
      <p class="fine">Playable in the development build and validated by automated replays of two different winning plans. Not yet tested on a headset.</p>"""
    else:
        extra = f'{mission_list(w)}<p><a class="more" href="worlds.html#{SLUG[w]}">More about {e(b["name"])}</a></p>'
    return f"""
<section class="world {tone}{" flip" if i % 2 else ""}" id="{SLUG[w]}" style="--slab:{bg}" aria-labelledby="h-{SLUG[w]}">
  <div class="world-obj">{img(f"obj_{w.lower()}.webp", OBJ_ALT[w], "obj", eager=(i == 0 and full))}
    {label(f"0{i + 1} / 05", b["name"], RENDER)}</div>
  <div class="world-text">
    <p class="kicker"><span class="num">0{i + 1}</span>{e(HAZARD[w])}</p>
    <h2 id="h-{SLUG[w]}">{e(b["name"])}</h2>
    <p class="identity">{e(b["identity"])}</p>{extra}
  </div>
  <figure class="world-detail">{img(f"det_{w.lower()}.webp", DETAIL[w], "det")}
    <figcaption>{label("Detail", DETAIL[w], RENDER)}</figcaption></figure>
</section>"""


def plate(file, n):
    alt, cap = PLATES[file]
    return f'<figure class="ui-plate">{img(file, alt)}<figcaption>{label(f"{n:02d}", cap, RENDER)}</figcaption></figure>'


def index():
    worlds = "".join(world_slab(w, i) for i, w in enumerate(WORLDS))
    body = f"""
<section class="hero" aria-labelledby="h-hero">
  <div class="hero-text">
    <p class="status">{e(STATUS_PILL)}</p>
    <h1 id="h-hero">Small worlds.<br>Big saves.</h1>
    <p class="lede">A mixed-reality rescue strategy game. A miniature valley appears on your real table; you read the danger that’s coming, then use your hands to bring every resident home.</p>
    <p class="hero-actions"><a class="pill cta big" href="#alder-valley">Meet the five worlds</a><a class="text-link" href="how-to-play.html">How it plays</a></p>
  </div>
  <div class="hero-obj">{img("obj_hero.webp", OBJ_ALT["W1"], "obj", eager=True)}
    {label("Alder Valley", "First Crossing, one minute in", RENDER)}</div>
</section>
{worlds}
<section class="plays" aria-labelledby="h-plays">
  <div class="section-head"><p class="kicker">How a rescue goes</p><h2 id="h-plays">See it coming.<br>Then make the move.</h2></div>
  <ol class="steps">
    <li>{plate("plan.jpg", 1)}<h3>Test an idea before you spend it</h3><p>Hold a tool over the board and the real simulation runs ahead: green cells stay dry, red ones would flood.</p></li>
    <li>{plate("act.jpg", 2)}<h3>Your hands give the orders</h3><p>Pinch a tool and drop it on a glowing spot. Pick a group, then where they go. Nobody can be picked up and carried: only the plan moves people.</p></li>
    <li>{plate("rescue.jpg", 3)}<h3>Find out why it worked</h3><p>Results show who reached safety, and a short debrief names the moment that mattered.</p></li>
    <li>{plate("menu.jpg", 4)}<h3>Then find another way</h3><p>Every mission has two quite different winning plans, and 30 challenges change the rules once you know them.</p></li>
  </ol>
</section>
<section class="access" aria-labelledby="h-access">
  <div class="section-head"><p class="kicker">Ways to play</p><h2 id="h-access">Two hands, one hand, or none at all.</h2></div>
  <ul class="ways">
    <li><strong>Either hand alone.</strong> Every mission finishes one-handed, left or right.</li>
    <li><strong>Hands-free.</strong> Look at a control until it fills; step time forward one or five seconds at a time.</li>
    <li><strong>Text to 150%.</strong> Menus, warnings, the tool tray and controls all grow.</li>
    <li><strong>High contrast and calmer visuals.</strong> Solid panels behind text; no flicker or pulsing.</li>
    <li><strong>Seated, at your own pace.</strong> Relaxed rules slow the hazards. Open play unlocks every mission.</li>
  </ul>
  <p><a class="more" href="accessibility.html">Accessibility in RESQ</a></p>
</section>
<section class="numbers" aria-label="RESQ in numbers">
  <dl>
    <div><dt>Worlds</dt><dd>5</dd></div>
    <div><dt>Missions</dt><dd>25</dd></div>
    <div><dt>Challenges</dt><dd>30</dd></div>
    <div><dt>Winning plans per mission</dt><dd>2</dd></div>
    <div><dt>Accounts, ads, trackers</dt><dd>0</dd></div>
  </dl>
  <p class="fine">{e(STATUS)} Every mission is validated by automated replays of both winning plans on both board sizes; headset testing comes in November.</p>
</section>"""
    page("index.html", "RESQ: small worlds, big saves", body,
         "RESQ is a mixed-reality rescue strategy game for Meta Quest 3 and 3S: a miniature valley on your real table, and every resident to bring home.")


def worlds():
    body = f"""
<section class="page-head"><p class="status">{e(STATUS_PILL)}</p><h1>Five worlds</h1>
  <p class="lede">Each valley teaches one kind of danger. Later missions combine what you’ve learned.</p>
  <nav class="jump" aria-label="Worlds"><ul>{"".join(f'<li><a href="#{SLUG[w]}"><span class="num">0{i + 1}</span>{e(BRIEF["worlds"][w]["name"])}</a></li>' for i, w in enumerate(WORLDS))}</ul></nav>
</section>
{"".join(world_slab(w, i, full=True) for i, w in enumerate(WORLDS))}"""
    page("worlds.html", "Worlds · RESQ", body, "The five valleys of RESQ and their 25 missions.")


def how_to_play():
    steps = [
        ("plan.jpg", "Put the valley on a table", "RESQ looks for a real table in your room and fits the board to it: a wide board on a big table, a compact one on a small table. No table nearby? Choose the virtual table. Raise, lower or turn it until the near edge is in easy reach; it stays where you left it next time."),
        ("plan.jpg", "Read the warnings, test an idea", "The card behind the board lists who needs help and what’s coming. Pinch a tool and hover it over a glowing spot: the forecast shows what that placement changes over the next minute. The clock doesn’t start until you press Start."),
        ("act.jpg", "Place, route, adjust", "Pinch or poke a tool card, then a spot on the board; or pinch, drag and let go. To move people, pick a group and then a destination. For a boat, bus, crawler or winch, pick the vehicle, the group, then the drop-off. Tap a placed pump, generator or sprinkler to switch it."),
        ("menu.jpg", "Pause any time", "The Menu control is always on the run controls, and pausing freezes everything. In hands-free play, look at a control until it fills; you can step time forward one or five seconds instead of letting it run. If your hands leave view mid-rescue, the game pauses and waits."),
    ]
    items = "".join(f'<li>{plate(f, n + 1)}<h3>{e(t)}</h3><p>{e(p)}</p></li>' for n, (f, t, p) in enumerate(steps[1:]))
    body = f"""
<section class="page-head"><p class="status">{e(STATUS_PILL)}</p><h1>How it plays</h1>
  <p class="lede">{e(steps[0][2])}</p></section>
<section class="plays tight" aria-label="Steps"><ol class="steps">{items}</ol></section>"""
    page("how-to-play.html", "How it plays · RESQ", body, "Placing the board, reading warnings, placing equipment and routing residents in RESQ.")


def accessibility():
    rows = [
        ("Either hand alone", "Every mission can be finished with one hand, left or right. The run controls can mirror to the left side."),
        ("Hands-free", "Look at a control and hold your gaze until it fills (0.5 to 1.5 seconds, your choice). Quest 3 and 3S have no eye tracking, so this follows your head, not your eyes."),
        ("Your own pace", "Relaxed pace slows the hazards. Hands-free play adds +1 s and +5 s steps, so you never have to act in real time. Restart and leaving a mission always ask first."),
        ("Text and contrast", "Text at 100, 125 or 150 percent; the tool tray and controls grow with it. High contrast puts solid panels behind all text."),
        ("Calmer visuals", "Reduced effects stops flicker and pulsing without hiding any hazard."),
        ("Sound", "Warnings, blocked actions and backup calls have their own volume, so they stay clear when music is low. Every warning is also shown as text; there’s no spoken dialogue to miss."),
        ("Open play", "Unlock every mission from the start. Each world’s first mission is always open."),
        ("Seated", "Designed for seated play at a table. No movement around the room is needed."),
    ]
    trs = "".join(f"<div><dt>{e(a)}</dt><dd>{e(b)}</dd></div>" for a, b in rows)
    body = f"""
<section class="page-head dark"><p class="status">{e(STATUS_PILL)}</p><h1>Nobody needs two steady hands to bring everyone home.</h1>
  <p class="lede">These options are in Settings and are remembered between sessions.</p></section>
<section class="rows-wrap"><dl class="rows">{trs}</dl>
<p class="fine">{e(STATUS)} These features are tested automatically in the development build. Testing with people and on headsets happens in November; this page will say what that finds.</p></section>"""
    page("accessibility.html", "Accessibility · RESQ", body, "One-hand, hands-free, text size, contrast, pace and other accessibility options in RESQ.")


def press():
    facts = [("Title", "RESQ"), ("Developer", "Frameless"), ("Platform", "Meta Quest 3 and Quest 3S (mixed reality)"),
             ("Genre", "Rescue strategy / puzzle"), ("Status", STATUS),
             ("Content", "5 worlds, 25 missions, 30 challenge variants"),
             ("Input", "Hand tracking (two hands or one), head-gaze dwell; no controllers needed"),
             ("Made with", "Unity 6, Meta XR SDK (Core, Interaction, MR Utility Kit), Blender")]
    trs = "".join(f"<div><dt>{e(a)}</dt><dd>{e(b)}</dd></div>" for a, b in facts)
    files = [(f"obj_{w.lower()}.webp", BRIEF["worlds"][w]["name"] + ", cut-out") for w in WORLDS] + \
            [(f"det_{w.lower()}.webp", BRIEF["worlds"][w]["name"] + ", detail") for w in WORLDS] + \
            [(f, PLATES[f][1]) for f in PLATES]
    tiles = "".join(f'<li><a href="media/{f}" download>{img(f, c, "press-img")}<span>{e(c)}</span></a></li>' for f, c in files)
    body = f"""
<section class="page-head"><h1>Press</h1>
  <p class="lede">RESQ turns your table into a small world in trouble. Each valley faces one danger it can see coming: a flood, a wildfire, an avalanche, a turning tide, a lava flow. You place barriers, bridges, pumps and boats with your hands, route each group to safety, and watch the plan play out. Residents can’t be picked up and carried; the only way to save them is a good plan.</p></section>
<section class="rows-wrap"><dl class="rows">{trs}</dl></section>
<section class="press-images" aria-labelledby="h-img"><h2 id="h-img">Images</h2>
  <p class="fine">Renders of the development build in the Unity Editor. Cut-outs have transparent backgrounds. A headset trailer follows after device testing.</p>
  <ul class="press-grid">{tiles}</ul></section>"""
    page("press.html", "Press · RESQ", body, "Fact sheet and images for RESQ.")


def doc_page(name, title, intro, sections, desc):
    toc = "".join(f'<li><a href="#{i}">{e(t)}</a></li>' for i, t, _ in sections)
    arts = "".join(f'<section id="{i}"><h2>{e(t)}</h2>{b}</section>' for i, t, b in sections)
    body = f"""
<section class="page-head"><h1>{e(title)}</h1><p class="lede">{e(intro)}</p></section>
<div class="doc"><nav aria-label="{e(title)} topics"><ol>{toc}</ol></nav><article>{arts}<p class="fine">{e(STATUS)}</p></article></div>"""
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
    body = f"""
<section class="world light lost" style="--slab:{SLAB['W1'][0]}">
  <div class="world-text"><p class="kicker">404</p><h1>This page floated off the table.</h1>
  <p><a class="pill cta big" href="index.html">Back to RESQ</a></p></div>
  <div class="world-obj">{img("obj_w1.webp", "", "obj")}</div>
</section>"""
    page("404.html", "Not found · RESQ", body, "Page not found.")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    index(); worlds(); how_to_play(); accessibility(); press(); support(); privacy(); not_found()
    used = {f"obj_{w.lower()}.webp" for w in WORLDS} | {f"det_{w.lower()}.webp" for w in WORLDS} | {"obj_hero.webp", "icon.png"} | set(PLATES)
    missing = sorted(f for f in used if not (MEDIA_DIR / f).exists())
    unused = sorted(p.name for p in MEDIA_DIR.iterdir() if p.name not in used)
    print("site written to", OUT, "| missing media:", ", ".join(missing) or "none", "| unused media:", ", ".join(unused) or "none")
