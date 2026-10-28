#!/usr/bin/env python3
"""Builds the static RESQ website into web/app (plain HTML + one stylesheet; no framework, no build step to deploy).

World and mission text comes from content/briefings.json (the world bible) and the mission contracts, so the site
says exactly what the game contains. Media are real RESQ captures listed in MEDIA with an honest provenance caption;
nothing is AI-generated. Status lines are deliberately conservative: the game is in development, every mission is
validated by automated replays, and nothing is claimed about headsets until device testing has happened.
    python3 tools/web/build_site.py
"""
import html, json, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "web/app"
BRIEF = json.loads((ROOT / "content/briefings.json").read_text())
CONTRACTS = {m["id"]: m for m in json.loads((ROOT / "specification/content/mission_contracts.json").read_text())["missions"]}
WORLDS = ["W1", "W2", "W3", "W4", "W5"]
SLUG = {"W1": "alder-valley", "W2": "emberwood", "W3": "frostline", "W4": "tidemark", "W5": "cinder-reach"}
HAZARD = {"W1": "Floods", "W2": "Wildfire", "W3": "Snow, avalanches and failing supports", "W4": "Tides and storm power", "W5": "Lava and heat"}

# Status is a single source of truth for every page: change it here when the evidence changes.
STATUS = "In development for the Meta VR Start Developer Competition 2026. Not yet released."
CAPTURE = "Development build, captured in the Unity Editor (October 2026). Not headset footage."

# media/<file>: (alt text, caption). Files are produced by tools/web/capture.sh from the running game.
MEDIA = {
    "hero.jpg": ("A miniature river valley on a wooden board: a bus waits by a flooded crossing while residents gather at a dock.",
                 "Alder Valley, First Crossing. " + CAPTURE),
    "w1.jpg": ("Alder Valley diorama with a river, cedar houses and a rescue depot.", "Alder Valley. " + CAPTURE),
    "w2.jpg": ("Emberwood diorama: pine forest, a ranger station and a fire line.", "Emberwood. " + CAPTURE),
    "w3.jpg": ("Frostline diorama: snowy terraces, a snowcat and a cable station.", "Frostline. " + CAPTURE),
    "w4.jpg": ("Tidemark diorama: harbour houses, a causeway and a lighthouse.", "Tidemark. " + CAPTURE),
    "w5.jpg": ("Cinder Reach diorama: dark basalt slopes and a lava channel.", "Cinder Reach. " + CAPTURE),
    "plan.jpg": ("A tool hovering over the board shows a forecast: green cells stay dry, red cells would flood.", "Forecast preview while placing a barrier. " + CAPTURE),
    "act.jpg": ("A rescue boat crosses the river while residents wait at a dock.", "Routing a boat to a waiting group. " + CAPTURE),
    "rescue.jpg": ("A glass card above the board reads Rescue complete, with each goal marked Done.", "Results after a rescue. " + CAPTURE),
    "menu.jpg": ("A glass card above the board lists missions for one world.", "Choosing a mission. " + CAPTURE),
}

NAV = [("how-to-play.html", "How to play"), ("worlds.html", "Worlds"), ("accessibility.html", "Accessibility"), ("press.html", "Press")]
FOOT = [("support.html", "Support"), ("accessibility.html", "Accessibility"), ("privacy.html", "Privacy"), ("press.html", "Press")]

RING = ('<svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="13" fill="#FF6B35"/>'
        '<circle cx="16" cy="16" r="7" fill="#F3EBDD"/><g fill="#F3EBDD"><rect x="14" y="2" width="4" height="7"/>'
        '<rect x="14" y="23" width="4" height="7"/><rect x="2" y="14" width="7" height="4"/><rect x="23" y="14" width="7" height="4"/></g></svg>')


def e(s):
    return html.escape(s, quote=True)


