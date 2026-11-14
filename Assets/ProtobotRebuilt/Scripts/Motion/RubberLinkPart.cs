using System.Collections.Generic;
using Protobot.Tools;
using UnityEngine;

namespace Protobot.Motion {
    // V2: VEX's rubber link: a rubber body with an 8-32 stud at each end. Its studs are marked like a cylinder rod's
    // thread while the mouse is on it (or something is being placed), a standoff or coupler put on it goes onto the
    // stud nearer the mouse against the rubber (never into it), and it bends in its rubber (the Bend tool, saved as a
    // "bend:" like a bent shaft): the far stud and what's on it follow, and its hole detector follows both studs (a
    // second trigger along the far stud).
    public class RubberLinkPart : MonoBehaviour {
        private const float ThreadRadius = 0.09f;
        private Transform markA, markB;
        private MeshRenderer rendererA, rendererB;
        private string placedFor;
        private CapsuleCollider farCapsule;

        /// <summary>A stud: the middle of its face on the rubber, the way out along it, its length (world).</summary>
        public struct Stud {
            public Vector3 face, outward;
            public float length;
        }

        public static bool Is(string id) => id == PartBend.LinkId;

        private void Start() {
            markA = Mark("Stud Mark A", out rendererA);
            markB = Mark("Stud Mark B", out rendererB);
            Refresh(PartBend.SavedBends(gameObject));
        }

        private Transform Mark(string name, out MeshRenderer markRenderer) {
            var mark = new GameObject(name) { layer = gameObject.layer };
            mark.transform.SetParent(transform, false);
            mark.AddComponent<MeshFilter>();
            markRenderer = mark.AddComponent<MeshRenderer>();
            markRenderer.sharedMaterial = Dynamics.Highlight;
            markRenderer.enabled = false;
            return mark.transform;
        }

        // Each stud in the part's space (with its scale) for these bends: face, outward, length.
        private (Vector3 face, Vector3 outward, float length) LocalStud(bool high, IList<PartBend.Bend> bends) {
            var (start, rubber) = PartBend.LinkRubber(gameObject);
            var length = PartBend.Frame(gameObject, PartBend.Straight(gameObject)).length;
            var s = high ? start + rubber : start;
            var (point, turn) = PartBend.CenterAt(gameObject, s, bends);
            var along = turn * PartBend.AlongLocal(gameObject);
            return (point, high ? along : -along, high ? length - s : start);
        }

        public Stud StudOf(bool high) {
            var (face, outward, length) = LocalStud(high, PartBend.SavedBends(gameObject));
            return new Stud { face = PartBend.ToWorld(gameObject, face), outward = (transform.rotation * outward).normalized, length = length };
        }

        /// <summary>Where a part of this length goes on the stud nearer the mouse: its middle, and the stud's way out
        /// (world). It sits against the rubber.</summary>
        public (Vector3 center, Vector3 outward, bool high) Seat(Vector3 near, float depth) {
            var a = StudOf(true);
            var b = StudOf(false);
            var high = Vector3.Distance(near, a.face + a.outward * a.length / 2f) <= Vector3.Distance(near, b.face + b.outward * b.length / 2f);
            var stud = high ? a : b;
            return (stud.face + stud.outward * (depth / 2f), stud.outward, high);
        }

        /// <summary>The marks go where the studs are with these bends (the Bend tool's preview too).</summary>
        public void Refresh(IList<PartBend.Bend> bends) {
            if (markA == null || markB == null) return;
            foreach (var (mark, high) in new[] { (markA, true), (markB, false) }) {
                var (face, outward, length) = LocalStud(high, bends);
                var scale = transform.localScale;
                // The thread mesh runs from its origin back along -z: the origin on the stud's face, facing in.
                mark.localPosition = new Vector3(face.x / scale.x, face.y / scale.y, face.z / scale.z);
                mark.localRotation = Quaternion.LookRotation(-outward);
                var filter = mark.GetComponent<MeshFilter>();
                if (filter.sharedMesh == null || Mathf.Abs(filter.sharedMesh.bounds.size.z - length) > 0.001f)
                    filter.sharedMesh = Thread(length);
            }
        }

        private static readonly Dictionary<float, Mesh> threads = new();

        private static Mesh Thread(float length) {
            var key = Mathf.Round(length * 1000f) / 1000f;
            if (threads.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            return threads[key] = RodThread.ThreadMesh(key, ThreadRadius);
        }

        private void Update() {
            if (rendererA == null) return;
            var show = false;
            var camera = Camera.main;
            if (camera != null && !gameObject.IsDeleted() && !MouseInput.overUI
                && Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions")))
                show = hit.collider.transform.IsChildOf(transform);
            show = (show || Placement.IsPlacing) && !Sandbox.Running && !gameObject.IsDeleted();
            rendererA.enabled = rendererB.enabled = show;
        }

        // The hole detector along both studs (as the saved bends leave them).
        private void LateUpdate() {
            var state = TryGetComponent(out SavedObject saved) ? saved.state ?? "" : "";
            if (state == placedFor || !TryGetComponent(out Screw screw) || screw.holeDetector == null) return;
            placedFor = state;
            var bends = PartBend.SavedBends(gameObject);
            Refresh(bends);
            var detector = screw.holeDetector.transform;
            var main = detector.GetComponent<CapsuleCollider>();
            if (main == null) return;
            var length = PartBend.Frame(gameObject, PartBend.Straight(gameObject)).length;
            if (bends.Count == 0) {
                detector.localPosition = new Vector3(0f, 0f, -length / 2f);
                detector.localRotation = Quaternion.Euler(90f, 0f, 0f);
                main.height = length;
                main.center = Vector3.zero;
                if (farCapsule != null) farCapsule.enabled = false;
                return;
            }
            var (start, rubber) = PartBend.LinkRubber(gameObject);
            // The first stud (where it was put in): from the rubber's end to the tip, as bent.
            var from = start + rubber - 0.05f;
            var (middle, turn) = PartBend.CenterAt(gameObject, (from + length) / 2f, bends);
            detector.position = PartBend.ToWorld(gameObject, middle);
            detector.rotation = Quaternion.FromToRotation(Vector3.up, transform.rotation * (turn * PartBend.AlongLocal(gameObject)));
            main.height = length - from;
            main.center = Vector3.zero;
            // The far stud: a second trigger on the same body.
            if (farCapsule == null) {
                var far = new GameObject("Far Stud") { layer = detector.gameObject.layer };
                far.transform.SetParent(detector, false);
                farCapsule = far.AddComponent<CapsuleCollider>();
                farCapsule.isTrigger = true;
                farCapsule.radius = main.radius;
                farCapsule.direction = 1;
            }
            farCapsule.enabled = true;
            var to = start + 0.05f;
            var (farMiddle, farTurn) = PartBend.CenterAt(gameObject, to / 2f, bends);
            farCapsule.transform.position = PartBend.ToWorld(gameObject, farMiddle);
            farCapsule.transform.rotation = Quaternion.FromToRotation(Vector3.up, transform.rotation * (farTurn * PartBend.AlongLocal(gameObject)));
            farCapsule.height = to;
        }

        /// <summary>For the simulation: whether a hole lies on the far stud (not the one it was put in by).</summary>
        public bool OnFarStud(Vector3 point) {
            var a = StudOf(true);
            var b = StudOf(false);
            return Vector3.Distance(point, b.face + b.outward * b.length / 2f) < Vector3.Distance(point, a.face + a.outward * a.length / 2f);
        }
    }
}
