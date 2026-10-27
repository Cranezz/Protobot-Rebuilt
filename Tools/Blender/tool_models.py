"""
Builds the hand-held tool models (hack saw, spray gun, cordless drill) and exports each to
Assets/Protobot/Art/Tools/<Name>.fbx. Real-world size, pointing along +Y (forward) with +Z up,
which Unity imports as forward/up.

Named parts the game looks for:
  SprayGun: "PaintChamber"  (the paint cup, tinted with the loaded colour)
  Drill:    "Bit"           (built 1 cm across; scaled to the chosen bit size)

Run inside Blender: exec(open(r"<project>\\Tools\\Blender\\tool_models.py").read())
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT = r"C:\Dev\ProtobotRebuiltV2\ProtobotRebuiltV2"
OUT = os.path.join(PROJECT, r"Assets\Protobot\Art\Tools")
COLLECTION = "ToolModels"


def collection():
    col = bpy.data.collections.get(COLLECTION)
    if col is None:
        col = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(col)
    return col


def clear():
    col = collection()
    for obj in list(col.all_objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def material(name, color, metal=0.0, rough=0.5):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    mat.diffuse_color = (*color, 1.0)
    return mat


def link(obj, mat, parent, smooth=False):
    collection().objects.link(obj)
    obj.data.materials.append(mat)
    obj.parent = parent
    if smooth:
        for p in obj.data.polygons:
            p.use_smooth = True
    return obj


def box(name, size, loc, mat, parent, rot=(0, 0, 0), bevel=0.0):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = loc
    obj.rotation_euler = [math.radians(a) for a in rot]
    return link(obj, mat, parent)


def cyl(name, r1, r2, depth, loc, mat, parent, rot=(0, 0, 0), segments=16):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=r1, radius2=r2, depth=depth)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = loc
    obj.rotation_euler = [math.radians(a) for a in rot]
    obj = link(obj, mat, parent, smooth=True)
    for p in obj.data.polygons:
        if abs(p.normal.z) > 0.9:
            p.use_smooth = False
    return obj


def tube(name, points, radius, mat, parent, segments=10):
    """A bent bar through the given points (a sweep of a circle)."""
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = radius
    curve.bevel_resolution = 2
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for i, p in enumerate(points):
        spline.points[i].co = (*p, 1.0)
    obj = bpy.data.objects.new(name + "_curve", curve)
    collection().objects.link(obj)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    bpy.data.objects.remove(obj, do_unlink=True)
    result = bpy.data.objects.new(name, mesh)
    return link(result, mat, parent, smooth=True)


def empty(name):
    obj = bpy.data.objects.new(name, None)
    collection().objects.link(obj)
    return obj


def hack_saw():
    root = empty("HackSaw")
    frame = material("Saw_Frame", (0.08, 0.25, 0.62), metal=0.6, rough=0.35)
    blade = material("Saw_Blade", (0.72, 0.73, 0.75), metal=1.0, rough=0.3)
    grip = material("Saw_Grip", (0.04, 0.04, 0.045), rough=0.75)
    # Frame: front post, top bar, back post (blade runs along +Y at the bottom).
    tube("Saw_FrameBar", [(0, 0.30, 0.0), (0, 0.30, 0.09), (0, 0.0, 0.09), (0, 0.0, 0.02)], 0.0055, frame, root)
    box("Saw_Blade", (0.0008, 0.31, 0.012), (0, 0.15, 0.0), blade, root)
    for i in range(30):
        box(f"Saw_Tooth_{i}", (0.0008, 0.004, 0.003), (0, 0.006 + i * 0.01, -0.0065), blade, root, rot=(45, 0, 0))
    box("Saw_WingNut", (0.004, 0.012, 0.018), (0, 0.312, 0.0), frame, root, bevel=0.001)
    box("Saw_Grip", (0.026, 0.035, 0.11), (0, -0.03, -0.035), grip, root, rot=(-25, 0, 0), bevel=0.006)
    return root


def spray_gun():
    root = empty("SprayGun")
    body = material("Gun_Body", (0.7, 0.71, 0.73), metal=0.9, rough=0.3)
    dark = material("Gun_Dark", (0.06, 0.06, 0.07), rough=0.6)
    cup = material("PaintChamber", (0.8, 0.1, 0.1), rough=0.35)
    cyl("Gun_Body", 0.017, 0.017, 0.12, (0, 0.04, 0), body, root, rot=(90, 0, 0))
    cyl("Gun_AirCap", 0.014, 0.008, 0.025, (0, 0.11, 0), body, root, rot=(-90, 0, 0))
    cyl("Gun_Nozzle", 0.0025, 0.0025, 0.012, (0, 0.128, 0), dark, root, rot=(90, 0, 0), segments=8)
    cyl("Gun_CupNeck", 0.007, 0.007, 0.02, (0, 0.06, 0.025), body, root)
    cyl("PaintChamber", 0.032, 0.03, 0.07, (0, 0.06, 0.07), cup, root, segments=20)
    cyl("Gun_CupLid", 0.034, 0.034, 0.008, (0, 0.06, 0.108), dark, root, segments=20)
    box("Gun_Handle", (0.024, 0.034, 0.11), (0, -0.015, -0.06), dark, root, rot=(-14, 0, 0), bevel=0.006)
    box("Gun_Trigger", (0.006, 0.008, 0.05), (0, 0.02, -0.03), body, root, rot=(10, 0, 0), bevel=0.002)
    return root


def drill():
    root = empty("Drill")
    yellow = material("Drill_Body", (0.95, 0.72, 0.05), rough=0.45)
    black = material("Drill_Black", (0.05, 0.05, 0.055), rough=0.7)
    steel = material("Drill_Steel", (0.7, 0.71, 0.73), metal=1.0, rough=0.3)
    cyl("Drill_Motor", 0.03, 0.028, 0.12, (0, 0.0, 0.0), yellow, root, rot=(90, 0, 0), segments=20)
    cyl("Drill_Cap", 0.028, 0.02, 0.02, (0, -0.07, 0.0), black, root, rot=(90, 0, 0), segments=20)
    cyl("Drill_Chuck", 0.016, 0.011, 0.045, (0, 0.08, 0.0), black, root, rot=(-90, 0, 0), segments=16)
    bit = cyl("Bit", 0.005, 0.005, 0.07, (0, 0.135, 0.0), steel, root, rot=(-90, 0, 0), segments=10)
    bit.name = "Bit"
    box("Drill_Grip", (0.034, 0.04, 0.12), (0, -0.01, -0.075), black, root, rot=(-12, 0, 0), bevel=0.008)
    box("Drill_Battery", (0.06, 0.09, 0.035), (0, 0.0, -0.145), black, root, bevel=0.006)
    box("Drill_Trigger", (0.012, 0.012, 0.026), (0, 0.02, -0.035), yellow, root, bevel=0.003)
    return root


def export(root):
    """Applies every transform into the meshes (keeping each object's position) and writes the FBX."""
    objs = [o for o in collection().all_objects if o == root or o.parent == root]
    world = {o.name: o.matrix_world.copy() for o in objs}
    for o in objs:
        if o.type == "MESH":
            mw = world[o.name]
            loc = mw.to_translation()
            o.data.transform(Matrix.Translation(-loc) @ mw)
            o.matrix_parent_inverse = Matrix.Identity(4)
            o.matrix_basis = Matrix.Translation(loc)
    root.matrix_basis = Matrix.Identity(4)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, root.name + ".fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="STRIP")


def build_all():
    clear()
    roots = [hack_saw(), spray_gun(), drill()]
    for i, root in enumerate(roots):
        root.location = (2.0 + i * 0.5, -6.0, 1.0)  # off to the side of the workshop, for looking at
    bpy.context.view_layer.update()
    return roots


if __name__ in ("__main__", "builtins"):
    for r in build_all():
        loc = r.location.copy()
        r.location = (0, 0, 0)
        bpy.context.view_layer.update()
        export(r)
        r.location = loc