def page(name, title, body, desc):
    links = "".join(f'<li><a href="{h}"{" aria-current=\"page\"" if h == name else ""}>{e(t)}</a></li>' for h, t in NAV)
    foot = "".join(f'<li><a href="{h}">{e(t)}</a></li>' for h, t in FOOT)
    doc = f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{e(title)}</title>
<meta name="description" content="{e(desc)}">
<link rel="icon" href="media/icon.png">
<link rel="stylesheet" href="site.css">
</head>
<body>
<a class="skip" href="#main">Skip to content</a>
<header class="site-head"><div class="wrap">
  <a class="wordmark" href="index.html">{RING}RESQ</a>
  <nav class="nav" aria-label="Main"><ul>{links}</ul></nav>
  <details class="menu"><summary>Menu</summary><ul>{links}</ul></details>
</div></header>
<main id="main">
{body}
</main>
<footer class="site-foot"><div class="wrap">
  <div><strong>RESQ</strong><br><span class="note">{e(STATUS)}</span><br><span class="note">Made by Frameless.</span></div>
  <nav aria-label="Footer"><ul>{foot}</ul></nav>
</div></footer>
</body>
</html>
"""
    (OUT / name).write_text(doc)


def fig(file, extra_class=""):
    alt, cap = MEDIA[file]
    return f'<figure class="{extra_class}"><img src="media/{file}" alt="{e(alt)}" loading="lazy" width="1600" height="900"><figcaption>{e(cap)}</figcaption></figure>'


def missions(w):
    items = "".join(f"<li>{e(BRIEF['missions'][f'{w}-M{m}']['title'])}</li>" for m in range(1, 6))
    return f'<ol class="missions">{items}</ol>'


def index():
    alt, cap = MEDIA["hero.jpg"]
    worlds = "".join(
        f'<li><a href="worlds.html#{SLUG[w]}"><strong>{e(BRIEF["worlds"][w]["name"])}</strong></a> — {e(HAZARD[w])}.</li>' for w in WORLDS)
    body = f"""
<div class="hero-media"><img src="media/hero.jpg" alt="{e(alt)}" width="1920" height="1080"></div>
<div class="hero-rail"><div class="wrap">
  <div class="lead">
    <span class="status">{e(STATUS)}</span>
    <h1>Small worlds. Big saves.</h1>
    <p>RESQ is a mixed-reality strategy game for Meta Quest 3 and 3S. A miniature valley appears on your real table; you read the flood, fire, snow, tide or lava that's coming, then use your hands to place equipment and route every resident to safety.</p>
    <p class="note">{e(cap)}</p>
  </div>
  <div class="actions">
    <a class="btn primary" href="worlds.html">See the five worlds</a>
    <a class="btn secondary" href="how-to-play.html">How to play</a>
  </div>
</div></div>

<section class="block"><div class="wrap">
  <p class="eyebrow">Read. Plan. Act. Rescue.</p>
  <h2>Every rescue is a decision you can see coming.</h2>
  <div class="story">{fig("hero.jpg")}<div><span class="step">1 · Read</span><h3>Warnings come before the danger</h3><p>Each mission shows what will happen and when: the reservoir rises at one minute, the wind turns, the tide comes in. Nothing is random, so a plan that works will work again.</p></div></div>
  <div class="story flip">{fig("plan.jpg")}<div><span class="step">2 · Plan</span><h3>Hold a tool over the board to see the future</h3><p>A forecast runs the real simulation ahead: green cells stay dry, red ones would flood. You choose where a barrier, bridge, pump or firebreak does the most good before you spend supplies.</p></div></div>
  <div class="story">{fig("act.jpg")}<div><span class="step">3 · Act</span><h3>Your hands do the work</h3><p>Pinch a tool card and drop it on a glowing spot. Pick a group, then where they should go; pick a boat, a group, then the drop-off. You can't pick people up and carry them: residents, occupied vehicles and hazards are moved only by the plan you make.</p></div></div>
  <div class="story flip">{fig("rescue.jpg")}<div><span class="step">4 · Rescue</span><h3>See what happened, then try another plan</h3><p>Results show who reached safety and which goals you met. A short timeline explains the moment that mattered, and each mission has two quite different ways to win.</p></div></div>
