using System.Collections.Generic;
using System.Reflection;
using Protobot.Transformations;
using UnityEngine;

namespace Protobot {
    // V2: a capped shaft (VEX-CAD "6" Capped Shaft") from the parts list or the Move tool goes into the hole under the
    // mouse like a shaft: along the hole's axis, the cap's underside flush on the hole's face, the shaft through it.
    // Its axis, which end the cap is on and where the cap meets the shaft come from its model (worked out once).
    public class CappedShaftPlaceDisplacement : PlaceDisplacement {
        private MouseCast mouseCast;
        private HoleFace hoverHoleFace;
        public override bool ModifyRotation => true;

        public static void AddTo(Placement placement, List<PlaceDisplacement> list) {
            var displacement = placement.gameObject.AddComponent<CappedShaftPlaceDisplacement>();
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var other in list)
                if (other is HolePlaceDisplacement hole) {
                    displacement.mouseCast = typeof(HolePlaceDisplacement).GetField("mouseCast", Flags)?.GetValue(hole) as MouseCast;
                    displacement.hoverHoleFace = typeof(HolePlaceDisplacement).GetField("hoverHoleFace", Flags)?.GetValue(hole) as HoleFace;
                }
            list.Insert(0, displacement);
        }

        private struct Shape {
            public Vector3 capToTip;   // local, unit
            public Vector3 underCap;   // local: on the axis where the cap meets the shaft
        }

        private static readonly Dictionary<int, Shape?> shapes = new();

        private static Shape? ShapeOf(Mesh mesh) {
            if (mesh == null || !mesh.isReadable) return null;
            if (shapes.TryGetValue(mesh.GetInstanceID(), out var known)) return known;
            Shape? result = null;
            var b = mesh.bounds;
            var axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? Vector3.right : b.size.y >= b.size.z ? Vector3.up : Vector3.forward;
            var half = Vector3.Dot(b.extents, axis);
            var centre = b.center;
            var vertices = mesh.vertices;
            float RadiusAt(Vector3 v) => Vector3.ProjectOnPlane(v - centre, axis).magnitude;
            // The shaft's own radius: the middle third's largest.
            float shaft = 0f, endLow = 0f, endHigh = 0f;
            foreach (var v in vertices) {
                var t = Vector3.Dot(v - centre, axis);
                var r = RadiusAt(v);
                if (Mathf.Abs(t) < half / 3f) shaft = Mathf.Max(shaft, r);
                else if (t < -half * 0.8f) endLow = Mathf.Max(endLow, r);
                else if (t > half * 0.8f) endHigh = Mathf.Max(endHigh, r);
            }
            if (shaft <= 0f) shaft = Mathf.Min(endLow, endHigh);   // a plain prism: points only at its ends
            if (shaft > 0f && Mathf.Max(endLow, endHigh) > shaft * 1.05f && Mathf.Abs(endHigh - endLow) > 0.004f) {
                var capSign = endHigh > endLow ? 1f : -1f;
                // Where the cap ends: the innermost vertex wider than the shaft at the cap's end.
                var inner = half;
                foreach (var v in vertices) {
                    var t = Vector3.Dot(v - centre, axis) * capSign;
                    if (RadiusAt(v) > shaft * 1.05f && t > 0f) inner = Mathf.Min(inner, t);
                }
                result = new Shape { capToTip = -axis * capSign, underCap = centre + axis * capSign * inner };
            }
            shapes[mesh.GetInstanceID()] = result;
            return result;
        }

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            displacement = null;
            if (hoverHoleFace == null || !hoverHoleFace.gameObject.activeInHierarchy || hoverHoleFace.direction == Vector3.zero || hoverHoleFace.hole == null) return false;
            Mesh mesh = null;
            GameObject self = null;
            if (placementData is PartPlacementData part) {
                if (part.partGenerator == null || !part.partGenerator.GetId().Contains("Capped Shaft")) return false;
                mesh = part.partGenerator.GetMesh();
            }
            else if (placementData.TryParse(out GameObjectPlacementData moving)) {
                self = moving.GetGameObject();
                if (self == null || !self.TryGetComponent(out SavedObject saved) || !saved.id.Contains("Capped Shaft")) return false;
                mesh = self.GetComponent<MeshFilter>()?.sharedMesh;
            }
            if (hoverHoleFace.hole.part == self) return false;
            var shape = ShapeOf(mesh);
            if (shape == null) return false;
            // The shaft goes in against the face's outward direction; the cap's underside on the face.
            var inward = -hoverHoleFace.direction.normalized;
            var turn = Quaternion.FromToRotation(shape.Value.capToTip, inward);
            var position = hoverHoleFace.hole.position + hoverHoleFace.direction.normalized * (hoverHoleFace.hole.depth / 2f) - turn * shape.Value.underCap;
            displacement = new Displacement(position, turn);
            return true;
        }
    }
}
