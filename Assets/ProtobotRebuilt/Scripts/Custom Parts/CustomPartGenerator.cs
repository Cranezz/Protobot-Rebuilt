using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Custom {
    // V2: the part types of custom parts, made at start-up next to the ones in Resources/Part Prefabs: "CUST"
    // (imported models) and "POLY" (polycarbonate). Their one parameter is the part's key, so ids work like the
    // original's ("CUST-1a2b3c4d") and builds save and load them as any part.
    public class CustomPartGenerator : PartGenerator {
        public string kind;

        private static readonly List<CustomPartGenerator> generators = new();

        /// <summary>The two part types (inactive objects that live for the whole session).</summary>
        public static PartType[] CreateTypes() {
            generators.Clear();
            return new[] {
                Create(CustomParts.Imported, "Custom Part"),
                Create(CustomParts.Polycarbonate, "Polycarbonate"),
            };
        }

        private static PartType Create(string kind, string name) {
            var obj = new GameObject(name);
            obj.SetActive(false);
            Object.DontDestroyOnLoad(obj);
            var type = obj.AddComponent<PartType>();
            type.id = kind;
            type.group = PartType.PartGroup.None;
            var generator = obj.AddComponent<CustomPartGenerator>();
            generator.kind = kind;
            generator.param1 = new Parameter { name = "Part" };
            generator.param2 = new Parameter { name = "" };
            generators.Add(generator);
            return type;
        }

        public static CustomPartGenerator For(string kind) => generators.FirstOrDefault(g => g.kind == kind);

        /// <summary>After the library changed: keeps the chosen part valid.</summary>
        public static void Refresh() {
            foreach (var generator in generators) {
                if (generator == null) continue;
                var options = generator.GetParam1Options();
                if (options.Count > 0 && !options.Contains(generator.param1.value))
                    generator.param1.value = options[0];
            }
        }

        public CustomPartData Part => CustomParts.Find(kind + "-" + param1.value);

        public override List<string> GetParam1Options() => CustomParts.OfKind(kind).OrderByDescending(p => p.created).Select(p => p.key).ToList();
        public override List<string> GetParam2Options() => new();

        public override Mesh GetMesh() {
            var part = Part;
            return part != null ? CustomParts.MeshOf(part) : new Mesh();
        }

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var part = Part;
            if (part == null) {
                // A build that uses a part this computer doesn't have (and the file didn't carry): a marker.
                var missing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                missing.name = "Missing custom part";
                missing.transform.SetPositionAndRotation(position, rotation);
                missing.AddComponent<SavedObject>().id = GetId();
                return missing;
            }
            var obj = CustomParts.Build(part, position, rotation);
            SetId(obj);
            return obj;
        }
    }
}
