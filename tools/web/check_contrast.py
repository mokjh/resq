#!/usr/bin/env python3
"""WCAG contrast for every text/background pair the site uses (slab colours come from build_site.SLAB).
Body text needs 4.5:1; the slabs carry body text, so every pair must pass 4.5:1."""
import sys
sys.path.insert(0, __file__.rsplit("/", 1)[0])
from build_site import SLAB

INK, WHITE, CTA, STATUS = "#132B2A", "#FFFFFF", "#FF6B35", "#F2BE22"


def lum(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    c = [x / 12.92 if x <= 0.03928 else ((x + 0.055) / 1.055) ** 2.4 for x in c]
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def ratio(a, b):
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


pairs = [(f"{w} slab", (WHITE if tone == "light" else INK), bg) for w, (bg, tone) in SLAB.items()]
pairs += [("CTA pill", INK, CTA), ("status pill", INK, STATUS), ("ink on white", INK, WHITE), ("white on ink", WHITE, INK)]
bad = 0
for name, fg, bg in pairs:
    r = ratio(fg, bg); ok = r >= 4.5; bad += not ok
    print(f"{name:14} {fg} on {bg}: {r:5.2f}:1 {'ok' if ok else 'FAIL'}")
sys.exit(1 if bad else 0)
