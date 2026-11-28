using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    /// <summary>V2 (build 88): a kind of string or rope teams use (Override R19d: any non-elastic rope/string up to
    /// 1/4" thick). Weights are worked out from the cord's size and nylon/PE density (VEX gives its rope's: 0.010 lb a
    /// 4 ft section); strengths are what the sellers rate them at.</summary>
    public class StringKind {
        public string key, name, shortName, material, source;
        public float diameter;        // inches
        public Color color;
        public float strength;        // lbf, the seller's rating
        public float gramsPerInch;

        /// <summary>Drawn at least this thick, so a fishing line still shows.</summary>
        public float DrawRadius => Mathf.Max(diameter, 0.045f) / 2f;

        /// <summary>V2 (build 88): an open chain (a run of chain tied to parts at its ends, as cascade and chain lifts
        /// use: its links drawn along the line, round sprockets on their pitch circle, driven by a motor's sprocket's teeth
        /// in the simulation instead of sliding round it). Null for rope and string.</summary>
        public ChainFamily chain;
        public bool IsChain => chain != null;
        /// <summary>How far the line's middle stands off a post: a rope's radius; a chain's pitch line is on the sprocket's.</summary>
        public float PathRadius => IsChain ? 0f : DrawRadius;
        /// <summary>Whether the seller rates it (VEX doesn't rate its chain).</summary>
        public bool Rated => !float.IsInfinity(strength);
        public string RatingText => Rated ? strength.ToString("0", CultureInfo.InvariantCulture) + " lb rating" : "not rated";

        public static readonly List<StringKind> All = new() {
            new StringKind { key = "VEX", name = "VEX rope (1/8\")", shortName = "VEX rope", material = "solid braided nylon", source = "VEX Winch and Pulley Kit (276-1546), 4 ft",
                             diameter = 0.125f, color = new Color(0.4f, 0.4f, 0.38f), strength = 100f, gramsPerInch = 0.095f },
            new StringKind { key = "PARA", name = "Paracord 550 (5/32\")", shortName = "Paracord", material = "nylon kernmantle", source = "outdoor and hardware stores",
                             diameter = 0.156f, color = new Color(0.03f, 0.03f, 0.035f), strength = 550f, gramsPerInch = 0.2f },
            new StringKind { key = "LINE", name = "Braided nylon line (1/16\")", shortName = "Nylon line", material = "braided nylon (#18 mason line)", source = "hardware stores",
                             diameter = 0.066f, color = new Color(0.46f, 0.43f, 0.03f), strength = 150f, gramsPerInch = 0.045f },
            new StringKind { key = "BRAID", name = "Braided fishing line (50 lb)", shortName = "Fishing line", material = "braided PE (Spectra/Dyneema)", source = "fishing stores",
                             diameter = 0.014f, color = new Color(0.07f, 0.15f, 0.07f), strength = 50f, gramsPerInch = 0.006f },
            // V2 (build 88): open chain. VEX doesn't publish a breaking strength for its chain, so it doesn't break here.
            new StringKind { key = "CHAIN", name = "VEX chain (normal strength)", shortName = "Chain", material = "VEX plastic links, 0.145\" pitch", source = "the VEX sprocket and chain kits",
                             diameter = 0.1f, color = new Color(0.16f, 0.16f, 0.17f), strength = float.PositiveInfinity, chain = ChainFamily.Normal,
                             gramsPerInch = ChainFamily.Normal.gramsPerLink / ChainFamily.Normal.pitch },
            new StringKind { key = "HSCHAIN", name = "VEX high strength chain", shortName = "HS chain", material = "VEX steel-pin links, 0.385\" pitch", source = "the VEX high strength sprocket and chain kits",
                             diameter = 0.2f, color = new Color(0.16f, 0.16f, 0.17f), strength = float.PositiveInfinity, chain = ChainFamily.HighStrength,
                             gramsPerInch = ChainFamily.HighStrength.gramsPerLink / ChainFamily.HighStrength.pitch },
        };

        public static StringKind Of(string key) => All.FirstOrDefault(k => k.key == key) ?? All[0];
    }

    /// <summary>One thing a string's path is on: tied to it (an end), round it (a post, a pulley, a shaft: its
    /// side), through it (a hole), over it (a point on an edge), or wound on it (an end on a winch drum or shaft).</summary>
    public class StringNode {
        public BandPost post;
        /// <summary>Which way round a post it goes: +1 / -1, or 0 = the way it bends least (as it would lie).</summary>
        public int side;
        /// <summary>An end wound on its post (a winch drum, a shaft): turns already on it.</summary>
        public bool spool;

        public StringNode Copy() => new() { post = post.Copy(), side = side, spool = spool };
        public bool Round => post.HasAxis && !post.hole;
    }

    /// <summary>A string's path in the world: its line, how long it is tight, its straight runs (where slack hangs).</summary>
    public class StringPath {
        public readonly List<Vector3> line = new();
        public float tight;              // tight length (inches), without the knots and what's wound on
        public float wound;              // on a spool at an end
        public string problem;
        public bool Valid => problem == null && line.Count >= 2;
        public readonly List<(Vector3 a, Vector3 b)> spans = new();   // the straight runs, in order
        public readonly List<List<Vector3>> pieces = new();           // the path in pieces: [arc or knot][span][arc]...
        public readonly List<bool> pieceIsSpan = new();
        public readonly List<(Vector3 at, Vector3 along)> knots = new();
        public readonly List<(Vector3 center, Vector3 axis, float radius)> loops = new();   // tied round a post
        public readonly List<List<Vector3>> coils = new();                                    // wound on a spool
        /// <summary>Which way it goes round each node (+1 / -1; 0 where it doesn't).</summary>
        public int[] ways = new int[0];

        public float Turns(float radius) => radius > 1e-4f ? wound / (2f * Mathf.PI * radius) : 0f;

        /// <summary>The path through nodes (world centers, axes, radii...). stringRadius: the line's centre stands off
        /// posts by its radius. turns: wound on a spool end.</summary>
        public static StringPath Of(IList<StringNode> nodes, IList<(Vector3 center, Vector3 axis)> world, float stringRadius, float turns) {
            var path = new StringPath();
            var n = nodes.Count;
            if (n < 2) {
                path.problem = "Tie it to two things at least";
                return path;
            }
            var c = world.Select(w => w.center).ToArray();
            var axes = world.Select(w => w.axis).ToArray();
            var radius = nodes.Select(x => Mathf.Max(0f, x.post.radius) + stringRadius).ToArray();
            var round = new bool[n];
            for (var i = 0; i < n; i++) round[i] = axes[i] != Vector3.zero && !nodes[i].post.hole;
            var inPt = (Vector3[])c.Clone();
            var outPt = (Vector3[])c.Clone();
            var phiIn = new float[n];
            var phiOut = new float[n];
            var sweep = new float[n];
            var basis = new (Vector3 u, Vector3 v)[n];
            for (var i = 0; i < n; i++) {
                if (!round[i]) continue;
                var a = axes[i].normalized;
                var u = Vector3.Cross(a, Mathf.Abs(a.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                basis[i] = (u, Vector3.Cross(a, u));
            }
            // A spool's coils: the turns sit side by side along its axis, the string leaving from the last one.
            var pitch = stringRadius * 2.1f;
            float Along(int i) => nodes[i].spool ? (turns * pitch) / 2f : 0f;

            Vector2 To2(int i, Vector3 w) => new(Vector3.Dot(w - c[i], basis[i].u), Vector3.Dot(w - c[i], basis[i].v));
            Vector3 On(int i, float phi, float shift = 0f) => c[i] + (basis[i].u * Mathf.Cos(phi) + basis[i].v * Mathf.Sin(phi)) * radius[i] + axes[i].normalized * shift;
            // Tangent from a point to a circle (way +1 = round it counterclockwise): arriving at, or leaving from.
            float Arrive(int i, Vector3 from, int way) {
                var q = To2(i, from);
                var d = q.magnitude;
                var alpha = Mathf.Atan2(q.y, q.x);
                var beta = d > radius[i] * 1.001f ? Mathf.Acos(radius[i] / d) : 0f;
                return alpha + way * beta;
            }
            float Leave(int i, Vector3 to, int way) {
                var q = To2(i, to);
                var d = q.magnitude;
                var alpha = Mathf.Atan2(q.y, q.x);
                var beta = d > radius[i] * 1.001f ? Mathf.Acos(radius[i] / d) : 0f;
                return alpha - way * beta;
            }
            float Sweep(float from, float to, int way) {
                var s = Mathf.Repeat((to - from) * way, 2f * Mathf.PI);
                return s * way;
            }
            var ways = new int[n];
            for (var pass = 0; pass < 8; pass++)
                for (var i = 0; i < n; i++) {
                    if (!round[i]) continue;
                    var prev = i > 0 ? outPt[i - 1] : (Vector3?)null;
                    var next = i < n - 1 ? inPt[i + 1] : (Vector3?)null;
                    if (prev == null || next == null) {
                        // An end tied round (or wound on) it: the string leaves it on one side toward its neighbour.
                        var way = nodes[i].side != 0 ? nodes[i].side : 1;
                        ways[i] = way;
                        var toward = (next ?? prev).Value;
                        var phi = prev == null ? Leave(i, toward, way) : Arrive(i, toward, way);
                        phiIn[i] = phiOut[i] = phi;
                        inPt[i] = outPt[i] = On(i, phi, Along(i));
                        continue;
                    }
                    // Round it: the given side, or the side a tight string lies on: away from the straight line between
                    // its neighbours (the post pushes it out); in line with them, the side it bends least.
                    int best = nodes[i].side;
                    if (best == 0) {
                        var p2 = To2(i, prev.Value);
                        var n2 = To2(i, next.Value);
                        var chord = n2 - p2;
                        var cross = chord.x * (-p2.y) - chord.y * (-p2.x);   // the post's middle left (+) or right of the line
                        if (Mathf.Abs(cross) > 0.02f * chord.magnitude) best = cross > 0f ? -1 : 1;
                    }
                    if (best == 0) {
                        var s1 = Mathf.Abs(Sweep(Arrive(i, prev.Value, 1), Leave(i, next.Value, 1), 1));
                        var s2 = Mathf.Abs(Sweep(Arrive(i, prev.Value, -1), Leave(i, next.Value, -1), -1));
                        best = s1 <= s2 ? 1 : -1;
                    }
                    ways[i] = best;
                    phiIn[i] = Arrive(i, prev.Value, best);
                    phiOut[i] = Leave(i, next.Value, best);
                    sweep[i] = Sweep(phiIn[i], phiOut[i], best);
                    inPt[i] = On(i, phiIn[i]);
                    outPt[i] = On(i, phiOut[i]);
                }

            path.ways = ways;
            // The pieces, in order: round (or tied round / wound on) each node, then straight to the next.
            for (var i = 0; i < n; i++) {
                var piece = new List<Vector3>();
                if (round[i] && (i == 0 || i == n - 1)) {
                    if (nodes[i].spool) {
                        // Coils side by side along the axis, ending where it leaves.
                        var coil = new List<Vector3>();
                        var steps = Mathf.Max(8, Mathf.CeilToInt(turns * 24f));
                        var total = turns * 2f * Mathf.PI;
                        for (var k = 0; k <= steps; k++) {
                            var t = k / (float)steps;
                            // V2 (build 88): at the last point the string arrives and goes on round the same way (it was
                            // drawn coiling back the other way)
                            var phi = i == 0 ? phiOut[i] - ways[i] * total * (1f - t) : phiIn[i] + ways[i] * total * (1f - t);
                            coil.Add(On(i, phi, -Along(i) + t * 2f * Along(i)));
                        }
                        if (i == n - 1) coil.Reverse();
                        path.coils.Add(coil);
                        path.wound = turns * 2f * Mathf.PI * radius[i];
                    }
                    else {
                        path.loops.Add((c[i], axes[i].normalized, radius[i]));
                        path.knots.Add((inPt[i], Vector3.zero));
                    }
                    piece.Add(inPt[i]);
                }
                else if (round[i]) {
                    var steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep[i]) / (8f * Mathf.Deg2Rad)));
                    for (var k = 0; k <= steps; k++) piece.Add(On(i, phiIn[i] + sweep[i] * k / steps));
                }
                else {
                    piece.Add(c[i]);
                    if (i == 0 || i == n - 1) path.knots.Add((c[i], Vector3.zero));
                }
                path.pieces.Add(piece);
                path.pieceIsSpan.Add(false);
                if (i < n - 1) {
                    var a = outPt[i];
                    var b = inPt[i + 1];
                    path.spans.Add((a, b));
                    path.pieces.Add(new List<Vector3> { a, b });
                    path.pieceIsSpan.Add(true);
                }
            }
            foreach (var piece in path.pieces)
                foreach (var p in piece)
                    if (path.line.Count == 0 || (path.line[^1] - p).sqrMagnitude > 1e-10f) path.line.Add(p);
            path.tight = Strands.Length(path.line);
            // The knots point along the string.
            for (var k = 0; k < path.knots.Count; k++) {
                var at = path.knots[k].at;
                var along = path.line.Count > 1 ? ((at - path.line[0]).sqrMagnitude < (at - path.line[^1]).sqrMagnitude ? path.line[1] - path.line[0] : path.line[^1] - path.line[^2]) : Vector3.forward;
                path.knots[k] = (at, along.normalized);
            }
            return path;
        }

        /// <summary>The line drawn with slack: the extra length hanging in each straight run (a parabola, its sag from
        /// its length), shared by their lengths. Gravity pulls it down; a vertical run bows out sideways.</summary>
        public List<Vector3> Hanging(float slack) {
            if (slack <= 1e-3f || spans.Count == 0) return line;
            var total = spans.Sum(s => (s.b - s.a).magnitude);
            if (total < 1e-4f) return line;
            var result = new List<Vector3>();
            for (var p = 0; p < pieces.Count; p++) {
                var piece = pieces[p];
                if (!pieceIsSpan[p]) {
                    foreach (var q in piece) Add(result, q);
                    continue;
                }
                var a = piece[0];
                var b = piece[1];
                var length = (b - a).magnitude;
                var extra = slack * length / total;
                var h = Mathf.Sqrt(3f * length * extra / 8f);
                var dir = length > 1e-5f ? (b - a) / length : Vector3.forward;
                var down = Vector3.ProjectOnPlane(Vector3.down, dir);
                if (down.sqrMagnitude < 0.04f) down = Vector3.ProjectOnPlane(Vector3.right, dir);
                down.Normalize();
                const int Steps = 16;
                for (var k = 0; k <= Steps; k++) {
                    var t = k / (float)Steps;
                    Add(result, Vector3.Lerp(a, b, t) + down * (4f * h * t * (1f - t)));
                }
            }
            return result;
        }

        private static void Add(List<Vector3> list, Vector3 p) {
            if (list.Count == 0 || (list[^1] - p).sqrMagnitude > 1e-10f) list.Add(p);
        }
    }

    // V2 (build 88): string or rope (Docs/String concept.md, phase 1): tied to a part at each end, round pulleys, posts
    // and shafts on the way (the side it goes round), through holes, over edges, or wound on a winch drum or shaft at an
    // end. It follows its parts, is drawn tight or hanging with its slack, knots at the ends, coils on a drum. Placed with
    // the String tool (StringTool), changed where it lies (StringEditor). Part "STRG-<kind>", saved as
    // "string:kind|length|turns|node/node..." (node: center from the string's place, axis, radius, flags (1 hole, 2
    // spool), side). Not physics yet (phase 2): it's for looks, the parts list and its length.
    public class StringLine : MonoBehaviour {
        public const float KnotLength = 1.5f;   // what a knot (and its tail) takes up, cutting it to length

        public StringKind kind = StringKind.All[0];
        public readonly List<StringNode> nodes = new();
        /// <summary>Its free length (end to end, not counting knots or what's wound on); tight when the path is longer.</summary>
        public float length;
        public float turns = 3f;

        public static readonly List<StringLine> All = new();
        public StringPath Path { get; private set; }
        public List<Vector3> Line { get; private set; } = new();
        public float Slack => Path != null ? length - Path.tight : 0f;
        /// <summary>The length to cut: the free length, what's wound on and the knots.</summary>
        public float CutLength => kind.IsChain ? Mathf.Max(length, Path?.tight ?? 0f)
            : Mathf.Max(length, Path?.tight ?? 0f) + (Path?.wound ?? 0f) + KnotLength * (Path?.knots.Count ?? 0);
        /// <summary>V2: an open chain's links (its cut length in whole links).</summary>
        public int LinkCount => kind.IsChain ? Mathf.CeilToInt(CutLength / kind.chain.pitch - 0.01f) : 0;
        private string PartTitle => kind.IsChain ? "Chain: " + kind.name : "String: " + kind.name;

        private Mesh mesh, hitMesh;
        private MeshCollider hit;
        private string parsedToken, shownKey;
        private long seenVersion = -1;
        /// <summary>While its path is being changed (dragged), it isn't saved each frame.</summary>
        public bool editing;
        /// <summary>Broken in the simulation (pulled past its rating: Sandbox.Strings): drawn as two loose ends.</summary>
        public bool broken;
        /// <summary>V2 (build 88, phase 3): the turns on its drum now, while the simulation winds it (-1: its own).</summary>
        public float simTurns = -1f;
        /// <summary>V2 (build 88, phase 4): the end whose pin slid out of its loop in the simulation (-1: none).</summary>
        public int released = -1;
        public float TurnsNow => Sandbox.Running && simTurns >= 0f ? simTurns : turns;

        private void Awake() {
            All.Add(this);
            mesh = new Mesh { name = "String" };
            hitMesh = new Mesh { name = "String (clicks)" };
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var child = transform.Find("Line");
            if (child == null) {
                child = new GameObject("Line") { layer = gameObject.layer }.transform;
                child.SetParent(transform, false);
                child.gameObject.AddComponent<SelectAs>().target = gameObject;
            }
            child.GetComponent<SelectAs>().target = gameObject;
            hit = child.gameObject.GetOrAdd<MeshCollider>();
            hit.sharedMesh = null;
        }

        private void OnDestroy() {
            All.Remove(this);
            if (mesh != null) Destroy(mesh);
            if (hitMesh != null) Destroy(hitMesh);
        }

        public void UseKind(StringKind k) {
            if (k == null) return;
            var changed = kind != k;
            kind = k;
            var saved = GetComponent<SavedObject>();
            if (saved != null && saved.id != "STRG-" + k.key) saved.id = "STRG-" + k.key;
            if (TryGetComponent(out Parts_List.PartName partName)) partName.name = PartTitle;
            if ((changed || GetComponent<MeshRenderer>().sharedMaterial == null) && TryGetComponent(out MeshRenderer r)) r.sharedMaterial = StringPartGenerator.MaterialOf(k);
            shownKey = null;
        }

        public float Grams => CutLength * kind.gramsPerInch;

        public string Describe() {
            if (Path == null || !Path.Valid) return Path?.problem ?? "";
            var text = Inch.Format(Mathf.Round(length * 8f) / 8f) + " " + (kind.IsChain ? kind.shortName.ToLowerInvariant() + " (" + LinkCount + " links)" : kind.shortName.ToLowerInvariant());
            var slack = Slack;
            text += slack > 0.06f ? " · " + Inch.Format(Mathf.Round(slack * 8f) / 8f) + " slack" : slack < -0.06f ? " · stretched " + Inch.Format(Mathf.Round(-slack * 8f) / 8f) + " too far" : " · tight";
            if (Path.wound > 0f) text += " · " + turns.ToString("0.#", CultureInfo.InvariantCulture) + " turns on the drum";
            text += kind.IsChain ? "\n" + LinkCount + " links of " + kind.chain.name.ToLowerInvariant() + ", its ends bolted to their parts"
                : "\nCut it " + Inch.Format(Mathf.Ceil(CutLength * 4f) / 4f) + " (knots and turns included) · " + kind.RatingText;
            return text;
        }

        // ------------------------------------------------------------------ the path

        public (Vector3 center, Vector3 axis) World(StringNode node) {
            var c = node.post.Center;
            var a = node.post.Axis;
            if (Sandbox.Running && node.post.part != null) {
                c = Sandbox.SimPoint(node.post.part, c);
                if (a != Vector3.zero) a = Sandbox.SimDirection(node.post.part, a);
            }
            return (c, a);
        }

        public static StringPath PathOf(IList<StringNode> nodes, StringKind kind, float turns) =>
            StringPath.Of(nodes, nodes.Select(n => (n.post.Center, n.post.Axis)).ToList(), kind.PathRadius, kind.IsChain ? 0f : turns);

        // ------------------------------------------------------------------ saving

        private static string F(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);

        public string Token() {
            var origin = transform.position;
            var list = nodes.Select(n => V(n.post.Center - origin) + "," + V(n.post.Axis) + "," + F(n.post.radius) + "," + ((n.post.hole ? 1 : 0) | (n.spool ? 2 : 0)) + "," + n.side);
            return kind.key + "|" + F(length) + "|" + F(turns) + "|" + string.Join("/", list);
        }

        public void Save() {
            var token = Token();
            Dynamics.SetToken(gameObject, "string", token);
            parsedToken = token;
            if (TryGetComponent(out Parts_List.PartName partName)) {
                partName.weightInGrams = Grams;
                // The parts list says what to cut.
                if (Path != null && Path.Valid) partName.name = kind.IsChain ? PartTitle + ", " + LinkCount + " links" : PartTitle + ", cut " + Inch.Format(Mathf.Ceil(CutLength * 4f) / 4f);
            }
        }

        /// <summary>Its path (nodes on their parts) and free length, then saves it.</summary>
        public void Set(IList<StringNode> route, float freeLength, float spoolTurns) {
            nodes.Clear();
            foreach (var n in route) nodes.Add(n.Copy());
            if (kind.IsChain) foreach (var n in nodes) n.spool = false;   // (a chain isn't wound on anything)
            length = freeLength;
            turns = spoolTurns;
            if (nodes.Count > 0) transform.SetPositionAndRotation(nodes[0].post.Center, Quaternion.identity);
            Refresh(true);
            Save();
        }

        private static bool Parse3(string[] f, int at, out Vector3 v) {
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
            var fields = token.Split('|');
            if (fields.Length < 4) return;
            UseKind(StringKind.Of(fields[0]));
            float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out length);
            float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out turns);
            var origin = transform.position;
            foreach (var entry in fields[3].Split('/')) {
                var f = entry.Split(',');
                if (f.Length < 9 || !Parse3(f, 0, out var c) || !Parse3(f, 3, out var axis)) continue;
                float.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var r);
                int.TryParse(f[7], out var flags);
                int.TryParse(f[8], out var side);
                nodes.Add(new StringNode { post = Attach(origin + c, axis, r, (flags & 1) != 0, null), spool = (flags & 2) != 0 && !kind.IsChain, side = Mathf.Clamp(side, -1, 1) });
            }
        }

        /// <summary>A post at a world point, on the part there (a hole's part by its hole), or with the fallback.</summary>
        public static BandPost Attach(Vector3 center, Vector3 axis, float radius, bool hole, GameObject fallback) {
            GameObject part = null;
            if (hole)
                foreach (var col in Physics.OverlapSphere(center, 0.06f, HoleCollider.HOLE_COLLISIONS_MASK, QueryTriggerInteraction.Collide))
                    if (col.TryGetComponent(out HoleCollider h) && h.transform.parent != null) {
                        part = h.transform.parent.gameObject;
                        break;
                    }
            if (part == null && !hole && axis.sqrMagnitude > 1e-8f) part = RoundPartAt(center, axis.normalized, radius);
            if (part == null) part = Strands.PartAt(center, Mathf.Max(0.25f, radius + 0.15f), Skip);
            if (part == null) part = fallback;
            return new BandPost {
                part = part, radius = radius, hole = hole,
                local = part != null ? part.transform.InverseTransformPoint(center) : center,
                axisLocal = axis.sqrMagnitude < 1e-8f ? Vector3.zero : part != null ? part.transform.InverseTransformDirection(axis) : axis,
            };
        }

        // V2 (build 88): round a sprocket, pulley or winch drum on a shaft, the post is that part (its middle on the
        // post's axis, within a little of its center along it), not the shaft through it: reopened, an open chain was
        // put on the motor's shaft and its sprocket no longer drove it.
        private static GameObject RoundPartAt(Vector3 center, Vector3 axis, float radius) {
            GameObject best = null;
            var bestDistance = 0.12f;
            foreach (var collider in Physics.OverlapSphere(center, Mathf.Max(0.3f, radius + 0.2f), LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore)) {
                var saved = collider.GetComponentInParent<SavedObject>();
                if (saved == null || saved.gameObject.IsDeleted() || Skip(saved.gameObject)) continue;
                var part = saved.gameObject;
                Vector3 middle;
                if (ChainFamily.ForSprocket(part) != null) middle = ChainLoop.SprocketFrame(part).center;
                else if (StringUi.IsGrooved(part) && part.TryGetComponent(out Renderer r)) middle = r.bounds.center;
                else continue;
                var off = middle - center;
                var along = Vector3.Dot(off, axis);
                var across = (off - along * axis).magnitude;
                if (Mathf.Abs(along) > 0.6f || across >= bestDistance) continue;
                bestDistance = across;
                best = part;
            }
            return best;
        }

        /// <summary>Strings, bands and tubes aren't things a string ties to.</summary>
        public static bool Skip(GameObject p) => p.GetComponent<StringLine>() != null || p.GetComponent<ElasticBand>() != null || p.GetComponent<PneumaticTube>() != null;

        // ------------------------------------------------------------------ following its parts

        private void LateUpdate() {
            var token = Dynamics.Token(gameObject, "string");
            if (token == null) return;
            if (links.Count > 0 && links[0] != null && links[0].gameObject.layer != gameObject.layer)
                foreach (var l in links) if (l != null) l.gameObject.layer = gameObject.layer;   // (deleted, hidden, placed: its links go with it)
            if (token != parsedToken && !editing) {
                Load(token);
                shownKey = null;
            }
            if (seenVersion != Dynamics.Version) {
                seenVersion = Dynamics.Version;
                for (var i = 0; i < nodes.Count; i++)
                    if (nodes[i].post.part != null && nodes[i].post.part.IsDeleted())
                        nodes[i].post = Attach(nodes[i].post.Center, nodes[i].post.Axis, nodes[i].post.radius, nodes[i].post.hole, null);
            }
            Refresh(false);
        }

        public void Refresh(bool force) {
            if (nodes.Count == 0) return;
            var world = nodes.Select(World).ToList();
            if (!Sandbox.Running) {
                broken = false;
                simTurns = -1f;
                released = -1;
            }
            var turnsNow = TurnsNow;
            var key = (broken ? "broken|" : "") + released + kind.key + "|" + F(length) + "|" + F(turnsNow) + "|" + string.Join(";", world.Select(w => w.center.ToString("F4") + w.axis.ToString("F3")))
                      + "|" + string.Join("", nodes.Select(n => n.side + (n.spool ? "s" : "")));
            if (!force && key == shownKey) return;
            shownKey = key;
            // Moved with the build (not the simulation): its own place follows its first point, and it's saved where
            // every point is now (a pulley in the middle moved: the build file must find it there).
            if (!Sandbox.Running && !editing) {
                if (nodes[0].post.part != null && (transform.position - world[0].center).sqrMagnitude > 1e-9f)
                    transform.SetPositionAndRotation(world[0].center, Quaternion.identity);
                var token = Token();
                if (token != parsedToken) Save();
            }
            Path = StringPath.Of(nodes, world, kind.PathRadius, kind.IsChain ? 0f : turnsNow);
            // (winding in the simulation: the free length is what isn't on the drum)
            var free = length - (Path.Valid && turnsNow != turns ? Path.wound - StringPath.Of(nodes, world, kind.PathRadius, turns).wound : 0f);
            Line = Path.Valid ? Path.Hanging(free - Path.tight) : new List<Vector3>();
            if (broken && Path.Valid) Line = released >= 0 ? LooseEnd(Path.line, released != 0) : LooseEnds(Path.line);
            Draw(!Sandbox.Running);
        }

        // Broken: an end hangs from each tie, a few inches of it, frayed where it parted (the knots and coils stay).
        private static List<Vector3> LooseEnds(List<Vector3> line) {
            var result = new List<Vector3>();
            void Tail(Vector3 from, Vector3 toward) {
                var dir = (toward - from).sqrMagnitude > 1e-6f ? (toward - from).normalized : Vector3.down;
                var bend = Vector3.Lerp(dir, Vector3.down, 0.6f).normalized;
                result.Add(from);
                result.Add(from + dir * 0.6f);
                result.Add(from + dir * 0.6f + bend * 1.2f);
                result.Add(from + dir * 0.6f + bend * 1.2f + Vector3.down * 1.8f);
            }
            Tail(line[0], line[1]);
            var first = new List<Vector3>(result);
            result.Clear();
            Tail(line[^1], line[^2]);
            first.Reverse();
            // (one list: the tube jumps between the ends through the first tail's tip; drawn as two tubes below)
            first.Add(Vector3.positiveInfinity);
            first.AddRange(result);
            return first;
        }

        // Let go at one end: it hangs from the other (from the drum or its tie), the loop that held the pin trailing.
        private static List<Vector3> LooseEnd(List<Vector3> line, bool fromStart) {
            var from = fromStart ? line[0] : line[^1];
            var next = fromStart ? line[1] : line[^2];
            var dir = (next - from).sqrMagnitude > 1e-6f ? (next - from).normalized : Vector3.down;
            var bend = Vector3.Lerp(dir, Vector3.down, 0.7f).normalized;
            var length = Mathf.Min(Strands.Length(line), 8f);
            return new List<Vector3> { from, from + dir * 0.5f, from + dir * 0.5f + bend * 1.2f, from + dir * 0.5f + bend * 1.2f + Vector3.down * Mathf.Max(0.5f, length - 1.7f) };
        }

        private void Draw(bool clicks) {
            mesh.Clear();
            if (clicks) hitMesh.Clear();
            if (Path == null || !Path.Valid || Line.Count < 2) {
                hit.sharedMesh = null;
                return;
            }
            var origin = transform.position;
            if (kind.IsChain) PlaceLinks(Line);
            else ClearLinks();
            // (let go of a pin: its loop and knot went with it)
            Vector3? gone = broken && released >= 0 && Path.line.Count > 0 ? (released == 0 ? Path.line[0] : Path.line[^1]) : null;
            if (kind.IsChain) mesh.Clear();
            else BuildMesh(mesh, Path, Line, kind, origin, gone);
            if (!clicks) return;
            var hv = new List<Vector3>();
            var hn = new List<Vector3>();
            var ht = new List<int>();
            Strands.Tube(Line.Select(p => p - origin).ToList(), false, 0.09f, 6, hv, hn, ht);
            hitMesh.SetVertices(hv);
            hitMesh.SetNormals(hn);
            hitMesh.SetTriangles(ht, 0);
            hitMesh.RecalculateBounds();
            hit.sharedMesh = null;
            hit.sharedMesh = hitMesh;
        }

        // V2 (build 88): an open chain's links, children like a chain loop's: one per pitch along its line (hanging when
        // slack), pins along the plane's normal (the sprockets' axis), inner and outer links alternating.
        private readonly List<Transform> links = new();

        private void PlaceLinks(List<Vector3> line) {
            var family = kind.chain;
            var meshes = family.Links;
            var normal = Vector3.zero;
            foreach (var n in nodes) {
                var a = n.post.Axis;
                if (a == Vector3.zero || n.post.hole) continue;
                if (Sandbox.Running && n.post.part != null) a = Sandbox.SimDirection(n.post.part, a);
                normal += Vector3.Dot(a, normal) < 0f ? -a : a;
            }
            var count = 0;
            var pieces = new List<List<Vector3>> { new() };
            foreach (var p in line) {
                if (float.IsInfinity(p.x)) pieces.Add(new List<Vector3>());
                else pieces[^1].Add(p);
            }
            var material = TryGetComponent(out MeshRenderer root) ? root.sharedMaterial : null;
            foreach (var piece in pieces) {
                if (piece.Count < 2) continue;
                var total = Strands.Length(piece);
                var number = Mathf.FloorToInt(total / family.pitch + 1e-3f);
                var seg = 0;
                var segStart = 0f;
                Vector3 At(float d) {
                    while (seg < piece.Count - 2 && segStart + Vector3.Distance(piece[seg], piece[seg + 1]) < d) {
                        segStart += Vector3.Distance(piece[seg], piece[seg + 1]);
                        seg++;
                    }
                    var len = Vector3.Distance(piece[seg], piece[seg + 1]);
                    return Vector3.Lerp(piece[seg], piece[seg + 1], len > 1e-6f ? Mathf.Clamp01((d - segStart) / len) : 0f);
                }
                var pin = At(0f);
                for (var i = 0; i < number; i++) {
                    var next = At((i + 1) * family.pitch);
                    var dir = next - pin;
                    if (dir.sqrMagnitude < 1e-10f) dir = Vector3.up;
                    var z = normal.sqrMagnitude > 1e-6f ? Vector3.ProjectOnPlane(normal, dir) : Vector3.zero;
                    if (z.sqrMagnitude < 1e-6f) z = Vector3.Cross(dir, Mathf.Abs(dir.normalized.y) < 0.9f ? Vector3.up : Vector3.right);
                    var y = Vector3.Cross(z, dir);
                    if (count >= links.Count) {
                        var link = new GameObject("Link " + links.Count) { layer = gameObject.layer };
                        link.transform.SetParent(transform, false);
                        link.AddComponent<MeshFilter>();
                        link.AddComponent<MeshRenderer>();
                        links.Add(link.transform);
                    }
                    var t = links[count];
                    t.gameObject.layer = gameObject.layer;
                    t.GetComponent<MeshFilter>().sharedMesh = meshes[count % 2];
                    var r = t.GetComponent<MeshRenderer>();
                    if (r.sharedMaterial != material) r.sharedMaterial = material;
                    t.SetPositionAndRotation(pin, Quaternion.LookRotation(z, y));
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    count++;
                    pin = next;
                }
            }
            for (var i = count; i < links.Count; i++) if (links[i] != null && links[i].gameObject.activeSelf) links[i].gameObject.SetActive(false);
        }

        private void ClearLinks() {
            if (links.Count == 0) return;
            foreach (var l in links) if (l != null) Destroy(l.gameObject);
            links.Clear();
        }

        /// <summary>The string's mesh (relative to origin): the line, a knot at each tied end, a loop round a post it's
        /// tied to, its coils on a drum. Its own drawing and the tool's preview use it.</summary>
        public static void BuildMesh(Mesh target, StringPath path, List<Vector3> line, StringKind kind, Vector3 origin, Vector3? leftOut = null) {
            target.Clear();
            if (path == null || !path.Valid || line.Count < 2) return;
            var r = kind.DrawRadius;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            // (a broken string's two loose ends come apart at an infinite point: LooseEnds)
            var run = new List<Vector3>();
            foreach (var p in line) {
                if (float.IsInfinity(p.x)) {
                    if (run.Count >= 2) Strands.Tube(run, false, r, 8, vertices, normals, triangles);
                    run = new List<Vector3>();
                    continue;
                }
                run.Add(p - origin);
            }
            if (run.Count >= 2) Strands.Tube(run, false, r, 8, vertices, normals, triangles);
            foreach (var coil in path.coils) Strands.Tube(coil.Select(p => p - origin).ToList(), false, r, 8, vertices, normals, triangles);
            foreach (var (center, axis, radius) in path.loops) {
                if (leftOut.HasValue && (center - leftOut.Value).magnitude < radius + 0.4f) continue;
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var v = Vector3.Cross(axis, u);
                var loop = new List<Vector3>();
                for (var k = 0; k < 24; k++) {
                    var phi = k * Mathf.PI * 2f / 24f;
                    loop.Add(center - origin + (u * Mathf.Cos(phi) + v * Mathf.Sin(phi)) * radius + axis * (r * 1.1f));
                }
                Strands.Tube(loop, true, r, 8, vertices, normals, triangles);
            }
            // A knot: a fat little bundle where it's tied, with a short tail.
            foreach (var (at, along) in path.knots) {
                if (leftOut.HasValue && (at - leftOut.Value).magnitude < 0.6f) continue;
                var a = along.sqrMagnitude > 0.5f ? along : Vector3.forward;
                var side = Vector3.Cross(a, Mathf.Abs(a.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Strands.Tube(new List<Vector3> { at - origin - a * r * 2.2f, at - origin + a * r * 2.2f }, false, r * 2.3f, 8, vertices, normals, triangles);
                Strands.Tube(new List<Vector3> { at - origin + side * r * 1.5f, at - origin + side * r * 1.5f - a * r * 9f + side * r * 3f }, false, r * 0.95f, 6, vertices, normals, triangles);
            }
            target.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            target.SetVertices(vertices);
            target.SetNormals(normals);
            target.SetTriangles(triangles, 0);
            target.RecalculateBounds();
        }

        // ------------------------------------------------------------------ undo

        public class StringElement : IElement {
            private readonly StringLine line;
            private readonly string token;
            private readonly Vector3 position;

            public StringElement(StringLine line) {
                this.line = line;
                token = Dynamics.Token(line.gameObject, "string");
                position = line.transform.position;
            }

            public void Load() {
                if (line == null) return;
                line.transform.position = position;
                Dynamics.SetToken(line.gameObject, "string", token);
            }
        }
    }

    // "STRG-<kind>": made at start-up like the bands and tubing.
    public class StringPartGenerator : PartGenerator {
        public static PartType CreateType() {
            var obj = new GameObject("String");
            obj.SetActive(false);
            Object.DontDestroyOnLoad(obj);
            var type = obj.AddComponent<PartType>();
            type.id = "STRG";
            type.group = PartType.PartGroup.Motion;
            var generator = obj.AddComponent<StringPartGenerator>();
            generator.param1 = new Parameter { name = "String", value = "VEX" };
            generator.param2 = new Parameter { name = "" };
            return type;
        }

        public override List<string> GetParam1Options() => StringKind.All.Select(k => k.key).ToList();
        public override List<string> GetParam2Options() => new();

        private static Mesh empty;
        private static Mesh Empty => empty != null ? empty : empty = new Mesh { name = "String (routed)" };
        public override Mesh GetMesh() => Empty;

        public static Material MaterialOf(StringKind k) => ElasticPartGenerator.MaterialFor("String " + k.key, k.color, 0.15f);

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var kind = StringKind.Of(param1.value);
            var obj = new GameObject("String");
            obj.SetActive(false);
            obj.transform.SetPositionAndRotation(position, Quaternion.identity);
            obj.AddComponent<MeshFilter>().sharedMesh = Empty;
            obj.AddComponent<MeshRenderer>().sharedMaterial = MaterialOf(kind);
            var name = obj.AddComponent<Parts_List.PartName>();
            name.name = "String: " + kind.name;
            name.weightInGrams = 1f;
            obj.AddComponent<StringLine>().kind = kind;
            obj.SetActive(true);
            SetId(obj);
            return obj;
        }
    }
}
