#!/usr/bin/env python3
"""Trailer pipeline: source manifest -> edit decisions (video/edl.json) -> master + captioned exports -> QC.

  uv run --with pillow python3 tools/video/edit.py manifest      # probe video/raw/*.mp4 (+ .json sidecars; raw stays local)
  uv run --with pillow python3 tools/video/edit.py build         # cut video/exports/resq-trailer{,-captioned}.mp4 + .srt/.vtt
  uv run --with pillow python3 tools/video/edit.py check FILE    # duration, size, streams, black frames, loudness
  uv run --with pillow python3 tools/video/edit.py selftest DIR  # whole pipeline on generated test patterns, in DIR

Competition rule: footage must come from a headset or the Meta XR Simulator. Every source clip needs a sidecar
<clip>.json with "kind" (quest | xrsim | editor | test), "input" (human | replay | autopilot), "mission", "build" and
"captured"; build refuses kinds outside the EDL's allowed_kinds. Test patterns are stamped "PIPELINE TEST - NOT
FOOTAGE" on every frame and never written to video/exports. ffmpeg here has no drawtext/subtitles filters, so title cards
and burned-in captions are PNGs drawn with Pillow and overlaid.
"""
import hashlib, json, os, shutil, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VIDEO = ROOT / "video"
FONTS = ROOT / "web" / "app" / "fonts"
INK, PAPER, ACTION = (19, 43, 42), (243, 235, 221), (255, 107, 53)


def run(cmd, capture=False):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit(f"command failed: {' '.join(map(str, cmd))[:300]}\n{r.stderr[-1500:]}")
    return r.stdout + r.stderr if capture else None


def probe(path):
    out = json.loads(run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(path)], True))
    v = next((s for s in out["streams"] if s["codec_type"] == "video"), None)
    a = next((s for s in out["streams"] if s["codec_type"] == "audio"), None)
    num, den = (v["avg_frame_rate"].split("/") + ["1"])[:2] if v else ("0", "1")
    return {"duration": round(float(out["format"]["duration"]), 3),
            "width": v and v["width"], "height": v and v["height"],
            "fps": round(float(num) / float(den or 1), 3) if v and float(den or 1) else 0,
            "video_codec": v and v["codec_name"], "audio": bool(a), "audio_codec": a and a["codec_name"]}


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def manifest(src_dir=VIDEO / "raw", out=VIDEO / "manifest.json"):
    clips, problems = {}, []
    for clip in sorted(Path(src_dir).glob("*.mp4")):
        side = clip.with_suffix(".json")
        if not side.exists():
            problems.append(f"{clip.name}: no sidecar {side.name}")
            continue
        meta = json.loads(side.read_text())
        missing = [k for k in ("kind", "input", "mission", "build", "captured") if k not in meta]
        if missing:
            problems.append(f"{clip.name}: sidecar lacks {missing}")
        clips[clip.stem] = {"file": clip.name, "sha256": sha256(clip), **probe(clip), **meta}
    Path(out).write_text(json.dumps({"clips": clips, "problems": problems}, indent=2) + "\n")
    print(f"manifest: {len(clips)} clips, {len(problems)} problems -> {out}")
    for p in problems:
        print("  " + p)
    return clips, problems


# ---- cards and caption overlays (Pillow) ----

def font(size, bold=False):
    from PIL import ImageFont
    return ImageFont.truetype(str(FONTS / f"AtkinsonHyperlegible-{'Bold' if bold else 'Regular'}.ttf"), size)


