#!/usr/bin/env python3
"""RESQ app icon and VR splash, drawn by script (no AI imagery or AI text; the wordmark is set in the game's own
Atkinson Hyperlegible, SIL OFL). Writes game/Assets/Resq/Content/Brand/{app_icon.png, vr_splash.png}.
    uv run --with pillow tools/brand/icon.py
"""
import math, pathlib
from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "game/Assets/Resq/Content/Brand"
FONT = ROOT / "game/Assets/Resq/Content/Resources/Fonts/AtkinsonHyperlegible-Bold.ttf"
TEAL, DEEP, ORANGE, CREAM = (19, 43, 42), (11, 26, 26), (255, 107, 53), (243, 245, 242)


def ring(draw, cx, cy, r_out, r_in, segments=4):
    """A lifebuoy: orange ring with four cream bands."""
    draw.ellipse([cx - r_out, cy - r_out, cx + r_out, cy + r_out], fill=ORANGE)
    for k in range(segments):
        a0 = math.radians(k * 90 - 18); a1 = math.radians(k * 90 + 18)
        pts = [(cx + r_in * math.cos(a0), cy + r_in * math.sin(a0)), (cx + r_out * math.cos(a0), cy + r_out * math.sin(a0)),
               (cx + r_out * math.cos(a1), cy + r_out * math.sin(a1)), (cx + r_in * math.cos(a1), cy + r_in * math.sin(a1))]
        # approximate the arc band with a fan of thin wedges
        steps = 12
        poly = [(cx + r_out * math.cos(a0 + (a1 - a0) * i / steps), cy + r_out * math.sin(a0 + (a1 - a0) * i / steps)) for i in range(steps + 1)]
        poly += [(cx + r_in * math.cos(a1 - (a1 - a0) * i / steps), cy + r_in * math.sin(a1 - (a1 - a0) * i / steps)) for i in range(steps + 1)]
        draw.polygon(poly, fill=CREAM)
    draw.ellipse([cx - r_in, cy - r_in, cx + r_in, cy + r_in], fill=None)


def icon(size=1024):
    img = Image.new("RGBA", (size, size), TEAL + (255,))
    d = ImageDraw.Draw(img)
    # soft valley: two hills and a river under the ring
    d.polygon([(0, size * 0.78), (size * 0.32, size * 0.6), (size * 0.6, size * 0.74), (size, size * 0.58), (size, size), (0, size)], fill=(53, 93, 71, 255))
    d.polygon([(size * 0.42, size), (size * 0.5, size * 0.72), (size * 0.58, size * 0.72), (size * 0.66, size)], fill=(67, 139, 155, 255))
    cx, cy = size / 2, size * 0.42
    ring(d, cx, cy, size * 0.27, size * 0.155)
    d.ellipse([cx - size * 0.155, cy - size * 0.155, cx + size * 0.155, cy + size * 0.155], fill=TEAL + (255,))
    f = ImageFont.truetype(str(FONT), int(size * 0.17))
    w = d.textlength("RESQ", font=f)
    d.text(((size - w) / 2, size * 0.72), "RESQ", font=f, fill=CREAM)
    return img


def splash(w=2048, h=1024):
    img = Image.new("RGBA", (w, h), DEEP + (255,))
    d = ImageDraw.Draw(img)
    ring(d, w / 2, h * 0.42, h * 0.2, h * 0.115)
    d.ellipse([w / 2 - h * 0.115, h * 0.42 - h * 0.115, w / 2 + h * 0.115, h * 0.42 + h * 0.115], fill=DEEP + (255,))
    f = ImageFont.truetype(str(FONT), int(h * 0.11))
    tw = d.textlength("RESQ", font=f)
    d.text(((w - tw) / 2, h * 0.68), "RESQ", font=f, fill=CREAM)
    return img


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    icon().save(OUT / "app_icon.png")
    splash().save(OUT / "vr_splash.png")
    print("wrote", OUT / "app_icon.png", "and", OUT / "vr_splash.png")
