"""
Decoration pass for the workshop: pegboards with hand tools, wall shelves, side door,
bench-top props and floor clutter. Run after workshop_build.build().
"""
import os

import bpy
import numpy as np

import workshop_lib as L
from workshop_build import BENCH_TOP, X0, X1, Y0, Y1, DOOR_Y, DOOR_H

PEGBOARD_TEXTURE = os.path.join(L.TEXTURES, "pegboard", "pegboard_diff.png")


def make_pegboard_texture():
    """Hardboard colour with a hole every inch: 512 px = 10 holes = 0.254 m."""
    size, holes = 512, 10
    os.makedirs(os.path.dirname(PEGBOARD_TEXTURE), exist_ok=True)
    yy, xx = np.mgrid[0:size, 0:size]
    pitch = size / holes
    dx = (xx % pitch) - pitch / 2
    dy = (yy % pitch) - pitch / 2
    hole = (dx * dx + dy * dy) < (pitch * 0.14) ** 2
    rng = np.random.default_rng(3)
    noise = rng.normal(0, 0.015, (size, size))
    base = np.stack([0.62 + noise, 0.47 + noise, 0.30 + noise, np.ones_like(noise)], axis=-1)
    base[hole] = (0.05, 0.04, 0.03, 1.0)
    img = bpy.data.images.new("pegboard_diff", size, size)
    img.pixels = np.clip(base, 0, 1).astype(np.float32).ravel()
    img.filepath_raw = PEGBOARD_TEXTURE
    img.file_format = "PNG"
    img.save()
    return img


def decor_materials():
    make_pegboard_texture()
    peg = bpy.data.materials.get("Wood_Pegboard") or bpy.data.materials.new("Wood_Pegboard")
    peg.use_nodes = True
    nt = peg.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(PEGBOARD_TEXTURE, check_existing=True)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.75
    L.MATERIAL_TEXTURES["Wood_Pegboard"] = ("pegboard", 0.254)
    return {
        "pegboard": peg,
        "wood_handle": L.material("Wood_Handle", color=(0.55, 0.36, 0.18), rough=0.5),
        "steel": L.material("Metal_ToolSteel", color=(0.55, 0.56, 0.58), rough=0.3, metal=1.0),
        "red": L.material("Plastic_Red", color=(0.72, 0.08, 0.06), rough=0.4),
        "yellow": L.material("Plastic_Yellow", color=(0.95, 0.72, 0.05), rough=0.4),
        "blue": L.material("Plastic_Blue", color=(0.08, 0.25, 0.65), rough=0.4),
        "green": L.material("Plastic_Green", color=(0.12, 0.45, 0.2), rough=0.4),
        "cardboard": L.material("Cardboard", color=(0.55, 0.40, 0.24), rough=0.85),
        "tool_chest": L.material("Paint_ToolChestRed", color=(0.62, 0.05, 0.04), rough=0.3, metal=0.4),
        "can_grey": L.material("Metal_PaintCan", color=(0.7, 0.71, 0.72), rough=0.35, metal=1.0),
        "jar": L.material("Glass_Jar", color=(0.8, 0.85, 0.8), rough=0.1, alpha=0.45),
        "trash": L.material("Plastic_TrashCan", color=(0.18, 0.2, 0.22), rough=0.6),
    }


# ---------------------------------------------------------------- hand tools (local frame)
# Each tool is built hanging on a board: local X along the board, Z up, -Y out of the wall.

def hammer(name, x, z, m, parent):
    L.box(f"{name}_Handle", (0.028, 0.022, 0.30), (x, -0.045, z - 0.15), m["wood_handle"], parent)
    L.box(f"{name}_Head", (0.12, 0.028, 0.03), (x, -0.045, z + 0.01), m["steel"], parent)


