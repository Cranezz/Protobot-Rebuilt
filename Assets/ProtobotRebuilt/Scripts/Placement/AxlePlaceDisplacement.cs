using System.Collections.Generic;
using System.Reflection;
using Protobot.Transformations;
using UnityEngine;

namespace Protobot {
    // V2: round parts go straight onto what they'd be put on, from the parts list or the Move tool: wheels, gears,
    // sprockets, pulleys, spacers, washers, collars, bearings (any part whose middle hole runs through its thin way).
    // - Over a screw: it slides along it with the mouse, goes against the part beside it when close (Ctrl: doesn't),
    //   and over parts already on it jumps to the nearest free stretch it fits in (between two channels, past the
    //   last part). Nuts too.
    // - Over a shaft: the same along the shaft.
    // - Over a hole: its middle hole lines up with it, the part on the face.
    // It keeps the part's own turn about the axle (R turns it), and faces it whichever way along the axle is the
    // smaller turn.
    public class AxlePlaceDisplacement : PlaceDisplacement {
        private MouseCast mouseCast;
        private HoleFace hoverHoleFace;

        public override bool ModifyRotation => true;

        public static void AddTo(Placement placement, List<PlaceDisplacement> list) {
            var displacement = placement.gameObject.AddComponent<AxlePlaceDisplacement>();
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var other in list)
                if (other is HolePlaceDisplacement hole) {
                    displacement.mouseCast = typeof(HolePlaceDisplacement).GetField("mouseCast", Flags)?.GetValue(hole) as MouseCast;
                    displacement.hoverHoleFace = typeof(HolePlaceDisplacement).GetField("hoverHoleFace", Flags)?.GetValue(hole) as HoleFace;
                }
            list.Insert(0, displacement);
        }

        // A part's axle in its own space: the middle hole's centre and direction, and how far the part reaches along it.
        public struct Axle {
            public Vector3 center, axis;
            public float low, high;
        }

        private static readonly Dictionary<int, (bool ok, Axle axle)> axles = new();

        /// <summary>The axle of a round part (a template or a part in the build); false for anything else.
        /// <paramref name="byHole"/>: its length along the axle is its middle hole's (a wheel's or gear's hub: that is
        /// what goes flush against the part beside it, the rim and rollers can stand out past it); otherwise the whole
        /// part's (spacers, nuts, washers).</summary>
        public static bool AxleOf(GameObject part, bool byHole, out Axle axle) {
            axle = default;
            if (part == null || !part.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return false;
            var key = (part.GetInstanceID() ^ filter.sharedMesh.GetInstanceID()) * 2 + (byHole ? 1 : 0);
            if (axles.TryGetValue(key, out var known)) {
                axle = known.axle;
                return known.ok;
            }
            var ok = Measure(part, filter.sharedMesh, byHole, out axle);
            axles[key] = (ok, axle);
            return ok;
        }

        public static bool AxleOf(GameObject part, out Axle axle) => AxleOf(part, false, out axle);

        /// <summary>Wheels, gears, sprockets and pulleys: what sits on an axle by its hub.</summary>
        public static bool ByHub(string id) {
            var upper = (id ?? "").ToUpperInvariant();
            if (upper.StartsWith("OMNI") || upper.StartsWith("TWHL") || upper.StartsWith("FWHL") || upper.StartsWith("MECN")
                || upper.StartsWith("GEAR") || upper.StartsWith("SPKT")) return true;
            return upper.StartsWith("VX") && (upper.Contains("WHEEL") || upper.Contains("GEAR") || upper.Contains("SPROCKET") || upper.Contains("PULLEY"));
        }

        private static bool Measure(GameObject part, Mesh mesh, bool byHole, out Axle axle) {
            axle = default;
            var bounds = mesh.bounds;
            HoleCollider middle = null;
            var best = float.MaxValue;
            foreach (var hole in part.GetComponentsInChildren<HoleCollider>(true)) {
                if (hole.transform.parent != part.transform) continue;
                var d = (hole.transform.localPosition - bounds.center).sqrMagnitude;
                if (d < best) {
                    best = d;
                    middle = hole;
                }
            }
            if (middle == null || best > 0.2f * 0.2f) return false;
            var axis = (middle.transform.localRotation * Vector3.forward).normalized;
            var size = bounds.size;
            var thick = Mathf.Abs(axis.x) * size.x + Mathf.Abs(axis.y) * size.y + Mathf.Abs(axis.z) * size.z;
            // Across the axle: the two other sides (the box's sides most square to the axle).
            var across = new List<float>();
            for (var i = 0; i < 3; i++) {
                var unit = Vector3.zero;
                unit[i] = 1f;
                if (Mathf.Abs(Vector3.Dot(unit, axis)) < 0.7f) across.Add(size[i]);
            }
            if (across.Count != 2) return false;
            var wide = Mathf.Max(across[0], across[1]);
            var narrow = Mathf.Min(across[0], across[1]);
            // Thin along its axle, not a long bar (a channel's middle hole isn't where it goes on).
            if (thick > narrow + 0.01f || wide > 6.5f || wide > narrow * 4f) return false;
            axle.center = middle.transform.localPosition;
            axle.axis = axis;
            float low = float.MaxValue, high = float.MinValue;
            if (byHole) {
                var depth = middle.transform.localScale.z;
                low = -depth / 2f;
                high = depth / 2f;
            }
            else if (mesh.isReadable)
                foreach (var v in mesh.vertices) {
                    var along = Vector3.Dot(v - axle.center, axis);
                    low = Mathf.Min(low, along);
                    high = Mathf.Max(high, along);
                }
            else {
                var mid = Vector3.Dot(bounds.center - axle.center, axis);
                low = mid - thick / 2f;
                high = mid + thick / 2f;
            }
            axle.low = low;
            axle.high = high;
            return high > low;
        }

        private static readonly string[] Skip = { "SCRW", "SNDF", "PLSI", "MESI", "SHFT" };

        // Ctrl: slide freely along a screw or shaft, without going against the parts beside it.
        private static bool Free {
            get {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                return keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed);
            }
        }