</div></section>

<section class="block"><div class="wrap">
  <p class="eyebrow">Five worlds. Twenty-five missions.</p>
  <h2>Each valley has its own danger.</h2>
  <ul>{worlds}</ul>
  <p>All 25 missions are playable in the development build and pass automated replays of two winning strategies on both board sizes. Headset testing comes in November.</p>
  <p><a class="btn secondary" href="worlds.html">Visit the worlds</a></p>
</div></section>

<section class="block"><div class="wrap">
  <p class="eyebrow">Find your way to play</p>
  <h2>Hands, one hand, or no hands at all.</h2>
  <p>Every mission can be finished with either hand alone, or hands-free by looking at a control until it fills. Larger text, high contrast, reduced effects and a relaxed pace are in Settings, and Open play unlocks every mission.</p>
  <p><a class="btn secondary" href="accessibility.html">Accessibility in RESQ</a></p>
</div></section>

<section class="block"><div class="wrap">
  <p class="eyebrow">A closer look</p>
  <h2>The worlds so far</h2>
  <div class="gallery">{fig("w1.jpg")}{fig("w2.jpg")}{fig("w3.jpg")}{fig("w4.jpg")}{fig("w5.jpg")}{fig("menu.jpg")}</div>
</div></section>
"""
    page("index.html", "RESQ: small worlds, big saves", body,
         "RESQ is a mixed-reality rescue strategy game for Meta Quest 3 and 3S: a miniature valley on your real table, and every resident to bring home.")


def worlds():
    chapters = ""
    for w in WORLDS:
        b = BRIEF["worlds"][w]
        chapters += f"""
<section class="chapter" id="{SLUG[w]}" aria-labelledby="h-{SLUG[w]}">
  <p class="eyebrow">{e(HAZARD[w])}</p>
  <h2 id="h-{SLUG[w]}">{e(b["name"])}</h2>
  <div class="story">{fig(w.lower() + ".jpg")}<div>
    <p>{e(b["identity"])}</p>
    <p><strong>What you learn:</strong> {e(b["thesis"])}</p>
    <p><strong>Afterwards:</strong> {e(b["aftercare"])}</p>
    <h3>Missions</h3>{missions(w)}
    <p class="note">Status: playable in the development build; validated by automated replays; not yet tested on a headset.</p>
  </div></div>
</section>"""
    body = f'<div class="wrap" style="padding-top:64px"><h1>Five worlds</h1><p>{e(STATUS)} Each world teaches one kind of danger, then later worlds combine them.</p>{chapters}</div>'
    page("worlds.html", "Worlds · RESQ", body, "The five valleys of RESQ and their 25 missions.")


def how_to_play():
    body = f"""
<div class="wrap" style="padding-top:64px">
  <h1>How to play</h1>
  <p>{e(STATUS)} This guide describes the development build.</p>
  <div class="route-line" aria-hidden="true"></div>
  <div class="story">{fig("hero.jpg")}<div><span class="step">Set up</span><h3>Put the valley on a table</h3><p>RESQ looks for a real table in your room and fits the board to it: a wide board on a big table, a compact one on a small table. No table nearby? Choose the virtual table. Then raise, lower or turn the board until the near edge is in easy reach. The valley stays where you left it next time.</p></div></div>
  <div class="story flip">{fig("plan.jpg")}<div><span class="step">Plan</span><h3>Read the warnings, test an idea</h3><p>The card behind the board lists who needs help and what's coming. Pinch a tool and hover it over a glowing spot: the forecast shows what that placement changes over the next minute. The clock doesn't start until you press Start.</p></div></div>
  <div class="story">{fig("act.jpg")}<div><span class="step">Act</span><h3>Place, route, adjust</h3><p>Pinch or poke a tool card, then a spot on the board; or pinch, drag and let go. To move people, pick a group and then a destination. For a boat, bus, crawler or winch, pick the vehicle, the group, then the drop-off. Tap a placed pump, generator or sprinkler to switch it.</p></div></div>
  <div class="story flip">{fig("menu.jpg")}<div><span class="step">Pause</span><h3>The Menu control is always there</h3><p>Pausing freezes everything. In hands-free play, look at a control and hold your gaze until it fills; you can also step time forward one or five seconds at a time instead of letting it run. If your hands leave view mid-rescue, the game pauses calmly and waits for you.</p></div></div>
