using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    // V2: VEX's universal joint (Advanced Gear Kit) works like one: its model is cut in two at the cross, the far
    // yoke (with its set screw and its shaft hole) turns about the cross, so it can sit bent up to 45° any way
    // (right-click > Bend joint…; what's in its far yoke, the shaft and what's on it, turns with it, one undo step;
    // saved as "uj:angle,toward"). Both shafts turn together (GearTrain couples them); in the simulation the two
    // yokes are bodies joined at the cross: free to bend, but not to twist, so it carries the turn.
    public class UniversalJoint : MonoBehaviour {
        public const float MaxBend = 45f;
        private Transform pivot, yoke;
        private float bend, toward;
        private Vector3 crossPoint;

        public Transform Yoke => yoke;
        public Vector3 Cross => transform.TransformPoint(crossPoint);
        /// <summary>The cross's two pins (world).</summary>
        public Vector3 PinA => transform.TransformDirection(Vector3.right);
        public Vector3 PinB => (pivot != null ? pivot.rotation : transform.rotation) * Vector3.forward;
        public float Bend => bend;

        public static bool Is(string id) => id == "VXADVANCEDGEARS-Universal Joint";

        private static readonly Dictionary<Mesh, (Mesh near, Mesh far, float cross)> Split = new();

        private void Awake() {
            var filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) { enabled = false; return; }
            var source = filter.sharedMesh;
            pivot = transform.Find("UJ Cross");
            if (pivot != null) {
                yoke = pivot.Find("UJ Far Yoke");
                crossPoint = pivot.localPosition;
                return;   // a copy: already cut
            }
            if (!Split.TryGetValue(source, out var cut)) cut = Split[source] = Cut(source);
            crossPoint = new Vector3(source.bounds.center.x, cut.cross, source.bounds.center.z);
            filter.sharedMesh = cut.near;
            if (TryGetComponent(out MeshCollider collider)) collider.sharedMesh = cut.near;
            pivot = new GameObject("UJ Cross") { layer = gameObject.layer }.transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = crossPoint;
            yoke = new GameObject("UJ Far Yoke") { layer = gameObject.layer }.transform;
            yoke.SetParent(pivot, false);
            yoke.localPosition = -crossPoint;
            yoke.gameObject.AddComponent<MeshFilter>().sharedMesh = cut.far;
            yoke.gameObject.AddComponent<MeshRenderer>().sharedMaterials = GetComponent<MeshRenderer>().sharedMaterials;
            yoke.gameObject.AddComponent<MeshCollider>().sharedMesh = cut.far;
            yoke.gameObject.AddComponent<SelectAs>().target = gameObject;
        }

        private void Start() {
            // The far shaft hole goes with the far yoke (holes are added after Awake: VexHoleFixes).
            if (yoke != null)
                foreach (var hole in GetComponentsInChildren<HoleCollider>(true))
                    if (hole.transform.parent == transform && hole.transform.localPosition.y > crossPoint.y + 0.05f) {
                        hole.transform.SetParent(yoke, true);
                        hole.Refresh();
                    }
            var token = Dynamics.Token(gameObject, "uj");
            if (token != null) {
                var v = token.Split(',');
                if (v.Length == 2 && float.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                    && float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) SetInstant(a, t);
            }
        }

        // The model's connected pieces: the near yoke (and the cross and its middle) stay, the far yoke and its set
        // screw (pieces whose middle is past the cross) go on the far side.
        private static (Mesh, Mesh, float) Cut(Mesh mesh) {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var cross = mesh.bounds.center.y;
            var weld = new Dictionary<Vector3Int, int>();
            var point = new int[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) {
                var k = Vector3Int.RoundToInt(vertices[i] * 2000f);
                if (!weld.TryGetValue(k, out var w)) weld[k] = w = weld.Count;
                point[i] = w;
            }
            var parent = new int[weld.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            for (var i = 0; i < triangles.Length; i += 3) {
                var a = Find(point[triangles[i]]);
                parent[Find(point[triangles[i + 1]])] = a;
                parent[Find(point[triangles[i + 2]])] = a;
            }
            var sum = new Dictionary<int, (Vector3 total, int count)>();
            foreach (var t in triangles) {
                var r = Find(point[t]);
                sum.TryGetValue(r, out var s);
                sum[r] = (s.total + vertices[t], s.count + 1);
            }
            List<int> near = new(), far = new();
            for (var i = 0; i < triangles.Length; i += 3) {
                var r = Find(point[triangles[i]]);
                var middle = sum[r].total / sum[r].count;
                var list = middle.y > cross + 0.1f ? far : near;
                list.Add(triangles[i]);
                list.Add(triangles[i + 1]);
                list.Add(triangles[i + 2]);
            }
            Mesh Piece(List<int> tris, string name) {
                var m = Object.Instantiate(mesh);
                m.name = mesh.name + name;
                m.subMeshCount = 1;
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                return m;
            }
            return (Piece(near, " (near)"), Piece(far, " (far)"), cross);
        }

        private void SetInstant(float angle, float direction) {
            bend = Mathf.Clamp(angle, 0f, MaxBend);
            toward = ((direction % 360f) + 360f) % 360f;
            var a = toward * Mathf.Deg2Rad;
            // Turning about an axis across the joint tips the far yoke toward its perpendicular.
            var axis = new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a));
            if (pivot != null) pivot.localRotation = Quaternion.AngleAxis(bend, axis);
            Dynamics.SetToken(gameObject, "uj", bend < 0.01f ? null : bend.ToString("0.#", CultureInfo.InvariantCulture) + "," + toward.ToString("0", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ bending it by hand

        private List<GameObject> riders;
        private List<IElement> before;
        private float startBend, startToward;

        private bool BeginManual() {
            var found = Dynamics.Fastened(new[] { yoke.gameObject }, new[] { gameObject }, null, out var frame);
            if (frame) {
                Interface.Interface.Say("Can't bend it: what's in its far yoke is also fastened to what's on its near side", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return false;
            }
            // The shaft in the far yoke (shafts aren't screwed: found by the hole it's in) and what's on it.
            foreach (var rotor in GearTrain.Rotors())
                if (rotor.parts.Contains(yoke.gameObject))
                    foreach (var part in rotor.parts) found.Add(part);
            riders = found.Where(o => o != null && o.GetComponent<SavedObject>() != null && !o.transform.IsChildOf(transform)).Distinct().ToList();
            before = riders.Select(r => (IElement)new ObjectElement(r)).ToList();
            before.Add(new BendElement(this, bend, toward));
            startBend = bend;
            startToward = toward;
            return true;
        }

        private void ManualTo(float angle, float direction) {
            var old = pivot.rotation;
            SetInstant(angle, direction);
            var turn = pivot.rotation * Quaternion.Inverse(old);
            var c = Cross;
            foreach (var r in riders)
                if (r != null) r.transform.SetPositionAndRotation(c + turn * (r.transform.position - c), turn * r.transform.rotation);
        }

        private void EndManual() {
            if (riders == null) return;
            if (Mathf.Abs(bend - startBend) > 0.01f || Mathf.Abs(toward - startToward) > 0.01f) {
                StateSystem.AddElements(before);
                var after = riders.Select(r => (IElement)new ObjectElement(r)).ToList();
                after.Add(new BendElement(this, bend, toward));
                StateSystem.AddState(new State(after));
            }
            riders = null;
        }

        private class BendElement : IElement {
            private readonly UniversalJoint joint;
            private readonly float bend, toward;
            public BendElement(UniversalJoint joint, float bend, float toward) { this.joint = joint; this.bend = bend; this.toward = toward; }
            public void Load() { if (joint != null) joint.SetInstant(bend, toward); }
        }

        public static void Edit(UniversalJoint joint) {
            if (joint == null || joint.yoke == null || !joint.BeginManual()) return;
            var dialog = Dialog.Open("Universal joint", new Vector2(460f, 260f));
            Ui.Column(dialog.Body, 8f);   // V2: the body lays its rows out (they overlapped)
            Dialog.Heading(dialog.Body, "Bend (0° straight, 45° most)");
            MotionUi.Slider(dialog.Body, joint.bend / MaxBend, 0f, 1f / MaxBend, f => (f * MaxBend).ToString("0", CultureInfo.InvariantCulture) + "°", f => joint.ManualTo(f * MaxBend, joint.toward));
            Dialog.Heading(dialog.Body, "Toward (round the joint)");
            MotionUi.Slider(dialog.Body, joint.toward / 360f, 0f, 15f / 360f, f => (f * 360f).ToString("0", CultureInfo.InvariantCulture) + "°", f => joint.ManualTo(joint.bend, f * 360f));
            dialog.OnClose += () => {
                joint.EndManual();
                Interface.Interface.Say("Universal joint bent " + joint.bend.ToString("0", CultureInfo.InvariantCulture) + "°");
            };
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(110f, 34f);
        }
    }
}