def screwdriver(name, x, z, color, m, parent):
    L.cylinder(f"{name}_Handle", 0.013, 0.10, (x, -0.04, z), m[color], parent, segments=8)
    L.cylinder(f"{name}_Shaft", 0.0035, 0.13, (x, -0.04, z - 0.115), m["steel"], parent, segments=6)


def wrench(name, x, z, length, m, parent):
    L.box(f"{name}_Bar", (0.016, 0.008, length), (x, -0.035, z - length / 2), m["steel"], parent)
    L.box(f"{name}_Jaw", (0.03, 0.008, 0.028), (x, -0.035, z), m["steel"], parent)
    L.cylinder(f"{name}_Ring", 0.014, 0.008, (x, -0.035, z - length), m["steel"], parent, rotation=(90, 0, 0), segments=10)


def pliers(name, x, z, m, parent):
    for side, angle in (("A", 7), ("B", -7)):
        L.box(f"{name}_Handle{side}", (0.016, 0.012, 0.13), (x + (0.012 if angle > 0 else -0.012), -0.04, z - 0.1), m["red"], parent, rotation=(0, angle, 0))
    L.box(f"{name}_Nose", (0.022, 0.014, 0.07), (x, -0.04, z), m["steel"], parent)


def handsaw(name, x, z, m, parent):
    L.box(f"{name}_Blade", (0.42, 0.002, 0.10), (x, -0.03, z - 0.06), m["steel"], parent, rotation=(0, -4, 0))
    L.box(f"{name}_Handle", (0.11, 0.025, 0.12), (x + 0.25, -0.035, z - 0.04), m["wood_handle"], parent)


def tape_measure(name, x, z, m, parent):
    L.box(f"{name}_Body", (0.07, 0.035, 0.07), (x, -0.04, z), m["yellow"], parent)


def peg(name, x, z, m, parent):
    L.cylinder(name, 0.003, 0.05, (x, -0.025, z), m["steel"], parent, rotation=(90, 0, 0), segments=6)


def pegboard(name, width, height, location, rotation_z, tools, m, parent):
    """A pegboard panel with tools hung from pegs. `location` is the bottom-centre on the wall."""
    grp = L.empty(name, parent, location)
    grp.rotation_euler = (0, 0, rotation_z)
    L.box_span(f"{name}_Board", -width / 2, width / 2, -0.026, -0.02, 0, height, m["pegboard"], grp)
    L.box_span(f"{name}_FrameTop", -width / 2, width / 2, -0.03, -0.02, height, height + 0.02, m["wood_handle"], grp)
    L.box_span(f"{name}_FrameBottom", -width / 2, width / 2, -0.03, -0.02, -0.02, 0, m["wood_handle"], grp)
    for i, (kind, x, z, *extra) in enumerate(tools):
        tname = f"{name}_{kind}_{i}"
        peg(f"{tname}_Peg", x, z + 0.02, m, grp)
        if kind == "hammer":
            hammer(tname, x, z, m, grp)
        elif kind == "screwdriver":
            screwdriver(tname, x, z, extra[0], m, grp)
        elif kind == "wrench":
            wrench(tname, x, z, extra[0], m, grp)
        elif kind == "pliers":
            pliers(tname, x, z, m, grp)
        elif kind == "saw":
            handsaw(tname, x, z, m, grp)
        elif kind == "tape":
            tape_measure(tname, x, z, m, grp)
    return grp


