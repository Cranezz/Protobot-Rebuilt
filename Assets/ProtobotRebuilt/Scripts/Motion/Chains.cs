using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    // V2: chains. VEX has three: Normal (the 10T-48T sprockets, 0.145" pitch as VEX-CAD models them), high
    // strength (6T-30T and the tank tread drive wheel, 0.385") and 6P (8T-40T, 0.25"). A sprocket's pitch radius is
    // pitch / (2 sin(180° / teeth)). The links are VEX-CAD's own models (the Normal chain's is the 6P link scaled to
    // its pitch: VEX-CAD has no model of it).
    public class ChainFamily {
        public string key, name;
        public float pitch, width, gramsPerLink;

        public static readonly ChainFamily Normal = new() { key = "Normal", name = "Normal chain", pitch = 0.145f, width = 0.26f, gramsPerLink = 0.2f };
        public static readonly ChainFamily HighStrength = new() { key = "HS", name = "High strength chain", pitch = 0.385f, width = 0.57f, gramsPerLink = 1.1f };
        public static readonly ChainFamily SixP = new() { key = "6P", name = "6P chain", pitch = 0.25f, width = 0.44f, gramsPerLink = 0.6f };
        public static readonly ChainFamily[] All = { Normal, HighStrength, SixP };

        public static ChainFamily Of(string key) => All.FirstOrDefault(f => f.key == key) ?? Normal;

        /// <summary>The chain a sprocket takes, from its id (null: not a sprocket).</summary>
        public static ChainFamily ForSprocket(GameObject part) {
            if (part == null || GearTrain.Teeth(part, out var sprocket) <= 0 || !sprocket) return null;
            var id = part.GetComponent<SavedObject>().id.ToUpperInvariant();
            if (id.Contains("6P")) return SixP;
            if (id.Contains("HIGH STRENGTH") || id.Contains("-HS ") || id.Contains("9P")) return HighStrength;
            return Normal;
        }

        public float PitchRadius(int teeth) => pitch / (2f * Mathf.Sin(Mathf.PI / Mathf.Max(teeth, 3)));

        /// <summary>V2: the chain's pitch line round a smooth idler (a spacer): its rollers sit on the surface, so the
        /// line is about a roller's radius out from it.</summary>
        public float IdlerRadius(float surface) => surface + pitch * 0.3f;

        /// <summary>V2: a wrap's radius: a sprocket's pitch radius, or round a smooth idler (teeth 0) its own.</summary>
        public float RadiusOf(int teeth, float idlerSurface) => teeth > 0 ? PitchRadius(teeth) : IdlerRadius(idlerSurface);

        /// <summary>V2: whether a part can be a smooth idler in a chain (a spacer or washer: the chain slides round it).</summary>
        public static bool IsIdler(GameObject part) {
            if (part == null || !part.TryGetComponent(out SavedObject saved) || saved.id == null) return false;
            var id = saved.id;
            if (!(id.StartsWith("SPCR-") || id.StartsWith("PSPC-") || id.StartsWith("VXSPACERS"))) return false;
            return GearTrain.Teeth(part, out _) <= 0;
        }

        // ------------------------------------------------------------------ the link (pins at x = 0 and x = pitch, along z)

        private Mesh[] meshes;
        private Material[] materials;

        /// <summary>The link models: [0] and [1] alternate (a roller chain's inner and outer links).</summary>
        public Mesh[] Links {
            get {
                if (meshes != null && meshes[0] != null) return meshes;
                meshes = new[] { VexLink(), null };
                if (meshes[1] == null) meshes[1] = meshes[0];
                return meshes;
            }
        }

        public Material[] VexMaterials => materials;

        /// <summary>What the chain is made of: "chain", or for high strength chain "tread" / "traction" (VEX's
        /// tank tread links, same pitch; their tread faces out of the loop).</summary>
        public static readonly (string key, string name)[] Styles = { ("chain", "Chain"), ("tread", "Tank tread"), ("traction", "Traction tread") };

        public bool HasTreads => this == HighStrength;

        private readonly Dictionary<string, Mesh> styled = new();

        public Mesh[] LinksFor(string style) {
            if (!HasTreads || style == null || style == "chain") return Links;
            if (!styled.TryGetValue(style, out var mesh) || mesh == null) {
                var family = Resources.Load<GameObject>("Part Prefabs/VEX/Chain 9P");
                var child = family != null ? family.GetComponentsInChildren<PartData>(true).FirstOrDefault(d => d.name == (style == "traction" ? "Tank Tread Traction Link" : "Tread Link")) : null;
                if (child == null) return Links;
                // The tread link's pins run along y at z = -0.185, x = -0.19 and +0.195; its tread is on +z.
                var turn = Quaternion.AngleAxis(90f, Vector3.right);
                mesh = MeshAlign.Moved(child.GetComponent<MeshFilter>().sharedMesh, turn, turn * new Vector3(0.19f, 0f, 0.185f));
                mesh.name = style + " link";
                styled[style] = mesh;
            }
            return new[] { mesh, mesh };
        }

        public float WidthFor(string style) => HasTreads && style != null && style != "chain" ? 1.5f : width;

        private Mesh VexLink() {
            // VEX-CAD has no model of the standard (Normal) chain's link: it's VEX's snap-together plastic link like
            // the 6P one, so that one scaled to the standard pitch stands in for it.
            var path = this != HighStrength ? ("Part Prefabs/VEX/Motion Parts", "IQ Chain") : ("Part Prefabs/VEX/Chain 9P", "High Strength Chain Link");
            var family = Resources.Load<GameObject>(path.Item1);
            var child = family != null ? family.GetComponentsInChildren<PartData>(true).FirstOrDefault(d => d.name == path.Item2) : null;
            if (child == null || !child.TryGetComponent(out MeshFilter filter)) return new Mesh { name = name + " link (missing model)" };
            materials = child.GetComponent<MeshRenderer>().sharedMaterials;
            var source = filter.sharedMesh;
            // Into the chain's frame: pins along z, the next pin at +x, centred on z = 0.
            Quaternion turn;
            Vector3 shift;
            if (this != HighStrength) {
                turn = Quaternion.AngleAxis(180f, new Vector3(1f, 0f, 1f).normalized);   // the model runs along z, pins along x
                shift = Vector3.zero;
            }
            else {
                turn = Quaternion.identity;
                shift = new Vector3(0f, 0f, -source.bounds.center.z);
            }
            var mesh = MeshAlign.Moved(source, turn, turn * shift);
            if (this == Normal) {
                var scale = pitch / SixP.pitch;
                var vertices = mesh.vertices;
                for (var i = 0; i < vertices.Length; i++) vertices[i] *= scale;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }
            mesh.name = name + " link";
            return mesh;
        }
    }

    /// <summary>One sprocket the chain goes round: its middle and pitch radius in the chain's plane, and the way
    /// round (1: counterclockwise about the chain's normal, -1: clockwise; the inside of a serpentine).</summary>
    public class ChainWrap {
        public Vector2 center;
        public float radius;
        public int way = 1;
        public int teeth;   // V2: 0 = a smooth idler (a spacer the chain slides round)

        public ChainWrap Copy() => (ChainWrap)MemberwiseClone();
    }

    // The chain's line in its plane: straight spans tangent to each pair of sprockets in turn and arcs round them,
    // in the given ways (so a chain can wrap a sprocket from inside the loop). VEX chains are run tight: the loop is a
    // whole number of links, the part of a link over or short shared by all of them (a few thousandths each).
    public class ChainPath {
        public readonly List<Vector2> points = new();
        public readonly List<float> along = new();
        public float length;       // the chain's own length (links x pitch)
        public float tight;        // the loop's length round the sprockets
        public int links;
        public float[] wrapAngles;  // how far round each sprocket the chain goes (radians)
        public Vector2[] spanFrom, spanTo;   // V2: the straight run leaving each sprocket for the next
        public string problem;

        /// <summary>Teeth of a sprocket in the chain at once (fewer than 3 and it can skip under load).</summary>
        public int TeethIn(int wrap, int teeth) => wrapAngles == null || wrap >= wrapAngles.Length ? 0 : Mathf.RoundToInt(wrapAngles[wrap] / (2f * Mathf.PI) * teeth);
        public bool Valid => problem == null;

        /// <summary>V2: the loop's length round the wraps (spans and arcs) without making its points, for solving a
        /// tensioner many times a step; NaN when two wraps are too close for the chain to go between them.</summary>
        public static float Tight(IList<ChainWrap> wraps) {
            var n = wraps.Count;
            if (n < 2 || n > 64) return float.NaN;
            System.Span<Vector2> outPoint = stackalloc Vector2[n];
            System.Span<Vector2> inPoint = stackalloc Vector2[n];
            var total = 0f;
            for (var i = 0; i < n; i++) {
                var a = wraps[i];
                var b = wraps[(i + 1) % n];
                if (!Tangent(a, b, out var left, out var straight)) return float.NaN;
                outPoint[i] = a.center - a.way * a.radius * left;
                inPoint[(i + 1) % n] = b.center - b.way * b.radius * left;
                total += straight;
            }
            var area = 0f;
            for (var i = 0; i < n; i++) {
                var a = wraps[i].center;
                var b = wraps[(i + 1) % n].center;
                area += a.x * b.y - b.x * a.y;
            }
            var loop = LoopWay(wraps, area);
            for (var i = 0; i < n; i++) {
                var w = wraps[i];
                var from = Mathf.Atan2(inPoint[i].y - w.center.y, inPoint[i].x - w.center.x);
                var to = Mathf.Atan2(outPoint[i].y - w.center.y, outPoint[i].x - w.center.x);
                var sweep = Mathf.Repeat((to - from) * w.way, Mathf.PI * 2f);
                // As Build: an idler pressed in from outside the loop has to touch it; no span runs through a wrap.
                if (n > 2 && w.way != loop && sweep > Mathf.PI) return float.NaN;
                total += sweep * w.radius;
                for (var j = 0; j < n; j++) {
                    if (j == i || j == (i + 1) % n) continue;
                    if (DistanceToSegment(wraps[j].center, outPoint[i], inPoint[(i + 1) % n]) < wraps[j].radius - 0.02f) return float.NaN;
                }
            }
            return total;
        }

        // V2: the way round the loop goes: the way most of its wraps go (an idler pressed in from outside goes the
        // other way), else the way its middles go round. By the middles alone, an idler pressed in past the line between
        // the other two sprockets turned the loop round and the chain was refused.
        private static int LoopWay(IList<ChainWrap> wraps, float area) {
            var sum = 0;
            foreach (var w in wraps) sum += w.way;
            return sum != 0 ? (sum > 0 ? 1 : -1) : area < 0f ? -1 : 1;
        }

        private static bool Tangent(ChainWrap a, ChainWrap b, out Vector2 left, out float straight) {
            var d = b.center - a.center;
            var distance = d.magnitude;
            var delta = b.way * b.radius - a.way * a.radius;
            left = Vector2.zero;
            straight = 0f;
            if (distance <= Mathf.Abs(delta) + 1e-3f) return false;
            straight = Mathf.Sqrt(distance * distance - delta * delta);
            var angle = Mathf.Atan2(d.y, d.x) - Mathf.Atan2(delta, straight);
            left = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            return true;
        }

        /// <summary>The chain round the wraps. <paramref name="fixedLinks"/> (V2): a chain kept tight by a tensioner
        /// keeps its own number of links whatever the loop comes to; 0 = as many as fit the loop.</summary>
        public static ChainPath Build(IList<ChainWrap> wraps, float pitch, Vector2 down, int fixedLinks = 0) {
            var path = new ChainPath();
            if (wraps.Count < 2) {
                path.problem = "Pick at least two sprockets";
                return path;
            }
            var n = wraps.Count;
            var outPoint = new Vector2[n];
            var inPoint = new Vector2[n];
            var spans = new float[n];
            for (var i = 0; i < n; i++) {
                var a = wraps[i];
                var b = wraps[(i + 1) % n];
                var d = b.center - a.center;
                var distance = d.magnitude;
                var sa = a.way * a.radius;
                var sb = b.way * b.radius;
                var delta = sb - sa;
                if (distance <= Mathf.Abs(delta) + 1e-3f) {
                    path.problem = "Sprockets " + (i + 1) + " and " + ((i + 1) % n + 1) + " are too close for the chain to go between them that way: move one or flip one";
                    return path;
                }
                var straight = Mathf.Sqrt(distance * distance - delta * delta);
                var angle = Mathf.Atan2(d.y, d.x) - Mathf.Atan2(delta, straight);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var left = new Vector2(-dir.y, dir.x);
                outPoint[i] = a.center - sa * left;
                inPoint[(i + 1) % n] = b.center - sb * left;
                spans[i] = straight;
            }
            path.spanFrom = outPoint;
            path.spanTo = new Vector2[n];
            for (var i = 0; i < n; i++) path.spanTo[i] = inPoint[(i + 1) % n];
            // The tight length: spans and arcs.
            var sweeps = new float[n];
            var tight = 0f;
            for (var i = 0; i < n; i++) {
                var w = wraps[i];
                var from = Mathf.Atan2(inPoint[i].y - w.center.y, inPoint[i].x - w.center.x);
                var to = Mathf.Atan2(outPoint[i].y - w.center.y, outPoint[i].x - w.center.x);
                var sweep = Mathf.Repeat((to - from) * w.way, Mathf.PI * 2f);
                sweeps[i] = sweep * w.way;
                tight += sweep * w.radius + spans[i];
            }
            // A sprocket wrapped from inside has to push into the loop: one outside the straight line between its
            // neighbours wouldn't touch the chain, and the chain would have to loop round it. (Two sprockets with one
            // flipped is a crossed chain, which is fine.)
            if (n > 2) {
                var area = 0f;
                for (var i = 0; i < n; i++) {
                    var a = wraps[i].center;
                    var b = wraps[(i + 1) % n].center;
                    area += a.x * b.y - b.x * a.y;
                }
                var loop = LoopWay(wraps, area);
                for (var i = 0; i < n; i++)
                    if (wraps[i].way != loop && Mathf.Abs(sweeps[i]) > Mathf.PI) {
                        path.problem = "Sprocket " + (i + 1) + " is outside the chain's path, so it can't press on it from inside: move it in toward the others, or wrap it from outside";
                        return path;
                    }
            }
            path.tight = tight;
            path.wrapAngles = new float[n];
            for (var i = 0; i < n; i++) path.wrapAngles[i] = Mathf.Abs(sweeps[i]);
            path.links = fixedLinks > 0 ? fixedLinks : Mathf.Max(2, Mathf.RoundToInt(tight / pitch));
            path.length = path.links * pitch;
            for (var i = 0; i < n; i++) {
                var w = wraps[i];
                var from = Mathf.Atan2(inPoint[i].y - w.center.y, inPoint[i].x - w.center.x);
                var steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweeps[i]) / (Mathf.PI / 36f)));
                for (var k = 0; k <= steps; k++) {
                    var a = from + sweeps[i] * k / steps;
                    path.Add(w.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * w.radius);
                }
            }
            path.Close();

            // What a real chain can't do: cross itself, or run through a sprocket it goes round.
            for (var i = 0; i < n; i++) {
                var a = outPoint[i];
                var b = inPoint[(i + 1) % n];
                for (var j = 0; j < n; j++) {
                    if (j == i || j == (i + 1) % n) continue;
                    if (DistanceToSegment(wraps[j].center, a, b) < wraps[j].radius - 0.02f) {
                        path.problem = "The chain from sprocket " + (i + 1) + " to " + ((i + 1) % n + 1) + " would run through sprocket " + (j + 1) + ": pick them in the order the chain goes round, or flip one";
                        return path;
                    }
                }
                for (var j = i + 1; j < n; j++) {
                    if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                    if (Crosses(a, b, outPoint[j], inPoint[(j + 1) % n])) {
                        path.problem = "The chain would cross itself (between sprockets " + (i + 1) + " and " + ((i + 1) % n + 1) + "): a real chain can't, so pick them in another order or flip a side";
                        return path;
                    }
                }
            }
            if (n == 2 && wraps[0].way != wraps[1].way) {
                path.problem = "A chain round two sprockets wrapped opposite ways crosses itself: a real chain can't (use gears to turn one the other way)";
                return path;
            }
            return path;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) {
            var ab = b - a;
            var t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d) {
            float Side(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            var d1 = Side(c, d, a);
            var d2 = Side(c, d, b);
            var d3 = Side(a, b, c);
            var d4 = Side(a, b, d);
            return d1 * d2 < -1e-8f && d3 * d4 < -1e-8f;
        }

        private void Add(Vector2 p) {
            if (points.Count > 0 && (points[^1] - p).sqrMagnitude < 1e-10f) return;
            points.Add(p);
        }

        private void Close() {
            along.Clear();
            var s = 0f;
            for (var i = 0; i < points.Count; i++) {
                along.Add(s);
                s += Vector2.Distance(points[i], points[(i + 1) % points.Count]);
            }
            along.Add(s);
            // The links are spaced over the drawn loop, so it closes.
            drawn = s;
        }

        private float drawn;

        /// <summary>The point at a distance along the chain (in chain lengths; wraps round).</summary>
        public Vector2 At(float s) {
            if (points.Count == 0) return Vector2.zero;
            s = Mathf.Repeat(s / Mathf.Max(length, 1e-6f) * drawn, drawn);
            var lo = 0;
            var hi = points.Count - 1;
            while (lo < hi) {
                var mid = (lo + hi + 1) / 2;
                if (along[mid] <= s) lo = mid;
                else hi = mid - 1;
            }
            var a = points[lo];
            var b = points[(lo + 1) % points.Count];
            var span = along[lo + 1] - along[lo];
            return span > 1e-6f ? Vector2.Lerp(a, b, (s - along[lo]) / span) : a;
        }
    }

    // A chain (part "CHAN-<family>"): its loop in its own space (x/y = the chain's plane, z = its normal), the
    // links placed along it, and how far it has run (phase). It joins the sprockets it goes round (GroupManager, so
    // the build moves with it); GearTrain couples them. Kept in the part's saved state ("chain:family|ways|...").
    public class ChainLoop : ConnectingPart {
        public static readonly List<ChainLoop> All = new();

        public ChainFamily family = ChainFamily.Normal;
        public string style = "chain";
        public readonly List<ChainWrap> wraps = new();
        public float phase;
        public int fixedLinks;   // V2: kept by a tensioner (ChainTensioner): the chain's own links, not the loop's
        public ChainPath Path { get; private set; }
        public Vector3 Normal => transform.forward;

        private readonly List<Transform> links = new();
        private bool clockwise;
        private Material material;
        private bool connected;

        public override bool CanFormConnection => false;

        public override HoleDetector CreateHoleDetector() {
            // A chain doesn't find holes: an inactive detector keeps the connecting part happy.
            var go = new GameObject("No Holes");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            return go.AddComponent<HoleDetector>();
        }

        public override List<GameObject> GetAttachedParts() => Sprockets().Select(s => s.part).Where(p => p != null).ToList();

        private void Start() {
            All.Add(this);
            if (wraps.Count == 0) Load();
            Rebuild();
            StartCoroutine(ConnectSoon());
        }

        private new void OnDestroy() {
            All.Remove(this);
            if (band != null) Destroy(band);
            base.OnDestroy();
        }

        private IEnumerator ConnectSoon() {
            // A loaded build makes its sprockets first; their holes settle in a frame or two.
            yield return null;
            yield return null;
            Connect();
            if (fixedLinks > 0) Follow(false);   // V2: a tensioner settles round the chain it was made with
        }

        /// <summary>Joins the sprockets it goes round (so it moves with the build).</summary>
        public void Connect() {
            var found = Sprockets();
            if (found.Count == 0) return;
            parts = new GameObject[wraps.Count];
            foreach (var (part, wrap) in found) parts[wrap] = part;
            GroupManager.Remove(gameObject);
            GroupManager.FormConnection(this, found.Select(f => f.part).ToList());
            connected = true;
        }

        private GameObject[] parts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init() {
            StateSystem.OnChangeState += () => {
                foreach (var chain in All.ToList())
                    if (chain != null && !chain.gameObject.IsDeleted()) chain.Follow();
            };
        }

        /// <summary>A sprocket moved apart from the others (its shaft moved): the chain goes round it where it is now.</summary>
        public void Follow() => Follow(true);

        /// <summary>V2: reads where its sprockets are; a tensioner (an idler on an arm a band pulls) then turns until the
        /// chain is tight again. <paramref name="speak"/>: say when it can't (not while dragging).</summary>
        public bool Follow(bool speak) {
            if (parts == null || parts.Length != wraps.Count) return false;
            var changed = ReadCenters(0.01f);
            var tensioner = fixedLinks > 0 ? ChainTensioner.Of(this) : null;
            if (tensioner != null) {
                if (ChainTensioner.Settle(tensioner, out var problem) && ReadCenters(0.0005f)) changed = true;
                if (problem != null && speak) Interface.Interface.Say(problem, false);
            }
            if (!changed) return false;
            Save();
            Rebuild();
            if (speak && Path != null && !Path.Valid) Interface.Interface.Say("The chain can't go round its sprockets where they are now: " + Path.problem, false);
            return true;
        }

        private bool ReadCenters(float tolerance) {
            var changed = false;
            for (var i = 0; i < wraps.Count; i++) {
                if (parts[i] == null || parts[i].IsDeleted()) continue;
                var local = transform.InverseTransformPoint(SprocketFrame(parts[i]).center);
                var flat = new Vector2(local.x, local.y);
                if (Vector2.Distance(flat, wraps[i].center) < tolerance) continue;
                wraps[i].center = flat;
                changed = true;
            }
            return changed;
        }

        /// <summary>V2: a point in its plane's coordinates.</summary>
        public Vector2 ToFlat(Vector3 world) {
            var local = transform.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
        }

        /// <summary>V2: the parts it goes round, by wrap (null where one is missing).</summary>
        public GameObject PartOf(int wrap) => parts != null && wrap >= 0 && wrap < parts.Length ? parts[wrap] : null;

        // V2: a tensioned chain follows its sprockets live (a sprocket's arm dragged, a shaft moved): its tensioner
        // swings as it goes, not only once the move ends. Not while the tensioner itself is being dragged.
        private void LateUpdate() {
            if (fixedLinks <= 0 || parts == null || Sandbox.Running || Time.frameCount % 2 != 0) return;
            var tensioner = ChainTensioner.Of(this);
            if (tensioner == null || tensioner.BeingMoved()) return;
            var moved = false;
            for (var i = 0; i < wraps.Count && !moved; i++) {
                if (parts[i] == null) continue;
                var local = transform.InverseTransformPoint(SprocketFrame(parts[i]).center);
                if (Vector2.Distance(new Vector2(local.x, local.y), wraps[i].center) > 0.003f) moved = true;
            }
            if (moved) Follow(false);
        }

        // ------------------------------------------------------------------ data

        public string Token() {
            var c = CultureInfo.InvariantCulture;
            return family.key + "|" + phase.ToString("0.####", c) + "|" + string.Join("/", wraps.Select(w =>
                w.center.x.ToString("0.#####", c) + "," + w.center.y.ToString("0.#####", c) + "," + w.radius.ToString("0.#####", c) + "," + w.way + "," + w.teeth))
                + "|" + style + (fixedLinks > 0 ? "|" + fixedLinks : "");
        }

        public void Load(string token = null) {
            token ??= Dynamics.Token(gameObject, "chain");
            if (string.IsNullOrEmpty(token)) return;
            var parts = token.Split('|');
            if (parts.Length < 3) return;
            var c = CultureInfo.InvariantCulture;
            family = ChainFamily.Of(parts[0]);
            float.TryParse(parts[1], NumberStyles.Float, c, out phase);
            style = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : "chain";
            fixedLinks = parts.Length > 4 && int.TryParse(parts[4], out var kept) ? kept : 0;   // V2
            wraps.Clear();
            foreach (var entry in parts[2].Split('/')) {
                var v = entry.Split(',');
                if (v.Length < 5) continue;
                wraps.Add(new ChainWrap {
                    center = new Vector2(float.Parse(v[0], c), float.Parse(v[1], c)), radius = float.Parse(v[2], c),
                    way = int.Parse(v[3]) < 0 ? -1 : 1, teeth = int.Parse(v[4])
                });
            }
        }

        public void Save() => Dynamics.SetToken(gameObject, "chain", Token());

        /// <summary>Its wraps from sprockets in the world (the chain's frame: origin on the first, z = their axle).</summary>
        public void Set(ChainFamily chainFamily, IList<(Vector3 center, Vector3 axis, int teeth, int way)> world, string chainStyle = "chain") =>
            Set(chainFamily, world.Select(w => (w.center, w.axis, w.teeth, w.way, 0f)).ToList(), chainStyle);

        /// <summary>V2: with each wrap's radius when it's a smooth idler (teeth 0; its pitch line's radius).</summary>
        public void Set(ChainFamily chainFamily, IList<(Vector3 center, Vector3 axis, int teeth, int way, float radius)> world, string chainStyle = "chain") {
            family = chainFamily;
            style = chainFamily.HasTreads ? chainStyle : "chain";
            var normal = world[0].axis.normalized;
            var origin = world[0].center;
            var up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(normal, up));
            wraps.Clear();
            foreach (var (center, _, teeth, way, radius) in world) {
                var local = transform.InverseTransformPoint(center);
                wraps.Add(new ChainWrap { center = new Vector2(local.x, local.y), radius = teeth > 0 ? family.PitchRadius(teeth) : radius, way = way, teeth = teeth });
            }
            fixedLinks = 0;
            Save();
        }

        // Every sprocket in the build with its chain size, teeth and frame, kept until the build changes
        // (Dynamics.Version): finding a chain's sprockets is then a short list, not the whole build.
        private static readonly List<(GameObject part, ChainFamily family, int teeth, Vector3 center, Vector3 axis)> index = new();
        private static long indexVersion = -1;

        /// <summary>Every sprocket in the build (not deleted): its chain size, teeth, middle and axle.</summary>
        public static List<(GameObject part, ChainFamily family, int teeth, Vector3 center, Vector3 axis)> AllSprockets() {
            if (indexVersion == Dynamics.Version) return index;
            index.Clear();
            foreach (var saved in Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None)) {
                var go = saved.gameObject;
                if (go.IsDeleted()) continue;
                var chainFamily = ChainFamily.ForSprocket(go);
                if (chainFamily == null) continue;
                var (c, axis) = SprocketFrame(go);
                index.Add((go, chainFamily, GearTrain.Teeth(go, out _), c, axis));
            }
            indexVersion = Dynamics.Version;
            return index;
        }

        // V2: every smooth idler (spacer, washer) with its middle, axle, surface radius and length along the axle.
        private static readonly List<(GameObject part, Vector3 center, Vector3 axis, float radius, float length)> idlers = new();
        private static long idlerVersion = -1;

        public static List<(GameObject part, Vector3 center, Vector3 axis, float radius, float length)> AllIdlers() {
            if (idlerVersion == Dynamics.Version) return idlers;
            idlers.Clear();
            foreach (var saved in Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None)) {
                var go = saved.gameObject;
                if (go.IsDeleted() || !ChainFamily.IsIdler(go)) continue;
                var (c, axis, radius, length) = IdlerFrame(go);
                idlers.Add((go, c, axis, radius, length));
            }
            idlerVersion = Dynamics.Version;
            return idlers;
        }

        /// <summary>V2: a smooth idler's middle, axle, outside radius and length (from its mesh round its middle hole).</summary>
        public static (Vector3 center, Vector3 axis, float radius, float length) IdlerFrame(GameObject part) {
            var (center, axis, _) = SprocketTeeth(part);
            if (!part.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !Teeth.TryGetValue(filter.sharedMesh, out var teeth))
                return (center, axis, 0.15f, 0.25f);
            var t = part.transform;
            var radius = t.TransformVector(teeth.tip).magnitude;
            var size = Vector3.Scale(filter.sharedMesh.bounds.size, t.lossyScale);
            var localAxis = t.InverseTransformDirection(axis);
            var length = Mathf.Abs(size.x * localAxis.x) + Mathf.Abs(size.y * localAxis.y) + Mathf.Abs(size.z * localAxis.z);
            return (center, axis, radius, length);
        }

        /// <summary>The sprockets it goes round (found by place), each with its wrap's index.</summary>
        public List<(GameObject part, int wrap)> Sprockets() {
            var result = new List<(GameObject, int)>();
            var all = AllSprockets();
            for (var i = 0; i < wraps.Count; i++) {
                var center = transform.TransformPoint(wraps[i].center);
                GameObject best = null;
                var bestDistance = 0.08f;
                if (wraps[i].teeth <= 0) {   // V2: a smooth idler: a spacer there, as big as its wrap
                    foreach (var (part, c, axis, radius, length) in AllIdlers()) {
                        if (part == null || Mathf.Abs(Vector3.Dot(axis, Normal)) < 0.98f) continue;
                        if (Mathf.Abs(family.IdlerRadius(radius) - wraps[i].radius) > 0.03f) continue;
                        var offset = c - center;
                        if (Mathf.Abs(Vector3.Dot(offset, Normal)) > length / 2f + 0.1f) continue;
                        var inPlane = Vector3.ProjectOnPlane(offset, Normal).magnitude;
                        if (inPlane < bestDistance) {
                            bestDistance = inPlane;
                            best = part;
                        }
                    }
                    if (best != null) result.Add((best, i));
                    continue;
                }
                foreach (var (part, chainFamily, teeth, c, axis) in all) {
                    if (part == null || chainFamily != family || teeth != wraps[i].teeth) continue;
                    if (Mathf.Abs(Vector3.Dot(axis, Normal)) < 0.98f) continue;
                    var offset = c - center;
                    if (Mathf.Abs(Vector3.Dot(offset, Normal)) > 0.3f) continue;
                    var inPlane = Vector3.ProjectOnPlane(offset, Normal).magnitude;
                    if (inPlane < bestDistance) {
                        bestDistance = inPlane;
                        best = part;
                    }
                }
                if (best != null) result.Add((best, i));
            }
            return result;
        }

        private static readonly Dictionary<Mesh, (float along, Vector3 tip)> Teeth = new();

        /// <summary>A sprocket's middle (on its axle, in the plane of its teeth) and axle.</summary>
        public static (Vector3 center, Vector3 axis) SprocketFrame(GameObject sprocket) {
            var (center, axis, _) = SprocketTeeth(sprocket);
            return (center, axis);
        }

        /// <summary>Its middle, axle, and the way from the middle to one tooth's tip (the gaps are half a tooth on).</summary>
        public static (Vector3 center, Vector3 axis, Vector3 tip) SprocketTeeth(GameObject sprocket) {
            var middle = sprocket.TryGetComponent(out Renderer r) ? r.bounds.center : sprocket.transform.position;
            var hole = sprocket.GetComponentsInChildren<HoleCollider>(true).OrderBy(h => (h.transform.position - middle).sqrMagnitude).FirstOrDefault();
            if (hole == null) return (middle, sprocket.transform.forward, sprocket.transform.up);
            var axis = hole.transform.forward;
            var center = hole.transform.position + axis * Vector3.Dot(middle - hole.transform.position, axis);
            if (!sprocket.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return (center, axis, Vector3.ProjectOnPlane(sprocket.transform.up, axis).normalized);
            var mesh = filter.sharedMesh;
            var t = sprocket.transform;
            var localAxis = t.InverseTransformDirection(axis).normalized;
            var localHole = t.InverseTransformPoint(hole.transform.position);
            if (!Teeth.TryGetValue(mesh, out var teeth)) {
                // The teeth: the vertices farthest from the axle; their middle along it is the chain's plane.
                var vertices = mesh.vertices;
                var most = 0f;
                foreach (var v in vertices) most = Mathf.Max(most, Vector3.ProjectOnPlane(v - localHole, localAxis).sqrMagnitude);
                most = Mathf.Sqrt(most);
                float sum = 0f, best = 0f;
                var count = 0;
                var tip = Vector3.zero;
                foreach (var v in vertices) {
                    var radial = Vector3.ProjectOnPlane(v - localHole, localAxis);
                    var length = radial.magnitude;
                    if (length < most * 0.93f) continue;
                    sum += Vector3.Dot(v - localHole, localAxis);
                    count++;
                    if (length > best) {
                        best = length;
                        tip = radial;
                    }
                }
                teeth = (count > 0 ? sum / count : Vector3.Dot(t.InverseTransformPoint(middle) - localHole, localAxis), tip);
                Teeth[mesh] = teeth;
            }
            var plane = t.TransformPoint(localHole + localAxis * teeth.along);
            return (plane, axis, t.TransformDirection(teeth.tip).normalized);
        }

        /// <summary>Starts the chain with a pin in a gap between the first sprocket's teeth (as it goes on).</summary>
        public void SeatOn(GameObject sprocket) {
            if (Path == null || !Path.Valid || sprocket == null || wraps.Count == 0) return;
            var (_, _, tip) = SprocketTeeth(sprocket);
            var w = wraps[0];
            var local = transform.InverseTransformDirection(tip);
            var angle = Mathf.Atan2(local.y, local.x) + Mathf.PI / Mathf.Max(w.teeth, 3);
            var gap = w.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * w.radius;
            // The place along the chain nearest the gap.
            var bestS = 0f;
            var bestD = float.MaxValue;
            var steps = Mathf.Max(200, Path.links * 8);
            for (var k = 0; k < steps; k++) {
                var sAt = Path.length * k / steps;
                var d = (Path.At(sAt) - gap).sqrMagnitude;
                if (d < bestD) {
                    bestD = d;
                    bestS = sAt;
                }
            }
            var step = Path.length / Path.links;
            phase = Mathf.Repeat(bestS, step);
            Place(links, phase);
            Save();
        }

        // ------------------------------------------------------------------ the links

        private Vector2 LocalDown() {
            var d = transform.InverseTransformDirection(Vector3.down);
            return new Vector2(d.x, d.y);
        }

        public void Rebuild() {
            Path = ChainPath.Build(wraps, family.pitch, LocalDown(), fixedLinks);
            var count = Path.Valid ? Path.links : 0;
            var meshes = family.LinksFor(style);
            var linkWidth = family.WidthFor(style);
            // Which side is out of the loop: the links are turned so their outside (a tread's face) is there.
            var area = 0f;
            if (Path.Valid)
                for (var i = 0; i < Path.points.Count; i++) {
                    var a = Path.points[i];
                    var b = Path.points[(i + 1) % Path.points.Count];
                    area += a.x * b.y - b.x * a.y;
                }
            clockwise = area < 0f;
            if (TryGetComponent(out MeshRenderer root)) material = root.sharedMaterial;
            for (var i = links.Count - 1; i >= count; i--) {
                if (links[i] != null) Destroy(links[i].gameObject);
                links.RemoveAt(i);
            }
            while (links.Count < count) {
                var link = new GameObject("Link " + links.Count) { layer = gameObject.layer };
                link.transform.SetParent(transform, false);
                link.AddComponent<MeshFilter>();
                link.AddComponent<MeshRenderer>();
                links.Add(link.transform);
            }
            for (var i = 0; i < links.Count; i++) {
                links[i].GetComponent<MeshFilter>().sharedMesh = meshes[i % 2];
                links[i].GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            Band(linkWidth, family.pitch * 0.8f + 0.04f);
            if (TryGetComponent(out Parts_List.PartName partName)) {
                partName.name = family.name + " (" + count + " links)";
                partName.weightInGrams = count * family.gramsPerLink;
            }
            Place(links, phase);
        }

        private Mesh band;

        // V2: one collider round the loop to click the chain by (a flat band as wide and tall as the links), not a
        // box on every link: running a chain moves no colliders, and a build has hundreds fewer of them.
        private void Band(float width, float height) {
            if (!TryGetComponent(out MeshCollider collider)) collider = gameObject.AddComponent<MeshCollider>();
            if (Path == null || !Path.Valid || Path.points.Count < 3) {
                collider.enabled = false;
                return;
            }
            if (band == null) band = new Mesh { name = "Chain band" };
            band.Clear();
            var points = Path.points;
            var n = points.Count;
            var vertices = new List<Vector3>(n * 4);
            for (var i = 0; i < n; i++) {
                var tangent = (points[(i + 1) % n] - points[(i + n - 1) % n]).normalized;
                var side = new Vector2(-tangent.y, tangent.x) * (height / 2f);
                Vector2 outer = points[i] + side, inner = points[i] - side;
                vertices.Add(new Vector3(outer.x, outer.y, width / 2f));
                vertices.Add(new Vector3(outer.x, outer.y, -width / 2f));
                vertices.Add(new Vector3(inner.x, inner.y, -width / 2f));
                vertices.Add(new Vector3(inner.x, inner.y, width / 2f));
            }
            // Four faces round the loop, both ways (so a ray hits it from either side).
            var triangles = new List<int>(n * 48);
            for (var i = 0; i < n; i++) {
                var a = i * 4;
                var b = (i + 1) % n * 4;
                for (var k = 0; k < 4; k++) {
                    int a0 = a + k, a1 = a + (k + 1) % 4, b0 = b + k, b1 = b + (k + 1) % 4;
                    triangles.AddRange(new[] { a0, b0, b1, a0, b1, a1, a0, b1, b0, a0, a1, b1 });
                }
            }
            band.SetVertices(vertices);
            band.SetTriangles(triangles, 0);
            band.RecalculateBounds();
            collider.sharedMesh = null;
            collider.sharedMesh = band;
            collider.enabled = true;
        }

        /// <summary>Puts links (its own or a copy's) along the chain at a phase.</summary>
        public void Place(IList<Transform> targets, float at) => Place(targets, at, Path);

        /// <summary>V2: along another path of the same chain (the sandbox's, round where its sprockets are now).</summary>
        public void Place(IList<Transform> targets, float at, ChainPath path) {
            if (path == null || !path.Valid) return;
            var count = Mathf.Min(targets.Count, path.links);
            var step = path.length / Mathf.Max(path.links, 1);
            var next = path.At(at);
            for (var i = 0; i < count; i++) {
                var pin = next;
                next = path.At(at + (i + 1) * step);
                var dir = next - pin;
                var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                if (targets[i] == null) continue;
                targets[i].localPosition = new Vector3(pin.x, pin.y, 0f);
                targets[i].localRotation = Quaternion.Euler(0f, 0f, angle) * (clockwise ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity);
            }
        }

        /// <summary>Runs the chain along by a distance (inches; positive: the way its wraps go round).</summary>
        public void Advance(float distance) {
            if (Path == null || !Path.Valid || Mathf.Abs(distance) < 1e-7f) return;
            phase = Mathf.Repeat(phase + distance, Path.length);
            Place(links, phase);
        }

        public IReadOnlyList<Transform> Links => links;

        /// <summary>Inches of chain per radian a sprocket turns: one link per tooth (not quite its pitch radius).</summary>
        public float Rolling(int wrap) => wraps[wrap].teeth > 0 ? family.pitch * wraps[wrap].teeth / (2f * Mathf.PI) : wraps[wrap].radius;

        private void Update() {
            // Painted: the links take the part's color.
            if (TryGetComponent(out MeshRenderer root) && root.sharedMaterial != material) {
                material = root.sharedMaterial;
                foreach (var link in links)
                    if (link != null) link.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            if (!connected && Time.frameCount % 30 == 0) Connect();
        }

        /// <summary>For undo: how far it had run.</summary>
        public class PhaseElement : IElement {
            private readonly ChainLoop chain;
            private readonly float phase;

            public PhaseElement(ChainLoop chain) {
                this.chain = chain;
                phase = chain.phase;
            }

            public void Load() {
                if (chain == null) return;
                chain.phase = phase;
                chain.Place(chain.links, phase);
                chain.Save();
            }
        }

        /// <summary>For undo: its whole loop (editing a chain).</summary>
        public class LoopElement : IElement {
            private readonly ChainLoop chain;
            private readonly string token;

            public LoopElement(ChainLoop chain) {
                this.chain = chain;
                token = chain.Token();
            }

            public void Load() {
                if (chain == null) return;
                chain.Load(token);
                chain.Save();
                chain.Rebuild();
                chain.Connect();
            }
        }
    }

    // The chain part type ("CHAN-Normal", "CHAN-HS", "CHAN-6P"), made at start-up like the custom parts.
    public class ChainPartGenerator : PartGenerator {
        public static PartType CreateType() {
            var obj = new GameObject("Chain");
            obj.SetActive(false);
            Object.DontDestroyOnLoad(obj);
            var type = obj.AddComponent<PartType>();
            type.id = "CHAN";
            type.group = PartType.PartGroup.Motion;
            type.connectingPart = true;
            var generator = obj.AddComponent<ChainPartGenerator>();
            generator.param1 = new Parameter { name = "Chain", value = "Normal" };
            generator.param2 = new Parameter { name = "" };
            return type;
        }

        public override List<string> GetParam1Options() => ChainFamily.All.Select(f => f.key).ToList();
        public override List<string> GetParam2Options() => new();
        // Every chain's own mesh is empty (its links are its children): one shared.
        private static Mesh empty;
        private static Mesh Empty => empty != null ? empty : empty = new Mesh { name = "Chain (links are its children)" };

        public override Mesh GetMesh() => Empty;

        private static Material chainMaterial;

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var obj = new GameObject("Chain");
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.AddComponent<MeshFilter>().sharedMesh = Empty;
            if (chainMaterial == null) {
                var source = Interface.Interface.Instance != null ? Interface.Interface.Instance.plasticMaterial : null;
                chainMaterial = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                chainMaterial.name = "Chain";
                var dark = new Color(0.16f, 0.16f, 0.17f, 1f);
                chainMaterial.color = dark;
                if (chainMaterial.HasProperty("_BaseColor")) chainMaterial.SetColor("_BaseColor", dark);
            }
            obj.AddComponent<MeshRenderer>().sharedMaterial = new Material(chainMaterial);
            var name = obj.AddComponent<Parts_List.PartName>();
            name.name = "Chain";
            var chain = obj.AddComponent<ChainLoop>();
            chain.family = ChainFamily.Of(param1.value);
            SetId(obj);
            return obj;
        }
    }
}
