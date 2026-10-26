"""
Builds the Protobot workshop (garage) scene.

Run inside Blender:  exec(open(r"<project>\\Tools\\Blender\\workshop_build.py").read())
Layout (top view, +Y is the back wall, the player starts near the garage door at -Y):

    +----------------- back wall: bench, pegboards, window --------------+[bins]
    | left bench                                                right bench|
    |                      [ build table ]                                  |
    | door                                                     tool chest   |
    +--------------------------- garage door -----------------------------+
"""
import importlib
import math
import sys

sys.path.insert(0, r"C:\Dev\ProtobotRebuiltV2\ProtobotRebuiltV2\Tools\Blender")
import workshop_lib as L  # noqa: E402

importlib.reload(L)

# Room interior: 6.4 m x 6.4 m, 2.9 m ceiling, 15 cm walls.
X0, X1, Y0, Y1, H, T = -3.2, 3.2, -3.2, 3.2, 2.9, 0.15
BENCH_DEPTH, BENCH_TOP = 0.65, 0.9

# Window in the back wall, man door in the left wall, garage door in the front wall.
WIN_X, WIN_Z = (-0.7, 0.7), (1.3, 2.2)
DOOR_Y, DOOR_H = (-2.6, -1.7), 2.05
GARAGE_X, GARAGE_H = (-2.4, 2.4), 2.2

# Bin shelf along the right wall, starting just clear of the back bench (which runs into the corner).
BIN_SHELF_Y = (Y1 - BENCH_DEPTH - 0.05 - 1.93, Y1 - BENCH_DEPTH - 0.05)


def materials():
    return {
        "floor": L.material("Floor_Concrete", "garage_floor", 1.886),
        "plaster": L.material("Wall_Plaster", "beige_wall_001", 3.0),
        "block": L.material("Wall_CinderBlock", "painted_block", 1.6),
        "ceiling": L.material("Ceiling_Plaster", "beige_wall_001", 3.0, tint=(0.95, 0.95, 0.95)),
        "joist": L.material("Wood_Joist", "plywood", 0.5, tint=(0.85, 0.75, 0.6)),
        "benchtop": L.material("Wood_BenchTop", "oak_veneer_01", 1.83),
        "plywood": L.material("Wood_Plywood", "plywood", 0.5),
        "steel_dark": L.material("Steel_DarkPainted", "metal_plate_02", 2.0, tint=(0.28, 0.29, 0.31), metal=1.0),
        "steel_shelf": L.material("Steel_Shelving", "metal_plate_02", 2.0, tint=(0.62, 0.63, 0.65), metal=1.0),
        "cabinet": L.material("Metal_BlueCabinet", "blue_metal_plate", 2.5),
        "garage_door": L.material("Metal_GarageDoor", "painted_metal_shutter", 2.0, tint=(0.82, 0.83, 0.85)),
        "door": L.material("Paint_DoorGrey", color=(0.32, 0.36, 0.40), rough=0.45),
        "trim": L.material("Paint_TrimWhite", color=(0.86, 0.85, 0.82), rough=0.5),
        "glass": L.material("Glass_Window", color=(0.75, 0.85, 0.9), rough=0.05, alpha=0.18),
        "mat_rubber": L.material("Rubber_Mat", color=(0.06, 0.065, 0.07), rough=0.9),
        "chrome": L.material("Metal_Chrome", color=(0.8, 0.8, 0.82), rough=0.2, metal=1.0),
        "black_plastic": L.material("Plastic_Black", color=(0.03, 0.03, 0.035), rough=0.55),
        "light_housing": L.material("Metal_LightHousing", color=(0.85, 0.86, 0.86), rough=0.4, metal=0.6),
        "light_tube": L.material("Light_Fluorescent", color=(1, 1, 1), emission=(1.0, 0.98, 0.92), emission_strength=8.0),
        "label": L.material("Paper_Label", color=(0.95, 0.94, 0.9), rough=0.8),
    }


