"""
Helpers for building the workshop in Blender.

Every object is built from simple primitives at real-world size (1 unit = 1 metre, Z up).
Materials point at the Poly Haven textures inside the Unity project, and UVs are projected
in world space so every texture tiles at its real-world size.
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT = r"C:\Dev\ProtobotRebuiltV2\ProtobotRebuiltV2"
TEXTURES = os.path.join(PROJECT, r"Assets\Protobot\Art\Environment\Textures")
COLLECTION = "Workshop"

# name -> (texture id or None, real-world texture size in metres)
MATERIAL_TEXTURES = {}


def collection():
    col = bpy.data.collections.get(COLLECTION)
    if col is None:
        col = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(col)
    return col


def clear():
    col = bpy.data.collections.get(COLLECTION)
    if col:
        for obj in list(col.all_objects):
            bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(mesh)


def _load(path, non_color):
    """Loads `<path>.jpg` or `<path>.png`, whichever exists (path is given with a .jpg suffix)."""
    if not os.path.exists(path):
        path = os.path.splitext(path)[0] + ".png"
    img = bpy.data.images.load(path, check_existing=True)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    return img


def material(name, texture=None, tex_size=2.0, color=(0.8, 0.8, 0.8), rough=0.6, metal=0.0,
             emission=None, emission_strength=0.0, alpha=1.0, tint=None):
    """Creates (or reuses) a Principled material, optionally with a Poly Haven texture set."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    MATERIAL_TEXTURES[name] = (texture, tex_size)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if texture:
        base = os.path.join(TEXTURES, texture, texture)
        diff = nt.nodes.new("ShaderNodeTexImage")
        diff.image = _load(base + "_diff.jpg", False)
        if tint:
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.inputs["Factor"].default_value = 1.0
            mix.inputs["B"].default_value = (*tint, 1.0)
            nt.links.new(diff.outputs["Color"], mix.inputs["A"])
            nt.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
        else:
            nt.links.new(diff.outputs["Color"], bsdf.inputs["Base Color"])
        rough_tex = nt.nodes.new("ShaderNodeTexImage")
        rough_tex.image = _load(base + "_rough.jpg", True)
        nt.links.new(rough_tex.outputs["Color"], bsdf.inputs["Roughness"])
        nrm_tex = nt.nodes.new("ShaderNodeTexImage")
        nrm_tex.image = _load(base + "_nor_gl.jpg", True)
        nrm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nrm_tex.outputs["Color"], nrm.inputs["Color"])
        nt.links.new(nrm.outputs["Normal"], bsdf.inputs["Normal"])
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        mat.surface_render_method = "BLENDED"
    mat.diffuse_color = (*color, alpha)
    return mat


def _finish(obj, mat, parent, smooth=False):
    collection().objects.link(obj)
    if mat:
        obj.data.materials.append(mat)
    if parent:
        obj.parent = parent
    if smooth:
        for poly in obj.data.polygons:
            poly.use_smooth = True
    return obj


def empty(name, parent=None, location=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    obj.location = location
    collection().objects.link(obj)
    if parent:
        obj.parent = parent
    return obj


def box(name, size, center, mat=None, parent=None, rotation=(0, 0, 0)):
    """Axis-aligned box: size (x, y, z) in metres, centred on `center`."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = center
    obj.rotation_euler = [math.radians(a) for a in rotation]
    return _finish(obj, mat, parent)


def box_span(name, x0, x1, y0, y1, z0, z1, mat=None, parent=None):
    """Box given by its min/max corners, which is easier for architecture."""
    return box(name, (x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), mat, parent)


def cylinder(name, radius, depth, center, mat=None, parent=None, rotation=(0, 0, 0), segments=12, smooth=True):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=radius, depth=depth)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = center
    obj.rotation_euler = [math.radians(a) for a in rotation]
    obj = _finish(obj, mat, parent, smooth)
    if smooth:
        _sharpen_caps(obj)
    return obj


def _sharpen_caps(obj):
    """Keeps the flat ends of a cylinder flat-shaded while the sides stay smooth."""
    for poly in obj.data.polygons:
        if abs(poly.normal.z) > 0.9:
            poly.use_smooth = False


def world_uvs():
    """Projects UVs in world space (per face, along its dominant axis) so textures tile at real size."""
    for obj in collection().all_objects:
        if obj.type != "MESH" or not obj.data.materials:
            continue
        mat = obj.data.materials[0]
        _, size = MATERIAL_TEXTURES.get(mat.name, (None, 2.0))
        mw = obj.matrix_world
        rot = mw.to_3x3().normalized()
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        uv = bm.loops.layers.uv.verify()
        for face in bm.faces:
            n = rot @ face.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in face.loops:
                p = mw @ loop.vert.co
                if ax == 0:
                    u, v = p.y, p.z
                elif ax == 1:
                    u, v = p.x, p.z
                else:
                    u, v = p.x, p.y
                loop[uv].uv = (u / size, v / size)
        bm.to_mesh(obj.data)
        bm.free()
