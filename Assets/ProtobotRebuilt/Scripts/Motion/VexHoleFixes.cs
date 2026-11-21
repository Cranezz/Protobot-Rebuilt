using System.Collections.Generic;
using System.Linq;
using Protobot.Custom;
using UnityEngine;

namespace Protobot.Motion {
    // V2: holes the hole scanner gets wrong on some VEX-CAD parts, fixed when a part is made (worked out once per
    // part and kept):
    // - Long slots (slotted angle's flange, chassis rails' top): the scanner only saw short pieces of them (or none).
    //   Along every row of holes on a structure part, rays through the part find each opening; one longer than
    //   0.45" becomes one slot hole (as wide as the opening), and the pieces inside it go.
    // - The GPS sensor's camera lens isn't a hole; a standoff retainer's standoff pocket isn't a hole (as on the
    //   hex retainer); a click-on spacer gets its middle hole.
    public static class VexHoleFixes {
        private class Fix {
            public readonly List<int> remove = new();
            public readonly List<CustomHole> add = new();
        }

        private static readonly Dictionary<string, Fix> cache = new();

        public static void Apply(GameObject part, string id) {
            var holes = part.GetComponentsInChildren<HoleCollider>(true).Where(h => h.transform.parent == part.transform).ToList();
            if (!cache.TryGetValue(id, out var fix)) {
                fix = Plan(part, id, holes);
                cache[id] = fix;
            }
            foreach (var index in fix.remove.OrderByDescending(i => i)) {
                if (index >= holes.Count || holes[index] == null) continue;
                holes[index].transform.SetParent(null);
                Object.DestroyImmediate(holes[index].gameObject);
            }
            if (fix.add.Count > 0) CustomParts.AddHoles(part, fix.add);
        }