def build_room(m, root):
    room = L.empty("Room", root)
    L.box_span("Floor", X0 - T, X1 + T, Y0 - T, Y1 + T, -0.1, 0.0, m["floor"], room)
    L.box_span("Ceiling", X0 - T, X1 + T, Y0 - T, Y1 + T, H, H + 0.05, m["ceiling"], room)
    for i in range(11):  # exposed ceiling joists
        x = X0 + 0.3 + i * 0.58
        L.box_span(f"Joist_{i:02}", x - 0.025, x + 0.025, Y0, Y1, H - 0.18, H, m["joist"], room)

    # Back wall with a window opening.
    L.box_span("Wall_Back_L", X0 - T, WIN_X[0], Y1, Y1 + T, 0, H, m["plaster"], room)
    L.box_span("Wall_Back_R", WIN_X[1], X1 + T, Y1, Y1 + T, 0, H, m["plaster"], room)
    L.box_span("Wall_Back_Below", WIN_X[0], WIN_X[1], Y1, Y1 + T, 0, WIN_Z[0], m["plaster"], room)
    L.box_span("Wall_Back_Above", WIN_X[0], WIN_X[1], Y1, Y1 + T, WIN_Z[1], H, m["plaster"], room)

    # Left wall with the side door opening.
    L.box_span("Wall_Left_Back", X0 - T, X0, DOOR_Y[1], Y1, 0, H, m["plaster"], room)
    L.box_span("Wall_Left_Front", X0 - T, X0, Y0, DOOR_Y[0], 0, H, m["plaster"], room)
    L.box_span("Wall_Left_Above", X0 - T, X0, DOOR_Y[0], DOOR_Y[1], DOOR_H, H, m["plaster"], room)

    # Right wall: bare cinder block.
    L.box_span("Wall_Right", X1, X1 + T, Y0, Y1, 0, H, m["block"], room)

    # Front wall around the garage door.
    L.box_span("Wall_Front_L", X0 - T, GARAGE_X[0], Y0 - T, Y0, 0, H, m["plaster"], room)
    L.box_span("Wall_Front_R", GARAGE_X[1], X1 + T, Y0 - T, Y0, 0, H, m["plaster"], room)
    L.box_span("Wall_Front_Above", GARAGE_X[0], GARAGE_X[1], Y0 - T, Y0, GARAGE_H, H, m["plaster"], room)


def build_garage_door(m, root):
    grp = L.empty("GarageDoor", root)
    panels, gap = 4, 0.01
    ph = (GARAGE_H - gap * (panels - 1)) / panels
    y = Y0 - 0.05
    for i in range(panels):
        z0 = i * (ph + gap)
        L.box_span(f"GarageDoor_Panel_{i}", GARAGE_X[0], GARAGE_X[1], y - 0.02, y + 0.02, z0, z0 + ph, m["garage_door"], grp)
    # Row of small windows in the top panel.
    z0 = (panels - 1) * (ph + gap) + 0.12
    for i in range(4):
        cx = GARAGE_X[0] + 0.6 + i * 1.2
        L.box_span(f"GarageDoor_Window_{i}", cx - 0.42, cx + 0.42, y + 0.015, y + 0.03, z0, z0 + ph - 0.24, m["glass"], grp)
    # Tracks: vertical along the opening, then horizontal under the ceiling.
    for side, x in (("L", GARAGE_X[0] - 0.06), ("R", GARAGE_X[1] + 0.06)):
        L.box_span(f"GarageDoor_TrackV_{side}", x - 0.03, x + 0.03, Y0 + 0.02, Y0 + 0.1, 0, GARAGE_H + 0.15, m["steel_shelf"], grp)
        L.box_span(f"GarageDoor_TrackH_{side}", x - 0.03, x + 0.03, Y0 + 0.02, Y0 + 2.7, GARAGE_H + 0.15, GARAGE_H + 0.21, m["steel_shelf"], grp)
    # Opener: rail from the door to a motor unit hanging from the ceiling.
    L.box_span("GarageDoor_OpenerRail", -0.03, 0.03, Y0 + 0.05, Y0 + 3.0, GARAGE_H + 0.25, GARAGE_H + 0.29, m["steel_shelf"], grp)
    L.box("GarageDoor_OpenerMotor", (0.38, 0.5, 0.18), (0, Y0 + 3.2, GARAGE_H + 0.3), m["light_housing"], grp)
    L.box_span("GarageDoor_OpenerHanger", -0.02, 0.02, Y0 + 3.15, Y0 + 3.2, GARAGE_H + 0.39, H - 0.18, m["steel_dark"], grp)


