using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Protobot.Menus
{
    /// <summary>
    /// A little photo studio far below the workshop for showing one part in a menu: a copy of the part's
    /// meshes (no physics or scripts), its own camera rendering into a texture, and its own light. The copy
    /// turns slowly, or with <see cref="Turn"/>; colours can be tried on it without touching the real part.
    /// </summary>
    public class PartPreviewStage
    {
        const int Layer = 30;  // seen only by the stage camera and lit only by the stage light
        static readonly Vector3 Place = new(0f, -1000f, 0f);
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        readonly GameObject root;
        readonly Camera camera;
        readonly Transform pivot;
        readonly List<MeshRenderer> renderers = new();
        readonly MaterialPropertyBlock block = new();
        float yaw = 30f;
        float pitch = 20f;

        public RenderTexture Texture { get; }

        public PartPreviewStage(Color background)
        {
            root = new GameObject("Part Preview Stage");
            root.transform.position = Place;
            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root.transform, false);

            Texture = new RenderTexture(768, 640, 24) { name = "Part Preview", antiAliasing = 4 };
            var camGo = new GameObject("Preview Camera");
            camGo.transform.SetParent(root.transform, false);
            camera = camGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.cullingMask = 1 << Layer;
            camera.targetTexture = Texture;
            camera.fieldOfView = 25f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
            camera.enabled = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;
            data.renderPostProcessing = false;

            foreach (var (direction, intensity) in new[] { (new Vector3(40f, -30f, 0f), 1.4f), (new Vector3(-20f, 150f, 0f), 0.6f) })
            {
                var lightGo = new GameObject("Preview Light");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.rotation = Quaternion.Euler(direction);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = intensity;
                light.cullingMask = 1 << Layer;
                light.shadows = LightShadows.None;
            }

            // Keep the stage out of the main view, in case it's ever in front of it.
            if (Camera.main != null)
                Camera.main.cullingMask &= ~(1 << Layer);
            root.SetActive(false);
        }

        /// <summary>Shows a copy of <paramref name="part"/> (its full-detail meshes and materials).</summary>
        public void Show(GameObject part, IEnumerable<MeshRenderer> detail)
        {
            Clear();
            root.SetActive(true);
            camera.enabled = true;
            var bounds = new Bounds();
            var first = true;
            foreach (var source in detail)
            {
                var go = new GameObject(source.name) { layer = Layer };
                go.transform.SetParent(pivot, false);
                var m = part.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
                go.transform.localPosition = m.GetColumn(3);
                go.transform.localRotation = m.rotation;
                go.transform.localScale = m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = source.sharedMaterials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers.Add(r);
                var b = r.bounds;
                if (first)
                    bounds = b;
                else
                    bounds.Encapsulate(b);
                first = false;
            }
            // Centre the copy on the pivot and pull the camera back to fit it.
            foreach (Transform child in pivot)
                child.position -= bounds.center - pivot.position;
            var radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            var distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            camera.transform.position = pivot.position + new Vector3(0f, 0f, -distance);
            camera.transform.rotation = Quaternion.identity;
            camera.nearClipPlane = Mathf.Max(0.005f, distance - radius * 2f);
            camera.farClipPlane = distance + radius * 2f;
            Apply();
        }

        /// <summary>Spins the copy (degrees); with no input it turns slowly by itself.</summary>
        public void Turn(Vector2 degrees)
        {
            yaw -= degrees.x;
            pitch = Mathf.Clamp(pitch + degrees.y, -80f, 80f);
            Apply();
        }

        public void Tick(float deltaTime, bool idle)
        {
            if (idle)
                yaw += 18f * deltaTime;
            Apply();
        }

        void Apply() => pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f) * Quaternion.Euler(0f, yaw, 0f);

        /// <summary>Paints the copy: each material either its paint colour (satin finish) or its own look.</summary>
        public void Paint(IReadOnlyDictionary<Material, Color> paints)
        {
            foreach (var r in renderers)
            {
                var materials = r.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !paints.TryGetValue(materials[i], out var color))
                    {
                        r.SetPropertyBlock(null, i);
                        continue;
                    }
                    block.Clear();
                    block.SetColor(BaseColor, color);
                    block.SetFloat(Metallic, 0.05f);
                    block.SetFloat(Smoothness, 0.55f);
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        public void Hide()
        {
            Clear();
            camera.enabled = false;
            root.SetActive(false);
        }

        void Clear()
        {
            foreach (var r in renderers)
                if (r != null)
                    Object.Destroy(r.gameObject);
            renderers.Clear();
            pivot.localRotation = Quaternion.identity;
        }
    }
}
