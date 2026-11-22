# From pivot.sh's marks: each turn moved the build origin from p to p' and turned it by d (clockwise degrees);
# the point it turned about is c = (I - R)^-1 (p' - R p), R turning clockwise by d (x right, y forward).
import math, re, sys
rows = []
for line in open(sys.argv[1], encoding="utf-8", errors="replace"):
    m = re.search(r"TRUE x=(-?[\d.]+) y=(-?[\d.]+) h=(-?[\d.]+)", line)
    if m:
        rows.append(tuple(float(v) for v in m.groups()))
for (x0, y0, h0), (x1, y1, h1) in zip(rows, rows[1:]):
    d = (h1 - h0 + 180) % 360 - 180
    if abs(d) < 20:
        continue
    a = math.radians(-d)   # clockwise d = counter-clockwise -d in x/y
    c, s = math.cos(a), math.sin(a)
    rx, ry = c * x0 - s * y0, s * x0 + c * y0
    bx, by = x1 - rx, y1 - ry
    # (I - R) c = b
    m00, m01, m10, m11 = 1 - c, s, -s, 1 - c
    det = m00 * m11 - m01 * m10
    cx, cy = (m11 * bx - m01 * by) / det, (-m10 * bx + m00 * by) / det
    # in the robot's own frame at the start of the turn (x right, y forward)
    t = math.radians(h0)
    lx = (cx - x0) * math.cos(t) - (cy - y0) * math.sin(t)
    ly = (cx - x0) * math.sin(t) + (cy - y0) * math.cos(t)
    print("turn %+.0f deg: pivot %.2f in right, %.2f in forward of the build origin (moved %.2f in)" % (d, lx, ly, math.hypot(x1 - x0, y1 - y0)))