        private static Fix Plan(GameObject part, string id, List<HoleCollider> holes) {
            var fix = new Fix();
            var name = id.IndexOf('-') > 0 ? id.Substring(id.IndexOf('-') + 1) : id;
            var filter = part.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return fix;
            float Size(HoleCollider h) => Mathf.Max(h.transform.localScale.x, h.transform.localScale.y);

            if (name.Contains("GPS Sensor"))
                for (var i = 0; i < holes.Count; i++)
                    if (Size(holes[i]) > 0.2f && holes[i].holeType == HoleCollider.HoleType.Normal) fix.remove.Add(i);
            if (name.Contains("Standoff Retainer"))
                for (var i = 0; i < holes.Count; i++)
                    if (Size(holes[i]) > 0.25f) fix.remove.Add(i);
            if (name.Contains("Click On Spacer") && holes.Count == 0) {
                var size = mesh.bounds.size;
                var thin = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
                fix.add.Add(new CustomHole { center = mesh.bounds.center, axis = thin, size = 0.17f, depth = Vector3.Dot(size, thin) });
            }
            var type = PartsManager.GetPartType(id);
            if (name.Contains("Bogie Wheel") && type != null) BogieMounts(type, name, fix);
            if (Through(id, name)) ThroughHoles(mesh, holes, fix, !id.StartsWith("VX"));
            if (name == "Potentiometer") {   // V2: its shaft socket (blind, so not an opening) stays with its slots
                var shaft = holes.Select((h, i) => (h, i)).Where(x => Size(x.h) > 0.2f).OrderBy(x => (x.h.transform.localPosition - mesh.bounds.center).sqrMagnitude).FirstOrDefault();
                if (shaft.h != null) fix.remove.Remove(shaft.i);
            }
            // V2: the Vision Sensor mounts by two #8-32 threaded inserts in its back (VEX KB); its other "holes" were
            // the case's screws, bosses and the lens.
            if (name == "Vision Sensor")
                for (var i = 0; i < holes.Count; i++)
                    if (!(holes[i].holeType == HoleCollider.HoleType.Threaded && Size(holes[i]) > 0.158f && Size(holes[i]) < 0.172f) && !fix.remove.Contains(i)) fix.remove.Add(i);
            // V2: the AI Vision Sensor (276-8659) too: two #8-32 inserts 1/2" apart in its back (modelled at the thread's
            // minor diameter, 0.15"); the lens, the smart port and the USB port aren't holes.
            if (name == "AI Vision Sensor")
                for (var i = 0; i < holes.Count; i++)
                    if (!(holes[i].holeType == HoleCollider.HoleType.Threaded && Size(holes[i]) > 0.14f && Size(holes[i]) < 0.172f) && !fix.remove.Contains(i)) fix.remove.Add(i);
            // Pneumatic ports: the cylinders' M5 ports (side, near each end), the reservoirs' 1/8" NPT ports; the
            // valves' small ports become threaded (their fittings thread in).
            if (id.StartsWith("PNMT-")) Ports(mesh, 0.15f, 0.25f, fix, false, 0.197f);   // V2: M5, as the fittings' studs
            if (name.Contains("Pneumatic Air Reservoir") || id.StartsWith("TANK-")) {
                // In the side of each end cap; their mouths measure ~0.47" with the chamfer. V2: the legacy tank's
                // thread is 1/8" NPT; V5's Air Tank takes M5 (VEX: its valve stem and gauge are M5).
                var before = fix.add.Count;
                var v5 = name.Contains("V5") || id == "TANK-V5";
                Ports(mesh, id.StartsWith("TANK-") ? 0.15f : 0.3f, 0.6f, fix, false, v5 ? 0.197f : 0.40f);
                for (var i = 0; i < holes.Count; i++)
                    for (var k = before; k < fix.add.Count; k++)
                        if ((holes[i].transform.localPosition - fix.add[k].center).magnitude < 0.35f && !fix.remove.Contains(i)) fix.remove.Add(i);
            }
            if (id.StartsWith("VXPNEUMATICS"))
                for (var i = 0; i < holes.Count; i++) {
                    var s = Size(holes[i]);
                    // V2: V5's double acting solenoid has M5 ports (V5's straight and elbow fittings thread in).
                    if (name.StartsWith("Solenoid") && name.Contains("Double") && s > 0.095f && s < 0.2f && !fix.remove.Contains(i)) {
                        fix.remove.Add(i);
                        var port = holes[i].transform;
                        fix.add.Add(new CustomHole { center = port.localPosition, axis = port.localRotation * Vector3.forward, size = 0.197f, depth = port.localScale.z, threaded = true });
                        continue;
                    }
                    if (s > 0.095f && s < 0.135f && holes[i].holeType != HoleCollider.HoleType.Threaded && !fix.remove.Contains(i) && name != "Cylinder Mount") {
                        fix.remove.Add(i);
                        var t = holes[i].transform;
                        fix.add.Add(new CustomHole { center = t.localPosition, axis = t.localRotation * Vector3.forward, size = 0.118f, depth = t.localScale.z, threaded = true });
                    }
                }
            if (name == "Cylinder Mount")   // its base hole takes the rod's 8-32 thread
                for (var i = 0; i < holes.Count; i++)
                    if (Size(holes[i]) < 0.15f && !fix.remove.Contains(i)) {
                        fix.remove.Add(i);
                        var t = holes[i].transform;
                        fix.add.Add(new CustomHole { center = t.localPosition, axis = t.localRotation * Vector3.forward, size = 0.17f, depth = t.localScale.z });
                    }
            if (ChildPartGenerator.IsStud(id, name) || ChildPartGenerator.IsLegacyFitting(name) || name == ChildPartGenerator.PlugName)   // a thread outside, no hole
                for (var i = 0; i < holes.Count; i++) fix.remove.Add(i);
            if (id.StartsWith("VXPNEUMATICS")) Sockets(part, mesh, holes, name, fix);   // V2: V5's push-in tube sockets
            if (name == "Line Tracker V2") {   // the case's screws beside its mounting hole aren't holes
                var mount = holes.FirstOrDefault(h => h.holeType == HoleCollider.HoleType.Normal && Size(h) > 0.16f);
                if (mount != null)
                    for (var i = 0; i < holes.Count; i++)
                        if (holes[i] != mount && (holes[i].transform.localPosition - mount.transform.localPosition).magnitude < 0.2f && !fix.remove.Contains(i)) fix.remove.Add(i);
            }
            if (type != null && type.group == PartType.PartGroup.Structure && holes.Count > 0 && !Through(id, name)) Slots(part, mesh, holes, fix);
            return fix;
        }

        // ------------------------------------------------------------------ V5 push-in fittings' tube sockets

        // Each red release collar of a V5 fitting (straight, elbow, tee, flow valve, shut off valve) is a 4mm tube
        // socket: a threaded-type hole of the tube's size (0.157") along the collar's axis, so the 4mm plug pushes in
        // (and fastens, as a screw does). The model's own holes there go.
        private static readonly string[] SocketParts = { "Straight Pneumatic Fitting", "Elbow Fitting", "Air Flow Valve Fitting", "Tee Fittings", "Pneumatic Hand Valve" };