def build_window(m, root):
    grp = L.empty("Window", root)
    t, y0, y1 = 0.06, Y1 + 0.02, Y1 + 0.12
    (x0, x1), (z0, z1) = WIN_X, WIN_Z
    L.box_span("Window_Frame_Bottom", x0, x1, y0, y1, z0, z0 + t, m["trim"], grp)
    L.box_span("Window_Frame_Top", x0, x1, y0, y1, z1 - t, z1, m["trim"], grp)
    L.box_span("Window_Frame_Left", x0, x0 + t, y0, y1, z0, z1, m["trim"], grp)
    L.box_span("Window_Frame_Right", x1 - t, x1, y0, y1, z0, z1, m["trim"], grp)
    L.box_span("Window_Mullion_V", -0.02, 0.02, y0 + 0.02, y1 - 0.02, z0, z1, m["trim"], grp)
    L.box_span("Window_Mullion_H", x0, x1, y0 + 0.02, y1 - 0.02, (z0 + z1) / 2 - 0.02, (z0 + z1) / 2 + 0.02, m["trim"], grp)
    L.box_span("Window_Glass", x0 + t, x1 - t, Y1 + 0.07, Y1 + 0.075, z0 + t, z1 - t, m["glass"], grp)
    L.box_span("Window_Sill", x0 - 0.08, x1 + 0.08, Y1 - 0.06, Y1 + 0.02, z0 - 0.03, z0, m["trim"], grp)


def bench(name, x0, x1, y0, y1, m, parent, against="y"):
    """Workbench: wooden top, painted steel legs, plywood lower shelf and back apron."""
    grp = L.empty(name, parent)
    L.box_span(f"{name}_Top", x0, x1, y0, y1, BENCH_TOP - 0.05, BENCH_TOP, m["benchtop"], grp)
    L.box_span(f"{name}_Shelf", x0 + 0.04, x1 - 0.04, y0 + 0.04, y1 - 0.04, 0.17, 0.19, m["plywood"], grp)
    length = (x1 - x0) if against == "y" else (y1 - y0)
    legs = max(2, math.ceil(length / 1.5) + 1)
    for i in range(legs):
        f = i / (legs - 1)
        for j, side in enumerate((0, 1)):
            if against == "y":
                lx = x0 + 0.05 + f * (x1 - x0 - 0.1)
                ly = (y0 + 0.05) if side == 0 else (y1 - 0.05)
            else:
                ly = y0 + 0.05 + f * (y1 - y0 - 0.1)
                lx = (x0 + 0.05) if side == 0 else (x1 - 0.05)
            L.box(f"{name}_Leg_{i}_{j}", (0.05, 0.05, BENCH_TOP - 0.05), (lx, ly, (BENCH_TOP - 0.05) / 2), m["steel_dark"], grp)
    if against == "y":
        L.box_span(f"{name}_Apron", x0, x1, y1 - 0.03, y1, BENCH_TOP - 0.15, BENCH_TOP - 0.05, m["steel_dark"], grp)
    else:
        ax = x0 if x0 < 0 else x1 - 0.03
        L.box_span(f"{name}_Apron", ax, ax + 0.03, y0, y1, BENCH_TOP - 0.15, BENCH_TOP - 0.05, m["steel_dark"], grp)
    return grp


