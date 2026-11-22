#!/usr/bin/env python3
"""Better-looking temporary art for the City Life textures (aspect ratios match the defs). Never overwrites without --force.
Run: uv run --with pillow python3 tools/city_placeholders.py [--force]
Real art replaces these later (Art module). Files: Things/HH/City/{ProjectorScreen,SalonChair,ParkBench}_{north,east,south}.png,
TaxiStand.png, TaxiCab.png, UI/HH/City/Type_{Cinema,Salon,Gallery}.png"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

R = Path(__file__).resolve().parent.parent
T = R / "Common" / "Textures"
FORCE = "--force" in sys.argv
OUT = (28, 20, 17, 255)

def save(im, rel):
    p = T / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    if p.exists() and not FORCE:
        return
    im.save(p)

def rot(im, quarter):  # quarter turns counter-clockwise
    return im.rotate(90 * quarter, expand=True)

def screen_img(w=384, h=128):
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle([4, 30, w - 5, h - 30], 6, fill=(58, 50, 66, 255), outline=OUT, width=4)
    d.rectangle([16, 40, w - 17, h - 40], fill=(214, 226, 240, 255), outline=(120, 130, 150, 255), width=2)
    # film strip dots
    for i in range(8):
        x = 30 + i * (w - 60) // 8
        d.rectangle([x, 46, x + 18, h - 46], fill=(176, 192, 214, 255))
    d.polygon([(w // 2 - 14, h // 2 - 18), (w // 2 - 14, h // 2 + 18), (w // 2 + 20, h // 2)], fill=(64, 72, 100, 255))
    d.rectangle([w // 2 - 6, h - 30, w // 2 + 6, h - 10], fill=(90, 80, 70, 255))  # projector foot
    return im

def chair_img(s=128):
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle([30, 20, s - 31, 46], 8, fill=(150, 70, 90, 255), outline=OUT, width=3)       # back
    d.rounded_rectangle([26, 44, s - 27, 92], 10, fill=(190, 96, 118, 255), outline=OUT, width=3)    # seat
    d.rectangle([20, 60, 32, 96], fill=(120, 104, 90, 255), outline=OUT, width=2)                    # arms
    d.rectangle([s - 33, 60, s - 21, 96], fill=(120, 104, 90, 255), outline=OUT, width=2)
    d.ellipse([s // 2 - 22, 96, s // 2 + 22, 116], fill=(90, 90, 100, 255), outline=OUT, width=3)    # base
    return im

def bench_img(w=256, h=128):
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    for i in range(4):
        y = 22 + i * 16
        d.rounded_rectangle([14, y, w - 15, y + 11], 3, fill=(150, 108, 66, 255), outline=OUT, width=2)
    d.rectangle([24, 84, 36, 118], fill=(70, 66, 62, 255)); d.rectangle([w - 37, 84, w - 25, 118], fill=(70, 66, 62, 255))
    return im

def stand_img(s=256):
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rectangle([6, 6, s - 7, s - 7], fill=(86, 88, 92, 255), outline=OUT, width=4)
    n = 8
    for i in range(n):
        for j in range(n):
            if (i + j) % 2 == 0:
                d.rectangle([14 + i * (s - 28) // n, 14 + j * (s - 28) // n, 14 + (i + 1) * (s - 28) // n - 1, 14 + (j + 1) * (s - 28) // n - 1], fill=(222, 186, 52, 255))
    d.rectangle([s - 60, 24, s - 48, 110], fill=(60, 60, 64, 255))                                     # post
    d.rounded_rectangle([s - 96, 18, s - 14, 56], 6, fill=(236, 200, 60, 255), outline=OUT, width=3)   # sign
    d.text((s - 82, 28), "TAXI", fill=OUT)
    return im

def cab_img(s=256):
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.ellipse([20, 52, s - 21, s - 52], fill=(236, 200, 60, 255), outline=OUT, width=5)
    d.ellipse([70, 84, s - 71, s - 84], fill=(150, 190, 214, 255), outline=OUT, width=4)
    d.rectangle([s // 2 - 22, 40, s // 2 + 22, 58], fill=(248, 248, 240, 255), outline=OUT, width=3)  # roof light
    for x in (46, s - 62):
        d.ellipse([x, s // 2 - 8, x + 16, s // 2 + 8], fill=(255, 255, 220, 255))
    return im

def icon(kind, s=64):
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle([3, 3, s - 4, s - 4], 8, fill={"cinema": (82, 78, 150, 255), "salon": (200, 110, 150, 255), "gallery": (176, 156, 90, 255)}[kind], outline=OUT, width=2)
    if kind == "cinema":
        d.rectangle([12, 22, s - 13, s - 14], fill=(230, 232, 240, 255)); d.polygon([(26, 28), (26, 46), (44, 37)], fill=(70, 74, 110, 255))
        d.rectangle([12, 12, s - 13, 20], fill=(40, 36, 50, 255))
    elif kind == "salon":
        d.line([(18, 16), (44, 48)], fill=(240, 240, 240, 255), width=4); d.line([(44, 16), (18, 48)], fill=(240, 240, 240, 255), width=4)
        d.ellipse([12, 44, 24, 56], outline=(250, 250, 250, 255), width=3); d.ellipse([38, 44, 50, 56], outline=(250, 250, 250, 255), width=3)
    else:
        d.rectangle([14, 14, s - 15, s - 15], fill=(244, 238, 220, 255), outline=(90, 70, 40, 255), width=3)
        d.ellipse([24, 22, 40, 38], fill=(150, 170, 200, 255)); d.polygon([(18, s - 20), (32, 34), (46, s - 20)], fill=(120, 150, 110, 255))
    return im

scr = screen_img(); ch = chair_img(); bn = bench_img()
save(scr, "Things/HH/City/ProjectorScreen_north.png"); save(scr, "Things/HH/City/ProjectorScreen_south.png"); save(rot(scr, 1), "Things/HH/City/ProjectorScreen_east.png")
for d_ in ("north", "east", "south"): save(ch, f"Things/HH/City/SalonChair_{d_}.png")
save(bn, "Things/HH/City/ParkBench_north.png"); save(bn, "Things/HH/City/ParkBench_south.png"); save(rot(bn, 1), "Things/HH/City/ParkBench_east.png")
save(stand_img(), "Things/HH/City/TaxiStand.png"); save(cab_img(), "Things/HH/City/TaxiCab.png")
for k in ("cinema", "salon", "gallery"): save(icon(k), f"UI/HH/City/Type_{k.capitalize()}.png")
print("City Life placeholder art written (use --force to overwrite)")
