using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Motion {
    // V2: wiring. A V5 Smart Cable (flat 4-wire, black, a clear RJ-style plug with its latch on each end) or a 3-wire
    // extension cable (black, red and white wires side by side, a black 3-pin housing on each end), routed like
    // pneumatic tubing (it is one: same route, points, bending, editing, saving): from a port on a device or the brain
    // (TubeNode.Port, its mouth and the way out) through points and holes to another port. Its plugs sit in the
    // sockets. A device wired to a brain port takes that port's number (WirePorts.BrainPortOf: "Motor 11"). Part
    // "WIRE-V5" / "WIRE-3W".
    public class Wire : PneumaticTube {
        public WireKind kind;
        public static readonly List<Wire> Wires = new();

        // Plugs: how far each sits out of its socket (the cable leaves straight from there) and its size.
        public const float SmartOut = 0.32f, SmartIn = 0.36f, SmartLong = 0.33f, SmartShort = 0.25f;
        public const float ThreeOut = 0.5f, ThreeLong = 0.31f, ThreeShort = 0.12f;
        // A V5 Smart Cable is flat 4-wire modular cable (its plug is a 4-contact RJ-style one): about 4.4 x 2.3 mm,
        // its edges round.
        public const float FlatWidth = 0.17f, FlatThick = 0.09f, WireRadius = 0.024f;

        /// <summary>A port node's depth so the cable leaves straight out of the plug (BuildLine: depth / 2 + Lead).</summary>
        public static float PortDepth(WireKind kind) => 2f * ((kind == WireKind.Smart ? SmartOut : ThreeOut) + 0.06f - Lead);

        public override float WeightPerInch => kind == WireKind.Smart ? 0.45f : 0.3f;
        public override string Title => kind == WireKind.Smart ? "V5 Smart Cable" : "3-wire cable";
        public override string Describe(float length) {
            var text = Inch.Format(Mathf.Round(length * 8f) / 8f) + " of " + (kind == WireKind.Smart ? "smart cable" : "3-wire cable");
            var ends = new[] { nodes.FirstOrDefault(), nodes.LastOrDefault() }.Where(n => n != null && n.kind == TubeNode.Port && n.part != null)
                .Select(n => EndName(n)).ToList();
            return ends.Count == 2 ? text + "\n" + ends[0] + " to " + ends[1] : text;
        }

        private string EndName(TubeNode n) {
            var port = WirePorts.Near(n.part, n.Position(transform), 0.2f);
            var name = WirePorts.IsBrain(n.part) ? "the brain" : n.part.TryGetComponent(out MotorDrive motor) ? motor.Label : PartLabels.Short(n.part);
            return port != null && port.brain ? name + " " + port.label.Replace("Port ", "port ") : name;
        }

        protected override void Awake() {
            base.Awake();
            Wires.Add(this);
        }

        protected override void OnDestroy() {
            Wires.Remove(this);
            base.OnDestroy();
        }

        // ------------------------------------------------------------------ drawing

        protected override void Draw(bool clicks = true) {
            mesh.Clear();
            if (clicks) hitMesh.Clear();
            if (Line.Count < 2) {
                hit.sharedMesh = null;
                return;
            }
            var origin = transform.position;
            // Each end: the plug in its socket, and the cable starting at the plug's back.
            var start = End(0, out var startOut, out var startUp);
            Vector3 endOut = default, endUp = default;
            var end = nodes.Count > 1 ? End(nodes.Count - 1, out endOut, out endUp) : null;
            BuildMesh(mesh, Line, kind, origin, start, startOut, startUp, end, endOut, endUp);
            AppendTies(mesh, origin);   // V2: zip ties
            if (!clicks) return;
            var hv = new List<Vector3>();
            var hn = new List<Vector3>();
            var ht = new List<int>();
            Strands.Tube(Line.Select(p => p - origin).ToList(), false, 0.11f, 6, hv, hn, ht);
            hitMesh.SetVertices(hv);
            hitMesh.SetNormals(hn);
            hitMesh.SetTriangles(ht, 0);
            hitMesh.RecalculateBounds();
            hit.sharedMesh = null;
            hit.sharedMesh = hitMesh;
        }

        /// <summary>The cable along a line (world points, the mesh relative to origin) with a plug in each socket given
        /// (its mouth, the way out of it, its long side): the cable itself and the routing tool's preview.</summary>
        public static void BuildMesh(Mesh mesh, List<Vector3> world, WireKind kind, Vector3 origin, Vector3? start, Vector3 startOut, Vector3 startUp,
                                     Vector3? end, Vector3 endOut, Vector3 endUp) {
            mesh.Clear();
            if (world.Count < 2) return;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var cable = new List<int>();
            var plug = new List<int>();
            var extra = new List<int>();   // contacts (V5) / the red wire (3-wire)
            var white = new List<int>();   // the white wire (3-wire)
            var line = world.Select(p => p - origin).ToList();
            var cut = kind == WireKind.Smart ? SmartOut - 0.08f : ThreeOut - 0.06f;
            if (start != null) line = Trim(line, true, cut);
            if (end != null) line = Trim(line, false, cut);
            var up0 = start != null ? startUp : Vector3.zero;
            if (kind == WireKind.Smart) Flat(line, up0, FlatWidth, FlatThick, vertices, normals, cable);
            else {
                // Three round wires side by side (black, red, white).
                var sides = Sides(line, up0);
                for (var w = 0; w < 3; w++) {
                    var shifted = new List<Vector3>();
                    for (var i = 0; i < line.Count; i++) shifted.Add(line[i] + sides[i] * ((w - 1) * WireRadius * 2.05f));
                    Strands.Tube(shifted, false, WireRadius, 6, vertices, normals, w == 0 ? cable : w == 1 ? extra : white);
                }
            }
            if (start != null) Plug(kind, start.Value - origin, startOut, startUp, vertices, normals, plug, extra);
            if (end != null) Plug(kind, end.Value - origin, endOut, endUp, vertices, normals, plug, extra);
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 4;
            mesh.SetTriangles(cable, 0);
            mesh.SetTriangles(plug, 1);
            mesh.SetTriangles(extra, 2);
            mesh.SetTriangles(white, 3);
            mesh.RecalculateBounds();
        }

        // A plugged end: its socket mouth (world), the way out and its long side.
        private Vector3? End(int index, out Vector3 outward, out Vector3 up) {
            outward = up = Vector3.zero;
            if (index < 0 || index >= nodes.Count || nodes[index].kind != TubeNode.Port) return null;
            var (p, d, _, _) = World(nodes[index]);
            outward = d.sqrMagnitude > 1e-6f ? d : Vector3.up;
            var port = nodes[index].part != null ? WirePorts.Near(nodes[index].part, nodes[index].Position(transform), 0.2f) : null;
            up = port != null ? port.Up : Vector3.zero;
            if (port != null && Sandbox.Running) up = Sandbox.SimDirection(nodes[index].part, up);
            up = Vector3.ProjectOnPlane(up.sqrMagnitude > 1e-6f ? up : Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.forward, outward);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.right, outward);
            up.Normalize();
            return p;
        }

        // The line without its first (or last) stretch (inside the plug).
        private static List<Vector3> Trim(List<Vector3> line, bool atStart, float length) {
            var list = atStart ? line : Enumerable.Reverse(line).ToList();
            var left = length;
            var i = 0;
            while (i + 1 < list.Count) {
                var seg = Vector3.Distance(list[i], list[i + 1]);
                if (seg > left) break;
                left -= seg;
                i++;
            }
            var result = new List<Vector3>();
            if (i + 1 < list.Count) result.Add(Vector3.Lerp(list[i], list[i + 1], left / Mathf.Max(1e-6f, Vector3.Distance(list[i], list[i + 1]))));
            for (var k = i + 1; k < list.Count; k++) result.Add(list[k]);
            if (result.Count < 2) result = list.Skip(list.Count - 2).ToList();
            if (!atStart) result.Reverse();
            return result;
        }

        // The cable's sideways direction along its line, carried smoothly from its first end's plug.
        private static List<Vector3> Sides(IList<Vector3> line, Vector3 up0) {
            var sides = new List<Vector3>();
            var side = Vector3.zero;
            for (var i = 0; i < line.Count; i++) {
                var t = Strands.Tangent(line, i, false);
                if (i == 0) side = up0.sqrMagnitude > 1e-6f ? up0 : Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right);
                side = Vector3.ProjectOnPlane(side, t);
                if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(t, Vector3.up);
                side.Normalize();
                sides.Add(side);
            }
            return sides;
        }

        // A flat cable: width along its side, thickness across, its two edges round (a stadium-shaped section).
        private static void Flat(IList<Vector3> line, Vector3 up0, float width, float thick, List<Vector3> v, List<Vector3> n, List<int> t) {
            if (line.Count < 2) return;
            var sides = Sides(line, up0);
            // The section: flat faces between two half circles, as (along the side, across) points and their normals.
            var r = thick / 2f;
            var half = width / 2f - r;
            var profile = new List<(Vector2 p, Vector2 normal)>();
            const int Arc = 6;
            for (var k = 0; k <= Arc; k++) {
                var a = -Mathf.PI / 2f + Mathf.PI * k / Arc;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                profile.Add((new Vector2(half, 0f) + dir * r, dir));
            }
            for (var k = 0; k <= Arc; k++) {
                var a = Mathf.PI / 2f + Mathf.PI * k / Arc;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                profile.Add((new Vector2(-half, 0f) + dir * r, dir));
            }
            var ring = profile.Count;
            var start = v.Count;
            for (var i = 0; i < line.Count; i++) {
                var tan = Strands.Tangent(line, i, false);
                var s0 = sides[i];
                var across = Vector3.Cross(tan, s0).normalized;
                foreach (var (p, normal) in profile) {
                    v.Add(line[i] + s0 * p.x + across * p.y);
                    n.Add((s0 * normal.x + across * normal.y).normalized);
                }
            }
            for (var i = 0; i + 1 < line.Count; i++)
                for (var k = 0; k < ring; k++) {
                    var a = start + i * ring + k;
                    var b = start + i * ring + (k + 1) % ring;
                    var c = a + ring;
                    var d = b + ring;
                    // (faces out: the profile goes round anticlockwise seen along the line)
                    t.AddRange(new[] { a, b, c, b, d, c });
                }
        }

        // A box between two points along an axis (axis, up, side frame), faces outward.
        private static void Box(Vector3 from, Vector3 to, Vector3 up, float alongUp, float alongSide, List<Vector3> v, List<Vector3> n, List<int> t) {
            var axis = (to - from).normalized;
            var side = Vector3.Cross(up, axis).normalized;
            up = Vector3.Cross(axis, side).normalized;
            var hu = up * (alongUp / 2f);
            var hs = side * (alongSide / 2f);
            void Face(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d) {
                var s = v.Count;
                v.AddRange(new[] { a, b, c, d });
                n.AddRange(new[] { normal, normal, normal, normal });
                // Wound so it faces along its normal.
                // (Unity's front face: cross(b - a, c - a) points out of it.)
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f) t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                else t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            Vector3 P(Vector3 o, float u, float w) => o + hu * u + hs * w;
            Face(up, P(from, 1, -1), P(from, 1, 1), P(to, 1, 1), P(to, 1, -1));
            Face(-up, P(from, -1, -1), P(to, -1, -1), P(to, -1, 1), P(from, -1, 1));
            Face(side, P(from, -1, 1), P(to, -1, 1), P(to, 1, 1), P(from, 1, 1));
            Face(-side, P(from, -1, -1), P(from, 1, -1), P(to, 1, -1), P(to, -1, -1));
            Face(axis, P(to, -1, -1), P(to, 1, -1), P(to, 1, 1), P(to, -1, 1));
            Face(-axis, P(from, -1, -1), P(from, -1, 1), P(from, 1, 1), P(from, 1, -1));
        }

        private static void Plug(WireKind kind, Vector3 mouth, Vector3 outward, Vector3 up, List<Vector3> v, List<Vector3> n, List<int> body, List<int> extra) {
            if (kind == WireKind.Smart) {
                // The clear RJ-style plug: into the socket and out of it, its latch on one side, its boot behind.
                Box(mouth - outward * SmartIn, mouth + outward * (SmartOut - 0.1f), up, SmartLong, SmartShort, v, n, body);
                var side = Vector3.Cross(up, outward).normalized;
                Box(mouth + outward * 0.02f + side * (SmartShort / 2f + 0.02f), mouth + outward * (SmartOut - 0.12f) + side * (SmartShort / 2f + 0.02f), up, SmartLong * 0.4f, 0.04f, v, n, body);
                Box(mouth + outward * (SmartOut - 0.1f), mouth + outward * SmartOut, up, SmartLong * 0.7f, SmartShort * 0.75f, v, n, body);
                // Gold contacts showing through the front.
                Box(mouth - outward * (SmartIn - 0.02f) + side * (SmartShort * 0.3f), mouth - outward * 0.05f + side * (SmartShort * 0.3f), up, SmartLong * 0.6f, 0.02f, v, n, extra);
            }
            else {
                // A black 3-pin housing against the port.
                Box(mouth - outward * 0.02f, mouth + outward * ThreeOut, up, ThreeLong, ThreeShort, v, n, body);
            }
        }

        public static Material[] Materials(WireKind kind) {
            var black = ElasticPartGenerator.MaterialFor("Cable black", new Color(0.04f, 0.04f, 0.045f), 0.35f);
            if (kind == WireKind.Smart) {
                var clear = ClearPlastic();
                var gold = ElasticPartGenerator.MaterialFor("Contacts gold", new Color(0.62f, 0.5f, 0.2f), 0.75f);
                return new[] { new Material(black), clear, new Material(gold), new Material(black) };
            }
            var red = ElasticPartGenerator.MaterialFor("Wire red", new Color(0.55f, 0.06f, 0.05f), 0.4f);
            var white = ElasticPartGenerator.MaterialFor("Wire white", new Color(0.42f, 0.42f, 0.41f), 0.4f);
            return new[] { new Material(black), new Material(black), new Material(red), new Material(white) };
        }

        private static Material clearTemplate;

        // Clear polycarbonate-like plastic (URP Lit, alpha blended), as the plugs are.
        private static Material ClearPlastic() {
            if (clearTemplate == null) {
                clearTemplate = new Material(ElasticPartGenerator.MaterialFor("Plug clear", new Color(0.78f, 0.8f, 0.82f, 0.45f), 0.8f)) { name = "Plug clear" };
                clearTemplate.SetFloat("_Surface", 1f);
                clearTemplate.SetFloat("_Blend", 0f);
                clearTemplate.SetOverrideTag("RenderType", "Transparent");
                clearTemplate.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                clearTemplate.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                clearTemplate.SetFloat("_ZWrite", 0f);
                clearTemplate.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                clearTemplate.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return new Material(clearTemplate);
        }
    }

    // "WIRE-V5" / "WIRE-3W": made at start-up like the tubing.
    public class WirePartGenerator : PartGenerator {
        public static PartType CreateType() {
            var obj = new GameObject("Wiring");
            obj.SetActive(false);
            Object.DontDestroyOnLoad(obj);
            var type = obj.AddComponent<PartType>();
            type.id = "WIRE";
            type.group = PartType.PartGroup.Motion;
            var generator = obj.AddComponent<WirePartGenerator>();
            generator.param1 = new Parameter { name = "Cable", value = "V5" };
            generator.param2 = new Parameter { name = "" };
            return type;
        }

        public override List<string> GetParam1Options() => new() { "V5", "3W" };
        public override List<string> GetParam2Options() => new();

        private static Mesh empty;
        private static Mesh Empty => empty != null ? empty : empty = new Mesh { name = "Cable (routed)" };
        public override Mesh GetMesh() => Empty;

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var kind = param1.value == "3W" ? WireKind.ThreeWire : WireKind.Smart;
            var obj = new GameObject(kind == WireKind.Smart ? "V5 Smart Cable" : "3-Wire Cable");
            obj.SetActive(false);
            obj.transform.SetPositionAndRotation(position, Quaternion.identity);
            obj.AddComponent<MeshFilter>().sharedMesh = Empty;
            obj.AddComponent<MeshRenderer>().sharedMaterials = Wire.Materials(kind);
            var name = obj.AddComponent<Parts_List.PartName>();
            name.name = kind == WireKind.Smart ? "V5 Smart Cable" : "3-Wire Extension Cable";
            name.weightInGrams = 1f;
            obj.AddComponent<Wire>().kind = kind;
            obj.SetActive(true);
            SetId(obj);
            return obj;
        }
    }
}
