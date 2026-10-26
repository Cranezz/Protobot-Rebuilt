"""
Exports the Workshop collection to Unity: an FBX plus a JSON material manifest that the Unity
editor tool (Tools > Protobot > Import Workshop) turns into URP materials.
Also saves the .blend source and writes metallic/smoothness maps for URP.
"""
import json
import os

import bpy
import numpy as np
from mathutils import Matrix

import workshop_lib as L

MODELS = os.path.join(L.PROJECT, r"Assets\Protobot\Art\Environment\Models")
BLEND = os.path.join(L.PROJECT, r"ArtSource\Workshop.blend")


def _socket(bsdf, name):
    value = bsdf.inputs[name].default_value
    return list(value)[:3] if hasattr(value, "__len__") else float(value)


def material_manifest():
    used = {}
    for obj in L.collection().all_objects:
        if obj.type == "MESH":
            for mat in obj.data.materials:
                used[mat.name] = mat
    entries = []
    for name, mat in sorted(used.items()):
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        texture, _ = L.MATERIAL_TEXTURES.get(name, (None, 2.0))
        tint = next((list(n.inputs["B"].default_value)[:3] for n in mat.node_tree.nodes if n.type == "MIX"), None)
        entries.append({
            "name": name,
            "texture": texture,
            # Textured materials take their colour from the texture, optionally multiplied by a tint.
            "color": tint or ([1.0, 1.0, 1.0] if texture else _socket(bsdf, "Base Color")),
            "roughness": _socket(bsdf, "Roughness"),
            "metallic": _socket(bsdf, "Metallic"),
            "emission": _socket(bsdf, "Emission Color") if _socket(bsdf, "Emission Strength") > 0 else None,
            "emissionStrength": _socket(bsdf, "Emission Strength"),
            "alpha": _socket(bsdf, "Alpha"),
        })
    return entries


def write_metallic_smoothness(texture, metallic):
    """URP reads smoothness from the alpha of the metallic map: A = 1 - roughness."""
    folder = os.path.join(L.TEXTURES, texture)
    out = os.path.join(folder, f"{texture}_ms.png")
    rough_path = next((os.path.join(folder, f) for f in os.listdir(folder) if "_rough." in f), None)
    if rough_path is None or os.path.exists(out):
        return
    img = bpy.data.images.load(rough_path, check_existing=True)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    metal_path = next((os.path.join(folder, f) for f in os.listdir(folder) if "_metal." in f), None)
    metal = np.full((h, w), metallic, dtype=np.float32)
    if metal_path:
        mimg = bpy.data.images.load(metal_path, check_existing=True)
        metal = np.array(mimg.pixels[:], dtype=np.float32).reshape(h, w, 4)[..., 0]
    out_px = np.stack([metal, metal, metal, 1.0 - px[..., 0]], axis=-1)
    result = bpy.data.images.new(f"{texture}_ms", w, h, alpha=True)
    result.pixels = out_px.ravel()
    result.filepath_raw = out
    result.file_format = "PNG"
    result.save()
    bpy.data.images.remove(result)


def flatten_transforms():
    """
    Bakes every rotation/scale into the mesh data and leaves objects with only a world position,
    under groups that have identity transforms. Blender's FBX 'apply transform' option mangles
    rotated or offset parents, so this keeps the export unambiguous.
    """
    objects = list(L.collection().all_objects)
    world = {obj.name: obj.matrix_world.copy() for obj in objects}
    for obj in objects:
        if obj.type == "MESH":
            mw = world[obj.name]
            loc = mw.to_translation()
            obj.data.transform(Matrix.Translation(-loc) @ mw)
    for obj in objects:
        obj.matrix_parent_inverse = Matrix.Identity(4)
        if obj.type == "MESH":
            obj.matrix_basis = Matrix.Translation(world[obj.name].to_translation())
        else:
            obj.matrix_basis = Matrix.Identity(4)


def export():
    flatten_transforms()
    os.makedirs(MODELS, exist_ok=True)
    os.makedirs(os.path.dirname(BLEND), exist_ok=True)
    manifest = material_manifest()
    for entry in manifest:
        if entry["texture"]:
            write_metallic_smoothness(entry["texture"], entry["metallic"])
    with open(os.path.join(MODELS, "Workshop_materials.json"), "w", encoding="utf-8") as f:
        json.dump({"materials": manifest}, f, indent=1)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in L.collection().all_objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(MODELS, "Workshop.fbx"),
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,  # Unity bakes the axis conversion itself (bakeAxisConversion)
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
    )
    bpy.ops.wm.save_as_mainfile(filepath=BLEND, copy=True)
    return len(manifest)
