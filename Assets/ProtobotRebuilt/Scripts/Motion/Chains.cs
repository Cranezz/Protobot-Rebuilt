using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    // V2: chains. VEX has three: Normal (the 10T-48T sprockets, 0.145" pitch as VEX-CAD models them), high
    // strength (6T-30T and the tank tread drive wheel, 0.385") and 6P (8T-40T, 0.25"). A sprocket's pitch radius is
    // pitch / (2 sin(180° / teeth)). The high strength and 6P links are VEX-CAD's own models; the Normal link is
    // modelled here (a roller chain: rollers between alternating inner and outer plates).
    public class ChainFamily {
        public string key, name;
        public float pitch, width, gramsPerLink;

        public static readonly ChainFamily Normal = new() { key = "Normal", name = "Normal chain", pitch = 0.145f, width = 0.14f, gramsPerLink = 0.3f };
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

        // ------------------------------------------------------------------ the link (pins at x = 0 and x = pitch, along z)

        private Mesh[] meshes;
        private Material[] materials;

        /// <summary>The link models: [0] and [1] alternate (a roller chain's inner and outer links).</summary>
        public Mesh[] Links {
            get {
                if (meshes != null && meshes[0] != null) return meshes;
                meshes = this == Normal ? new[] { RollerLink(false), RollerLink(true) } : new[] { VexLink(), null };
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
            var path = this == SixP ? ("Part Prefabs/VEX/Motion Parts", "IQ Chain") : ("Part Prefabs/VEX/Chain 9P", "High Strength Chain Link");
            var family = Resources.Load<GameObject>(path.Item1);
            var child = family != null ? family.GetComponentsInChildren<PartData>(true).FirstOrDefault(d => d.name == path.Item2) : null;
            if (child == null || !child.TryGetComponent(out MeshFilter filter)) return RollerLink(false);
            materials = child.GetComponent<MeshRenderer>().sharedMaterials;
            var source = filter.sharedMesh;
            // Into the chain's frame: pins along z, the next pin at +x, centred on z = 0.
            Quaternion turn;
            Vector3 shift;
            if (this == SixP) {
                turn = Quaternion.AngleAxis(180f, new Vector3(1f, 0f, 1f).normalized);   // the model runs along z, pins along x
                shift = Vector3.zero;
            }
            else {
                turn = Quaternion.identity;
                shift = new Vector3(0f, 0f, -source.bounds.center.z);
            }
            var mesh = MeshAlign.Moved(source, turn, turn * shift);
            mesh.name = name + " link";
            return mesh;
        }

        // A roller chain link: a roller at x = 0 and two side plates to the next pin; outer links' plates sit wider.
        private Mesh RollerLink(bool outer) {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var height = pitch * 0.7f;
            var plate = width * 0.13f;
            var gap = width * 0.5f - plate * (outer ? 0.5f : 1.5f);
            void Stadium(float z0, float z1) {
                const int Steps = 10;
                var outline = new List<Vector2>();
                for (var i = 0; i <= Steps; i++) {
                    var a = Mathf.PI / 2f + Mathf.PI * i / Steps;
                    outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * height / 2f);
                }
                for (var i = 0; i <= Steps; i++) {
                    var a = -Mathf.PI / 2f + Mathf.PI * i / Steps;
                    outline.Add(new Vector2(pitch + Mathf.Cos(a) * height / 2f, Mathf.Sin(a) * height / 2f));
                }
                Prism(vertices, triangles, outline, z0, z1);
            }
            Stadium(gap, gap + plate);
            Stadium(-gap - plate, -gap);
            if (!outer) {
                var roller = new List<Vector2>();
                for (var i = 0; i < 12; i++) {
                    var a = Mathf.PI * 2f * i / 12f;
                    roller.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pitch * 0.29f);
                }
                Prism(vertices, triangles, roller, -gap, gap);
                var next = roller.Select(p => p + new Vector2(pitch, 0f)).ToList();
                Prism(vertices, triangles, next, -gap, gap);
            }
            var mesh = new Mesh { name = name + (outer ? " outer link" : " inner link") };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A convex outline extruded along z (flat-shaded sides).
        private static void Prism(List<Vector3> vertices, List<int> triangles, List<Vector2> outline, float z0, float z1) {
            var n = outline.Count;
            var center = outline.Aggregate(Vector2.zero, (s, p) => s + p) / n;
            foreach (var z in new[] { z0, z1 }) {
                var start = vertices.Count;
                vertices.Add(new Vector3(center.x, center.y, z));
                foreach (var p in outline) vertices.Add(new Vector3(p.x, p.y, z));
                for (var i = 0; i < n; i++) {
                    int a = start + 1 + i, b = start + 1 + (i + 1) % n;
                    if (z == z1) triangles.AddRange(new[] { start, a, b });
                    else triangles.AddRange(new[] { start, b, a });
                }
            }
            for (var i = 0; i < n; i++) {
                var p = outline[i];
                var q = outline[(i + 1) % n];
                var start = vertices.Count;
                vertices.Add(new Vector3(p.x, p.y, z0));
                vertices.Add(new Vector3(q.x, q.y, z0));
                vertices.Add(new Vector3(q.x, q.y, z1));
                vertices.Add(new Vector3(p.x, p.y, z1));
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
        }
    }

    /// <summary>One sprocket the chain goes round: its middle and pitch radius in the chain's plane, and the way
    /// round (1: counterclockwise about the chain's normal, -1: clockwise; the inside of a serpentine).</summary>
    public class ChainWrap {
        public Vector2 center;
        public float radius;
        public int way = 1;
        public int teeth;
    }

    // The chain's line in its plane: straight spans tangent to each pair of sprockets in turn and arcs round them,
    // in the given ways (so a chain can wrap a sprocket from inside the loop); the slack sags the longest span.
    public class ChainPath {
        public readonly List<Vector2> points = new();
        public readonly List<float> along = new();
        public float length;       // the chain's own length (links x pitch)
        public float tight;        // the length it would have pulled tight
        public int links;
        public string problem;
        public bool Valid => problem == null;

        public static ChainPath Build(IList<ChainWrap> wraps, float pitch, Vector2 down) {
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
                var loop = area < 0f ? -1 : 1;
                for (var i = 0; i < n; i++)
                    if (wraps[i].way != loop && Mathf.Abs(sweeps[i]) > Mathf.PI) {
                        path.problem = "Sprocket " + (i + 1) + " is outside the chain's path, so it can't press on it from inside: move it in toward the others, or wrap it from outside";
                        return path;
                    }
            }
            path.tight = tight;
            path.links = Mathf.Max(2, Mathf.CeilToInt(tight / pitch - 0.02f));
            path.length = path.links * pitch;
            var slack = path.length - tight;

            // The span the slack hangs in: the longest one, sagging the way down lies in the plane (else outward).
            var longest = 0;
            for (var i = 1; i < n; i++) if (spans[i] > spans[longest]) longest = i;
            for (var i = 0; i < n; i++) {
                var w = wraps[i];
                var from = Mathf.Atan2(inPoint[i].y - w.center.y, inPoint[i].x - w.center.x);
                var steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweeps[i]) / (Mathf.PI / 36f)));
                for (var k = 0; k <= steps; k++) {
                    var a = from + sweeps[i] * k / steps;
                    path.Add(w.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * w.radius);
                }
                var p = outPoint[i];
                var q = inPoint[(i + 1) % n];
                if (i == longest && slack > 1e-4f) {
                    var dir = (q - p).normalized;
                    var side = new Vector2(-dir.y, dir.x);
                    // Toward down, or away from the loop's middle when the chain lies flat.
                    var middle = wraps.Aggregate(Vector2.zero, (s, x) => s + x.center) / n;
                    var sideways = Vector2.Dot(side, down);
                    if (down.magnitude < 0.3f || Mathf.Abs(sideways) < 0.15f) sideways = Vector2.Dot(side, (p + q) / 2f - middle);
                    if (sideways < 0f) side = -side;
                    var sag = Mathf.Min(Mathf.Sqrt(3f * spans[i] * slack / 8f), spans[i] * 0.35f);
                    for (var k = 1; k < 24; k++) {
                        var t = k / 24f;
                        path.Add(Vector2.Lerp(p, q, t) + side * (4f * sag * t * (1f - t)));
                    }
                }
            }
            path.Close();
            return path;
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
            // The sag's arc length is close to, not exactly, the slack: spacing the links by the drawn length makes
            // the loop close.
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
            base.OnDestroy();
        }

        private IEnumerator ConnectSoon() {
            // A loaded build makes its sprockets first; their holes settle in a frame or two.
            yield return null;
            yield return null;
            Connect();
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
        public void Follow() {
            if (parts == null || parts.Length != wraps.Count) return;
            var changed = false;
            for (var i = 0; i < wraps.Count; i++) {
                if (parts[i] == null || parts[i].IsDeleted()) continue;
                var local = transform.InverseTransformPoint(SprocketFrame(parts[i]).center);
                var flat = new Vector2(local.x, local.y);
                if (Vector2.Distance(flat, wraps[i].center) < 0.01f) continue;
                wraps[i].center = flat;
                changed = true;
            }
            if (!changed) return;
            Save();
            Rebuild();
            if (Path != null && !Path.Valid) Interface.Interface.Say("The chain can't go round its sprockets where they are now: " + Path.problem, false);
        }

        // ------------------------------------------------------------------ data

        public string Token() {
            var c = CultureInfo.InvariantCulture;
            return family.key + "|" + phase.ToString("0.####", c) + "|" + string.Join("/", wraps.Select(w =>
                w.center.x.ToString("0.#####", c) + "," + w.center.y.ToString("0.#####", c) + "," + w.radius.ToString("0.#####", c) + "," + w.way + "," + w.teeth))
                + "|" + style;
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
        public void Set(ChainFamily chainFamily, IList<(Vector3 center, Vector3 axis, int teeth, int way)> world, string chainStyle = "chain") {
            family = chainFamily;
            style = chainFamily.HasTreads ? chainStyle : "chain";
            var normal = world[0].axis.normalized;
            var origin = world[0].center;
            var up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(normal, up));
            wraps.Clear();
            foreach (var (center, _, teeth, way) in world) {
                var local = transform.InverseTransformPoint(center);
                wraps.Add(new ChainWrap { center = new Vector2(local.x, local.y), radius = family.PitchRadius(teeth), way = way, teeth = teeth });
            }
            Save();
        }

        /// <summary>The sprockets it goes round (found by place), each with its wrap's index.</summary>
        public List<(GameObject part, int wrap)> Sprockets() {
            var result = new List<(GameObject, int)>();
            var all = Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None);
            for (var i = 0; i < wraps.Count; i++) {
                var center = transform.TransformPoint(wraps[i].center);
                GameObject best = null;
                var bestDistance = 0.08f;
                foreach (var saved in all) {
                    if (saved.gameObject.IsDeleted() || saved.gameObject == gameObject || ChainFamily.ForSprocket(saved.gameObject) != family) continue;
                    if (GearTrain.Teeth(saved.gameObject, out _) != wraps[i].teeth) continue;
                    var (c, axis) = SprocketFrame(saved.gameObject);
                    if (Mathf.Abs(Vector3.Dot(axis, Normal)) < 0.98f) continue;
                    var offset = c - center;
                    if (Mathf.Abs(Vector3.Dot(offset, Normal)) > 0.3f) continue;
                    var inPlane = Vector3.ProjectOnPlane(offset, Normal).magnitude;
                    if (inPlane < bestDistance) {
                        bestDistance = inPlane;
                        best = saved.gameObject;
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
            Path = ChainPath.Build(wraps, family.pitch, LocalDown());
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
                var box = link.AddComponent<BoxCollider>();
                link.AddComponent<SelectAs>().target = gameObject;
                links.Add(link.transform);
            }
            for (var i = 0; i < links.Count; i++) {
                var mesh = meshes[i % 2];
                links[i].GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = links[i].GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var box = links[i].GetComponent<BoxCollider>();
                box.center = new Vector3(family.pitch / 2f, 0f, 0f);
                box.size = new Vector3(family.pitch * 1.2f, family.pitch * 0.8f + 0.04f, linkWidth);
            }
            if (TryGetComponent(out Parts_List.PartName partName)) {
                partName.name = family.name + " (" + count + " links)";
                partName.weightInGrams = count * family.gramsPerLink;
            }
            Place(links, phase);
        }

        /// <summary>Puts links (its own or a copy's) along the chain at a phase.</summary>
        public void Place(IList<Transform> targets, float at) {
            if (Path == null || !Path.Valid) return;
            var count = Mathf.Min(targets.Count, Path.links);
            var step = Path.length / Mathf.Max(Path.links, 1);
            var next = Path.At(at);
            for (var i = 0; i < count; i++) {
                var pin = next;
                next = Path.At(at + (i + 1) * step);
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
        public float Rolling(int wrap) => family.pitch * wraps[wrap].teeth / (2f * Mathf.PI);

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
        public override Mesh GetMesh() => new Mesh();

        private static Material chainMaterial;

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var obj = new GameObject("Chain");
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = "Chain (links are its children)" };
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