        private static void Sockets(GameObject part, Mesh mesh, List<HoleCollider> holes, string name, Fix fix) {
            if (!SocketParts.Contains(name) || !part.TryGetComponent(out MeshRenderer renderer)) return;
            var materials = renderer.sharedMaterials;
            var vertices = mesh.vertices;
            for (var s = 0; s < mesh.subMeshCount && s < materials.Length; s++) {
                if (materials[s] == null || !materials[s].name.Contains("Red")) continue;
                foreach (var component in Components(mesh.GetTriangles(s), vertices)) {
                    var points = component.Select(i => vertices[i]).ToList();
                    if (points.Count < 8) continue;
                    var center = points.Aggregate(Vector3.zero, (a, b) => a + b) / points.Count;
                    var axis = SmallestSpread(points, center);
                    if (Vector3.Dot(center - mesh.bounds.center, axis) < 0f) axis = -axis;
                    var depth = points.Max(p => Vector3.Dot(p - center, axis)) - points.Min(p => Vector3.Dot(p - center, axis));
                    for (var i = 0; i < holes.Count; i++)
                        if (!fix.remove.Contains(i) && ((holes[i].transform.localPosition - center).magnitude < 0.2f || holes[i].transform.localScale.z > 1f))
                            fix.remove.Add(i);
                    fix.add.Add(new CustomHole { center = center, axis = axis, size = 0.157f, depth = Mathf.Max(0.1f, depth), threaded = true });
                }
            }
        }

        // Groups of a submesh's triangles that touch (vertices at the same place count as one).
        private static List<List<int>> Components(int[] triangles, Vector3[] vertices) {
            var parent = new Dictionary<int, int>();
            var byPlace = new Dictionary<Vector3Int, int>();
            int Find(int a) {
                while (parent[a] != a) a = parent[a] = parent[parent[a]];
                return a;
            }
            int Key(int v) {
                var p = Vector3Int.RoundToInt(vertices[v] * 2000f);
                if (!byPlace.TryGetValue(p, out var k)) {
                    byPlace[p] = k = v;
                    parent[k] = k;
                }
                return k;
            }
            for (var t = 0; t + 2 < triangles.Length; t += 3) {
                int a = Find(Key(triangles[t])), b = Find(Key(triangles[t + 1])), c = Find(Key(triangles[t + 2]));
                parent[b] = a;
                parent[Find(c)] = a;
            }
            return parent.Keys.ToList().GroupBy(Find).Select(g => g.ToList()).ToList();
        }

