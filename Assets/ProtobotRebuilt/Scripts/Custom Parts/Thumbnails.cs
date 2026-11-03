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

        private static Texture2D Render(CustomPartData part) {
            var root = new GameObject("Thumbnail Stage");
            root.transform.position = new Vector3(0f, -6000f, 0f);
            var model = new GameObject("Model") { layer = Layer };
            model.transform.SetParent(root.transform, false);
            var mesh = CustomParts.MeshOf(part);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CustomParts.MaterialFor(part);
            // Polycarbonate is nearly clear: show it a little more solid in its icon.
            if (part.kind == CustomParts.Polycarbonate && renderer.sharedMaterial != null) {
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
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = part.name };
            texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            if (renderer.sharedMaterial != null && renderer.sharedMaterial != CustomParts.MaterialFor(part))
                Object.Destroy(renderer.sharedMaterial);
            Object.Destroy(root);
            return texture;
        }
    }
}