</div>"""
    page("how-to-play.html", "How to play · RESQ", body, "Placing the board, reading warnings, placing equipment and routing residents in RESQ.")


def accessibility():
    rows = [
        ("Either hand alone", "Every mission can be finished with one hand, left or right. The run controls can mirror to the left side."),
        ("Hands-free", "Look at a control and hold your gaze until it fills (0.5 to 1.5 seconds, your choice). Quest 3 and 3S have no eye tracking, so this follows your head, not your eyes."),
        ("Your own pace", "Relaxed pace slows the hazards. Hands-free play adds +1 s and +5 s steps, so you never have to act in real time. Restart and leaving a mission always ask first."),
        ("Text and contrast", "Text at 100, 125 or 150 percent; the tool tray and controls grow with it. High contrast puts solid panels behind all text."),
        ("Calmer visuals", "Reduced effects stops flicker and pulsing without hiding any hazard."),
        ("Sound", "Warnings, blocked actions and backup calls use their own volume, so they stay clear when music is low. Every warning is also shown as text; there is no spoken dialogue to miss."),
        ("Open play", "Unlock every mission from the start. Each world's first mission is always open."),
        ("Seated", "Designed for seated play at a table. No movement around the room is needed."),
    ]
    trs = "".join(f"<tr><th scope=\"row\">{e(a)}</th><td>{e(b)}</td></tr>" for a, b in rows)
    body = f"""
<div class="wrap" style="padding-top:64px">
  <h1>Accessibility</h1>
  <p>RESQ is built so nobody needs two steady hands, quick reactions or perfect vision to bring everyone home. These options are in Settings and are remembered between sessions.</p>
  <table class="facts"><tbody>{trs}</tbody></table>
  <p class="note" style="margin-top:24px">{e(STATUS)} These features are tested automatically in the development build. Testing with people and on headsets happens in November; this page will be updated with what that finds.</p>
</div>"""
    page("accessibility.html", "Accessibility · RESQ", body, "One-hand, hands-free, text size, contrast, pace and other accessibility options in RESQ.")


def press():
    rows = [("Title", "RESQ"), ("Developer", "Frameless"), ("Platform", "Meta Quest 3 and Quest 3S (mixed reality)"),
            ("Genre", "Rescue strategy / puzzle"), ("Status", STATUS),
            ("Content", "5 worlds, 25 missions, 30 challenge variants"),
            ("Input", "Hand tracking (two hands or one), head-gaze dwell; no controllers needed"),
            ("Made with", "Unity 6, Meta XR SDK (Core, Interaction, MR Utility Kit), Blender")]
    trs = "".join(f"<tr><th scope=\"row\">{e(a)}</th><td>{e(b)}</td></tr>" for a, b in rows)
    body = f"""