def wall_shelf(name, width, location, rotation_z, items, m, base, parent):
    """A plank on two brackets with props on top. `items` are (kind, x) pairs."""
    grp = L.empty(name, parent, location)
    grp.rotation_euler = (0, 0, rotation_z)
    L.box_span(f"{name}_Plank", -width / 2, width / 2, -0.25, -0.02, 0, 0.025, base["plywood"], grp)
    for s in (-1, 1):
        L.box_span(f"{name}_Bracket_{'L' if s < 0 else 'R'}", s * width / 2 * 0.8 - 0.01, s * width / 2 * 0.8 + 0.01, -0.2, -0.02, -0.18, 0, base["steel_dark"], grp)
    for i, (kind, x) in enumerate(items):
        n = f"{name}_{kind}_{i}"
        if kind == "paint":
            L.cylinder(n, 0.085, 0.19, (x, -0.13, 0.12), m["can_grey"], grp, segments=14)
            L.cylinder(f"{n}_Label", 0.0865, 0.11, (x, -0.13, 0.11), m[["red", "blue", "green", "yellow"][i % 4]], grp, segments=14)
        elif kind == "jar":
            L.cylinder(n, 0.05, 0.14, (x, -0.13, 0.095), m["jar"], grp, segments=10)
            L.cylinder(f"{n}_Lid", 0.052, 0.02, (x, -0.13, 0.175), m["red"], grp, segments=10)
        elif kind == "spray":
            L.cylinder(n, 0.033, 0.2, (x, -0.13, 0.125), m[["yellow", "blue", "red"][i % 3]], grp, segments=10)
        elif kind == "box":
            L.box(n, (0.3, 0.2, 0.16), (x, -0.13, 0.105), m["cardboard"], grp)
    return grp


BACK_TOOLS_L = [
    ("hammer", -0.8, 0.7), ("hammer", -0.62, 0.7),
    ("screwdriver", -0.42, 0.72, "red"), ("screwdriver", -0.34, 0.72, "red"), ("screwdriver", -0.26, 0.72, "yellow"),
    ("screwdriver", -0.18, 0.72, "yellow"), ("screwdriver", -0.10, 0.72, "blue"), ("screwdriver", -0.02, 0.72, "blue"),
    ("pliers", 0.14, 0.72), ("pliers", 0.24, 0.72), ("pliers", 0.34, 0.72),
    ("saw", -0.55, 0.3), ("tape", 0.2, 0.3), ("tape", 0.35, 0.3),
]
BACK_TOOLS_R = [
    ("wrench", -0.55, 0.75, 0.14), ("wrench", -0.47, 0.75, 0.16), ("wrench", -0.39, 0.75, 0.18), ("wrench", -0.31, 0.75, 0.20),
    ("wrench", -0.23, 0.75, 0.22), ("wrench", -0.15, 0.75, 0.24), ("wrench", -0.07, 0.75, 0.26),
    ("hammer", 0.2, 0.72), ("pliers", 0.4, 0.72), ("pliers", 0.5, 0.72),
    ("screwdriver", -0.5, 0.3, "green"), ("screwdriver", -0.42, 0.3, "green"), ("tape", 0.0, 0.3), ("saw", 0.25, 0.32),
]
LEFT_TOOLS = [
    ("wrench", -0.7, 0.75, 0.2), ("wrench", -0.6, 0.75, 0.22), ("wrench", -0.5, 0.75, 0.24),
    ("hammer", -0.25, 0.72), ("screwdriver", 0.0, 0.72, "red"), ("screwdriver", 0.08, 0.72, "blue"),
    ("pliers", 0.3, 0.72), ("pliers", 0.42, 0.72), ("saw", 0.2, 0.3), ("tape", -0.6, 0.3),
]


def build_side_door(m, base, root):
    grp = L.empty("SideDoor", root)
    (y0, y1), x = DOOR_Y, X0 - 0.06
    L.box_span("SideDoor_Slab", x - 0.022, x + 0.022, y0 + 0.01, y1 - 0.01, 0.01, DOOR_H - 0.01, base["door"], grp)
    L.cylinder("SideDoor_Knob", 0.028, 0.06, (x + 0.05, y1 - 0.09, 1.0), base["chrome"], grp, rotation=(0, 90, 0), segments=10)
    for name, (a, b, c, d) in {
        "SideDoor_Trim_Back": (y1, y1 + 0.07, 0, DOOR_H + 0.07),
        "SideDoor_Trim_Front": (y0 - 0.07, y0, 0, DOOR_H + 0.07),
        "SideDoor_Trim_Top": (y0, y1, DOOR_H, DOOR_H + 0.07),
    }.items():
        L.box_span(name, X0, X0 + 0.015, a, b, c, d, base["trim"], grp)


