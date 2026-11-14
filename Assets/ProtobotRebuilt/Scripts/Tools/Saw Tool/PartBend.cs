using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Tools {
    // V2: bending a straight part (a high strength shaft, a standoff), as teams bend HS axles and standoffs. A bend is
    // where it starts along the part (inches from the part's low end along its length), how far it turns, which way
    // (degrees round the part's length from its "up"), and its radius: the part is cut into thin slices across the
    // bend and every point is wrapped round the bend's radius (the inside squeezed, the outside stretched), so it
    // curves like real metal instead of creasing at a sharp corner. What's past the bend turns with it, straight.
    // Saved as "bend:start,angle,toward,radius;" and replayed by PartDrilling.ApplySaved (all bends at once, in the
    // straight part's frame). A standoff keeps a threaded hole in each straight end.
    public static class PartBend {
        public const string Prefix = "bend:";
        public const string LinkId = "VXHARDWARE-Rubber Link";

        public static bool IsLink(GameObject part) => part != null && part.TryGetComponent(out SavedObject saved) && saved.id == LinkId;

        /// <summary>The rubber link's rubber: where it starts from the low end (the far stud's tip) and how long it
        /// is (inches). Its studs are the thin ends.</summary>
        public static (float start, float length) LinkRubber(GameObject part) {
            var mesh = Straight(part);
            if (rubbers.TryGetValue(mesh, out var known)) return known;
            float low = float.MaxValue, high = float.MinValue;
            foreach (var v in mesh.vertices)
                if (new Vector2(v.x, v.y).magnitude > 0.12f) {
                    low = Mathf.Min(low, v.z);
                    high = Mathf.Max(high, v.z);
                }
            var result = low < high ? (low - mesh.bounds.min.z, high - low) : (mesh.bounds.size.z * 0.3f, mesh.bounds.size.z * 0.4f);
            return rubbers[mesh] = result;
        }

        private static readonly Dictionary<Mesh, (float, float)> rubbers = new();

        /// <summary>A bend of the rubber link: in its rubber, the rubber's whole length curving round.</summary>
        public static Bend LinkBend(GameObject part, float angle, float toward) {
            var (start, length) = LinkRubber(part);
            var radians = Mathf.Max(1f, Mathf.Abs(angle)) * Mathf.Deg2Rad;
            return new Bend { start = start, angle = angle, toward = toward, radius = length / radians };
        }

        public struct Bend {
            public float start, angle, toward, radius;
        }

        public static bool IsBendable(GameObject part) {
            if (part == null || !part.TryGetComponent(out SavedObject saved) || saved.id == null) return false;
            var id = saved.id;
            if (id.StartsWith("SNDF")) return true;
            if (id == LinkId) return true;   // the rubber link bends in its rubber
            if (id.StartsWith("SHFT") && id.Contains("High Strength")) return true;
            return id.Contains("HS Shaft");
        }

        /// <summary>The part's length axis (its longest side, scale included) in its own space, its low end along it
        /// and its length.</summary>
        public static (int axis, float low, float length, Vector3 middle) Frame(GameObject part, Mesh straight) {
            var s = part.transform.localScale;
            var b = straight.bounds;
            var size = Vector3.Scale(b.size, Abs(s));
            var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var middle = Vector3.Scale(b.center, s);
            return (axis, middle[axis] - size[axis] / 2f, size[axis], middle);
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>The part's thickness across its length (for the default radius).</summary>
        public static float Thickness(GameObject part, Mesh straight) {
            var (axis, _, _, _) = Frame(part, straight);
            var size = Vector3.Scale(straight.bounds.size, Abs(part.transform.localScale));
            return Mathf.Max(size[(axis + 1) % 3], size[(axis + 2) % 3]);
        }

        /// <summary>The bend's directions in the part's (scaled) space: along, toward (the way it bends), and the
        /// axis it turns about.</summary>
        public static (Vector3 along, Vector3 toward, Vector3 about) Directions(int axis, float towardDegrees) {
            var along = Vector3.zero;
            along[axis] = 1f;
            var up = Vector3.zero;
            up[(axis + 1) % 3] = 1f;
            var side = Vector3.Cross(along, up);
            var a = towardDegrees * Mathf.Deg2Rad;
            var toward = (up * Mathf.Cos(a) + side * Mathf.Sin(a)).normalized;
            return (along, toward, Vector3.Cross(along, toward));
        }

        // ------------------------------------------------------------------ the mesh

        /// <summary>A bent copy of the straight mesh (vertices in the mesh's own units: the part's scale is taken out
        /// again after bending in inches).</summary>
        public static Mesh Bent(Mesh straight, Vector3 scale, IList<Bend> bends) {
            var b = straight.bounds;
            var size = Vector3.Scale(b.size, Abs(scale));
            var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var middle = Vector3.Scale(b.center, scale);
            var low = middle[axis] - size[axis] / 2f;

            // Slices across each bend (about every 4°, at least 8), so the curve is smooth.
            var cuts = new List<float>();
            foreach (var bend in bends) {
                var arc = bend.radius * Mathf.Abs(bend.angle) * Mathf.Deg2Rad;
                var n = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(bend.angle) / 4f));
                for (var k = 0; k <= n; k++) cuts.Add(low + bend.start + arc * k / n);
            }
            var mesh = Subdivide(straight, scale, axis, cuts);

            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var original = new float[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) {
                vertices[i] = Vector3.Scale(vertices[i], scale);
                normals[i] = Div(normals[i], scale).normalized;
                original[i] = vertices[i][axis];
            }
            // Farthest bend first, each choosing what it moves by where the point was on the straight part.
            foreach (var bend in bends.OrderByDescending(x => x.start)) {
                var (along, toward, about) = Directions(axis, bend.toward);
                var angle = bend.angle * Mathf.Deg2Rad;
                var arc = bend.radius * Mathf.Abs(angle);
                var sign = Mathf.Sign(angle);
                var start = low + bend.start;
                var o = middle;
                o[axis] = start;
                var center = o + toward * (bend.radius * sign);
                for (var i = 0; i < vertices.Length; i++) {
                    var s = original[i];
                    if (s <= start) continue;
                    float turn;
                    Vector3 p = vertices[i];
                    if (s <= start + arc) {
                        // On the bend: its slice (moved back to the bend's start) turned round the centre.
                        turn = (s - start) / bend.radius * sign;
                        p -= along * (s - start);
                    }
                    else {
                        turn = angle;
                        p -= along * arc;
                    }
                    var q = Quaternion.AngleAxis(turn * Mathf.Rad2Deg, about);
                    vertices[i] = center + q * (p - center);
                    normals[i] = q * normals[i];
                }
            }
            for (var i = 0; i < vertices.Length; i++) {
                vertices[i] = Div(vertices[i], scale);
                normals[i] = Vector3.Scale(normals[i], scale).normalized;
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.name = straight.name + " (bent)";
            return mesh;
        }

        private static Vector3 Div(Vector3 v, Vector3 s) => new(v.x / (s.x == 0f ? 1f : s.x), v.y / (s.y == 0f ? 1f : s.y), v.z / (s.z == 0f ? 1f : s.z));

        // Every triangle crossing one of the cuts (planes across the axis, in inches) is split there, both sides kept.
        private static Mesh Subdivide(Mesh source, Vector3 scale, int axis, List<float> cuts) {
            var verts = new List<Vector3>(source.vertices);
            var norms = new List<Vector3>(source.normals);
            var uvs = new List<Vector2>(source.uv);
            var hasUv = uvs.Count == verts.Count;
            if (norms.Count != verts.Count) {
                source.RecalculateNormals();
                norms = new List<Vector3>(source.normals);
            }
            float S(int i) => verts[i][axis] * scale[axis];
            var subs = new List<List<int>>();
            for (var m = 0; m < source.subMeshCount; m++) subs.Add(new List<int>(source.GetTriangles(m)));
            var edgeCache = new Dictionary<(int, int, float), int>();
            int Mid(int a, int b, float cut) {
                var key = a < b ? (a, b, cut) : (b, a, cut);
                if (edgeCache.TryGetValue(key, out var made)) return made;
                var t = (cut - S(a)) / (S(b) - S(a));
                verts.Add(Vector3.Lerp(verts[a], verts[b], t));
                norms.Add(Vector3.Lerp(norms[a], norms[b], t).normalized);
                if (hasUv) uvs.Add(Vector2.Lerp(uvs[a], uvs[b], t));
                return edgeCache[key] = verts.Count - 1;
            }
            foreach (var cut in cuts.Distinct().OrderBy(c => c)) {
                for (var m = 0; m < subs.Count; m++) {
                    var tris = subs[m];
                    var result = new List<int>(tris.Count + 64);
                    for (var t = 0; t + 2 < tris.Count; t += 3) {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        float da = S(a) - cut, db = S(b) - cut, dc = S(c) - cut;
                        const float Eps = 1e-5f;
                        bool pa = da > Eps, pb = db > Eps, pc = dc > Eps, na = da < -Eps, nb = db < -Eps, nc = dc < -Eps;
                        var crosses = (pa || pb || pc) && (na || nb || nc);
                        if (!crosses) {
                            result.Add(a); result.Add(b); result.Add(c);
                            continue;
                        }
                        // Rotate so that 'a' is alone on its side (or on the plane).
                        int[] v = { a, b, c };
                        float[] d = { da, db, dc };
                        int lone = -1;
                        for (var k = 0; k < 3; k++) {
                            var s1 = Mathf.Sign(d[(k + 1) % 3]);
                            var s2 = Mathf.Sign(d[(k + 2) % 3]);
                            if (Mathf.Abs(d[k]) > Eps && Mathf.Sign(d[k]) != s1 && Mathf.Sign(d[k]) != s2) { lone = k; break; }
                        }
                        if (lone < 0) {
                            // One corner on the plane, the other two either side: split in two.
                            var on = 0;
                            for (var k = 0; k < 3; k++) if (Mathf.Abs(d[k]) <= Eps) on = k;
                            int x0 = v[on], x1 = v[(on + 1) % 3], x2 = v[(on + 2) % 3];
                            var mid = Mid(x1, x2, cut);
                            result.AddRange(new[] { x0, x1, mid, x0, mid, x2 });
                            continue;
                        }
                        int p0 = v[lone], p1 = v[(lone + 1) % 3], p2 = v[(lone + 2) % 3];
                        var m1 = Mid(p0, p1, cut);
                        var m2 = Mid(p0, p2, cut);
                        result.AddRange(new[] { p0, m1, m2, m1, p1, p2, m1, p2, m2 });
                    }
                    subs[m] = result;
                }
            }
            var mesh = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            if (hasUv) mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subs.Count;
            for (var m = 0; m < subs.Count; m++) mesh.SetTriangles(subs[m], m);
            return mesh;
        }

        // ------------------------------------------------------------------ on a part

        private static readonly Dictionary<GameObject, Mesh> straightMeshes = new();

        /// <summary>The part's mesh before any bend.</summary>
        public static Mesh Straight(GameObject part) {
            if (straightMeshes.TryGetValue(part, out var mesh) && mesh != null) return mesh;
            return part.GetComponent<MeshFilter>().sharedMesh;
        }

        public static List<Bend> SavedBends(GameObject part) {
            var list = new List<Bend>();
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.state)) return list;
            foreach (var entry in saved.state.Split(';'))
                if (TryParse(entry, out var bend)) list.Add(bend);
            return list;
        }

        public static bool TryParse(string entry, out Bend bend) {
            bend = default;
            if (entry == null || !entry.StartsWith(Prefix)) return false;
            var v = entry.Substring(Prefix.Length).Split(',');
            if (v.Length != 4) return false;
            var f = new float[4];
            for (var i = 0; i < 4; i++)
                if (!float.TryParse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return false;
            bend = new Bend { start = f[0], angle = f[1], toward = f[2], radius = Mathf.Max(0.05f, f[3]) };
            return true;
        }

        public static string Entry(Bend b) => Prefix + string.Join(",", new[] { b.start, b.angle, b.toward, b.radius }.Select(x => x.ToString("0.####", CultureInfo.InvariantCulture)));

        /// <summary>Shows the part with these bends (its saved bends are not changed): the preview while bending.</summary>
        public static void Show(GameObject part, IList<Bend> bends) {
            var filter = part.GetComponent<MeshFilter>();
            if (!straightMeshes.ContainsKey(part) || straightMeshes[part] == null) straightMeshes[part] = filter.sharedMesh;
            var straight = straightMeshes[part];
            var old = filter.sharedMesh;
            var mesh = bends.Count == 0 ? straight : Bent(straight, part.transform.localScale, bends);
            filter.sharedMesh = mesh;
            if (part.TryGetComponent(out MeshCollider collider)) {
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
            }
            if (old != straight && old != mesh && !keep.Contains(old)) Object.Destroy(old);
            PlaceHoles(part, straight, bends);
            PlaceDrilled(part, bends);   // V2: holes drilled across it go where the bends carry them
            if (part.TryGetComponent(out Motion.RubberLinkPart link)) link.Refresh(bends);
        }

        // Meshes an undo step still points at (not freed).
        private static readonly HashSet<Mesh> keep = new();

        /// <summary>Applies the part's saved bends (loading a build, or after a bend is added).</summary>
        public static void ApplySaved(GameObject part) {
            var bends = SavedBends(part);
            if (bends.Count > 0) Show(part, bends);
        }

        /// <summary>Adds a bend to the part: one undo step, saved in its state.</summary>
        public static void Add(GameObject part, Bend bend) {
            var before = new BendElement(part);
            var saved = part.GetComponent<SavedObject>();
            saved.state = (saved.state ?? "") + Entry(bend) + ";";
            Show(part, SavedBends(part));
            if (part.TryGetComponent(out Parts_List.PartName partName) && !partName.name.EndsWith("(bent)")) partName.name += " (bent)";
            StateSystem.AddElement(before);
            StateSystem.AddState(new State(new List<IElement> { new BendElement(part) }));
            SelectionSystem.HoverSelector.Changes++;
        }

        // A standoff's threaded hole: one in each straight end (from the end to the first bend, and past the last).
        private static void PlaceHoles(GameObject part, Mesh straight, IList<Bend> bends) {
            var holes = part.GetComponentsInChildren<HoleCollider>(true).Where(h => h.transform.parent == part.transform && h.GetComponent<DrilledHole>() == null).ToList();
            if (holes.Count == 0) return;
            var state = holeStates.TryGetValue(part, out var known) ? known : null;
            if (state == null) {
                var main = holes[0];
                state = new HoleState { position = main.transform.localPosition, rotation = main.transform.localRotation, scale = main.transform.localScale };
                holeStates[part] = state;
            }
            // Back to one straight hole.
            for (var i = 1; i < holes.Count; i++)
                if (holes[i].name == "Bent End Hole") {
                    holes[i].transform.SetParent(null);
                    Object.Destroy(holes[i].gameObject);
                }
            var first = holes[0];
            first.transform.localPosition = state.position;
            first.transform.localRotation = state.rotation;
            first.transform.localScale = state.scale;
            if (bends.Count == 0) {
                first.Refresh();
                return;
            }
            var (axis, low, length, middle) = Frame(part, straight);
            var scale = part.transform.localScale;
            var sorted = bends.OrderBy(b => b.start).ToList();
            var firstStart = Mathf.Clamp(sorted[0].start, 0.02f, length);
            var lastEnd = sorted.Max(b => b.start + b.radius * Mathf.Abs(b.angle) * Mathf.Deg2Rad);
            var tail = Mathf.Max(0.02f, length - lastEnd);
            // The low end: from the end to the first bend.
            var lowCenter = middle;
            lowCenter[axis] = low + firstStart / 2f;
            SetHole(first.transform, Div(lowCenter, scale), state.rotation, state.scale, firstStart / Mathf.Abs(scale[axis] == 0f ? 1f : scale[axis]), axis, part);
            first.Refresh();
            // The high end, where the bends have carried it.
            var copy = Object.Instantiate(first.gameObject, part.transform);
            copy.name = "Bent End Hole";
            var highCenter = middle;
            highCenter[axis] = low + length - tail / 2f;
            var (p, q) = Carry(highCenter, axis, low, middle, bends);
            copy.transform.localPosition = Div(p, scale);
            copy.transform.localRotation = q * state.rotation;
            var ls = state.scale;
            ls.z = tail / Mathf.Abs(scale[axis] == 0f ? 1f : scale[axis]) * (state.scale.z / Mathf.Max(1e-5f, state.scale.z));
            copy.transform.localScale = new Vector3(state.scale.x, state.scale.y, tail);
            var hc = copy.GetComponent<HoleCollider>();
            if (hc != null) hc.Refresh();
        }

        private static void SetHole(Transform t, Vector3 position, Quaternion rotation, Vector3 scale, float depth, int axis, GameObject part) {
            t.localPosition = position;
            t.localRotation = rotation;
            t.localScale = new Vector3(scale.x, scale.y, depth * Mathf.Abs(part.transform.localScale[axis]));
        }

        /// <summary>The middle line's point at <paramref name="s"/> (inches from the low end) after these bends, in
        /// the part's space with its scale (world = position + rotation * this), and how it's turned there.</summary>
        public static (Vector3 point, Quaternion turn) CenterAt(GameObject part, float s, IList<Bend> bends) {
            var (axis, low, _, middle) = Frame(part, Straight(part));
            var p = middle;
            p[axis] = low + s;
            return Carry(p, axis, low, middle, bends);
        }

        /// <summary>The part's length way in its own space (with its scale), straight.</summary>
        public static Vector3 AlongLocal(GameObject part) {
            var (axis, _, _, _) = Frame(part, Straight(part));
            var v = Vector3.zero;
            v[axis] = 1f;
            return v;
        }

        public static Vector3 ToWorld(GameObject part, Vector3 local) => part.transform.position + part.transform.rotation * local;
        public static Vector3 ToLocal(GameObject part, Vector3 world) => Quaternion.Inverse(part.transform.rotation) * (world - part.transform.position);

        /// <summary>Where a world point lies along the (bent) part: inches from its low end, by the middle line.</summary>
        public static float Along(GameObject part, Vector3 world, IList<Bend> bends) {
            var (_, _, length, _) = Frame(part, Straight(part));
            var local = ToLocal(part, world);
            float best = 0f, bestDistance = float.MaxValue;
            var steps = Mathf.Clamp(Mathf.CeilToInt(length / 0.02f), 20, 2000);
            for (var i = 0; i <= steps; i++) {
                var s = length * i / steps;
                var d = (CenterAt(part, s, bends).point - local).sqrMagnitude;
                if (d < bestDistance) {
                    bestDistance = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>The mesh before any bend is replaced (a hole drilled across the straight part).</summary>
        public static void SetStraight(GameObject part, Mesh mesh) {
            if (part == null) return;
            if (straightMeshes.ContainsKey(part)) straightMeshes[part] = mesh;
        }

        public static bool HasStraight(GameObject part) => part != null && straightMeshes.ContainsKey(part) && straightMeshes[part] != null;

        /// <summary>V2: which side of a bend moves and what goes with it, worked out once (from where things are
        /// now) so the bend tool can show the parts on that side moving while a ring is dragged, and put them back.</summary>
        public class Plan {
            public GameObject part;
            public float cut;
            public bool moveHigh, tied, partMoves;
            public string note = "";
            public List<GameObject> movers = new();   // saved parts that move (not the bent part)
            public Vector3 origin;
            public Quaternion rotation;
            public readonly Dictionary<GameObject, (Vector3 position, Quaternion rotation)> poses = new();
            public readonly List<Collider> paused = new();
            public IEnumerable<GameObject> Moving => partMoves ? movers.Append(part) : movers;
        }

        /// <summary>The side of the bend at <paramref name="cut"/> (inches from the part's end) that moves: the
        /// lighter side (fewer parts), never a grounded one; the rubber link's far stud.</summary>
        public static Plan MakePlan(GameObject part, float cut) {
            var before = SavedBends(part);
            var plan = new Plan { part = part, cut = cut, origin = part.transform.position, rotation = part.transform.rotation };
            // What's on each side of the change, by where it touches the part.
            var lowSeeds = new List<GameObject>();
            var highSeeds = new List<GameObject>();
            var detector = part.TryGetComponent(out ConnectingPart own) ? own.holeDetector : null;
            foreach (var neighbour in GroupManager.Neighbours(part)) {
                if (neighbour == null || neighbour.IsDeleted()) continue;
                var points = new List<Vector3>();
                if (detector != null)
                    foreach (var hole in detector.holes)
                        if (hole != null && hole.holeData != null && hole.holeData.part == neighbour) points.Add(hole.transform.position);
                if (neighbour.TryGetComponent(out ConnectingPart other) && other.holeDetector != null)
                    foreach (var hole in other.holeDetector.holes)
                        if (hole != null && hole.holeData != null && hole.holeData.part == part) points.Add(hole.transform.position);
                // The neighbour's own middle too: a long hole (a standoff's thread runs its whole length) has its
                // middle where a middle bend is, so a coupler in the far end counted as the near side.
                points.Add(neighbour.TryGetComponent(out Renderer shown) ? shown.bounds.center : neighbour.transform.position);
                var s = points.Select(x => Along(part, x, before)).OrderBy(x => Mathf.Abs(x - cut)).Last();
                (s > cut ? highSeeds : lowSeeds).Add(neighbour);
            }
            var stop = new List<GameObject> { part };
            var lowSet = Motion.Dynamics.Fastened(lowSeeds, stop, null, out _);
            var highSet = Motion.Dynamics.Fastened(highSeeds, stop, null, out _);
            var tied = lowSet.Overlaps(highSet);
            bool moveHigh;
            if (IsLink(part)) moveHigh = false;   // the stud it was put in by stays; the far stud bends
            else moveHigh = highSet.Count <= lowSet.Count;
            if (!tied) {
                if (moveHigh && highSet.Any(Motion.Linkage.IsGrounded)) moveHigh = false;
                else if (!moveHigh && lowSet.Any(Motion.Linkage.IsGrounded)) moveHigh = true;
                if ((moveHigh ? highSet : lowSet).Any(Motion.Linkage.IsGrounded)) {
                    tied = true;
                    plan.note = " (both ends are held by grounded parts: what's on it stayed)";
                }
            }
            else plan.note = " (what's on each end is fastened together: it stayed where it was)";
            plan.tied = tied;
            plan.moveHigh = moveHigh;
            plan.movers = tied ? new List<GameObject>() : (moveHigh ? highSet : lowSet).Where(o => o != null && o != part && o.GetComponent<SavedObject>() != null).ToList();
            plan.partMoves = !tied && !moveHigh;
            foreach (var o in plan.Moving) plan.poses[o] = (o.transform.position, o.transform.rotation);
            return plan;
        }

        /// <summary>Puts the moving side where it goes when the part's bends change from <paramref name="before"/> to
        /// <paramref name="after"/> (from the poses the plan was made at).</summary>
        public static void Pose(Plan plan, IList<Bend> before, IList<Bend> after) {
            if (plan == null || plan.tied || plan.part == null) return;
            var part = plan.part;
            var (axis, low, length, middle) = Frame(part, Straight(part));
            var highEnd = middle;
            highEnd[axis] = low + length;
            var (p0, q0) = Carry(highEnd, axis, low, middle, before);
            var (p1, q1) = Carry(highEnd, axis, low, middle, after);
            var turn = q1 * Quaternion.Inverse(q0);
            var rotation = plan.rotation;
            var origin = plan.origin;
            Vector3 MovePoint(Vector3 world) => origin + rotation * (p1 + turn * (Quaternion.Inverse(rotation) * (world - origin) - p0));
            var worldTurn = rotation * turn * Quaternion.Inverse(rotation);
            // The low side moving: everything there (the part too) takes the inverse move, so the high end stays.
            var inverseTurn = Quaternion.Inverse(worldTurn);
            Vector3 InversePoint(Vector3 world) {
                var b = origin + rotation * p0;
                var a = MovePoint(b);
                return b + inverseTurn * (world - a);
            }
            foreach (var pair in plan.poses) {
                var o = pair.Key;
                if (o == null) continue;
                if (plan.moveHigh) o.transform.SetPositionAndRotation(MovePoint(pair.Value.position), worldTurn * pair.Value.rotation);
                else o.transform.SetPositionAndRotation(InversePoint(pair.Value.position), inverseTurn * pair.Value.rotation);
            }
        }

        /// <summary>V2: where the moving end of the part ends up and which way it points (out of the part), when its
        /// bends change from <paramref name="before"/> to <paramref name="after"/>: the far end when that side moves,
        /// the near end when the near side does (aiming a bend at a point uses it).</summary>
        public static (Vector3 point, Vector3 direction) EndRay(Plan plan, IList<Bend> before, IList<Bend> after) {
            var part = plan.part;
            var (axis, low, length, middle) = Frame(part, Straight(part));
            var highEnd = middle;
            highEnd[axis] = low + length;
            var lowEnd = middle;
            lowEnd[axis] = low;
            var alongLocal = Vector3.zero;
            alongLocal[axis] = 1f;
            var (p0, q0) = Carry(highEnd, axis, low, middle, before);
            var (p1, q1) = Carry(highEnd, axis, low, middle, after);
            var rotation = plan.rotation;
            var origin = plan.origin;
            if (!plan.partMoves)
                return (origin + rotation * p1, rotation * (q1 * alongLocal));
            // The near side moves: the far end stays where it is now, the near end swings round it.
            var turn = q1 * Quaternion.Inverse(q0);
            var worldTurn = rotation * turn * Quaternion.Inverse(rotation);
            var inverseTurn = Quaternion.Inverse(worldTurn);
            var b = origin + rotation * p0;
            var a2 = origin + rotation * (p1 + turn * (p0 - p0));
            var lowWorld = origin + rotation * lowEnd;
            return (b + inverseTurn * (lowWorld - a2), inverseTurn * (rotation * -alongLocal));
        }

        /// <summary>While previewing: the hole detectors of the part and of the screws moving with it paused, so
        /// moving them doesn't make or break connections (switching a trigger off sends no exit).</summary>
        public static void Pause(Plan plan) {
            if (plan == null || plan.paused.Count > 0) return;
            foreach (var o in plan.Moving.Append(plan.part).Distinct())
                if (o != null && o.TryGetComponent(out ConnectingPart connecting) && connecting.holeDetector != null)
                    foreach (var collider in connecting.holeDetector.GetComponentsInChildren<Collider>(true))
                        if (collider.enabled) {
                            collider.enabled = false;
                            plan.paused.Add(collider);
                        }
            // The bent part's own holes too: they move with the preview, and a screw left behind would let go.
            if (plan.part != null)
                foreach (var hole in plan.part.GetComponentsInChildren<HoleCollider>(true))
                    if (hole.TryGetComponent(out Collider collider) && collider.enabled) {
                        collider.enabled = false;
                        plan.paused.Add(collider);
                    }
        }

        /// <summary>Everything back where the plan found it.</summary>
        public static void Restore(Plan plan) {
            if (plan == null) return;
            foreach (var pair in plan.poses)
                if (pair.Key != null) pair.Key.transform.SetPositionAndRotation(pair.Value.position, pair.Value.rotation);
            foreach (var collider in plan.paused)
                if (collider != null) collider.enabled = true;
            plan.paused.Clear();
        }

        /// <summary>Changes the part's bends (adds one, or straightens it) and moves what's fastened to the side that
        /// moves (<see cref="MakePlan"/>). One undo step. Returns a note for the message ("" or what was left behind).</summary>
        public static string ChangeBends(GameObject part, List<Bend> after, float cut) {
            var before = SavedBends(part);
            var plan = MakePlan(part, cut);
            var movers = plan.Moving.ToList();
            var elementsBefore = movers.Select(o => (IElement)new ObjectElement(o)).ToList();
            elementsBefore.Add(new BendElement(part));

            var saved = part.GetComponent<SavedObject>();
            var kept = (saved.state ?? "").Split(';').Where(e => e.Length > 0 && !e.StartsWith(Prefix)).ToList();
            kept.AddRange(after.Select(Entry));
            saved.state = kept.Count > 0 ? string.Join(";", kept) + ";" : "";
            Pose(plan, before, after);
            Show(part, after);
            if (part.TryGetComponent(out Parts_List.PartName partName)) {
                if (after.Count > 0 && !partName.name.EndsWith("(bent)")) partName.name += " (bent)";
                if (after.Count == 0 && partName.name.EndsWith(" (bent)")) partName.name = partName.name.Substring(0, partName.name.Length - 7);
            }
            StateSystem.AddElements(elementsBefore);
            var elementsAfter = movers.Select(o => (IElement)new ObjectElement(o)).ToList();
            elementsAfter.Add(new BendElement(part));
            StateSystem.AddState(new State(elementsAfter));
            SelectionSystem.HoverSelector.Changes++;
            var note = plan.note;
            var moved = plan.movers.Count;
            if (moved > 0) note = " (" + moved + (moved == 1 ? " part" : " parts") + " on its " + (plan.moveHigh ? "far" : "other") + " end moved with it)" + note;
            return note;
        }

        // Where a point of the straight part (inches, part space) goes, and how it's turned, after the bends.
        private static (Vector3, Quaternion) Carry(Vector3 point, int axis, float low, Vector3 middle, IList<Bend> bends) {
            var p = point;
            var turnAll = Quaternion.identity;
            var s = point[axis];
            foreach (var bend in bends.OrderByDescending(x => x.start)) {
                var (along, toward, about) = Directions(axis, bend.toward);
                var angle = bend.angle * Mathf.Deg2Rad;
                var arc = bend.radius * Mathf.Abs(angle);
                var sign = Mathf.Sign(angle);
                var start = low + bend.start;
                if (s <= start) continue;
                var o = middle;
                o[axis] = start;
                var center = o + toward * (bend.radius * sign);
                float turn;
                if (s <= start + arc) {
                    turn = (s - start) / bend.radius * sign;
                    p -= along * (s - start);
                }
                else {
                    turn = angle;
                    p -= along * arc;
                }
                var q = Quaternion.AngleAxis(turn * Mathf.Rad2Deg, about);
                p = center + q * (p - center);
                turnAll = q * turnAll;
            }
            return (p, turnAll);
        }

        // Holes drilled across the part (PartDrilling.CrossDrill) are kept in the straight part's frame.
        private static void PlaceDrilled(GameObject part, IList<Bend> bends) {
            var (axis, low, _, middle) = Frame(part, Straight(part));
            var scale = part.transform.localScale;
            foreach (var hole in part.GetComponentsInChildren<DrilledHole>(true)) {
                var (p, q) = Carry(Vector3.Scale(hole.straightPosition, scale), axis, low, middle, bends);
                hole.transform.localPosition = Div(p, scale);
                hole.transform.localRotation = q * hole.straightRotation;
                if (hole.TryGetComponent(out HoleCollider collider)) collider.Refresh();
            }
        }

        private class HoleState {
            public Vector3 position, scale;
            public Quaternion rotation;
        }

        private static readonly Dictionary<GameObject, HoleState> holeStates = new();

        // Undo/redo: the part's bends (its state) and how it looks.
        public class BendElement : IElement {
            private readonly GameObject part;
            private readonly string state, name;

            public BendElement(GameObject part) {
                this.part = part;
                state = part.GetComponent<SavedObject>().state;
                if (part.TryGetComponent(out Parts_List.PartName partName)) name = partName.name;
            }

            public void Load() {
                if (part == null) return;
                part.GetComponent<SavedObject>().state = state;
                if (name != null && part.TryGetComponent(out Parts_List.PartName partName)) partName.name = name;
                Show(part, SavedBends(part));
            }
        }
    }
}
