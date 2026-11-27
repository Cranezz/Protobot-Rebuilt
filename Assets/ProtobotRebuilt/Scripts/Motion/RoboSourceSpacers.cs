using System.Collections.Generic;
using System.Linq;
using Protobot.Custom;
using Parts_List;
using UnityEngine;

namespace Protobot.Motion {
    // V2: RoboSource's spacers and washers that VEX doesn't make (the user's ask: its really thin ones, and its many
    // sizes), as robosource.net lists them (2026-11-26): nylon washers 1/64", 1/32" and 1/16" thick (3/8" OD,
    // 0.173" hole), black nylon spacers 1/4" OD (0.166" hole) from 1/8" to 1", and the 1" steel fender washer.
    // They're plain rings, so they're made in code (part type "RSSP", made at start-up like the chain's).
    public class RoboSourceSpacers : PartGenerator {
        public class Kind {
            public string key, name, material;
            public float od, id, length, density;   // inches, g/cm³
            public Color color;
            public float smoothness;
        }

        // (the spacers take the one gray every spacer has: the user's choice, PartColors; paint them any color)
        private static readonly Color White = new(0.42f, 0.42f, 0.41f), Black = new(0.30f, 0.30f, 0.31f), Steel = new(0.32f, 0.33f, 0.34f);

        private static Kind Washer(string key, string name, float length) =>
            new() { key = key, name = name, material = "nylon", od = 0.375f, id = 0.173f, length = length, density = 1.15f, color = White, smoothness = 0.25f };

        private static Kind BlackSpacer(string key, string name, float length) =>
            new() { key = key, name = name, material = "black nylon", od = 0.25f, id = 0.166f, length = length, density = 1.15f, color = Black, smoothness = 0.2f };

        public static readonly Kind[] All = {
            Washer("W64", "1/64\" extra thin nylon washer", 1f / 64f),
            Washer("W32", "1/32\" thin nylon washer", 1f / 32f),
            Washer("W16", "1/16\" thick nylon washer", 1f / 16f),
            BlackSpacer("B8", "1/8\" black spacer (1/4\" OD)", 0.125f),
            BlackSpacer("B4", "1/4\" black spacer (1/4\" OD)", 0.25f),
            BlackSpacer("B38", "3/8\" black spacer (1/4\" OD)", 0.375f),
            BlackSpacer("B2", "1/2\" black spacer (1/4\" OD)", 0.5f),
            BlackSpacer("B78", "7/8\" black spacer (1/4\" OD)", 0.875f),
            BlackSpacer("B1", "1\" black spacer (1/4\" OD)", 1f),
            new() { key = "F", name = "1\" steel fender washer", material = "zinc plated steel", od = 1f, id = 0.188f, length = 0.048f, density = 7.85f, color = Steel, smoothness = 0.55f },
        };

        public static Kind Of(string key) => All.FirstOrDefault(k => k.key == key) ?? All[1];

        public static PartType CreateType() {
            var obj = new GameObject("RoboSource Spacers");
            obj.SetActive(false);
            Object.DontDestroyOnLoad(obj);
            var type = obj.AddComponent<PartType>();
            type.id = "RSSP";
            type.group = PartType.PartGroup.Motion;
            var generator = obj.AddComponent<RoboSourceSpacers>();
            generator.param1 = new Parameter { name = "Size", value = "W32" };
            generator.param2 = new Parameter { name = "" };
            return type;
        }

        public override List<string> GetParam1Options() => All.Select(k => k.key).ToList();
        public override List<string> GetParam2Options() => new();

        private static readonly Dictionary<string, Mesh> meshes = new();
        private static readonly Dictionary<string, Material> materials = new();

        public override Mesh GetMesh() => MeshOf(Of(param1.value));

        /// <summary>A ring: outside and inside walls and both faces, its axis along z, centred on the origin.</summary>
        public static Mesh MeshOf(Kind k) {
            if (meshes.TryGetValue(k.key, out var known) && known != null) return known;
            const int Sides = 40;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            float ro = k.od / 2f, ri = k.id / 2f, h = k.length / 2f;
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd) {
                var i = verts.Count;
                verts.AddRange(new[] { a, b, c, d });
                normals.AddRange(new[] { na, nb, nc, nd });
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            for (var s = 0; s < Sides; s++) {
                var a0 = s * Mathf.PI * 2f / Sides;
                var a1 = (s + 1) * Mathf.PI * 2f / Sides;
                var d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                var d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                var top = Vector3.forward * h;
                var bottom = -Vector3.forward * h;
                Quad(d0 * ro + bottom, d0 * ro + top, d1 * ro + top, d1 * ro + bottom, d0, d0, d1, d1);                 // outside
                Quad(d1 * ri + bottom, d1 * ri + top, d0 * ri + top, d0 * ri + bottom, -d1, -d1, -d0, -d0);             // inside
                Quad(d0 * ri + top, d1 * ri + top, d1 * ro + top, d0 * ro + top, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward);
                Quad(d0 * ro + bottom, d1 * ro + bottom, d1 * ri + bottom, d0 * ri + bottom, Vector3.back, Vector3.back, Vector3.back, Vector3.back);
            }
            var mesh = new Mesh { name = "RoboSource " + k.name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return meshes[k.key] = mesh;
        }

        private static Material MaterialOf(Kind k) {
            var key = k.material;
            if (materials.TryGetValue(key, out var known) && known != null) return known;
            var source = Interface.Interface.Instance != null ? Interface.Interface.Instance.plasticMaterial : null;
            var m = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "RoboSource " + k.material;
            m.color = k.color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", k.color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", k.smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", k.density > 5f ? 0.6f : 0f);
            return materials[key] = m;
        }

        // Placing snaps by a template (its holes and model): one hidden copy per kind.
        private static readonly Dictionary<string, PartData> templates = new();

        public override bool TryGetPartData(out PartData partData) {
            var k = Of(param1.value);
            if (!templates.TryGetValue(k.key, out partData) || partData == null) {
                var obj = Generate(new Vector3(0f, -1000f, 0f), Quaternion.identity);
                obj.SetActive(false);
                obj.name = "RoboSource template " + k.key;
                obj.hideFlags = HideFlags.HideInHierarchy;
                Object.DontDestroyOnLoad(obj);
                partData = obj.AddComponent<PartData>();
                partData.param1Value = k.key;
                templates[k.key] = partData;
            }
            return true;
        }

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var k = Of(param1.value);
            var obj = new GameObject(k.name) { tag = "Object" };
            obj.transform.SetPositionAndRotation(position, rotation);
            var mesh = MeshOf(k);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = MaterialOf(k);
            obj.AddComponent<MeshCollider>().sharedMesh = mesh;
            var partName = obj.AddComponent<PartName>();
            partName.name = "RoboSource " + k.name;
            var cubicInches = Mathf.PI / 4f * (k.od * k.od - k.id * k.id) * k.length;
            partName.weightInGrams = cubicInches * 16.387f * k.density;
            CustomParts.AddHoles(obj, new[] { new CustomHole { center = Vector3.zero, axis = Vector3.forward, size = k.id, depth = k.length } });
            SetId(obj);
            return obj;
        }
    }
}