def build_tool_chest(m, base, root):
    """Red rolling tool chest by the right wall, near the garage door."""
    grp = L.empty("ToolChest", root)
    x0, x1, y0, y1 = X1 - 0.5, X1 - 0.02, -2.75, -1.95
    L.box_span("ToolChest_Body", x0, x1, y0, y1, 0.1, 0.95, m["tool_chest"], grp)
    L.box_span("ToolChest_Top", x0 + 0.02, x1 - 0.02, y0 + 0.03, y1 - 0.03, 0.95, 1.33, m["tool_chest"], grp)
    for i in range(6):
        z = 0.16 + i * 0.13
        L.box_span(f"ToolChest_Drawer_{i}", x0 - 0.01, x0, y0 + 0.02, y1 - 0.02, z, z + 0.11, m["tool_chest"], grp)
        L.box_span(f"ToolChest_Handle_{i}", x0 - 0.03, x0 - 0.01, y0 + 0.06, y1 - 0.06, z + 0.08, z + 0.095, base["chrome"], grp)
    for i in range(3):
        z = 1.0 + i * 0.1
        L.box_span(f"ToolChest_TopDrawer_{i}", x0 + 0.01, x0 + 0.02, y0 + 0.05, y1 - 0.05, z, z + 0.08, m["tool_chest"], grp)
    for i, (cx, cy) in enumerate(((x0 + 0.06, y0 + 0.06), (x1 - 0.06, y0 + 0.06), (x0 + 0.06, y1 - 0.06), (x1 - 0.06, y1 - 0.06))):
        L.cylinder(f"ToolChest_Caster_{i}", 0.045, 0.03, (cx, cy, 0.045), base["black_plastic"], grp, rotation=(0, 90, 0), segments=10)


def build_floor_clutter(m, base, root):
    grp = L.empty("FloorClutter", root)
    L.box("Box_0", (0.55, 0.45, 0.4), (X0 + 0.35, Y0 + 0.3, 0.2), m["cardboard"], grp)
    L.box("Box_1", (0.45, 0.4, 0.35), (X0 + 0.38, Y0 + 0.32, 0.575), m["cardboard"], grp, rotation=(0, 0, 8))
    L.box("Box_2", (0.5, 0.42, 0.3), (X0 + 0.95, Y0 + 0.28, 0.15), m["cardboard"], grp, rotation=(0, 0, -5))
    L.cylinder("TrashCan", 0.2, 0.62, (X0 + 0.28, -1.5, 0.31), m["trash"], grp, segments=14)
    L.cylinder("TrashCan_Rim", 0.215, 0.04, (X0 + 0.28, -1.5, 0.61), m["trash"], grp, segments=14)
    # Fire extinguisher on its wall hook, right wall near the garage door.
    L.cylinder("FireExtinguisher", 0.075, 0.42, (X1 - 0.1, Y0 + 0.25, 0.95), m["red"], grp, segments=12)
    L.box("FireExtinguisher_Head", (0.05, 0.12, 0.08), (X1 - 0.1, Y0 + 0.25, 1.2), base["black_plastic"], grp)


