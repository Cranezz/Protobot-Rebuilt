"""
Draws the tool icons for the hotbar / tool wheel and the precision crosshair (white with a dark
outline, same style as make_cursors.py):
  tool_saw.png, tool_paint.png, tool_drill.png, ui_crosshair.png
Shapes are drawn on a 48-unit grid at 8x and scaled down.

Usage: python make_tool_icons.py <output folder> [size=96]
"""
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = sys.argv[1]
SIZE = int(sys.argv[2]) if len(sys.argv) > 2 else 96
S = 8
W = SIZE * S


def u(v):
    """A position on the 48-unit grid, inset by 2 units so outlines and shadows never touch the edge."""
    return int(round((2 + v * 44 / 48) / 48 * W))


def size(v):
    """A length (width, radius) on the same grid."""
    return max(1, int(round(v * 44 / 48 / 48 * W)))


def canvas():
    mask = Image.new("L", (W, W), 0)
    return mask, ImageDraw.Draw(mask)


def odd(n):
    return n if n % 2 else n + 1


def finish(mask, name, outline=1.8, shadow=True):
    grow = ImageChops.lighter(mask, mask.filter(ImageFilter.MaxFilter(odd(size(outline) * 2 + 1))))
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    if shadow:
        soft = ImageChops.offset(grow.filter(ImageFilter.GaussianBlur(size(1.2))), size(0.8), size(1.2))
        img.paste((0, 0, 0, 80), mask=soft)
    img.paste((25, 25, 28, 255), mask=grow)
    img.paste((255, 255, 255, 255), mask=mask)
    img.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(OUT, name))


def rrect(d, x0, y0, x1, y1, r):
    d.rounded_rectangle([u(x0), u(y0), u(x1), u(y1)], radius=size(r), fill=255)


def line(d, pts, width):
    d.line([(u(x), u(y)) for x, y in pts], fill=255, width=size(width), joint="curve")
    for x, y in (pts[0], pts[-1]):
        r = size(width) / 2
        d.ellipse([u(x) - r, u(y) - r, u(x) + r, u(y) + r], fill=255)


def poly(d, pts):
    d.polygon([(u(x), u(y)) for x, y in pts], fill=255)


def saw():
    mask, d = canvas()
    line(d, [(6.5, 30), (6.5, 12), (35, 12), (35, 24)], 3.2)    # bow frame
    line(d, [(6.5, 30), (36, 30)], 2.2)                          # blade
    for x in range(9, 34, 3):                                    # teeth
        poly(d, [(x, 31), (x + 1.5, 34), (x + 3, 31)])
    poly(d, [(33, 22), (40, 21), (45, 38), (38, 40)])            # pistol grip
    finish(mask, "tool_saw.png")


def paint():
    mask, d = canvas()
    rrect(d, 9, 17, 35, 25, 3)        # gun body
    rrect(d, 3, 19, 10, 23, 1)        # nozzle
    rrect(d, 15, 4, 29, 16, 3)        # paint cup (the colour chamber)
    rrect(d, 20, 15, 24, 18, 0.5)     # cup neck
    poly(d, [(26, 24), (33, 24), (37, 43), (30, 43)])  # handle
    rrect(d, 21, 25, 24, 33, 1)       # trigger
    finish(mask, "tool_paint.png")


def drill():
    mask, d = canvas()
    rrect(d, 14, 11, 40, 22, 4)       # motor body
    rrect(d, 8, 13, 15, 20, 1.5)      # chuck
    line(d, [(2, 16.5), (8, 16.5)], 1.8)  # bit
    poly(d, [(27, 21), (36, 21), (33, 36), (24, 36)])  # grip
    rrect(d, 19, 35, 39, 43, 2)       # battery
    finish(mask, "tool_drill.png")


def crosshair():
    mask, d = canvas()
    for a, b in (((24, 9), (24, 19)), ((24, 29), (24, 39)), ((9, 24), (19, 24)), ((29, 24), (39, 24))):
        line(d, [a, b], 2.2)
    d.ellipse([u(22.6), u(22.6), u(25.4), u(25.4)], fill=255)
    finish(mask, "ui_crosshair.png", outline=1.2, shadow=False)


os.makedirs(OUT, exist_ok=True)
saw()
paint()
drill()
crosshair()
print("tool icons written to", OUT)