def drawer_cabinet(name, x0, x1, y0, y1, m, parent, front="-y", drawers=4):
    grp = L.empty(name, parent)
    top = BENCH_TOP - 0.06
    L.box_span(f"{name}_Body", x0, x1, y0, y1, 0.0, top, m["cabinet"], grp)
    h = (top - 0.06) / drawers
    for i in range(drawers):
        z0 = 0.04 + i * h
        if front == "-y":
            L.box_span(f"{name}_Drawer_{i}", x0 + 0.02, x1 - 0.02, y0 - 0.01, y0, z0 + 0.01, z0 + h - 0.01, m["cabinet"], grp)
            L.box_span(f"{name}_Handle_{i}", (x0 + x1) / 2 - 0.08, (x0 + x1) / 2 + 0.08, y0 - 0.03, y0 - 0.01, z0 + h - 0.06, z0 + h - 0.04, m["chrome"], grp)
        else:  # front faces +x (left wall) or -x (right wall)
            fx = x1 if front == "+x" else x0
            s = 1 if front == "+x" else -1
            L.box_span(f"{name}_Drawer_{i}", min(fx, fx + s * 0.01), max(fx, fx + s * 0.01), y0 + 0.02, y1 - 0.02, z0 + 0.01, z0 + h - 0.01, m["cabinet"], grp)
            hx = fx + s * 0.02
            L.box_span(f"{name}_Handle_{i}", min(hx - 0.01, hx + 0.01), max(hx - 0.01, hx + 0.01), (y0 + y1) / 2 - 0.08, (y0 + y1) / 2 + 0.08, z0 + h - 0.06, z0 + h - 0.04, m["chrome"], grp)
    return grp


def build_benches(m, root):
    grp = L.empty("Workbenches", root)
    bench("Bench_Back", X0, X1, Y1 - BENCH_DEPTH, Y1, m, grp, "y")
    bench("Bench_Left", X0, X0 + BENCH_DEPTH, -1.3, Y1 - BENCH_DEPTH, m, grp, "x")
    bench("Bench_Right", X1 - BENCH_DEPTH, X1, -1.5, BIN_SHELF_Y[0] - 0.07, m, grp, "x")
    drawer_cabinet("Cabinet_Back", -2.0, -1.4, Y1 - BENCH_DEPTH + 0.03, Y1 - 0.04, m, grp, "-y")
    drawer_cabinet("Cabinet_Left", X0 + 0.04, X0 + BENCH_DEPTH - 0.03, 0.4, 1.0, m, grp, "+x")


def build_table(m, root):
    """The build table in the middle of the room, where robots are assembled."""
    grp = L.empty("BuildTable", root)
    cx, cy, sx, sy = 0.0, 0.25, 2.0, 1.1
    x0, x1, y0, y1 = cx - sx / 2, cx + sx / 2, cy - sy / 2, cy + sy / 2
    L.box_span("BuildTable_Top", x0, x1, y0, y1, BENCH_TOP - 0.06, BENCH_TOP, m["benchtop"], grp)
    L.box_span("BuildTable_Mat", x0 + 0.2, x1 - 0.2, y0 + 0.15, y1 - 0.15, BENCH_TOP, BENCH_TOP + 0.004, m["mat_rubber"], grp)
    for i, (lx, ly) in enumerate(((x0 + 0.08, y0 + 0.08), (x1 - 0.08, y0 + 0.08), (x0 + 0.08, y1 - 0.08), (x1 - 0.08, y1 - 0.08))):
        L.box(f"BuildTable_Leg_{i}", (0.08, 0.08, BENCH_TOP - 0.06), (lx, ly, (BENCH_TOP - 0.06) / 2), m["benchtop"], grp)
    L.box_span("BuildTable_RailX0", x0 + 0.08, x1 - 0.08, y0 + 0.06, y0 + 0.1, 0.15, 0.2, m["benchtop"], grp)
    L.box_span("BuildTable_RailX1", x0 + 0.08, x1 - 0.08, y1 - 0.1, y1 - 0.06, 0.15, 0.2, m["benchtop"], grp)
    L.box_span("BuildTable_Shelf", x0 + 0.1, x1 - 0.1, y0 + 0.1, y1 - 0.1, 0.2, 0.22, m["plywood"], grp)