def build_bench_props(m, base, root):
    grp = L.empty("BenchProps", root)
    top = BENCH_TOP
    # Bench vise on the back bench, right of the window.
    vx, vy = 1.75, Y1 - 0.58
    L.box("Vise_Base", (0.16, 0.2, 0.05), (vx, vy + 0.06, top + 0.025), base["cabinet"], grp)
    L.box("Vise_Body", (0.14, 0.2, 0.11), (vx, vy + 0.03, top + 0.105), base["cabinet"], grp)
    L.box("Vise_JawFixed", (0.16, 0.03, 0.06), (vx, vy - 0.08, top + 0.19), base["steel_dark"], grp)
    L.box("Vise_JawMoving", (0.16, 0.03, 0.06), (vx, vy - 0.14, top + 0.19), base["steel_dark"], grp)
    L.cylinder("Vise_Screw", 0.012, 0.22, (vx, vy - 0.2, top + 0.12), m["steel"], grp, rotation=(90, 0, 0), segments=8)
    L.cylinder("Vise_Handle", 0.008, 0.2, (vx, vy - 0.31, top + 0.12), m["steel"], grp, rotation=(0, 90, 0), segments=6)
    # Red toolbox on the left bench.
    L.box("Toolbox_Body", (0.25, 0.5, 0.2), (X0 + 0.32, 1.9, top + 0.1), m["red"], grp)
    L.box("Toolbox_Handle", (0.03, 0.3, 0.04), (X0 + 0.32, 1.9, top + 0.23), base["black_plastic"], grp)
    # Cordless drill on the right bench.
    L.box("Drill_Body", (0.06, 0.2, 0.07), (X1 - 0.3, -0.25, top + 0.2), m["yellow"], grp)
    L.box("Drill_Grip", (0.05, 0.05, 0.14), (X1 - 0.3, -0.19, top + 0.12), base["black_plastic"], grp)
    L.box("Drill_Battery", (0.08, 0.1, 0.06), (X1 - 0.3, -0.19, top + 0.03), base["black_plastic"], grp)
    L.cylinder("Drill_Chuck", 0.016, 0.06, (X1 - 0.3, -0.38, top + 0.2), m["steel"], grp, rotation=(90, 0, 0), segments=8)
    # Power strip along the back wall above the bench.
    L.box_span("PowerStrip", -2.6, -1.4, Y1 - 0.03, Y1, top + 0.15, top + 0.2, base["light_housing"], grp)
    # Clamp lamp on the right bench.
    L.cylinder("BenchLamp_Base", 0.07, 0.03, (X1 - 0.25, -0.9, top + 0.015), base["black_plastic"], grp, segments=12)
    L.cylinder("BenchLamp_Arm", 0.008, 0.45, (X1 - 0.25, -0.9, top + 0.24), base["steel_dark"], grp, segments=6)
    L.cylinder("BenchLamp_Shade", 0.07, 0.12, (X1 - 0.33, -0.9, top + 0.44), base["black_plastic"], grp, rotation=(0, -55, 0), segments=12)


def build_decor(base, root):
    m = decor_materials()
    deco = L.empty("Decor", root)
    pegboard("Pegboard_BackL", 1.9, 0.9, (-1.85, Y1, 1.05), 0.0, BACK_TOOLS_L, m, deco)
    pegboard("Pegboard_BackR", 1.4, 0.9, (1.75, Y1, 1.05), 0.0, BACK_TOOLS_R, m, deco)
    pegboard("Pegboard_Left", 1.8, 0.9, (X0, 0.6, 1.05), 1.5708, LEFT_TOOLS, m, deco)
    wall_shelf("Shelf_BackL", 1.9, (-1.85, Y1, 2.2), 0.0,
               [("paint", -0.75), ("paint", -0.55), ("jar", -0.3), ("jar", -0.18), ("jar", -0.06), ("spray", 0.15), ("spray", 0.23), ("box", 0.6)], m, base, deco)
    wall_shelf("Shelf_BackR", 1.4, (1.75, Y1, 2.2), 0.0,
               [("box", -0.45), ("paint", -0.05), ("paint", 0.15), ("spray", 0.4), ("spray", 0.48)], m, base, deco)
    build_side_door(m, base, deco)
    build_tool_chest(m, base, deco)
    build_floor_clutter(m, base, deco)
    build_bench_props(m, base, deco)
    return m
