"""
Builds the workshop's scrap bin (a big round plastic trash can, open at the top) and exports it to
Assets/Protobot/Art/Environment/Models/ScrapBin.fbx. Real size: ~0.56 m across, 0.75 m tall.
Origin on the floor at the can's centre; +Y is the front (where the SCRAP label is).

Run inside Blender: exec(open(r"<project>\\Tools\\Blender\\trash_can.py").read())
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT = r"C:\Dev\ProtobotRebuiltV2\ProtobotRebuiltV2"
OUT = os.path.join(PROJECT, r"Assets\Protobot\Art\Environment\Models")
COLLECTION = "ScrapBin"

HEIGHT = 0.75
R_BOTTOM = 0.25
R_TOP = 0.28
WALL = 0.012
SEGMENTS = 40


def collection():
    col = bpy.data.collections.get(COLLECTION)
    if col is None:
        col = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(col)
    return col


def clear():
    for obj in list(collection().all_objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def material(name, color, rough=0.6):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = 0.0
    mat.diffuse_color = (*color, 1.0)
    return mat


def mesh_object(name, bm, mat, parent, smooth=True):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    collection().objects.link(obj)
    obj.data.materials.append(mat)
    obj.parent = parent
    for p in obj.data.polygons:
        p.use_smooth = smooth and abs(p.normal.z) < 0.9
    return obj


def radius_at(z):
    return R_BOTTOM + (R_TOP - R_BOTTOM) * (z / HEIGHT)


def shell():
    """The tub: outer wall, inner wall, floor and a rounded top lip, as one closed mesh."""
    bm = bmesh.new()
    rings = []
    # Profile (radius, height) going up the outside, over the lip and down the inside.
    profile = [(R_BOTTOM - 0.01, 0.0), (R_BOTTOM, 0.012)]
    for k in range(1, 9):
        z = HEIGHT * k / 9
        profile.append((radius_at(z), z))
    profile += [(R_TOP + 0.012, HEIGHT - 0.01), (R_TOP + 0.014, HEIGHT + 0.006), (R_TOP, HEIGHT + 0.012),
                (R_TOP - WALL - 0.002, HEIGHT + 0.004)]
    for k in range(8, 0, -1):
        z = HEIGHT * k / 9
        profile.append((radius_at(z) - WALL, z))
    profile += [(R_BOTTOM - WALL, 0.03), (R_BOTTOM - WALL - 0.01, 0.022)]
    for r, z in profile:
        ring = []
        for i in range(SEGMENTS):
            a = 2 * math.pi * i / SEGMENTS
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for j in range(len(rings) - 1):
        for i in range(SEGMENTS):
            i2 = (i + 1) % SEGMENTS
            bm.faces.new((rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]))
    # Floor outside (bottom) and inside.
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def box(size, loc, rot_z=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(size) * 0.25, segments=2, affect="EDGES")
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(rot_z, 3, "Z"))
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return bm


def rib(angle, z0, z1, width=0.026, depth=0.01):
    """A rib on the outside of the wall at <angle>, hugging the tapered wall from z0 to z1."""
    bm = bmesh.new()
    radial = Vector((math.cos(angle), math.sin(angle), 0))
    side = Vector((-math.sin(angle), math.cos(angle), 0))
    verts = []
    for z in (z0, z1):
        for out in (-0.002, depth):
            for s_ in (-1, 1):
                verts.append(bm.verts.new(radial * (radius_at(z) + out) + side * (width / 2 * s_) + Vector((0, 0, z))))
    v = verts
    for quad in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        bm.faces.new([v[i] for i in quad])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def band(z0, z1, out):
    """A ring band hugging the outside of the wall between two heights."""
    bm = bmesh.new()
    rows = []
    for z, extra in ((z0, 0.0), (z0 + 0.004, out), (z1 - 0.004, out), (z1, 0.0)):
        row = []
        r = radius_at(z) + extra + 0.0015
        for i in range(SEGMENTS):
            a = 2 * math.pi * i / SEGMENTS
            row.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rows.append(row)
    for j in range(len(rows) - 1):
        for i in range(SEGMENTS):
            i2 = (i + 1) % SEGMENTS
            bm.faces.new((rows[j][i], rows[j][i2], rows[j + 1][i2], rows[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def label(text, z, size, mat, parent):
    """Flat lettering on the front of the can (+Y), standing just off the band."""
    curve = bpy.data.curves.new("TrashLabel", "FONT")
    curve.body = text
    curve.size = size
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.extrude = 0.0015
    tmp = bpy.data.objects.new("TrashLabel_tmp", curve)
    collection().objects.link(tmp)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(tmp.evaluated_get(depsgraph))
    bpy.data.objects.remove(tmp, do_unlink=True)
    obj = bpy.data.objects.new("Trash_Label", mesh)
    collection().objects.link(obj)
    obj.data.materials.append(mat)
    obj.parent = parent
    # Text lies in XY facing +Z: stand it up facing +Y, out on the band.
    obj.data.transform(Matrix.Rotation(math.radians(90), 4, "X"))
    obj.data.transform(Matrix.Rotation(math.radians(180), 4, "Z"))
    obj.location = (0, radius_at(z) + 0.012, z)
    return obj


def build():
    clear()
    root = bpy.data.objects.new("ScrapBin", None)
    collection().objects.link(root)
    body = material("Trash_Body", (0.16, 0.17, 0.18), rough=0.55)
    rim = material("Trash_Rim", (0.08, 0.08, 0.09), rough=0.5)
    yellow = material("Trash_Band", (0.95, 0.72, 0.05), rough=0.45)
    ink = material("Trash_Label", (0.03, 0.03, 0.035), rough=0.6)

    mesh_object("Trash_Shell", shell(), body, root)
    # Four moulded ribs down the outside, like a commercial bin (they follow the taper of the wall).
    for k in range(4):
        mesh_object(f"Trash_Rib_{k}", rib(math.pi / 4 + k * math.pi / 2, 0.035, HEIGHT * 0.55), rim, root, smooth=False)
    mesh_object("Trash_Band", band(HEIGHT * 0.56, HEIGHT * 0.74, 0.004), yellow, root)
    mesh_object("Trash_Foot", band(0.0, 0.03, 0.006), rim, root)
    label("SCRAP", HEIGHT * 0.65, 0.085, ink, root)
    bpy.context.view_layer.update()
    return root


def export(root):
    objs = [o for o in collection().all_objects if o == root or o.parent == root]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, "ScrapBin.fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="STRIP")


if __name__ in ("__main__", "builtins"):
    root = build()
    root.location = (0, 0, 0)
    bpy.context.view_layer.update()
    export(root)
    root.location = (3.0, -6.0, 0.0)  # out of the way of the workshop, for looking at