        // The direction the points spread least along (a collar's axis: it's shorter than it is wide).
        private static Vector3 SmallestSpread(List<Vector3> points, Vector3 center) {
            Vector3 best = Vector3.forward;
            var least = float.MaxValue;
            for (var i = 0; i < 400; i++) {
                var theta = Mathf.Acos(1f - (i + 0.5f) / 400f);
                var phi = i * 2.39996f;
                var d = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta));
                var spread = 0f;
                foreach (var p in points) spread += Mathf.Pow(Vector3.Dot(p - center, d), 2f);
                if (spread < least) {
                    least = spread;
                    best = d;
                }
            }
            // Snap to the model's axes when it's within a few degrees.
            foreach (var a in new[] { Vector3.right, Vector3.up, Vector3.forward })
                if (Mathf.Abs(Vector3.Dot(best, a)) > 0.99f) return a * Mathf.Sign(Vector3.Dot(best, a));
            return best;
        }

        // ------------------------------------------------------------------ holes measured from the model's openings
        // Some parts' holes were wrong in both directions: the scanner saw case screws and pockets as holes, and
        // missed slots (sensor bars, switches, gussets: a slot came out as a row of round holes, or not at all; the
        // pivot gusset's curved slot as three round holes). For these parts every hole is made from the model's own
        // through-openings: rays through the part on a 0.01" grid find each opening, its edges are found to 0.001",
        // and it becomes a round, square or slot hole, or a hole of its exact outline, as deep as the wall round it.
        private static readonly string[] ThroughNames = {
            "Line Sensor Mounting Bar", "Ultrasonic Range Finder", "Optical Shaft Encoder", "V5 Bumper Switch", "Limit Switch",
            "Angle Gusset", "Pivot Gusset",
            // V2 (2026-11-14): every sensor that mounts with screws through it: the Distance and Optical Sensors' tab
            // slots, the Inertial and Rotation Sensors' through holes, the 3-wire sensors' and potentiometers'
            // mounting holes and slots (the scanner had case screws as holes and dropped every slot on them).
            "Distance Sensor", "Optical Sensor", "Inertial Sensor", "Rotation Sensor", "Light Sensor", "Line Tracker",
            "Line Tracker V2", "Analog Accelerometer", "Yaw Rate Gyroscope Sensor", "Potentiometer", "Potentiometer V2",
            "V5 3 Wire Expander",
        };
        private static readonly string[] ThroughIds = { "SWCH-Bumper-V5", "SWCH-Bumper-Cortex", "SWCH-Limit-Cortex", "GSET-Pack-Pivot", "GSET-Pack-Angle" };

        /// <summary>Original (non-VEX-CAD) parts this class fixes too.</summary>
        public static bool Handles(string id) => ThroughIds.Contains(id) || id.StartsWith("PNMT-") || id.StartsWith("TANK-");

        private static bool Through(string id, string name) => ThroughIds.Contains(id) || (id.StartsWith("VX") && ThroughNames.Contains(name));

        private class Opening {
            public int axis;
            public Vector3 centre;      // part space, on the wall's middle
            public Vector3 normal, up;  // the hole's axis and its long side
            public float size, size2, depth;
            public bool square;
            public Vector2[] outline;
            public List<Vector2> cells = new();   // grid cells (u, v) in inches, for "is this hole on it"
        }

        private static void ThroughHoles(Mesh mesh, List<HoleCollider> holes, Fix fix, bool keepThreaded) {
            var probe = new GameObject("Opening Probe") { hideFlags = HideFlags.HideAndDontSave };
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            try {
                var openings = Openings(mesh, collider);
                var threaded = keepThreaded && holes.Any(h => h.holeType == HoleCollider.HoleType.Threaded);
                // Every old hole goes; the openings come back as holes.
                for (var i = 0; i < holes.Count; i++) fix.remove.Add(i);
                foreach (var o in openings)
                    fix.add.Add(new CustomHole {
                        center = o.centre, axis = o.normal, up = o.up, size = o.size, size2 = o.size2, depth = o.depth,
                        square = o.square, outline = o.outline, threaded = threaded,
                    });
            }
            finally {
                Object.DestroyImmediate(probe);
            }
        }

        private static List<Opening> Openings(Mesh mesh, MeshCollider collider) {
            var result = new List<Opening>();
            var b = mesh.bounds;
            const float Step = 0.01f;
            for (var ax = 0; ax < 3; ax++) {
                int u = (ax + 1) % 3, v = (ax + 2) % 3;
                int nu = Mathf.CeilToInt(b.size[u] / Step), nv = Mathf.CeilToInt(b.size[v] / Step);
                if (nu < 3 || nv < 3) continue;
                var dir = Vector3.zero;
                dir[ax] = 1f;
                var length = b.size[ax] + 2f;
                Vector3 Point(float pu, float pv) {
                    var p = Vector3.zero;
                    p[u] = pu;
                    p[v] = pv;
                    p[ax] = b.min[ax] - 1f;
                    return p;
                }
                bool Open(float pu, float pv) {
                    var p = Point(pu, pv);
                    return !collider.Raycast(new Ray(p, dir), out _, length) && !collider.Raycast(new Ray(p + dir * length, -dir), out _, length);
                }
                var open = new bool[nu, nv];
                for (var i = 0; i < nu; i++)
                    for (var j = 0; j < nv; j++)
                        open[i, j] = Open(b.min[u] + (i + 0.5f) * Step, b.min[v] + (j + 0.5f) * Step);
                var seen = new bool[nu, nv];
                var queue = new Queue<Vector2Int>();
                for (var i = 0; i < nu; i++)
                    for (var j = 0; j < nv; j++) {
                        if (!open[i, j] || seen[i, j]) continue;
                        var cells = new List<Vector2Int>();
                        var border = false;
                        seen[i, j] = true;
                        queue.Enqueue(new Vector2Int(i, j));
                        while (queue.Count > 0) {
                            var c = queue.Dequeue();
                            cells.Add(c);
                            if (c.x == 0 || c.y == 0 || c.x == nu - 1 || c.y == nv - 1) border = true;
                            for (var k = 0; k < 4; k++) {
                                var n = c + (k == 0 ? Vector2Int.right : k == 1 ? Vector2Int.left : k == 2 ? Vector2Int.up : Vector2Int.down);
                                if (n.x < 0 || n.y < 0 || n.x >= nu || n.y >= nv || !open[n.x, n.y] || seen[n.x, n.y]) continue;
                                seen[n.x, n.y] = true;
                                queue.Enqueue(n);
                            }
                        }
                        if (border) continue;   // round the part's outside, not a hole
                        int minI = cells.Min(c => c.x), maxI = cells.Max(c => c.x), minJ = cells.Min(c => c.y), maxJ = cells.Max(c => c.y);
                        float w = (maxI - minI + 1) * Step, h = (maxJ - minJ + 1) * Step;
                        if (Mathf.Min(w, h) < 0.1f || Mathf.Max(w, h) > 1.8f) continue;   // vents, gaps; or the part's own shape
                        var fill = cells.Count * Step * Step / (w * h);
                        // Its middle, then its exact edges: from the middle out along u and v to the material.
                        var mu = b.min[u] + ((minI + maxI) / 2f + 0.5f) * Step;
                        var mv = b.min[v] + ((minJ + maxJ) / 2f + 0.5f) * Step;
                        float Reach(float du, float dv, float most) {
                            // Out in 0.005" steps to the first material, then halving to 0.0003".
                            float low = 0f, high = -1f;
                            for (var t = 0.005f; t <= most + 0.05f; t += 0.005f) {
                                if (!Open(mu + du * t, mv + dv * t)) {
                                    high = t;
                                    break;
                                }
                                low = t;
                            }
                            if (high < 0f) return most;
                            for (var it = 0; it < 5; it++) {
                                var mid = (low + high) / 2f;
                                if (Open(mu + du * mid, mv + dv * mid)) low = mid;
                                else high = mid;
                            }
                            return (low + high) / 2f;
                        }
                        var o = new Opening { axis = ax };
                        foreach (var c in cells) o.cells.Add(new Vector2(b.min[u] + (c.x + 0.5f) * Step, b.min[v] + (c.y + 0.5f) * Step));
                        var plain = fill > 0.72f && Open(mu, mv);
                        if (plain) {
                            float pu = Reach(1f, 0f, w), nu2 = Reach(-1f, 0f, w), pv = Reach(0f, 1f, h), nv2 = Reach(0f, -1f, h);
                            mu += (pu - nu2) / 2f;
                            mv += (pv - nv2) / 2f;
                            w = pu + nu2;
                            h = pv + nv2;
                        }
                        var uDir = Vector3.zero;
                        uDir[u] = 1f;
                        var vDir = Vector3.zero;
                        vDir[v] = 1f;
                        var longU = w > h;
                        o.normal = dir;
                        o.up = longU ? uDir : vDir;   // the hole's "up" along its long side
                        var shortSide = Mathf.Min(w, h);
                        var longSide = Mathf.Max(w, h);
                        var aspect = longSide / shortSide;
                        o.size = shortSide;
                        o.size2 = longSide;
                        if (plain && aspect < 1.15f) {
                            o.square = fill > 0.9f;   // a round hole fills pi/4 of its box
                            if (!o.square) o.size = o.size2 = (w + h) / 2f;
                        }
                        else if (plain && fill > 0.95f) o.square = true;   // a straight slot
                        else if (plain && fill > 0.8f) {
                            // A slot with round ends: its exact outline (a stadium), in the hole's (right, up).
                            o.outline = Stadium(shortSide, longSide);
                        }
                        else {
                            // Any other shape (the pivot gusset's curved slot): traced round its cells.
                            o.outline = Trace(cells, nu, nv, open, Step, b.min[u], b.min[v], mu, mv, longU);
                            if (o.outline == null) continue;
                        }
                        // As deep as the wall round it: the thinnest material just outside its edge.
                        float bestSpan = float.MaxValue, bestMiddle = b.center[ax];
                        for (var k = 0; k < 16; k++) {
                            var a = k * Mathf.PI / 8f;
                            var du = Mathf.Cos(a);
                            var dv = Mathf.Sin(a);
                            var r = Reach(du, dv, Mathf.Max(w, h)) + 0.03f;
                            var p = Point(mu + du * r, mv + dv * r);
                            if (!collider.Raycast(new Ray(p, dir), out var enter, length)) continue;
                            var low = enter.point[ax];
                            // The near wall's far side: from just inside it, its back face.
                            var backfaces = Physics.queriesHitBackfaces;
                            Physics.queriesHitBackfaces = true;
                            var through = collider.Raycast(new Ray(enter.point + dir * 0.0005f, dir), out var exit, length);
                            Physics.queriesHitBackfaces = backfaces;
                            if (!through) continue;
                            var span = exit.point[ax] - low;
                            // A plain hole (a bore) goes through all of the part round it; a slot through its wall.
                            if (o.outline == null && aspect < 1.15f && collider.Raycast(new Ray(p + dir * length, -dir), out var top, length))
                                span = top.point[ax] - low;
                            if (span < bestSpan) {
                                bestSpan = span;
                                bestMiddle = low + span / 2f;
                            }
                        }
                        if (bestSpan == float.MaxValue) bestSpan = b.size[ax];
                        var centre = Vector3.zero;
                        centre[u] = mu;
                        centre[v] = mv;
                        centre[ax] = bestSpan == b.size[ax] ? b.center[ax] : bestMiddle;
                        o.centre = centre;
                        o.depth = Mathf.Max(bestSpan, 0.02f);
                        result.Add(o);
                    }
            }
            return result;
        }

        // A slot with round ends in (right, up), up along its length.
        private static Vector2[] Stadium(float width, float length) {
            var r = width / 2f;
            var half = Mathf.Max(0f, length / 2f - r);
            var points = new List<Vector2>();
            for (var i = 0; i <= 12; i++) {
                var a = i * Mathf.PI / 12f;
                points.Add(new Vector2(Mathf.Cos(a) * r, half + Mathf.Sin(a) * r));
            }
            for (var i = 0; i <= 12; i++) {
                var a = Mathf.PI + i * Mathf.PI / 12f;
                points.Add(new Vector2(Mathf.Cos(a) * r, -half + Mathf.Sin(a) * r));
            }
            return points.ToArray();
        }

        // The outline of a group of grid cells (Moore-neighbour tracing round its edge), simplified to 0.004", in the
        // hole's (right, up) round (mu, mv): up along u when the shape is longer that way, else along v.
        private static Vector2[] Trace(List<Vector2Int> cells, int nu, int nv, bool[,] open, float step, float minU, float minV, float mu, float mv, bool upIsU) {
            var set = new HashSet<Vector2Int>(cells);
            var start = cells.OrderBy(c => c.y).ThenBy(c => c.x).First();
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1), new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1) };
            var contour = new List<Vector2Int> { start };
            var current = start;
            var back = 4;   // came from the left
            for (var guard = 0; guard < 20000; guard++) {
                var found = false;
                for (var k = 1; k <= 8; k++) {
                    var d = (back + k) % 8;
                    var next = current + dirs[d];
                    if (!set.Contains(next)) continue;
                    back = (d + 4) % 8;
                    current = next;
                    found = true;
                    break;
                }
                if (!found || current == start) break;
                contour.Add(current);
            }
            if (contour.Count < 8) return null;
            var points = contour.Select(c => new Vector2(minU + (c.x + 0.5f) * step - mu, minV + (c.y + 0.5f) * step - mv)).ToList();
            // The cells' centres sit half a cell inside the real edge: out by half a cell from the shape's middle line
            // is good enough for a screw to slide in it.
            var simple = Simplify(points, 0.004f);
            // To (right, up): LookRotation(normal, up) makes right = up x normal.
            return simple.Select(p => upIsU ? new Vector2(-p.y, p.x) : new Vector2(p.x, p.y)).ToArray();
        }

        private static List<Vector2> Simplify(List<Vector2> points, float tolerance) {
            if (points.Count < 4) return points;
            var keep = new bool[points.Count];
            keep[0] = keep[points.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, points.Count - 1));
            while (stack.Count > 0) {
                var (a, z) = stack.Pop();
                var most = 0f;
                var index = -1;
                for (var i = a + 1; i < z; i++) {
                    var d = Interface.Overlay.DistanceToSegment(points[i], points[a], points[z]);
                    if (d > most) {
                        most = d;
                        index = i;
                    }
                }
                if (index >= 0 && most > tolerance) {
                    keep[index] = true;
                    stack.Push((a, index));
                    stack.Push((index, z));
                }
            }
            return points.Where((p, i) => keep[i]).ToList();
        }

        // Blind round holes in the model (ports): from each side, a depth map on a 0.01" grid; a cavity is where the
        // surface drops more than 0.08" below the ring 0.7 of its size round it; a round one the right size becomes a
        // threaded hole as deep as the cavity. alongLength: ports in the ends (reservoirs), else in the sides.
        private static void Ports(Mesh mesh, float minDiameter, float maxDiameter, Fix fix, bool alongLength, float threadSize = 0f) {
            var probe = new GameObject("Port Probe") { hideFlags = HideFlags.HideAndDontSave };
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            try {
                var b = mesh.bounds;
                var longAxis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
                const float Step = 0.01f;
                var ring = maxDiameter / 2f + 0.04f;
                var R = Mathf.CeilToInt(ring / Step);
                for (var ax = 0; ax < 3; ax++) {
                    if ((ax == longAxis) != alongLength) continue;
                    for (var sign = -1; sign <= 1; sign += 2) {
                        int u = (ax + 1) % 3, v = (ax + 2) % 3;
                        int nu = Mathf.CeilToInt(b.size[u] / Step), nv = Mathf.CeilToInt(b.size[v] / Step);
                        var dir = Vector3.zero;
                        dir[ax] = -sign;
                        var depth = new float[nu, nv];
                        Vector3 Origin(int i, int j) {
                            var p = Vector3.zero;
                            p[u] = b.min[u] + (i + 0.5f) * Step;
                            p[v] = b.min[v] + (j + 0.5f) * Step;
                            p[ax] = sign > 0 ? b.max[ax] + 0.5f : b.min[ax] - 0.5f;
                            return p;
                        }
                        for (var i = 0; i < nu; i++)
                            for (var j = 0; j < nv; j++)
                                depth[i, j] = collider.Raycast(new Ray(Origin(i, j), dir), out var hit, b.size[ax] + 1f) ? hit.distance : 99f;
                        var cavity = new bool[nu, nv];
                        var surface = new float[nu, nv];
                        for (var i = R; i < nu - R; i++)
                            for (var j = R; j < nv - R; j++) {
                                var d = depth[i, j];
                                if (d > 90f) continue;
                                var most = 99f;
                                var onPart = 0;
                                for (var k = 0; k < 24; k++) {
                                    var a = k * Mathf.PI / 12f;
                                    var x = i + Mathf.RoundToInt(Mathf.Cos(a) * R);
                                    var y = j + Mathf.RoundToInt(Mathf.Sin(a) * R);
                                    if (depth[x, y] > 90f) continue;
                                    onPart++;
                                    most = Mathf.Min(most, depth[x, y]);
                                }
                                if (onPart >= 18 && d - most > 0.08f) {
                                    cavity[i, j] = true;
                                    surface[i, j] = most;
                                }
                            }
                        var seen = new bool[nu, nv];
                        for (var i = 0; i < nu; i++)
                            for (var j = 0; j < nv; j++) {
                                if (!cavity[i, j] || seen[i, j]) continue;
                                var cells = new List<Vector2Int>();
                                var queue = new Queue<Vector2Int>();
                                queue.Enqueue(new Vector2Int(i, j));
                                seen[i, j] = true;
                                while (queue.Count > 0) {
                                    var c = queue.Dequeue();
                                    cells.Add(c);
                                    for (var k = 0; k < 4; k++) {
                                        var n = c + (k == 0 ? Vector2Int.right : k == 1 ? Vector2Int.left : k == 2 ? Vector2Int.up : Vector2Int.down);
                                        if (n.x < 0 || n.y < 0 || n.x >= nu || n.y >= nv || !cavity[n.x, n.y] || seen[n.x, n.y]) continue;
                                        seen[n.x, n.y] = true;
                                        queue.Enqueue(n);
                                    }
                                }
                                // Out to its rim: every cell still well below the surface round it.
                                var rim = cells.Min(c => surface[c.x, c.y]);
                                var mouth = new HashSet<Vector2Int>(cells);
                                foreach (var c in cells) queue.Enqueue(c);
                                while (queue.Count > 0) {
                                    var c = queue.Dequeue();
                                    for (var k = 0; k < 4; k++) {
                                        var n = c + (k == 0 ? Vector2Int.right : k == 1 ? Vector2Int.left : k == 2 ? Vector2Int.up : Vector2Int.down);
                                        if (n.x < 0 || n.y < 0 || n.x >= nu || n.y >= nv || mouth.Contains(n) || depth[n.x, n.y] > 90f || depth[n.x, n.y] < rim + 0.04f) continue;
                                        mouth.Add(n);
                                        seen[n.x, n.y] = true;
                                        if (mouth.Count < 4000) queue.Enqueue(n);
                                    }
                                }
                                cells = mouth.ToList();
                                int minI = cells.Min(c => c.x), maxI = cells.Max(c => c.x), minJ = cells.Min(c => c.y), maxJ = cells.Max(c => c.y);
                                float w = (maxI - minI + 1) * Step, h = (maxJ - minJ + 1) * Step;
                                var diameter = (w + h) / 2f;   // its mouth (the cells' area misses its chamfered rim)
                                if (Mathf.Max(w, h) / Mathf.Min(w, h) > 1.3f || diameter < minDiameter || diameter > maxDiameter) continue;
                                if (cells.Count * Step * Step / (w * h) < 0.6f) continue;   // a ring (round a rod), not a hole
                                float cu = 0f, cv = 0f, top = 99f, bottom = 0f;
                                foreach (var c in cells) {
                                    cu += c.x;
                                    cv += c.y;
                                    if (cavity[c.x, c.y]) top = Mathf.Min(top, surface[c.x, c.y]);
                                    bottom = Mathf.Max(bottom, depth[c.x, c.y]);
                                }
                                cu /= cells.Count;
                                cv /= cells.Count;
                                var deep = Mathf.Min(bottom - top, 0.6f);
                                var at = Origin(0, 0);
                                at[u] = b.min[u] + (cu + 0.5f) * Step;
                                at[v] = b.min[v] + (cv + 0.5f) * Step;
                                at += dir * top;
                                fix.add.Add(new CustomHole { center = at + dir * (deep / 2f), axis = -dir, size = threadSize > 0f ? threadSize : Mathf.Round(diameter * 100f) / 100f, depth = deep, threaded = true });
                            }
                    }
                }
            }
            finally {
                Object.DestroyImmediate(probe);
            }
        }

        // A bogie wheel's bracket mounts with 8-32 screws from the top, into nuts slid into pockets in its side. The
        // scanner saw the screw holes as 0.17" x 0.32" boxes and the pockets as nut-sized boxes (both removed as odd
        // holes): each becomes a round hole, the pocket's part threaded (the captured nut).
        private static void BogieMounts(PartType type, string name, Fix fix) {
            var template = type.GetComponentsInChildren<PartData>(true).FirstOrDefault(p => p.gameObject.name == name);
            if (template == null) return;
            foreach (var hole in template.GetComponentsInChildren<HoleCollider>(true)) {
                if (hole.transform.parent != template.transform) continue;
                var t = hole.transform;
                var narrow = Mathf.Min(t.localScale.x, t.localScale.y);
                var depth = t.localScale.z;
                var screw = narrow > 0.15f && narrow < 0.2f && depth > 0.65f && depth < 0.75f;     // through the top
                var pocket = narrow > 0.32f && narrow < 0.37f && depth > 0.45f && depth < 0.55f;   // the nut slid in
                if (!screw && !pocket) continue;
                fix.add.Add(new CustomHole {
                    center = t.localPosition, axis = t.localRotation * Vector3.forward, size = 0.17f, depth = t.localScale.z, threaded = pocket,
                });
            }
        }

        private static void Slots(GameObject part, Mesh mesh, List<HoleCollider> holes, Fix fix) {
            var bounds = mesh.bounds;
            var size = bounds.size;
            // Rows run along the part's length.
            var along = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            var length = Vector3.Dot(size, along);
            if (length < 2f) return;
            var probe = new GameObject("Slot Probe") { hideFlags = HideFlags.HideAndDontSave };
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            try {
                var rows = new Dictionary<string, (Vector3 axis, Vector3 basePoint, float depth, List<int> members)>();
                for (var i = 0; i < holes.Count; i++) {
                    var t = holes[i].transform;
                    var axis = t.localRotation * Vector3.forward;
                    axis = new Vector3(Mathf.Round(axis.x), Mathf.Round(axis.y), Mathf.Round(axis.z));
                    if (axis.sqrMagnitude < 0.5f || Mathf.Abs(Vector3.Dot(axis, along)) > 0.1f) continue;
                    var basePoint = t.localPosition - along * Vector3.Dot(t.localPosition, along);
                    var key = Mathf.Abs(axis.x) + "," + Mathf.Abs(axis.y) + "," + Mathf.Abs(axis.z) + "|" + Mathf.Round(basePoint.x * 50) + "," + Mathf.Round(basePoint.y * 50) + "," + Mathf.Round(basePoint.z * 50);
                    if (!rows.TryGetValue(key, out var row)) row = (axis.normalized, basePoint, t.localScale.z, new List<int>());
                    row.members.Add(i);
                    rows[key] = row;
                }
                var start = Vector3.Dot(bounds.min, along) + 0.02f;
                var end = Vector3.Dot(bounds.max, along) - 0.02f;
                const float Step = 0.01f;
                foreach (var (axis, basePoint, depth, members) in rows.Values) {
                    var across = Vector3.Cross(axis, along).normalized;
                    bool Open(Vector3 point) => !collider.Raycast(new Ray(point - axis * 1.5f, axis), out _, 3f);
                    var runStart = float.NaN;
                    var first = true;
                    for (var d = start; d <= end + Step / 2f; d += Step) {
                        var open = d <= end && Open(basePoint + along * d);
                        if (open && float.IsNaN(runStart)) runStart = first ? float.NegativeInfinity : d;
                        if (!open && !float.IsNaN(runStart)) {
                            var runEnd = d;
                            // Not one touching the scan's ends (that's the part's edge, not a slot).
                            if (!float.IsNegativeInfinity(runStart) && d <= end && runEnd - runStart > 0.45f) {
                                var middle = (runStart + runEnd) / 2f;
                                var centre = basePoint + along * middle;
                                // Its width: from the middle, out to the metal on each side.
                                float Reach(Vector3 direction) {
                                    for (var w = 0f; w < 0.5f; w += 0.005f)
                                        if (!Open(centre + direction * w)) return w;
                                    return 0.5f;
                                }
                                float plus = Reach(across), minus = Reach(-across);
                                if (plus + minus > 0.08f && plus + minus < 0.45f) {
                                    centre += across * ((plus - minus) / 2f);
                                    fix.add.Add(new CustomHole {
                                        center = centre, axis = axis, size = plus + minus, size2 = runEnd - runStart, square = true,
                                        up = along, depth = Mathf.Max(depth, 0.02f),
                                    });
                                    foreach (var m in members) {
                                        var p = Vector3.Dot(holes[m].transform.localPosition, along);
                                        if (p > runStart - 0.05f && p < runEnd + 0.05f && !fix.remove.Contains(m)) fix.remove.Add(m);
                                    }
                                }
                            }
                            runStart = float.NaN;
                        }
                        first = false;
                    }
                }
            }
            finally {
                Object.DestroyImmediate(probe);
            }
        }
    }
}
