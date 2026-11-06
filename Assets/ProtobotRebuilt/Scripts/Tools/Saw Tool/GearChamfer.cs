using System.Globalization;
using System.Linq;
using Parts_List;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Tools {
    // V2: chamfering a gear's teeth, as teams do with a file or a lathe: a 45° bevel round the rim on one face (or
    // both). A chamfered 48T fits inside a 2.75" wheel's hub recess, and chamfered teeth find their mesh when a
    // gear slides into place along its shaft. The bevel is cut into the mesh as a ring of planes (PartCutting,
    // 32 round the rim: within 0.005" of a true cone on a 2" gear) and kept in the part's saved state as
    // "chamfer:face centre (3), into the gear (3), rim radius, size;" (part space), made again on load in order
    // with cuts and drilled holes (PartDrilling.ApplySaved).
    public static class GearChamfer {
        public const string Prefix = "chamfer:";
        private const int Facets = 32;

        public static readonly float[] Sizes = { 1f / 32f, 1f / 16f, 3f / 32f, 1f / 8f, 3f / 16f, 1f / 4f };

        public enum Side { Near, Far, Both }

        /// <summary>A spur gear or sprocket (its teeth are what's chamfered).</summary>
        public static bool IsGear(GameObject part) => part != null && Motion.GearTrain.Teeth(part, out _) > 0 && PartCutting.IsCuttable(part);

        /// <summary>One face of the gear, part space: the middle of the face, the way into the gear, the rim radius.</summary>
        public struct Face {
            public Vector3 center, inward;
            public float radius;
        }

        /// <summary>The gear's two faces (low then high along its axle), part space.</summary>
        public static Face[] Faces(GameObject part) {
            var mesh = part.GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            // The axle: the hole nearest the middle, rounded to the part's own axes (gears are modelled square on).
            var hole = PartDrilling.Holes(part).OrderBy(h => (h.transform.localPosition - bounds.center).sqrMagnitude).FirstOrDefault();
            var axis = hole != null ? hole.transform.localRotation * Vector3.forward : ThinnestAxis(bounds.size);
            var abs = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            axis = abs.x >= abs.y && abs.x >= abs.z ? Vector3.right : abs.y >= abs.z ? Vector3.up : Vector3.forward;
            var middle = hole != null ? hole.transform.localPosition : bounds.center;
            float low = float.MaxValue, high = float.MinValue, radius = 0f;
            foreach (var v in vertices) {
                var along = Vector3.Dot(v - middle, axis);
                low = Mathf.Min(low, along);
                high = Mathf.Max(high, along);
                radius = Mathf.Max(radius, (v - middle - axis * along).sqrMagnitude);
            }
            radius = Mathf.Sqrt(radius);
            return new[] {
                new Face { center = middle + axis * low, inward = axis, radius = radius },
                new Face { center = middle + axis * high, inward = -axis, radius = radius },
            };
        }

        private static Vector3 ThinnestAxis(Vector3 size) =>
            size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;

        /// <summary>The faces a side picks: Near is the face toward the camera.</summary>
        public static Face[] Picked(GameObject part, Side side, Vector3 cameraPosition) {
            var faces = Faces(part);
            if (side == Side.Both) return faces;
            var t = part.transform;
            var toCamera = cameraPosition - t.TransformPoint((faces[0].center + faces[1].center) / 2f);
            var firstNear = Vector3.Dot(t.TransformDirection(-faces[0].inward), toCamera) > 0f;
            return new[] { (side == Side.Near) == firstNear ? faces[0] : faces[1] };
        }

        /// <summary>Bevels one face's rim by `size` (45°). False if the gear is too thin or small for it.</summary>
        public static bool Chamfer(GameObject part, Face face, float size) {
            if (size <= 0f || size >= face.radius * 0.5f) return false;
            Perpendicular(face.inward, out var a, out var b);
            var any = false;
            for (var i = 0; i < Facets; i++) {
                var angle = i * Mathf.PI * 2f / Facets;
                var radial = a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                // Keeps the side where (into the gear) - (out from the axle) >= the rim less the chamfer.
                var point = face.center + radial * (face.radius - size);
                var normal = (face.inward - radial).normalized;
                if (PartCutting.Cut(part, point, normal)) any = true;
            }
            return any;
        }

        private static void Perpendicular(Vector3 axis, out Vector3 a, out Vector3 b) {
            a = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            b = Vector3.Cross(axis, a).normalized;
        }

        public static void Record(SavedObject saved, Face face, float size) {
            saved.state += Prefix + string.Join(",", new[] {
                face.center.x, face.center.y, face.center.z, face.inward.x, face.inward.y, face.inward.z, face.radius, size
            }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + ";";
        }

        /// <summary>Makes a saved "chamfer:" entry again (false if it isn't one).</summary>
        public static bool ApplyEntry(GameObject part, string entry) {
            if (!entry.StartsWith(Prefix)) return false;
            var values = entry.Substring(Prefix.Length).Split(',');
            if (values.Length != 8) return true;
            var f = new float[8];
            for (var i = 0; i < 8; i++)
                if (!float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return true;
            var face = new Face { center = new Vector3(f[0], f[1], f[2]), inward = new Vector3(f[3], f[4], f[5]), radius = f[6] };
            if (Chamfer(part, face, f[7])) MarkName(part);
            return true;
        }

        public static void MarkName(GameObject part) {
            if (part.TryGetComponent(out PartName partName) && !partName.name.EndsWith("(chamfered)"))
                partName.name += " (chamfered)";
        }

        /// <summary>Chamfers the picked faces as one undo step. The message to show, and whether it worked.</summary>
        public static (string message, bool ok) Apply(GameObject part, Side side, float size, Vector3 cameraPosition) {
            var faces = Picked(part, side, cameraPosition);
            if (faces[0].radius > 0f && size >= faces[0].radius * 0.5f)
                return ("A " + Inch.Format(size) + " chamfer is too big for this gear", false);
            var before = new ChamferElement(part);
            var saved = part.GetComponent<SavedObject>();
            var done = 0;
            foreach (var face in faces)
                if (Chamfer(part, face, size)) {
                    Record(saved, face, size);
                    done++;
                }
            if (done == 0) return ("Nothing to chamfer there", false);
            MarkName(part);
            StateSystem.AddElement(before);
            StateSystem.AddState(new State(new System.Collections.Generic.List<IElement> { new ChamferElement(part) }));
            Object.FindAnyObjectByType<PartListOutput>()?.CalculatePartsList();
            return ("Chamfered the teeth " + Inch.Format(size) + " at 45° on " + (done == 2 ? "both faces" : side == Side.Far ? "the far face" : "the near face"), true);
        }
    }

    // Undo/redo of a chamfer: the part's mesh, saved state, name and weight.
    public class ChamferElement : IElement {
        private readonly GameObject part;
        private readonly Mesh mesh;
        private readonly string state, name;
        private readonly float grams;

        public ChamferElement(GameObject part) {
            this.part = part;
            mesh = part.GetComponent<MeshFilter>().sharedMesh;
            state = part.GetComponent<SavedObject>().state;
            if (part.TryGetComponent(out PartName partName)) {
                name = partName.name;
                grams = partName.weightInGrams;
            }
        }

        public void Load() {
            if (part == null) return;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (part.TryGetComponent(out MeshCollider collider)) collider.sharedMesh = mesh;
            part.GetComponent<SavedObject>().state = state;
            if (name != null && part.TryGetComponent(out PartName partName)) {
                partName.name = name;
                partName.weightInGrams = grams;
            }
        }
    }
}