        private static bool PartOf(PlacementData data, out GameObject template, out GameObject self) {
            template = self = null;
            var id = data.objectId ?? "";
            if (id.Contains("Hole")) return false;   // moving a part by one of its holes: that hole goes where clicked
            foreach (var prefix in Skip)
                if (id.StartsWith(prefix)) return false;
            if (data.TryParse(out GameObjectPlacementData objData)) {
                self = template = objData.GetGameObject();
                return template != null && template.GetComponent<SavedObject>() != null;
            }
            if (data.TryParse(out PartPlacementData partData) && partData.partGenerator != null && partData.partGenerator.TryGetPartData(out var pd) && pd != null) {
                template = pd.gameObject;
                return true;
            }
            return false;
        }

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            displacement = null;
            if (mouseCast == null || !PartOf(placementData, out var template, out var self)) return false;
            var id = self != null && self.TryGetComponent(out SavedObject saved) ? saved.id
                : placementData.TryParse(out PartPlacementData typed) && typed.partGenerator != null ? typed.partGenerator.GetId() : placementData.objectId;
            if (!AxleOf(template, ByHub(id), out var axle)) return false;
            // A nut turns its face to the part it goes against.
            var nut = (id ?? "").StartsWith("NUT");
            var hover = mouseCast.gameObject;
            var current = Placement.Instance != null ? Placement.Instance.transform.rotation : Quaternion.identity;
            var length = axle.high - axle.low;

            if (hover != null && hover.TryGetComponent(out Screw screw)) {
                var head = screw.transform.position;
                var down = -screw.transform.forward;
                var used = AxleLine.Used(self, head, down, screw.ScrewLength);
                var start = AxleLine.Free(used, Vector3.Dot(mouseCast.hit.point - head, down), length, nut ? screw.Shoulder : 0f, screw.ScrewLength, true, Free, out var edge);   // V2: a nut stops at a shoulder
                if (!start.HasValue) return false;
                displacement = Onto(axle, current, head, down, start.Value, screw.transform.up, nut ? edge : 0);
                return true;
            }
            if (hover != null && hover.TryGetComponent(out Shaft shaft) && hover.TryGetComponent(out MeshFilter shaftFilter) && shaftFilter.sharedMesh != null) {
                var b = shaftFilter.sharedMesh.bounds;
                var t = shaft.transform;
                var from = t.TransformPoint(new Vector3(b.center.x, b.center.y, b.min.z));
                var to = t.TransformPoint(new Vector3(b.center.x, b.center.y, b.max.z));
                var line = to - from;
                if (line.sqrMagnitude < 1e-6f) return false;
                var direction = line.normalized;
                var used = AxleLine.Used(self, from, direction, line.magnitude);
                var start = AxleLine.Free(used, Vector3.Dot(mouseCast.hit.point - from, direction), length, 0f, line.magnitude, false, Free, out var edge);
                if (!start.HasValue) return false;
                displacement = Onto(axle, current, from, direction, start.Value, t.up, nut ? edge : 0);
                return true;
            }
            if (placementData.objectId.StartsWith("NUT")) return false;   // a nut over a hole: as before
            if (hoverHoleFace != null && hoverHoleFace.gameObject.activeInHierarchy && hoverHoleFace.direction != Vector3.zero
                && (hover == null || hover.GetComponent<SavedObject>() == null || hoverHoleFace.hole == null || hoverHoleFace.hole.part != self)) {
                displacement = Onto(axle, current, hoverHoleFace.position, hoverHoleFace.direction.normalized, 0f, hoverHoleFace.transform.up);
                return true;
            }
            return false;
        }

        // The part's pose with its axle on the line (origin + direction * distance), reaching from `start` along it.
        // `face`: which way its axle (a nut's face: the side it threads on from) must point: -1 back along the line, +1
        // along it, 0 whichever is the smaller turn.
        private static Displacement Onto(Axle axle, Quaternion current, Vector3 origin, Vector3 direction, float start, Vector3 up, int face = 0) {
            var axisNow = current * axle.axis;
            var sign = face != 0 ? face : Vector3.Dot(axisNow, direction) >= 0f ? 1f : -1f;
            var rotation = Quaternion.FromToRotation(axisNow * sign, direction) * current;
            var nearEnd = sign > 0f ? axle.low : -axle.high;
            var position = origin + direction * (start - nearEnd) - rotation * axle.center;
            return new Displacement(position, rotation);
        }
    }
}
