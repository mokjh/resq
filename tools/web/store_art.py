#!/usr/bin/env python3
"""Store art from the game's own object renders (no generated imagery): cover 2560x1440, hero 3000x900, icon 512 and 1024 (the game's app icon).
    uv run --with pillow python3 tools/web/store_art.py   ->  docs/submission/store/"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
ROOT = Path(__file__).resolve().parents[2]; M = ROOT / "web/app/media"; OUT = ROOT / "docs/submission/store"; OUT.mkdir(parents=True, exist_ok=True)
BOLD = str(ROOT / "web/app/fonts/AtkinsonHyperlegible-Bold.ttf"); REG = str(ROOT / "web/app/fonts/AtkinsonHyperlegible-Regular.ttf")
NIGHT, NIGHT2, PAPER = (13, 25, 24), (27, 48, 46), (234, 241, 239)

def night(w, h, cx, cy):
    """Radial pool of light on the night table, as on the site's hero."""
    small = Image.new("RGB", (w // 8, h // 8)); px = small.load(); sw, sh = small.size; r = max(sw, sh) * 0.62
    for y in range(sh):
        for x in range(sw):
            k = min(1.0, (((x - cx * sw) ** 2 + (y - cy * sh) ** 2) ** 0.5) / r); k = 1 - k * k
            px[x, y] = tuple(int(NIGHT[i] + (NIGHT2[i] - NIGHT[i]) * k) for i in range(3))
    return small.resize((w, h), Image.BICUBIC)

def place(canvas, name, width, cx, cy):
    im = Image.open(M / name).convert("RGBA"); h = int(im.height * width / im.width); im = im.resize((width, h), Image.LANCZOS)
    canvas.paste(im, (int(cx - width / 2), int(cy - h / 2)), im)

# cover: the hero valley with the title in the inner 80 %
c = night(2560, 1440, 0.62, 0.55); place(c, "obj_hero.webp", 1560, 1660, 790); d = ImageDraw.Draw(c)
d.text((300, 530), "RESQ", font=ImageFont.truetype(BOLD, 210), fill=PAPER)
d.text((306, 770), "Small worlds.\nBig saves.", font=ImageFont.truetype(BOLD, 92), fill=PAPER, spacing=10)
d.text((308, 1010), "Mixed-reality rescue strategy", font=ImageFont.truetype(REG, 46), fill=(187, 204, 201))
c.save(OUT / "cover_2560x1440.png")
# hero: all five worlds along the shelf
h = night(3000, 900, 0.5, 0.6); W = 640
for i, n in enumerate(["obj_w2.webp", "obj_w3.webp", "obj_w1.webp", "obj_w4.webp", "obj_w5.webp"]): place(h, n, W, 310 + i * 595, 560)
d = ImageDraw.Draw(h); t = "RESQ"; f = ImageFont.truetype(BOLD, 150); d.text((1500 - d.textlength(t, font=f) / 2, 70), t, font=f, fill=PAPER)
h.save(OUT / "hero_3000x900.png")
# icon: the game's own app icon
icon = Image.open(ROOT / "game/Assets/Resq/Content/Brand/app_icon.png").convert("RGB")
for n in (1024, 512): icon.resize((n, n), Image.LANCZOS).save(OUT / f"icon_{n}.png")
print("store art written to", OUT)
