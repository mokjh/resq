#!/usr/bin/env python3
"""Turns ObjectShot's black/white render pairs (tools/.cache/objects/<name>_b.png, _w.png) into cut-outs with true
transparency for the website: web/app/media/<name>.webp (plus a PNG fallback when --png is given).

Difference matting: over black a pixel is a*F, over white a*F + (1-a)*W, so a = 1 - (white - black) / (W - B),
with W and B measured from the image corners (the pure background) rather than assumed, which keeps it exact
through the renderer's tonemapping. Colour is un-premultiplied from the black render. The result is trimmed to
the object with a small margin. Evidence kind: Editor render of the running game (labelled on the site).
  uv run --with pillow --with numpy python3 tools/web/cutout.py obj_w1 [obj_w2 ...] [--width 1600] [--png]
  uv run --with pillow --with numpy python3 tools/web/cutout.py --opaque det_w1 ...   # detail crops: no alpha
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "tools/.cache/objects"
OUT = ROOT / "web/app/media"


def corners(img, k=24):
    h, w = img.shape[:2]
    patches = [img[:k, :k], img[:k, w - k:], img[h - k:, :k], img[h - k:, w - k:]]
    return np.median(np.concatenate([p.reshape(-1, 3) for p in patches]), axis=0)


def cutout(name, width, png, opaque):
    b = np.asarray(Image.open(SRC / f"{name}_b.png").convert("RGB"), dtype=np.float32) / 255.0
    w = np.asarray(Image.open(SRC / f"{name}_w.png").convert("RGB"), dtype=np.float32) / 255.0
    if opaque:
        img = Image.fromarray((b * 255 + 0.5).astype(np.uint8))
    else:
        bg_b, bg_w = corners(b), corners(w)
        span = np.maximum(bg_w - bg_b, 1e-3)
        a = 1.0 - ((w - b) / span).mean(axis=2)
        a = np.clip(a, 0.0, 1.0)
        a[a < 0.02] = 0.0                     # render noise in the empty background
        rgb = np.where(a[..., None] > 1e-3, (b - (1 - a[..., None]) * bg_b) / np.maximum(a[..., None], 1e-3), 0.0)
        rgb = np.clip(rgb, 0.0, 1.0)
        ys, xs = np.nonzero(a > 0.05)
        pad = 12
        y0, y1 = max(0, ys.min() - pad), min(a.shape[0], ys.max() + pad)
        x0, x1 = max(0, xs.min() - pad), min(a.shape[1], xs.max() + pad)
        rgba = np.dstack([rgb, a])[y0:y1, x0:x1]
        img = Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), "RGBA")
    if width and img.width > width:
        img = img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)
    OUT.mkdir(parents=True, exist_ok=True)
    img.save(OUT / f"{name}.webp", quality=86, method=6)
    if png:
        img.save(OUT / f"{name}.png", optimize=True)
    print(f"{name}: {img.width}x{img.height} {'opaque' if opaque else 'alpha'} -> media/{name}.webp")


if __name__ == "__main__":
    args = sys.argv[1:]
    width = int(args[args.index("--width") + 1]) if "--width" in args else 1600
    names = [a for i, a in enumerate(args) if not a.startswith("--") and (i == 0 or args[i - 1] != "--width")]
    for n in names:
        cutout(n, width, "--png" in args, "--opaque" in args)