<div class="wrap" style="padding-top:64px">
  <h1>Press</h1>
  <table class="facts"><tbody>{trs}</tbody></table>
  <section class="block" style="padding-top:48px"><h2>About</h2>
  <p>RESQ turns your table into a small world in trouble. Each valley faces one danger it can see coming: a flood, a wildfire, an avalanche, a turning tide, a lava flow. You place barriers, bridges, pumps and boats with your hands, route each group to safety, and watch the plan play out. Residents can't be picked up and carried; the only way to save them is a good plan.</p></section>
  <section class="block" style="padding-top:48px"><h2>Images</h2>
  <p>Real captures from the development build. A headset trailer will follow after device testing.</p>
  <div class="gallery">{fig("hero.jpg")}{fig("w2.jpg")}{fig("w3.jpg")}{fig("w4.jpg")}{fig("w5.jpg")}{fig("rescue.jpg")}</div></section>
</div>"""
    page("press.html", "Press · RESQ", body, "Fact sheet and images for RESQ.")


def support():
    sections = [
        ("setup", "Setting up the board", "<p>RESQ asks to use your room's surfaces only so it can place the board on a table. If you say no, or there is no table that fits, choose the virtual table: the missions are the same. To move the board later, open the Menu and choose Move the board.</p><p>If the board seems to drift or jump, the headset may have lost track of the room. RESQ pauses and offers to find the table again or switch to the virtual table.</p>"),
        ("inputs", "Hands, one hand and hands-free", "<p>Pinch with your thumb and index finger, or poke a card with your fingertip. Hands-free: in Settings, set Controls to hands-free; look at a control until it fills. If your hands leave view during a rescue, the game pauses until you press Resume.</p>"),
        ("saves", "Progress and saves", "<p>Progress and settings are saved on the headset after every mission. If the save can't be read, RESQ offers your last good backup and never erases anything without asking. Reset progress in Settings clears stars and unlocks but keeps your settings.</p>"),
        ("trouble", "Something went wrong", "<p>If a mission feels impossible, open the briefing again: it lists who needs help and what's coming. After a failed run, the debrief shows the moment that mattered. Every mission has been won by two different reference plans on both board sizes.</p>"),
    ]
    toc = "".join(f'<li><a href="#{i}">{e(t)}</a></li>' for i, t, _ in sections)
    arts = "".join(f'<h2 id="{i}">{e(t)}</h2>{body}' for i, t, body in sections)
    body = f'<div class="wrap"><div class="doc"><nav aria-label="Support topics"><ol>{toc}</ol></nav><article><h1>Support</h1><p class="note">{e(STATUS)}</p>{arts}</article></div></div>'
    page("support.html", "Support · RESQ", body, "Help with setting up RESQ, inputs, saves and troubleshooting.")


def privacy():
    body = f"""
<div class="wrap"><div class="doc"><nav aria-label="Privacy topics"><ol><li><a href="#data">What stays on the headset</a></li><li><a href="#room">Your room</a></li><li><a href="#network">Network</a></li></ol></nav>
<article><h1>Privacy</h1>
<h2 id="data">What stays on the headset</h2><p>RESQ saves your progress, settings and any postcards you take in its own storage on the headset. There are no accounts, no sign-in and no analytics.</p>
<h2 id="room">Your room</h2><p>If you allow it, RESQ reads where tables are in your room (from your headset's Space Setup) to place the board, and saves one spatial anchor so the board returns to the same spot. It never records or stores camera images.</p>
<h2 id="network">Network</h2><p>The app has no internet permission: it cannot send anything anywhere. This website uses no cookies, trackers or analytics.</p>
<p class="note">{e(STATUS)}</p></article></div></div>"""
    page("privacy.html", "Privacy · RESQ", body, "RESQ keeps your data on the headset and has no internet access.")


def not_found():
    page("404.html", "Not found · RESQ", '<div class="wrap" style="padding-top:64px"><h1>This page isn\'t here.</h1><p><a class="btn secondary" href="index.html">Back to RESQ</a></p></div>', "Page not found.")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    index(); worlds(); how_to_play(); accessibility(); press(); support(); privacy(); not_found()
    missing = [f for f in MEDIA if not (OUT / "media" / f).exists()]
    print("site written to", OUT, "| missing media:", ", ".join(missing) if missing else "none")
