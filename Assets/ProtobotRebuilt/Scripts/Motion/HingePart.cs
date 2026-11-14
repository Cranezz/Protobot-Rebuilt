using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    // V2: a VEX hinge as one part (2 to 5 holes). The part is VEX's bottom leaf; the top leaf and the pin come from
    // VEX's own models (they're modelled apart, not assembled): the pin on the knuckles' line, the top leaf on the
    // same stretch of it, turned about the pin. Its angle between the leaves (0° folded shut, 180° open flat, up to
    // 270°) is set from its right-click menu, and whatever is screwed to the top leaf turns with it (one undo step).
    // Saved as "hinge:<degrees>". In the simulation the leaves turn freely about the pin within those limits.
    public class HingePart : MonoBehaviour {
        public const float MinAngle = 0f, MaxAngle = 270f, Flat = 180f;

        private Transform pivot, leaf;
        private float angle = Flat;
        private Vector3 pinPoint;   // part space, on the knuckles' line
        private Coroutine easing;

        public static bool IsHinge(string id) => id != null && id.StartsWith("VXHINGES-") && id.EndsWith("(bottom)");

        public Transform Leaf => leaf;
        public float Angle => angle;
        public Vector3 PinPoint => transform.TransformPoint(pinPoint);
        public Vector3 PinAxis => transform.TransformDirection(Vector3.up).normalized;

        private void Awake() {
            if (!TryGetComponent(out SavedObject saved) || !IsHinge(saved.id)) { enabled = false; return; }
            var name = saved.id.Substring("VXHINGES-".Length);
            var size = name.Substring(0, name.IndexOf(' '));
            var type = PartsManager.GetPartType("VXHINGES");
            if (type == null) { enabled = false; return; }
            var templates = type.GetComponentsInChildren<PartData>(true);
            var top = templates.FirstOrDefault(p => p.gameObject.name == size + " Hole Hinge (top)");
            var pin = templates.FirstOrDefault(p => p.gameObject.name == size + " Hole Hinge Pin");
            if (top == null || pin == null) { enabled = false; return; }

            var bottomMesh = GetComponent<MeshFilter>().sharedMesh;
            var topMesh = top.GetComponent<MeshFilter>().sharedMesh;
            var pinMesh = pin.GetComponent<MeshFilter>().sharedMesh;
            // The knuckles' line: the top leaf's knuckle holes (along y).
            var knuckle = top.GetComponentsInChildren<HoleCollider>(true).FirstOrDefault(h => Mathf.Abs((h.transform.localRotation * Vector3.forward).y) > 0.9f);
            pinPoint = knuckle != null ? new Vector3(knuckle.transform.localPosition.x, bottomMesh.bounds.center.y, knuckle.transform.localPosition.z)
                                       : new Vector3(bottomMesh.bounds.min.x + 0.125f, bottomMesh.bounds.center.y, bottomMesh.bounds.center.z);
            var shiftY = bottomMesh.bounds.center.y - topMesh.bounds.center.y;

            // A copy (duplicate) already has its leaf.
            pivot = transform.Find("Hinge Pivot");
            if (pivot == null) {
                pivot = new GameObject("Hinge Pivot") { layer = gameObject.layer }.transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = pinPoint;
                var leafObject = Instantiate(top.gameObject, pivot);
                leafObject.name = "Hinge Leaf";
                foreach (var c in leafObject.GetComponents<Component>())
                    if (c is SavedObject || c is PartData || c is Parts_List.PartName) DestroyImmediate(c);
                leafObject.transform.localPosition = new Vector3(0f, shiftY, 0f) - pinPoint;
                leafObject.AddComponent<SelectAs>().target = gameObject;
                foreach (var hole in leafObject.GetComponentsInChildren<HoleCollider>(true)) hole.Refresh();
                var pinObject = new GameObject("Hinge Pin") { layer = gameObject.layer };
                pinObject.transform.SetParent(transform, false);
                pinObject.transform.localPosition = new Vector3(pinPoint.x - pinMesh.bounds.center.x, bottomMesh.bounds.center.y - pinMesh.bounds.center.y, pinPoint.z - pinMesh.bounds.center.z);
                pinObject.AddComponent<MeshFilter>().sharedMesh = pinMesh;
                pinObject.AddComponent<MeshRenderer>().sharedMaterials = pin.GetComponent<MeshRenderer>().sharedMaterials;
                pinObject.AddComponent<SelectAs>().target = gameObject;
            }
            leaf = pivot.Find("Hinge Leaf");
            SetInstant(Flat);
        }

        private void Start() {
            var token = Dynamics.Token(gameObject, "hinge");
            if (token != null && float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) SetInstant(a);
        }

        private void SetInstant(float degrees) {
            angle = Mathf.Clamp(degrees, MinAngle, MaxAngle);
            if (pivot != null) pivot.localRotation = Quaternion.AngleAxis(angle, Vector3.up);
            Dynamics.SetToken(gameObject, "hinge", Mathf.Abs(angle - Flat) < 0.01f ? null : angle.ToString("0.##", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ turning it by hand

        /// <summary>What's fastened to the top leaf (through its holes), not reaching the bottom leaf.</summary>
        public List<GameObject> Riders(out bool frame) {
            frame = false;
            if (leaf == null) return new List<GameObject>();
            var found = Dynamics.Fastened(new[] { leaf.gameObject }, new[] { gameObject }, null, out frame);
            found.Remove(leaf.gameObject);
            return found.Where(o => o != null && o.GetComponent<SavedObject>() != null && !o.transform.IsChildOf(transform)).ToList();
        }

        private List<GameObject> manualRiders;
        private List<IElement> manualBefore;
        private float manualStart;

        public bool BeginManual() {
            var riders = Riders(out var frame);
            if (frame) {
                Interface.Interface.Say("Can't turn the hinge: what's on its top leaf is also fastened to what its bottom leaf is on", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return false;
            }
            manualRiders = riders;
            manualBefore = riders.Select(r => (IElement)new ObjectElement(r)).ToList();
            manualBefore.Add(new HingeElement(this, angle));
            manualStart = angle;
            return true;
        }

        public void ManualTo(float degrees) {
            if (manualRiders == null) return;
            var to = Mathf.Clamp(degrees, MinAngle, MaxAngle);
            var turn = Quaternion.AngleAxis(to - angle, PinAxis);
            var point = PinPoint;
            foreach (var rider in manualRiders)
                if (rider != null) rider.transform.SetPositionAndRotation(point + turn * (rider.transform.position - point), turn * rider.transform.rotation);
            SetInstant(to);
        }

        public void EndManual() {
            if (manualRiders == null) return;
            if (Mathf.Abs(angle - manualStart) > 0.01f) {
                StateSystem.AddElements(manualBefore);
                var after = manualRiders.Select(r => (IElement)new ObjectElement(r)).ToList();
                after.Add(new HingeElement(this, angle));
                StateSystem.AddState(new State(after));
            }
            manualRiders = null;
        }

        private class HingeElement : IElement {
            private readonly HingePart hinge;
            private readonly float angle;
            public HingeElement(HingePart hinge, float angle) { this.hinge = hinge; this.angle = angle; }
            public void Load() { if (hinge != null) hinge.SetInstant(angle); }
        }

        // ------------------------------------------------------------------ the window (right-click > Hinge angle…)

        public static void Edit(HingePart hinge) {
            if (hinge == null || !hinge.BeginManual()) return;
            var dialog = Dialog.Open("Hinge angle", new Vector2(460f, 200f));
            Ui.Column(dialog.Body, 8f);   // V2: the body lays its rows out (they overlapped)
            Dialog.Heading(dialog.Body, "Between the leaves (0° shut, 180° flat, 270° most)");
            MotionUi.Slider(dialog.Body, hinge.angle / MaxAngle, 0f, 5f / MaxAngle, f => (f * MaxAngle).ToString("0", CultureInfo.InvariantCulture) + "°", f => hinge.ManualTo(f * MaxAngle));
            dialog.OnClose += () => {
                hinge.EndManual();
                Interface.Interface.Say("Hinge at " + hinge.angle.ToString("0", CultureInfo.InvariantCulture) + "°");
            };
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(110f, 34f);
        }
    }
}
