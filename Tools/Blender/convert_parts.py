"""
Converts the Fusion OBJ exports of the VEX parts into FBX files for Unity.

Run headless:
  blender -b --factory-startup -P convert_parts.py -- <obj_root> <fbx_root> <fusion_state.json> [limit]

Per part it:
  - imports the OBJ (centimetres) with one object per body, and scales it to metres;
  - welds duplicate vertices, merges coplanar triangles and re-triangulates them cleanly
    (fewer triangles, identical shape: only exactly-flat areas are merged);
  - sets smooth/sharp shading by angle so round parts look round and flat faces stay crisp;
  - checks the size against the size Fusion reported, and refuses to write a part that differs;
  - exports an FBX with one child per body and the original material names.
A report (convert_report.json) records sizes, triangle counts and every material colour.
"""
import json
import math
import os
import sys
import time

import bmesh
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
OBJ_ROOT, FBX_ROOT, STATE_PATH = argv[:3]
LIMIT = int(argv[3]) if len(argv) > 3 else 0
REPORT_PATH = os.path.join(FBX_ROOT, "convert_report.json")
SIZE_TOLERANCE_M = 0.0005  # 0.5 mm
PLANAR_ANGLE = math.radians(0.1)
SMOOTH_ANGLE = math.radians(30)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mtl_colors(obj_path):
    colors, current = {}, None
    mtl = os.path.splitext(obj_path)[0] + ".mtl"
    if os.path.exists(mtl):
        with open(mtl, encoding="utf-8", errors="replace") as f:
            for line in f:
                parts = line.split()
                if not parts:
                    continue
                if parts[0] == "newmtl":
                    current = " ".join(parts[1:])
                elif parts[0] == "Kd" and current:
                    colors[current] = [float(v) for v in parts[1:4]]
    return colors


def optimise(obj):
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00001)
    before = len(bm.faces)
    bmesh.ops.dissolve_limit(bm, angle_limit=PLANAR_ANGLE, use_dissolve_boundaries=False,
                             verts=bm.verts, edges=bm.edges, delimit={"MATERIAL"})
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="BEAUTY")
    after = len(bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    custom = mesh.attributes.get("custom_normal")
    if custom is not None:
        mesh.attributes.remove(custom)
    for poly in mesh.polygons:
        poly.use_smooth = True
    # Mark edges sharper than the angle as sharp, so curved surfaces shade smoothly and flat faces stay flat.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    for edge in bm.edges:
        if len(edge.link_faces) != 2 or edge.calc_face_angle(math.pi) > SMOOTH_ANGLE:
            edge.smooth = False
    bm.to_mesh(mesh)
    bm.free()
    return before, after


def world_size(objects):
    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    for obj in objects:
        for v in obj.data.vertices:
            p = obj.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], p[i])
                hi[i] = max(hi[i], p[i])
    return [hi[i] - lo[i] for i in range(3)]


def convert(rel_obj, expected_cm):
    reset()
    src = os.path.join(OBJ_ROOT, rel_obj)
    bpy.ops.wm.obj_import(filepath=src, use_split_groups=True, use_split_objects=True,
                          forward_axis="NEGATIVE_Z", up_axis="Y")
    objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for obj in objects:
        obj.scale = (0.01, 0.01, 0.01)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    tris_before = tris_after = 0
    for obj in objects:
        b, a = optimise(obj)
        tris_before += b
        tris_after += a

    size = world_size(objects)
    expected = [c / 100 for c in expected_cm] if expected_cm else None
    if expected:
        diff = max(abs(a - b) for a, b in zip(sorted(size), sorted(expected)))
        if diff > SIZE_TOLERANCE_M:
            return {"ok": False, "reason": f"size {size} != fusion {expected}", "size_m": size}

    dst = os.path.join(FBX_ROOT, os.path.splitext(rel_obj)[0] + ".fbx")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    root = bpy.data.objects.new(os.path.splitext(os.path.basename(rel_obj))[0], None)
    bpy.context.scene.collection.objects.link(root)
    lod_tris = add_lods(objects, tris_after)
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            obj.parent = root
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=False, mesh_smooth_type="EDGE", use_mesh_modifiers=False,
                             add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
    return {"ok": True, "size_m": [round(s, 6) for s in size], "bodies": len(objects),
            "tris_obj": tris_before, "tris": tris_after, "lod_tris": lod_tris, "materials": mtl_colors(src)}


def add_lods(objects, total_tris):
    """
    Renames the full-detail bodies to *_LOD0 and adds decimated *_LOD1 / *_LOD2 copies for distance
    viewing, which Unity turns into a LOD Group automatically. Small parts only get what they need.
    LOD0 is never changed, so the exact geometry is always what's shown up close.
    """
    targets = [t for t in LOD_TARGETS if t < total_tris * 0.7]
    counts = [total_tris]
    for obj in objects:
        obj.name = obj.name + "_LOD0"
        obj.data.name = obj.name
    for level, target in enumerate(targets, 1):
        ratio = max(target / total_tris, 0.02)
        level_tris = 0
        for obj in objects:
            copy = obj.copy()
            copy.data = obj.data.copy()
            copy.name = obj.name[:-5] + f"_LOD{level}"
            copy.data.name = copy.name
            bpy.context.scene.collection.objects.link(copy)
            mod = copy.modifiers.new("Decimate", "DECIMATE")
            mod.decimate_type = "COLLAPSE"
            mod.ratio = ratio
            mod.use_collapse_triangulate = True
            bpy.context.view_layer.objects.active = copy
            bpy.ops.object.modifier_apply(modifier=mod.name)
            level_tris += len(copy.data.polygons)
        counts.append(level_tris)
    return counts


# Triangle budgets for LOD1 and LOD2 of a whole part.
LOD_TARGETS = [6000, 1500]


def main():
    with open(STATE_PATH, encoding="utf-8") as f:
        state = json.load(f)
    report = {}
    if os.path.exists(REPORT_PATH):
        with open(REPORT_PATH, encoding="utf-8") as f:
            report = json.load(f)

    todo = [rel for rel, info in sorted(state.items()) if info.get("state") == "exported"]
    if LIMIT:
        todo = todo[:LIMIT]
    start = time.time()
    for i, rel_ipt in enumerate(todo, 1):
        rel_obj = os.path.splitext(rel_ipt)[0] + ".obj"
        if report.get(rel_obj, {}).get("ok"):
            continue
        try:
            result = convert(rel_obj, state[rel_ipt].get("size_cm"))
        except Exception as e:  # keep going; the report says what failed
            result = {"ok": False, "reason": repr(e)}
        report[rel_obj] = result
        status = "ok" if result["ok"] else "FAILED " + result["reason"]
        print(f"[{i}/{len(todo)}] {rel_obj}: {status} {result.get('tris_obj', '')} -> {result.get('tris', '')} tris", flush=True)
        if i % 10 == 0:
            with open(REPORT_PATH, "w", encoding="utf-8") as f:
                json.dump(report, f, indent=1)
    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    ok = sum(1 for r in report.values() if r["ok"])
    print(f"done: {ok}/{len(report)} ok in {time.time() - start:.0f}s", flush=True)


main()
