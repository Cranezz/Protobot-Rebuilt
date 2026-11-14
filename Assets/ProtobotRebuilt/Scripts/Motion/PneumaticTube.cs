using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    /// <summary>A point a pneumatic tube's route goes through: pushed into a fitting's port, through a hole, or a
    /// point on (or near) a part. It moves with its part.</summary>
    public class TubeNode {
        // V2: Edge: a corner or edge of a part the tube goes round (its side touches it; dir: the face's normal).
        public const int Point = 0, Port = 1, Hole = 2, Edge = 3;
        public GameObject part;
        public Vector3 local;       // in the part's space (the tube's when there's no part)
        public Vector3 dirLocal;    // port: out of it; hole: its axis
        public int kind;
        public float depth;         // the port's or hole's depth

        public Vector3 Position(Transform tube) => part != null ? part.transform.TransformPoint(local) : tube.TransformPoint(local);
        public Vector3 Direction(Transform tube) => dirLocal.sqrMagnitude < 1e-8f ? Vector3.zero
            : (part != null ? part.transform.TransformDirection(dirLocal) : tube.TransformDirection(dirLocal)).normalized;
        public TubeNode Copy() => (TubeNode)MemberwiseClone();
    }

    // V2: pneumatic tubing (VEX's V5 kit: 4 mm OD x 2.5 mm ID black polyurethane), routed by the user from a fitting
    // through holes and round the robot to another fitting (TubeTool), then edited where it lies (TubeEditor: its
    // points as blue dots, drag them; a gray dot anywhere on it adds one). It's drawn bending round each point with
    // the tube's own bend radius, starts and ends pushed into the fittings' ports, goes straight through holes, and
    // follows the parts it's on. Purely for looks and the parts list (its length and weight); air still goes where the
    // cylinders' air settings say. Part "PTUB-4mm", saved as "tube:x,y,z,dx,dy,dz,kind,depth/...".
    public class PneumaticTube : MonoBehaviour {
        public const float Radius = 0.0787f;       // 4 mm OD
        public const float BendRadius = 0.6f;      // its bend radius unless set (bends smoothly round its points)
        public const float MinBend = 0.4f, MaxBend = 6f;   // 4 mm PU kinks tighter than about 10 mm; gentle: long curves
        public const float Lead = 0.25f;           // straight out of a port before it can bend
        public const float GramsPerInch = 0.23f;   // PU, 4 x 2.5 mm

        public readonly List<TubeNode> nodes = new();
        // V2: what a wire (Wire, a tube of another kind) changes.
        public virtual float WeightPerInch => GramsPerInch;
        public virtual string Title => "Pneumatic tubing";
        public virtual string Describe(float length) => Inch.Format(Mathf.Round(length * 8f) / 8f) + " of 4 mm tubing";
        /// <summary>How smoothly it bends round its points (the radius of each bend; legs too short for it get less):
        /// tight corners at the least, long flowing curves at the most. Saved as "tubebend:r".</summary>
        public float bendRadius = BendRadius;
        private string parsedBend;
        public static readonly List<PneumaticTube> All = new();
        public List<Vector3> Line { get; private set; } = new();
        public float Length { get; private set; }

        protected Mesh mesh, hitMesh;
        protected MeshCollider hit;
        private string parsedToken, shownKey;
        private long seenVersion = -1;
        /// <summary>While its route is being changed (dragged), nodes aren't saved each frame.</summary>
        public bool editing;

        protected virtual void Awake() {
            All.Add(this);
            mesh = new Mesh { name = "Pneumatic tube" };
            hitMesh = new Mesh { name = "Pneumatic tube (clicks)" };
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var child = transform.Find("Tube");
            if (child == null) {
                child = new GameObject("Tube") { layer = gameObject.layer }.transform;
                child.SetParent(transform, false);
                child.gameObject.AddComponent<SelectAs>().target = gameObject;
            }
            child.GetComponent<SelectAs>().target = gameObject;
            hit = child.gameObject.GetOrAdd<MeshCollider>();
            hit.sharedMesh = null;
        }

        protected virtual void OnDestroy() {
            All.Remove(this);
            if (mesh != null) Destroy(mesh);
            if (hitMesh != null) Destroy(hitMesh);
        }

        // ------------------------------------------------------------------ the route

        public (Vector3 p, Vector3 d, int kind, float depth) World(TubeNode node) {
            var p = node.Position(transform);
            var d = node.Direction(transform);
            if (Sandbox.Running && node.part != null) {
                p = Sandbox.SimPoint(node.part, p);
                if (d != Vector3.zero) d = Sandbox.SimDirection(node.part, d);
            }
            return (p, d, node.kind, node.depth);
        }

        /// <summary>The tube's centreline through these points (world): out of a port straight, through a hole
        /// straight, bent round the rest.</summary>
        public static List<Vector3> BuildLine(IList<(Vector3 p, Vector3 d, int kind, float depth)> route, float radius = BendRadius, IList<float> stretched = null) {
            var points = new List<Vector3>();
            var edges = new List<(int at, Vector3 corner, Vector3 normal)>();
            for (var i = 0; i < route.Count; i++) {
                var (p, d, kind, depth) = route[i];
                if (kind == TubeNode.Port && d != Vector3.zero) {
                    var outer = p + d * (depth / 2f + Lead);
                    // Stretched (the simulation: the fitting pulled away from the next point): the length it gained
                    // goes into the straight bit out of the port, as far as its bend toward the next point needs.
                    var gained = stretched != null && i < stretched.Count ? stretched[i] : 0f;
                    if (gained > 1e-4f && route.Count > 1) {
                        var toward = i == 0 ? route[1].p : route[i - 1].p;
                        var lead = depth / 2f + Lead;
                        for (var pass = 0; pass < 3; pass++) {
                            var turn = Vector3.Angle(d, toward - outer) * Mathf.Deg2Rad;
                            var cut = Mathf.Clamp(radius, MinBend, MaxBend) * Mathf.Tan(Mathf.Min(turn, 3.0f) / 2f);
                            // (Never past the next point along the port's axis: going further only makes it turn back.)
                            var ahead = Vector3.Dot(toward - p, d) - cut;
                            outer = p + d * Mathf.Clamp(2f * cut, lead, Mathf.Max(lead, Mathf.Min(lead + gained, ahead)));
                        }
                    }
                    if (i == 0) {
                        points.Add(p);
                        points.Add(outer);
                    }
                    else {
                        points.Add(outer);
                        points.Add(p);
                    }
                }
                else if (kind == TubeNode.Hole && d != Vector3.zero) {
                    var before = points.Count > 0 ? points[^1] : p;
                    var after = i + 1 < route.Count ? route[i + 1].p : p;
                    var way = Vector3.Dot(after - before, d) < 0f ? -1f : 1f;
                    var half = depth / 2f + 0.12f;
                    points.Add(p - d * (way * half));
                    points.Add(p + d * (way * half));
                }
                else {
                    if (kind == TubeNode.Edge) edges.Add((points.Count, p, d));
                    points.Add(p);
                }
            }
            // Round a corner or edge: the bend goes round it with the tube's side on it (the bend's middle is a tube
            // radius outside it), not through it.
            radius = Mathf.Clamp(radius, MinBend, MaxBend);
            foreach (var (at, corner, normal) in edges) {
                var place = corner + normal * Radius;
                if (at > 0 && at + 1 < points.Count) {
                    var a = (corner - points[at - 1]).normalized;
                    var b = (points[at + 1] - corner).normalized;
                    var turn = Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f));
                    var outward = a - b;
                    if (turn > 0.05f && outward.sqrMagnitude > 1e-6f && Vector3.Dot(outward, normal) > -0.2f) {
                        outward.Normalize();
                        // (The turn there grows as the point moves out: settle on it.)
                        for (var pass = 0; pass < 8; pass++) {
                            var into = (place - points[at - 1]).normalized;
                            var onward = (points[at + 1] - place).normalized;
                            var bend = Mathf.Acos(Mathf.Clamp(Vector3.Dot(into, onward), -1f, 1f));
                            var cut = radius * Mathf.Tan(Mathf.Min(bend, 3.0f) / 2f);
                            cut = Mathf.Min(cut, Vector3.Distance(place, points[at - 1]) * 0.5f, Vector3.Distance(place, points[at + 1]) * 0.5f);
                            place = corner + outward * (Radius * 1.05f + 0.5f * cut * Mathf.Sin(bend / 2f));
                        }
                    }
                }
                points[at] = place;
            }
            for (var i = points.Count - 1; i > 0; i--)
                if ((points[i] - points[i - 1]).sqrMagnitude < 1e-8f) points.RemoveAt(i);
            return Strands.Rounded(points, Mathf.Clamp(radius, MinBend, MaxBend), 5f);
        }

        // ------------------------------------------------------------------ saving

        private static string F(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);

        public string Token() {
            var origin = transform.position;
            return string.Join("/", nodes.Select(n => V(n.Position(transform) - origin) + "," + V(n.Direction(transform)) + "," + n.kind + "," + F(n.depth)));
        }

        public void Save() {
            var token = Token();
            Dynamics.SetToken(gameObject, "tube", token);
            parsedToken = token;
            parsedBend = Mathf.Abs(bendRadius - BendRadius) < 1e-4f ? null : F(bendRadius);
            Dynamics.SetToken(gameObject, "tubebend", parsedBend);
            if (TryGetComponent(out Parts_List.PartName partName)) partName.weightInGrams = Length * WeightPerInch;
        }

        /// <summary>Its route (nodes already on their parts), then saves it.</summary>
        public void Set(IList<TubeNode> route) {
            nodes.Clear();
            foreach (var n in route) nodes.Add(n.Copy());
            if (nodes.Count > 0) transform.SetPositionAndRotation(nodes[0].Position(transform), Quaternion.identity);
            Refresh(true);
            Save();
        }

        private static bool Parse(string[] f, int at, out Vector3 v) {
            v = default;
            if (f.Length < at + 3) return false;
            var ok = float.TryParse(f[at], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                     & float.TryParse(f[at + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                     & float.TryParse(f[at + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z);
            v = new Vector3(x, y, z);
            return ok;
        }

        private void Load(string token) {
            parsedToken = token;
            nodes.Clear();
            var origin = transform.position;
            foreach (var entry in token.Split('/')) {
                var f = entry.Split(',');
                if (f.Length < 8 || !Parse(f, 0, out var p) || !Parse(f, 3, out var d)) continue;
                int.TryParse(f[6], out var kind);
                float.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var depth);
                nodes.Add(Attach(transform, origin + p, d, kind, depth, nodes.Count > 0 ? nodes[^1].part : null));
            }
        }

        /// <summary>A node at a world point, on the part there: a port's or hole's part by its hole, else the part
        /// nearest; a point in the air goes with the part the route came from.</summary>
        public static TubeNode Attach(Transform tube, Vector3 point, Vector3 direction, int kind, float depth, GameObject fallback) {
            GameObject part = null;
            if (kind == TubeNode.Port || kind == TubeNode.Hole) {
                var best = 0.06f;
                foreach (var c in Physics.OverlapSphere(point, 0.06f, HoleCollider.HOLE_COLLISIONS_MASK, QueryTriggerInteraction.Collide)) {
                    if (!c.TryGetComponent(out HoleCollider hole) || hole.transform.parent == null) continue;
                    var d = Vector3.Distance(hole.transform.position, point);
                    if (d < best) {
                        best = d;
                        part = hole.transform.parent.gameObject;
                    }
                }
            }
            if (part == null) part = Strands.PartAt(point, 0.2f, p => p.GetComponent<PneumaticTube>() != null || p.GetComponent<ElasticBand>() != null);
            if (part == null) part = fallback;
            return Node(tube, part, point, direction, kind, depth);
        }

        public static TubeNode Node(Transform tube, GameObject part, Vector3 point, Vector3 direction, int kind, float depth) => new() {
            part = part, kind = kind, depth = depth,
            local = part != null ? part.transform.InverseTransformPoint(point) : tube.InverseTransformPoint(point),
            dirLocal = direction.sqrMagnitude < 1e-8f ? Vector3.zero : part != null ? part.transform.InverseTransformDirection(direction) : tube.InverseTransformDirection(direction),
        };

        // ------------------------------------------------------------------ following its parts

        private void LateUpdate() {
            var token = Dynamics.Token(gameObject, "tube");
            if (token == null) return;
            if (token != parsedToken && !editing) {
                Load(token);
                shownKey = null;
            }
            var bend = Dynamics.Token(gameObject, "tubebend");
            if (bend != parsedBend && !editing) {
                parsedBend = bend;
                bendRadius = bend != null && float.TryParse(bend, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? Mathf.Clamp(r, MinBend, MaxBend) : BendRadius;
                shownKey = null;
            }
            if (seenVersion != Dynamics.Version) {
                seenVersion = Dynamics.Version;
                for (var i = 0; i < nodes.Count; i++)
                    if (nodes[i].part != null && nodes[i].part.IsDeleted())
                        nodes[i] = Attach(transform, nodes[i].Position(transform), nodes[i].Direction(transform), nodes[i].kind, nodes[i].depth, null);
            }
            Refresh(false);
        }

        public void Refresh(bool force) {
            if (nodes.Count == 0) return;
            var route = nodes.Select(World).ToList();
            var key = bendRadius.ToString("F3") + "|" + string.Join(";", route.Select(r => r.p.ToString("F4") + r.d.ToString("F3")));
            if (!force && key == shownKey) return;
            shownKey = key;
            // Moved with the build (not the simulation): its own place follows its first point.
            if (!Sandbox.Running && !editing && nodes[0].part != null && (transform.position - route[0].p).sqrMagnitude > 1e-9f) {
                // Points in the tube's own space stay where they are in the world.
                var held = nodes.Where(n => n.part == null).Select(n => (n, n.Position(transform))).ToList();
                transform.SetPositionAndRotation(route[0].p, Quaternion.identity);
                foreach (var (n, at) in held) n.local = transform.InverseTransformPoint(at);
                Save();
            }
            // V2: in the simulation it's drawn the same way through where its points are now (no physics); a fitting
            // pulled away lengthens the straight bit out of its port so the bend stays as gentle as in the build.
            if (Sandbox.Running) {
                var stretched = new float[route.Count];
                if (route.Count > 1)
                    foreach (var i in new[] { 0, route.Count - 1 }) {
                        if (nodes[i].kind != TubeNode.Port) continue;
                        var next = i == 0 ? 1 : i - 1;
                        var built = Vector3.Distance(nodes[i].Position(transform), nodes[next].Position(transform));
                        stretched[i] = Mathf.Max(0f, Vector3.Distance(route[i].p, route[next].p) - built);
                    }
                Line = BuildLine(route, bendRadius, stretched);
                Draw(false);
                return;
            }
            Line = BuildLine(route, bendRadius);
            Length = Strands.Length(Line);
            Draw();
        }

        protected virtual void Draw(bool clicks = true) {
            mesh.Clear();
            if (clicks) hitMesh.Clear();
            if (Line.Count < 2) {
                hit.sharedMesh = null;
                return;
            }
            var origin = transform.position;
            var local = Line.Select(p => p - origin).ToList();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            Strands.Tube(local, false, Radius, 10, vertices, normals, triangles);
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            if (!clicks) return;   // (the simulation doesn't click it: no collider to rebuild every frame)
            var hv = new List<Vector3>();
            var hn = new List<Vector3>();
            var ht = new List<int>();
            Strands.Tube(local, false, 0.11f, 6, hv, hn, ht);
            hitMesh.SetVertices(hv);
            hitMesh.SetNormals(hn);
            hitMesh.SetTriangles(ht, 0);
            hitMesh.RecalculateBounds();
            hit.sharedMesh = null;
            hit.sharedMesh = hitMesh;
        }

        // ------------------------------------------------------------------ how smoothly it bends

        public static float RadiusAt(float v) => Mathf.Exp(Mathf.Lerp(Mathf.Log(MinBend), Mathf.Log(MaxBend), v));
        public static float SliderAt(float radius) => Mathf.InverseLerp(Mathf.Log(MinBend), Mathf.Log(MaxBend), Mathf.Log(Mathf.Clamp(radius, MinBend, MaxBend)));

        /// <summary>The Bend tool's slider for tubing: Tight (as tight as it bends) to Gentle (long curves).</summary>
        public static RectTransform BendSlider(Transform parent, float radius, System.Action<float> changed, System.Action<float> done = null) {
            var row = Ui.Rect("Bend", parent);
            Ui.Row(row, 8f);
            row.Size(-1, 30f);
            Ui.Text(row, "Tight", "Tight", Theme.FontSmall, Theme.TextDim).Size(38f, 26f);
            var holder = Ui.Rect("Slider", row);
            holder.Size(-1, 30f, 1f);
            Ui.Row(holder, 0f);
            var slider = MotionUi.Slider(holder, SliderAt(radius), 0f, 0f, v => Inch.Format(Mathf.Round(RadiusAt(v) * 16f) / 16f) + " R",
                                         v => changed(RadiusAt(v)), done != null ? v => done(RadiusAt(v)) : null);
            slider.Size(-1, 26f, 1f);
            var label = slider.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (label != null) label.Size(56f, 26f);
            Ui.Text(row, "Gentle", "Gentle", Theme.FontSmall, Theme.TextDim).Size(46f, 26f);
            Ui.Tip(holder, "How smoothly it bends", null, "The radius of each bend: left is as tight as 4 mm tubing bends without kinking, right is long flowing curves through its points.");
            return row;
        }

        // ------------------------------------------------------------------ ports

        /// <summary>A push-in port for 4 mm tubing: a fitting's, tee's or valve's 4 mm hole.</summary>
        public static bool IsPort(HoleCollider hole) {
            if (hole == null || hole.transform.parent == null) return false;
            var part = hole.transform.parent.gameObject;
            var id = part.GetComponent<SavedObject>()?.id ?? "";
            if (!id.StartsWith("VXPNEUMATICS") && !ChildPartGenerator.IsFittingPart(part)) return false;
            var scale = hole.transform.lossyScale;
            var size = Mathf.Min(scale.x, scale.y);
            return size > 0.145f && size < 0.19f;
        }

        /// <summary>A port's tube end: its middle, the way out of it (away from the part's middle), its depth.</summary>
        public static (Vector3 point, Vector3 outward, float depth) PortFrame(HoleCollider hole) {
            var part = hole.transform.parent;
            var center = part.TryGetComponent(out Renderer r) ? r.bounds.center : part.position;
            var axis = hole.transform.forward;
            if (Vector3.Dot(hole.transform.position - center, axis) < 0f) axis = -axis;
            return (hole.transform.position, axis, Mathf.Abs(hole.transform.lossyScale.z));
        }

        /// <summary>Whether a tube already ends in this port (besides one).</summary>
        public static bool PortTaken(HoleCollider hole, PneumaticTube except = null) {
            var at = hole.transform.position;
            foreach (var tube in All)
                if (tube != except && tube != null && !tube.gameObject.IsDeleted())
                    foreach (var n in tube.nodes)
                        if (n.kind == TubeNode.Port && (n.Position(tube.transform) - at).sqrMagnitude < 0.0025f) return true;
            return false;
        }

        // ------------------------------------------------------------------ undo

        public class TubeElement : IElement {
            private readonly PneumaticTube tube;
            private readonly string token, bend;
            private readonly Vector3 position;

            public TubeElement(PneumaticTube tube) {
                this.tube = tube;
                token = Dynamics.Token(tube.gameObject, "tube");
                bend = Dynamics.Token(tube.gameObject, "tubebend");
                position = tube.transform.position;
            }

            public void Load() {
                if (tube == null) return;
                tube.transform.position = position;
                Dynamics.SetToken(tube.gameObject, "tube", token);
                Dynamics.SetToken(tube.gameObject, "tubebend", bend);
            }
        }
    }

    // "PTUB-4mm": made at start-up like the chains.
    public class TubePartGenerator : PartGenerator {
        public static PartType CreateType() {
            var obj = new GameObject("Pneumatic Tubing");
            obj.SetActive(false);
            Object.DontDestroyOnLoad(obj);
            var type = obj.AddComponent<PartType>();
            type.id = "PTUB";
            type.group = PartType.PartGroup.Motion;
            var generator = obj.AddComponent<TubePartGenerator>();
            generator.param1 = new Parameter { name = "Tubing", value = "4mm" };
            generator.param2 = new Parameter { name = "" };
            return type;
        }

        public override List<string> GetParam1Options() => new() { "4mm" };
        public override List<string> GetParam2Options() => new();

        private static Mesh empty;
        private static Mesh Empty => empty != null ? empty : empty = new Mesh { name = "Pneumatic tubing (routed)" };
        public override Mesh GetMesh() => Empty;

        public static Material Material => ElasticPartGenerator.MaterialFor("Pneumatic tubing", new Color(0.035f, 0.035f, 0.04f), 0.55f);

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var obj = new GameObject("Pneumatic Tubing");
            obj.SetActive(false);
            obj.transform.SetPositionAndRotation(position, Quaternion.identity);
            obj.AddComponent<MeshFilter>().sharedMesh = Empty;
            obj.AddComponent<MeshRenderer>().sharedMaterial = new Material(Material);
            var name = obj.AddComponent<Parts_List.PartName>();
            name.name = "Pneumatic Tubing (4 mm)";
            name.weightInGrams = 1f;
            obj.AddComponent<PneumaticTube>();
            obj.SetActive(true);
            SetId(obj);
            return obj;
        }
    }
}
