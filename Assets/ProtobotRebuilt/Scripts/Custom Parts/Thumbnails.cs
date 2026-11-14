using System.Collections.Generic;
using System.Linq;
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
                    materials = Motion.PartColors.For(typeId + "-" + p1, materials) ?? materials;   // V2
                    texture = Render(filter.sharedMesh, materials, p1, false);
                    break;
                }
            }
            cache[key] = texture;
            return texture;
        }


        // ------------------------------------------------------------------ V2: every part's picture, and icons

        /// <summary>A rendered picture of any part (the app's own generated parts too): the part is made off screen
        /// with these sizes, pictured, and removed. Kept for the session.</summary>
        public static Texture2D ForPart(string typeId, string p1, string p2) {
            var key = "part|" + typeId + "|" + p1 + "|" + p2;
            if (cache.TryGetValue(key, out var texture) && texture != null) return texture;
            texture = null;
            var stage = Stage(typeId, p1, p2);
            if (stage != null) {
                texture = RenderObject(stage, false, typeId + " " + p1);
                Object.DestroyImmediate(stage);
            }
            cache[key] = texture;
            return texture;
        }

        private static readonly Dictionary<string, Sprite> icons = new();

        /// <summary>A Protobot-style icon of a part: its flat white outline seen square-on along its thinnest side
        /// (holes through it left open), for the interface to tint like the original icons.</summary>
        public static Sprite IconFor(string typeId, string p1, string p2) {
            var key = typeId + "|" + p1 + "|" + p2;
            if (icons.TryGetValue(key, out var sprite) && sprite != null) return sprite;
            sprite = null;
            var stage = Stage(typeId, p1, p2);
            if (stage != null) {
                var texture = RenderObject(stage, true, typeId + " icon");
                Object.DestroyImmediate(stage);
                if (texture != null) sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }
            icons[key] = sprite;
            return sprite;
        }

        // The part, made far below the build, its renderers on the picture layer.
        private static GameObject Stage(string typeId, string p1, string p2) {
            var type = Interface.PartsCatalog.Type(typeId);
            var generator = type != null ? type.GetComponent<PartGenerator>() : null;
            if (generator == null) return null;
            // VEX-CAD parts: their model straight from the family (nothing to make).
            if (generator is ChildPartGenerator && typeId.StartsWith("VX")) {
                foreach (var data in type.GetComponentsInChildren<PartData>(true)) {
                    if (data.param1Value != p1 || !data.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                    var go = new GameObject("Thumbnail Part") { layer = Layer };
                    go.transform.position = new Vector3(0f, -6000f, 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var shown = data.GetComponent<MeshRenderer>().sharedMaterials;
                    go.AddComponent<MeshRenderer>().sharedMaterials = Motion.PartColors.For(typeId + "-" + p1, shown) ?? shown;
                    return go;
                }
                return null;
            }
            string old1 = generator.UsesParams ? generator.param1.value : null, old2 = generator.UsesTwoParams ? generator.param2.value : null;
            GameObject part = null;
            try {
                if (generator.UsesParams && p1 != null) generator.param1.value = p1;
                if (generator.UsesTwoParams) {
                    if (p2 != null) generator.param2.value = p2;
                    else if (generator is AluminumPartGenerator) {
                        var options = generator.GetParam2Options();
                        generator.param2.value = options != null && options.Contains("5") ? "5" : "10";   // a short piece shows its holes
                    }
                    else if (!generator.param2.custom) {
                        var options = generator.GetParam2Options();
                        if (options != null && options.Count > 0 && !options.Contains(generator.param2.value)) generator.param2.value = options[0];
                    }
                }
                part = generator.Generate(new Vector3(0f, -6000f, 0f), Quaternion.identity);
            }
            catch {
                if (part != null) Object.DestroyImmediate(part);
                part = null;
            }
            finally {
                if (old1 != null) generator.param1.value = old1;
                if (old2 != null) generator.param2.value = old2;
            }
            if (part == null) return null;
            foreach (var r in part.GetComponentsInChildren<Renderer>(true)) {
                var hole = r.GetComponentInParent<HoleCollider>() != null || r.GetComponent<HoleFace>() != null;
                r.gameObject.layer = hole || !r.enabled ? 0 : Layer;
            }
            return part;
        }

        private static Material flat;

        private static Texture2D RenderObject(GameObject stage, bool silhouette, string name) {
            var renderers = stage.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.layer == Layer && r.enabled).ToList();
            if (renderers.Count == 0) return null;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var saved = new Dictionary<Renderer, Material[]>();
            if (silhouette) {
                if (flat == null) {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                    flat = new Material(shader) { color = Color.white };
                    if (flat.HasProperty("_BaseColor")) flat.SetColor("_BaseColor", Color.white);
                }
                foreach (var r in renderers) {
                    saved[r] = r.sharedMaterials;
                    r.sharedMaterials = Enumerable.Repeat(flat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                }
            }
            var root = new GameObject("Thumbnail Stage");
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            var radius = Mathf.Max(bounds.extents.magnitude, 0.05f);
            Quaternion rotation;
            if (silhouette) {
                // Square on, along the thinnest side.
                var s = bounds.size;
                var look = s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.down : Vector3.forward;
                rotation = Quaternion.LookRotation(look, Mathf.Abs(look.y) > 0.5f ? Vector3.forward : Vector3.up);
                camera.orthographic = true;
                var right = rotation * Vector3.right;
                var up = rotation * Vector3.up;
                var half = Mathf.Max(Mathf.Abs(Vector3.Dot(s, Abs(right))), Mathf.Abs(Vector3.Dot(s, Abs(up)))) / 2f;
                camera.orthographicSize = half * 1.08f;
            }
            else {
                rotation = Quaternion.Euler(28f, -35f, 0f);
                camera.fieldOfView = 25f;
            }
            var distance = silhouette ? radius * 3f : radius / Mathf.Sin(12.5f * Mathf.Deg2Rad);
            camera.transform.position = bounds.center - rotation * Vector3.forward * distance;
            camera.transform.rotation = rotation;
            camera.nearClipPlane = distance * 0.05f;
            camera.farClipPlane = distance * 3f;
            Light light = null;
            if (!silhouette) {
                light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.type = LightType.Directional;
                light.cullingMask = 1 << Layer;
                light.transform.rotation = Quaternion.Euler(45f, -20f, 0f);
            }
            var size = silhouette ? 128 : Size;
            var target = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            target.antiAliasing = 4;
            camera.targetTexture = target;
            RenderLit(camera);   // V2: with less flat light, so a red gear's teeth show
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
            texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(root);
            foreach (var pair in saved) if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            if (silhouette) {
                // Pure white where the part is (the alpha keeps its edge smooth).
                var pixels = texture.GetPixels32();
                for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, pixels[i].a);
                texture.SetPixels32(pixels);
                texture.Apply();
            }
            return texture;
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // V2: the app lights everything with a strong flat ambient (CLAUDE.md "Colors"), which washed out pictures of
        // saturated parts (a red gear's teeth ran together). Pictures are taken with less ambient (62%) and a stronger
        // key light plus a soft fill from the other side, so faces turned different ways read apart.
        private static void RenderLit(Camera camera) {
            var ambient = RenderSettings.ambientLight;
            var intensity = RenderSettings.ambientIntensity;
            var lights = camera.transform.parent != null ? camera.transform.parent.GetComponentsInChildren<Light>() : new Light[0];
            foreach (var l in lights) l.intensity = 1.35f;
            GameObject fill = null;
            if (lights.Length > 0) {
                fill = new GameObject("Fill Light");
                fill.transform.SetParent(camera.transform.parent, false);
                var f = fill.AddComponent<Light>();
                f.type = LightType.Directional;
                f.cullingMask = camera.cullingMask;
                f.intensity = 0.35f;
                f.shadows = LightShadows.None;
                f.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            }
            RenderSettings.ambientLight = ambient * 0.62f;
            RenderSettings.ambientIntensity = intensity * 0.62f;
            try { camera.Render(); }
            finally {
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientIntensity = intensity;
                if (fill != null) Object.DestroyImmediate(fill);
            }
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
            RenderLit(camera);   // V2: with less flat light, so a red gear's teeth show
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