def title_card(lines, path, w, h):
    from PIL import Image, ImageDraw
    img = Image.new("RGB", (w, h), INK)
    d = ImageDraw.Draw(img)
    sizes = [int(h * 0.15)] + [int(h * 0.045)] * (len(lines) - 1)
    fonts = [font(s, i == 0) for i, s in enumerate(sizes)]
    boxes = [d.textbbox((0, 0), t, font=f) for t, f in zip(lines, fonts)]
    gap = int(h * 0.04)
    total = sum(b[3] - b[1] for b in boxes) + gap * (len(lines) - 1)
    y = (h - total) // 2
    for i, (t, f, b) in enumerate(zip(lines, fonts, boxes)):
        d.text(((w - (b[2] - b[0])) // 2 - b[0], y - b[1]), t, font=f, fill=PAPER if i == 0 else ACTION)
        y += b[3] - b[1] + gap
    img.save(path)


def caption_overlay(text, path, w, h):
    """Bottom-centred caption on a transparent frame: paper text on an ink box, >= 4.5:1 contrast."""
    from PIL import Image, ImageDraw
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    f = font(int(h * 0.042), True)
    words, lines, line = text.split(), [], ""
    for wd in words:  # wrap to 70 % of the width
        trial = (line + " " + wd).strip()
        if d.textlength(trial, font=f) > w * 0.7 and line:
            lines.append(line)
            line = wd
        else:
            line = trial
    lines.append(line)
    lh = int(h * 0.058)
    pad = int(h * 0.018)
    bw = int(max(d.textlength(l, font=f) for l in lines)) + 2 * pad
    bh = lh * len(lines) + 2 * pad
    x0, y0 = (w - bw) // 2, int(h * 0.9) - bh
    d.rounded_rectangle([x0, y0, x0 + bw, y0 + bh], radius=pad, fill=INK + (225,))
    for i, l in enumerate(lines):
        d.text(((w - d.textlength(l, font=f)) / 2, y0 + pad + i * lh), l, font=f, fill=PAPER)
    img.save(path)


def stamp_overlay(path, w, h):
    from PIL import Image, ImageDraw
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    f = font(int(h * 0.035), True)
    d.rectangle([0, 0, w, int(h * 0.06)], fill=(180, 0, 0, 200))
    d.text((int(w * 0.02), int(h * 0.01)), "PIPELINE TEST - NOT FOOTAGE", font=f, fill=(255, 255, 255))
    img.save(path)


# ---- build ----

def ts(sec, sep):
    ms = int(round(sec * 1000))
    return f"{ms // 3600000:02d}:{ms // 60000 % 60:02d}:{ms // 1000 % 60:02d}{sep}{ms % 1000:03d}"


def build(edl_path=VIDEO / "edl.json", src_dir=VIDEO / "raw", out_dir=VIDEO / "exports", test=False):
    edl = json.loads(Path(edl_path).read_text())
    ex = edl["export"]
    W, H, FPS = ex["width"], ex["height"], ex["fps"]
    clips, problems = manifest(src_dir, Path(src_dir).parent / "manifest.json")
    allowed = set(edl["allowed_kinds"]) | ({"test"} if test else set())
    work = Path(out_dir) / "work"
    shutil.rmtree(work, ignore_errors=True)
    work.mkdir(parents=True)
    stamp = None
    if test:
        stamp = work / "stamp.png"
        stamp_overlay(stamp, W, H)

    parts, captions, t = [], [], 0.0
    for i, seg in enumerate(edl["segments"]):
        part = work / f"p{i:02d}.mp4"
        norm = (f"scale={W}:{H}:force_original_aspect_ratio=decrease,pad={W}:{H}:(ow-iw)/2:(oh-ih)/2:color=0x132B2A,"
                f"fps={FPS},format=yuv420p,setsar=1")
        if "card" in seg:
            dur = seg["duration"]
            png = work / f"card{i:02d}.png"
            title_card(seg["text"], png, W, H)
            inputs = ["-loop", "1", "-t", str(dur), "-i", str(png),
                      "-f", "lavfi", "-t", str(dur), "-i", "anullsrc=r=48000:cl=stereo"]
            vf = norm
        else:
            c = clips.get(seg["source"])
            if not c:
                sys.exit(f"segment {seg.get('id', i)}: source '{seg['source']}' is not in video/raw")
            if c["kind"] not in allowed:
                sys.exit(f"segment {seg.get('id', i)}: source kind '{c['kind']}' is not allowed "
                         f"(competition footage must be {sorted(edl['allowed_kinds'])})")
            dur = seg["out"] - seg["in"]
            src = Path(src_dir) / c["file"]
            inputs = ["-ss", str(seg["in"]), "-t", str(dur), "-i", str(src)]
            if not c["audio"]:
                inputs += ["-f", "lavfi", "-t", str(dur), "-i", "anullsrc=r=48000:cl=stereo"]
            # optional "crop": "w:h:x:y" of the source frame, scaled back up (a closer look at a menu card)
            vf = (f"crop={seg['crop']}," if seg.get("crop") else "") + norm
        fi, fo = seg.get("fade_in", 0), seg.get("fade_out", 0)
        if fi:
            vf += f",fade=t=in:st=0:d={fi}"
        if fo:
            vf += f",fade=t=out:st={dur - fo}:d={fo}"
        if stamp:
            inputs += ["-i", str(stamp)]
            filt = f"[0:v]{vf}[b];[b][{2 if ('card' in seg or not clips[seg['source']]['audio']) else 1}:v]overlay=0:0[v]"
        else:
            filt = f"[0:v]{vf}[v]"
        a_in = "1:a" if ("card" in seg or not clips[seg["source"]]["audio"]) else "0:a"
        af = f"aresample=48000,aformat=channel_layouts=stereo,apad,atrim=0:{dur}"
        if fi:
            af += f",afade=t=in:st=0:d={fi}"
        if fo:
            af += f",afade=t=out:st={dur - fo}:d={fo}"
        run(["ffmpeg", "-loglevel", "error", "-y", *inputs, "-filter_complex", f"{filt};[{a_in}]{af}[a]",
             "-map", "[v]", "-map", "[a]", "-t", str(dur), "-c:v", "libx264", "-preset", "medium", "-crf", "18",
             "-c:a", "aac", "-b:a", "192k", "-ar", "48000", str(part)])
        parts.append(part)
        if seg.get("caption"):
            captions.append((t + seg.get("caption_at", 0.3), t + dur - 0.2, seg["caption"]))
        t += dur

    listing = work / "parts.txt"
    listing.write_text("".join(f"file '{p}'\n" for p in parts))
    joined = work / "joined.mp4"
    run(["ffmpeg", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", str(listing), "-c", "copy", str(joined)])

    # optional music bed (game stems), ducked under the cut's own sound
    mixed = joined
    if edl.get("music"):
        mixed = work / "mixed.mp4"
        m = edl["music"]
        run(["ffmpeg", "-loglevel", "error", "-y", "-i", str(joined), "-stream_loop", "-1", "-i", str(ROOT / m["file"]),
             "-filter_complex",
             f"[1:a]volume={m.get('level', 0.35)},atrim=0:{t},afade=t=out:st={max(0, t - 2)}:d=2[m];"
             f"[0:a]asplit[g][key];[m][key]sidechaincompress=threshold=0.05:ratio=6:attack=20:release=400[d];"
             f"[g][d]amix=inputs=2:normalize=0[a]",
             "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", str(mixed)])

    out = Path(out_dir)
    master = out / f"{edl['name']}.mp4"
    # two-pass loudness normalisation to the chosen web targets
    meas = run(["ffmpeg", "-hide_banner", "-i", str(mixed), "-af",
                f"loudnorm=I={ex['lufs']}:TP={ex['true_peak']}:LRA=11:print_format=json", "-f", "null", "-"], True)
    js = json.loads(meas[meas.rindex("{"):meas.rindex("}") + 1])
    ln = (f"loudnorm=I={ex['lufs']}:TP={ex['true_peak']}:LRA=11:measured_I={js['input_i']}:measured_TP={js['input_tp']}:"
          f"measured_LRA={js['input_lra']}:measured_thresh={js['input_thresh']}:offset={js['target_offset']}:linear=true")
    run(["ffmpeg", "-loglevel", "error", "-y", "-i", str(mixed), "-af", ln, "-ar", "48000", "-c:v", "copy",
         "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", str(master)])

    srt = out / f"{edl['name']}.srt"
    vtt = out / f"{edl['name']}.vtt"
    srt.write_text("".join(f"{n}\n{ts(a, ',')} --> {ts(b, ',')}\n{txt}\n\n" for n, (a, b, txt) in enumerate(captions, 1)))
    vtt.write_text("WEBVTT\n\n" + "".join(f"{ts(a, '.')} --> {ts(b, '.')}\n{txt}\n\n" for a, b, txt in captions))

    captioned = out / f"{edl['name']}-captioned.mp4"
    if captions:
        ins, chain, last = ["-i", str(master)], [], "0:v"
        for n, (a, b, txt) in enumerate(captions, 1):
            png = work / f"cap{n:02d}.png"
            caption_overlay(txt, png, W, H)
            ins += ["-loop", "1", "-framerate", str(FPS), "-t", f"{t:.3f}", "-i", str(png)]  # a looped still: a lone frame stops compositing
            chain.append(f"[{last}][{n}:v]overlay=0:0:eof_action=pass:enable='between(t,{a:.3f},{b:.3f})'[c{n}]")
            last = f"c{n}"
        run(["ffmpeg", "-loglevel", "error", "-y", *ins, "-filter_complex", ";".join(chain), "-map", f"[{last}]",
             "-map", "0:a", "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-c:a", "copy",
             "-movflags", "+faststart", str(captioned)])
    else:
        shutil.copy(master, captioned)

    decisions = {"edl": str(edl_path), "segments": edl["segments"], "captions": captions, "export": ex,
                 "sources": {k: {f: v[f] for f in ("sha256", "kind", "input", "mission", "build")} for k, v in clips.items()
                             if any(s.get("source") == k for s in edl["segments"])}}
    (out / f"{edl['name']}.decisions.json").write_text(json.dumps(decisions, indent=2) + "\n")
    print(f"build: {master.name} ({t:.1f} s, {len(parts)} segments, {len(captions)} captions)")
    return master, captioned, ex


# ---- quality gates ----

def check(path, ex=None):
    ex = ex or json.loads((VIDEO / "edl.json").read_text())["export"]
    p = probe(path)
    fails = []
    if p["duration"] >= ex["max_seconds"]:
        fails.append(f"duration {p['duration']} s >= {ex['max_seconds']} s")
    if (p["width"], p["height"]) != (ex["width"], ex["height"]):
        fails.append(f"size {p['width']}x{p['height']}")
    if not p["audio"]:
        fails.append("no audio stream")
    log = run(["ffmpeg", "-hide_banner", "-i", str(path), "-vf", "blackdetect=d=0.6:pix_th=0.06",
               "-af", "ebur128=peak=true", "-f", "null", "-"], True)
    blacks = [l.split("black_start:")[1].split()[0] for l in log.splitlines() if "black_start:" in l]
    summ = log[log.rindex("Summary:"):] if "Summary:" in log else ""
    lufs = float(summ.split("I:")[1].split("LUFS")[0]) if "I:" in summ else None
    peak = float(summ.split("Peak:")[1].split("dBFS")[0]) if "Peak:" in summ else None
    if lufs is None or abs(lufs - ex["lufs"]) > 1.0:
        fails.append(f"integrated loudness {lufs} LUFS (target {ex['lufs']})")
    if peak is not None and peak > ex["true_peak"] + 0.2:
        fails.append(f"true peak {peak} dBTP (limit {ex['true_peak']})")
    frames = Path(path).with_suffix("")
    for name, at in (("first", "0.5"), ("last", str(max(0, p["duration"] - 0.5)))):
        run(["ffmpeg", "-loglevel", "error", "-y", "-ss", at, "-i", str(path), "-frames:v", "1", f"{frames}-{name}.png"])
    print(f"check {Path(path).name}: {p['duration']} s {p['width']}x{p['height']} {p['fps']} fps, "
          f"audio={p['audio']}, {lufs} LUFS, peak {peak} dBTP, black runs at {blacks or 'none'}")
    print("  frames for review: " + f"{frames}-first.png, {frames}-last.png")
    print("  PASS" if not fails else "  FAIL: " + "; ".join(fails))
    return not fails


def selftest(out_dir):
    """Runs the whole pipeline on generated test patterns (stamped as such) and checks the gates."""
    out = Path(out_dir)
    src = out / "sources"
    src.mkdir(parents=True, exist_ok=True)
    for name, pat, dur, audio in (("clipA", "testsrc2", 12, True), ("clipB", "smptebars", 10, False)):
        a = ["-f", "lavfi", "-i", f"sine=frequency=330:duration={dur}"] if audio else []
        run(["ffmpeg", "-loglevel", "error", "-y", "-f", "lavfi", "-i", f"{pat}=size=1280x720:rate=30:duration={dur}",
             *a, "-c:v", "libx264", "-pix_fmt", "yuv420p", *(["-c:a", "aac"] if audio else []), "-shortest" if audio else "-an",
             str(src / f"{name}.mp4")])
        (src / f"{name}.json").write_text(json.dumps({"kind": "test", "input": "replay", "mission": "none",
                                                      "build": "selftest", "captured": "generated"}))
    edl = {"name": "selftest", "allowed_kinds": ["quest", "xrsim"],
           "export": {"width": 1280, "height": 720, "fps": 30, "lufs": -16, "true_peak": -1.0, "max_seconds": 178},
           "music": {"file": "game/Assets/Resq/Content/Resources/ResqAudio/music_w1_calm.wav", "level": 0.3},
           "segments": [{"id": "a", "source": "clipA", "in": 1, "out": 7, "caption": "One small move.", "fade_in": 0.3},
                        {"id": "t", "card": "title", "duration": 3, "text": ["RESQ", "Small worlds. Big saves."]},
                        {"id": "b", "source": "clipB", "in": 2, "out": 6, "caption": "Plan the rescue. Keep the way out open.",
                         "fade_out": 0.5}]}
    if not (ROOT / edl["music"]["file"]).exists():
        edl.pop("music")
    (out / "edl.json").write_text(json.dumps(edl, indent=2))
    master, captioned, ex = build(out / "edl.json", src, out, test=True)
    ok = check(master, ex) and check(captioned, ex)
    # the competition gate must refuse test patterns in a real build
    r = subprocess.run([sys.executable, __file__, "_refuse", str(out / "edl.json"), str(src), str(out / "refuse")],
                       capture_output=True, text=True)
    refused = r.returncode != 0 and "not allowed" in (r.stdout + r.stderr)
    print("selftest: kind gate refuses test footage in a real build:", "yes" if refused else "NO")
    print("SELFTEST " + ("PASS" if ok and refused else "FAIL"))
    return ok and refused


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "manifest":
        _, problems = manifest()
        sys.exit(1 if problems else 0)
    elif cmd == "build":
        m, c, ex = build()
        sys.exit(0 if check(m, ex) and check(c, ex) else 1)
    elif cmd == "check":
        sys.exit(0 if check(sys.argv[2]) else 1)
    elif cmd == "selftest":
        sys.exit(0 if selftest(sys.argv[2]) else 1)
    elif cmd == "_refuse":
        Path(sys.argv[4]).mkdir(parents=True, exist_ok=True)
        build(Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]), test=False)
    else:
        sys.exit(__doc__)