def build_lights(m, root):
    grp = L.empty("CeilingLights", root)
    for i, (x, y) in enumerate(((-1.4, -1.0), (1.4, -1.0), (-1.4, 1.4), (1.4, 1.4))):
        z = H - 0.45
        L.box(f"Light_{i}_Housing", (1.25, 0.18, 0.06), (x, y, z + 0.03), m["light_housing"], grp)
        L.box(f"Light_{i}_Tube", (1.2, 0.12, 0.02), (x, y, z - 0.01), m["light_tube"], grp)
        for s in (-0.5, 0.5):
            L.box(f"Light_{i}_Chain_{'L' if s < 0 else 'R'}", (0.01, 0.01, 0.39), (x + s, y, z + 0.255), m["steel_dark"], grp)


def build_bin_shelf(m, root):
    """Steel shelving against the right wall, next to the back bench, holding the part bins (4 rows x 6)."""
    grp = L.empty("BinShelf", root)
    x0, x1 = X1 - 0.45, X1 - 0.02
    y0, y1 = BIN_SHELF_Y
    levels = [0.08, 0.52, 0.96, 1.40, 1.84]
    for i, (px, py) in enumerate(((x0, y0), (x1, y0), (x0, y1), (x1, y1))):
        L.box(f"BinShelf_Post_{i}", (0.035, 0.035, 2.1), (px + (0.0175 if px == x0 else -0.0175), py + (0.0175 if py == y0 else -0.0175), 1.05), m["steel_shelf"], grp)
    for i, z in enumerate(levels):
        L.box_span(f"BinShelf_Level_{i}", x0, x1, y0, y1, z, z + 0.025, m["steel_shelf"], grp)

    bins = L.empty("PartBins", grp)
    n, bw, bd, bh = 6, 0.28, 0.38, 0.22
    pitch = (y1 - y0 - 0.06) / n
    index = 0
    for row in range(4):
        z = levels[row] + 0.025
        for col in range(n):
            cy = y0 + 0.03 + pitch * (col + 0.5)
            part_bin(f"Bin_{index:02}", x0 + 0.02, cy, z, bw, bd, bh, m, bins)
            index += 1


BIN_COLORS = [(0.11, 0.33, 0.75), (0.78, 0.16, 0.14), (0.95, 0.68, 0.08), (0.13, 0.55, 0.25)]


def part_bin(name, front_x, cy, z, w, d, h, m, parent):
    """Open-front stacking bin facing -x (into the room), like the plastic hardware bins in a shop."""
    grp = L.empty(name, parent, (front_x, cy, z))
    color = BIN_COLORS[int(name[-2:]) // 6 % len(BIN_COLORS)]
    mat = L.material(f"Plastic_Bin_{int(name[-2:]) // 6}", color=color, rough=0.45)
    t = 0.008
    L.box_span(f"{name}_Bottom", 0, d, -w / 2, w / 2, 0, t, mat, grp)
    L.box_span(f"{name}_Back", d - t, d, -w / 2, w / 2, 0, h, mat, grp)
    L.box_span(f"{name}_SideL", 0, d, -w / 2, -w / 2 + t, 0, h, mat, grp)
    L.box_span(f"{name}_SideR", 0, d, w / 2 - t, w / 2, 0, h, mat, grp)
    L.box_span(f"{name}_Lip", 0, t, -w / 2, w / 2, 0, h * 0.45, mat, grp)
    L.box_span(f"{name}_Label", -0.002, 0, -w / 2 + 0.03, w / 2 - 0.03, h * 0.12, h * 0.38, m["label"], grp)


def build(include=("room", "garage_door", "window", "benches", "table", "lights", "bin_shelf")):
    L.clear()
    m = materials()
    root = L.empty("Workshop")
    steps = {
        "room": build_room, "garage_door": build_garage_door, "window": build_window,
        "benches": build_benches, "table": build_table, "lights": build_lights, "bin_shelf": build_bin_shelf,
    }
    for key in include:
        steps[key](m, root)
    return root, m


if __name__ in ("__main__", "<run_path>", "builtins"):
    build()
    L.world_uvs()
