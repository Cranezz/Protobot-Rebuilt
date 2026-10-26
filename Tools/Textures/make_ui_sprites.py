"""
Generates the UI sprites used by the menus (white shapes, tinted in Unity):
  ui_rounded.png         - rounded rectangle, 9-slice (border 20 px)
  ui_rounded_outline.png - outline of the same shape, 9-slice
  ui_hazard.png          - diagonal safety stripes (yellow/black), tiling
  ui_dot.png             - centre-screen dot for the reticle
  ui_shadow.png          - soft drop shadow, 9-slice (border 40 px)
Drawn at 4x and scaled down for smooth edges.

Usage: python make_ui_sprites.py <output folder>
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1]
S = 4


def save(img, name):
    img.save(os.path.join(OUT, name))


def rounded(size=64, radius=14, outline=0):
    big = Image.new("RGBA", (size * S, size * S), (255, 255, 255, 0))
    d = ImageDraw.Draw(big)
    box = [0, 0, size * S - 1, size * S - 1]
    if outline:
        d.rounded_rectangle(box, radius=radius * S, outline=(255, 255, 255, 255), width=outline * S)
    else:
        d.rounded_rectangle(box, radius=radius * S, fill=(255, 255, 255, 255))
    return big.resize((size, size), Image.LANCZOS)


def hazard(size=64):
    big = Image.new("RGBA", (size * S, size * S), (242, 183, 5, 255))
    d = ImageDraw.Draw(big)
    w = size * S
    stripe = w // 4
    for k in range(-4, 8):
        x = k * stripe * 2
        d.polygon([(x, w), (x + stripe, w), (x + stripe + w, 0), (x + w, 0)], fill=(24, 24, 26, 255))
    return big.resize((size, size), Image.LANCZOS)


def dot(size=32):
    big = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    c = size * S / 2
    d.ellipse([c - 5 * S, c - 5 * S, c + 5 * S, c + 5 * S], fill=(20, 20, 22, 200))
    d.ellipse([c - 3.2 * S, c - 3.2 * S, c + 3.2 * S, c + 3.2 * S], fill=(255, 255, 255, 255))
    return big.resize((size, size), Image.LANCZOS)


def shadow(size=128, inset=40):
    img = Image.new("L", (size, size), 0)
    ImageDraw.Draw(img).rounded_rectangle([inset, inset, size - inset, size - inset], radius=14, fill=200)
    img = img.filter(ImageFilter.GaussianBlur(14))
    rgba = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    rgba.putalpha(img)
    return rgba


os.makedirs(OUT, exist_ok=True)
save(rounded(), "ui_rounded.png")
save(rounded(outline=2), "ui_rounded_outline.png")
save(hazard(), "ui_hazard.png")
save(dot(), "ui_dot.png")
save(shadow(), "ui_shadow.png")
print("ui sprites written to", OUT)
