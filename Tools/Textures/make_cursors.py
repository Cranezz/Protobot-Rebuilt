"""
Draws the mouse cursor icons (white with a dark outline, anti-aliased):
  cursor_default.png  - arrow
  cursor_point.png    - pointing hand (something you can click)
  cursor_grab.png     - open hand (something you can pick up)
  cursor_grabbing.png - closed hand (holding something)
Shapes are drawn at 8x size and scaled down for smooth edges.

Usage: python make_cursors.py <output folder> [size=48]
"""
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = sys.argv[1]
SIZE = int(sys.argv[2]) if len(sys.argv) > 2 else 48
S = 8  # supersampling
W = SIZE * S


def u(v):
    """Design units (0-48) to supersampled pixels."""
    return int(round(v / 48 * W))


def finish(mask, name, outline=2.2):
    """Turns a white shape mask into white-fill + dark-outline icon with a soft shadow."""
    grow = ImageChops.lighter(mask, mask.filter(ImageFilter.MaxFilter(_odd(u(outline) * 2 + 1))))
    shadow = grow.filter(ImageFilter.GaussianBlur(u(1.5)))
    shadow = ImageChops.offset(shadow, u(1), u(1.5))
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    img.paste((0, 0, 0, 70), mask=shadow)
    img.paste((25, 25, 28, 255), mask=grow)
    img.paste((255, 255, 255, 255), mask=mask)
    img.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(OUT, name))


def _odd(n):
    return n if n % 2 else n + 1


def canvas():
    mask = Image.new("L", (W, W), 0)
    return mask, ImageDraw.Draw(mask)


def rrect(d, x0, y0, x1, y1, r):
    d.rounded_rectangle([u(x0), u(y0), u(x1), u(y1)], radius=u(r), fill=255)


def arrow():
    mask, d = canvas()
    pts = [(3, 2), (3, 34), (11, 27), (17, 40), (22, 38), (16, 25.5), (27, 25.5)]
    d.polygon([(u(x), u(y)) for x, y in pts], fill=255)
    finish(mask, "cursor_default.png")


def palm(d):
    rrect(d, 12, 22, 36, 44, 7)


def pointing_hand():
    mask, d = canvas()
    palm(d)
    rrect(d, 16, 3, 22, 30, 3)       # index finger up
    rrect(d, 21.5, 16, 27.5, 30, 3)  # curled fingers
    rrect(d, 27, 18, 33, 31, 3)
    rrect(d, 32, 20, 37.5, 32, 2.8)
    rrect(d, 6, 24, 14, 30, 3.5)     # thumb
    finish(mask, "cursor_point.png")


def open_hand():
    mask, d = canvas()
    palm(d)
    rrect(d, 13, 8, 18.5, 30, 2.8)
    rrect(d, 19.5, 5, 25, 30, 2.8)
    rrect(d, 26, 7, 31.5, 30, 2.8)
    rrect(d, 32.5, 11, 37.5, 31, 2.6)
    rrect(d, 4, 21, 13, 27.5, 3.2)  # thumb out to the side
    finish(mask, "cursor_grab.png")


def closed_hand():
    mask, d = canvas()
    palm(d)
    for x in (13, 19.5, 26, 32):
        rrect(d, x, 16, x + 5.5, 27, 2.8)  # knuckles of the curled fingers
    rrect(d, 7, 25, 15, 31, 3.2)           # thumb tucked in
    finish(mask, "cursor_grabbing.png")


os.makedirs(OUT, exist_ok=True)
arrow()
pointing_hand()
open_hand()
closed_hand()
print("cursors written to", OUT)
