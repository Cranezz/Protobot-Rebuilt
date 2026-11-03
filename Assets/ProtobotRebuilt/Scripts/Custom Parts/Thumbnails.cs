using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Custom {
    // V2: the parts browser's icon for a custom part: a small render of its model (lit, three-quarter view, on a
    // clear background), made once per part and kept for the session.
    public static class Thumbnails {
        private const int Size = 128;
        private const int Layer = 15;
        private static readonly Dictionary<string, Texture2D> cache = new();

        public static Texture2D Get(CustomPartData part) {
            if (cache.TryGetValue(part.key, out var texture) && texture != null)
                return texture;
            texture = Render(part);
            cache[part.key] = texture;
            return texture;
        }

        public static void Forget(string key) => cache.Remove(key);

        /// <summary>V2: the icon of one part of a VEX-CAD family (each part its own picture, not the family's).</summary>
        public static Texture2D ForVexPart(string typeId, string p1) {
            var key = typeId + "|" + p1;
            if (cache.TryGetValue(key, out var texture) && texture != null)
                return texture;
            texture = null;
            var type = Interface.PartsCatalog.Type(typeId);
            if (type != null) {
                foreach (var data in type.GetComponentsInChildren<PartData>(true)) {
                    if (data.param1Value != p1 || !data.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                    var materials = data.TryGetComponent(out MeshRenderer source) ? source.sharedMaterials : null;
                    texture = Render(filter.sharedMesh, materials, p1, false);
                    break;
                }
            }
            cache[key] = texture;
            return texture;
        }

        private static Texture2D Render(CustomPartData part) {
            var mesh = CustomParts.MeshOf(part);
            return Render(mesh, new[] { CustomParts.MaterialFor(part) }, part.name, part.kind == CustomParts.Polycarbonate);
        }

        private static Texture2D Render(Mesh mesh, Material[] materials, string name, bool clear) {
            var root = new GameObject("Thumbnail Stage");
            root.transform.position = new Vector3(0f, -6000f, 0f);
            var model = new GameObject("Model") { layer = Layer };
            model.transform.SetParent(root.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            var original = renderer.sharedMaterial;
            // Polycarbonate is nearly clear: show it a little more solid in its icon.
            if (clear && renderer.sharedMaterial != null) {
                var tinted = new Material(renderer.sharedMaterial);
                var c = tinted.color;
                c.a = 0.85f;
                tinted.color = c;
                if (tinted.HasProperty("_BaseColor")) tinted.SetColor("_BaseColor", c);
                renderer.sharedMaterial = tinted;
            }
            model.transform.localPosition = -mesh.bounds.center;

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.fieldOfView = 25f;
            var radius = Mathf.Max(mesh.bounds.extents.magnitude, 0.05f);
            var distance = radius / Mathf.Sin(12.5f * Mathf.Deg2Rad);
            var rotation = Quaternion.Euler(28f, -35f, 0f);
            camera.transform.localPosition = rotation * new Vector3(0f, 0f, -distance);
            camera.transform.localRotation = rotation;
            camera.nearClipPlane = distance * 0.05f;
            camera.farClipPlane = distance * 3f;

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Directional;
            light.cullingMask = 1 << Layer;
            light.transform.rotation = Quaternion.Euler(45f, -20f, 0f);

            var target = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            target.antiAliasing = 4;
            camera.targetTexture = target;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = name };
            texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            if (renderer.sharedMaterial != null && renderer.sharedMaterial != original)
                Object.Destroy(renderer.sharedMaterial);
            // Gone at once: Destroy waits for the end of the frame, and the next icon made in this frame would
            // show this model too.
            Object.DestroyImmediate(root);
            return texture;
        }
    }
}
