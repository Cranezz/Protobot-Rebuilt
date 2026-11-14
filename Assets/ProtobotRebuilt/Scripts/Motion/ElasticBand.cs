using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    // V2: rubber bands and latex tubing as teams use them: a loop wrapped round two or more posts (standoffs, screws,
    // shafts, spacers, collars, wheels, through a hole round a channel's edge, or round any part's edge), each from
    // outside the loop or pressed in from inside (a band routed round a standoff used as a pulley). Several bands side
    // by side and a band wrapped 1 to 4 times are the usual ways to make it stronger. The loop lies in the plane that
    // fits its posts (square to them), hugs each post and follows the parts it's on.
    //
    // How hard it pulls comes from the band's own size and rubber (the sizes VEX and RoboSource sell; neither gives
    // forces): stretch ratio λ = loop length x wraps / its natural loop (twice its flat length), each strand pulls
    // E/3 (λ − 1/λ²) x its cross-section (rubber's neo-Hookean curve; E is the rubber's stiffness at small stretch), and
    // every span between posts carries bands x wraps strands. A VEX #64 at twice its length pulls about 1 lbf a
    // strand, 2 lbf between two posts, which is what teams measure. Saved as "loop:kind|bands|wraps|rest|normal|posts".
    public class ElasticKind {
        public string key, name, shortName, source, material;
        public bool tubing;
        public float flat;            // a band's flat length (half its loop), inches
        public float width, thick;    // band: width x thickness; tubing: outside x inside diameter
        public float modulus;         // the rubber's stiffness at small stretch (Young's modulus), psi
        public float maxStretch;      // past this it wears out or snaps (tubing: VEX says 3x and it still springs back)
        public float density;         // g/cm³
        public Color color;

        public float Area => tubing ? Mathf.PI / 4f * (width * width - thick * thick) : width * thick;   // one strand, in²
        public float NaturalLoop => 2f * flat;
        public float DrawWidth => tubing ? width : Mathf.Max(width, 0.06f);
        public float DrawThick => tubing ? width : Mathf.Max(thick, 0.035f);

        /// <summary>The pull in one strand (lbf) at a stretch ratio (stretched / natural length).</summary>
        public float Tension(float stretch) => stretch <= 1f ? 0f : modulus / 3f * (stretch - 1f / (stretch * stretch)) * Area;

        public string Size => tubing ? Inch.Format(width) + " OD x " + Inch.Format(thick) + " ID" : Inch.Format(flat) + " x " + Inch.Format(width) + " x " + Inch.Format(thick);

        private static ElasticKind Band(string key, string shortName, string source, string material, float flat, float width, float thick, float modulus, float maxStretch, float density, Color color) =>
            new() { key = key, name = shortName + " (" + source + ")", shortName = shortName, source = source, material = material, flat = flat, width = width, thick = thick,
                    modulus = modulus, maxStretch = maxStretch, density = density, color = color };

        // Colors are about half the real ones (the app's light doubles them).
        private static readonly Color Orange = new(0.47f, 0.15f, 0.04f), LightOrange = new(0.5f, 0.27f, 0.11f), Black = new(0.04f, 0.04f, 0.045f),
                                      Natural = new(0.42f, 0.31f, 0.19f), Sterling = new(0.40f, 0.27f, 0.16f), Amber = new(0.45f, 0.28f, 0.1f);

        public static readonly ElasticKind[] All = {
            // VEX (sizes from its store page: length x width x thickness)
            Band("V32", "#32 synthetic", "VEX 275-1089", "synthetic rubber", 3.125f, 0.125f, 1f / 32f, 232f, 5f, 1.1f, Orange),
            Band("V64", "#64 synthetic", "VEX 276-3990", "synthetic rubber", 3.5f, 0.25f, 1f / 32f, 232f, 5f, 1.1f, Orange),
            Band("S32", "#32 silicone", "VEX 228-6633", "silicone (grips plastic)", 3.75f, 0.125f, 0.05f, 200f, 4f, 1.15f, LightOrange),
            Band("S64", "#64 silicone", "VEX 228-6634", "silicone (grips plastic)", 3.75f, 0.25f, 0.05f, 200f, 4f, 1.15f, LightOrange),
            Band("E32", "#32 EPDM", "VEX 276-8076", "EPDM rubber", 3.75f, 0.25f, 1f / 32f, 261f, 5f, 1.1f, Black),
            Band("E64", "#64 EPDM", "VEX 276-8077", "EPDM rubber", 3.5f, 0.25f, 1f / 32f, 261f, 5f, 1.1f, Black),
            // RoboSource (natural rubber: Soft Stretch, Sterling is firmer, Black Platinum the strongest)
            Band("RS32", "#32 Soft Stretch", "RoboSource", "natural rubber, soft stretch", 3f, 0.125f, 1f / 32f, 190f, 6f, 0.95f, Natural),
            Band("RS64", "#64 Soft Stretch", "RoboSource", "natural rubber, soft stretch", 3.5f, 0.25f, 1f / 32f, 190f, 6f, 0.95f, Natural),
            Band("RS117", "#117B Soft Stretch", "RoboSource", "natural rubber, soft stretch", 7f, 0.125f, 1f / 16f, 190f, 6f, 0.95f, Natural),
            Band("RT32", "#32 Sterling", "RoboSource", "natural rubber, firmer stretch", 3f, 0.125f, 1f / 32f, 245f, 6f, 0.95f, Sterling),
            Band("RT64", "#64 Sterling", "RoboSource", "natural rubber, firmer stretch", 3.5f, 0.25f, 1f / 32f, 245f, 6f, 0.95f, Sterling),
            Band("RT117", "#117B Sterling", "RoboSource", "natural rubber, firmer stretch", 7f, 0.125f, 1f / 16f, 245f, 6f, 0.95f, Sterling),
            Band("BP64", "#64 Black Platinum", "RoboSource", "95% rubber, the strongest", 3.5f, 0.25f, 1f / 32f, 290f, 6f, 0.95f, Black),
            // VEX latex tubing: 1/8" OD, 1/16" ID, 35A latex (about 1.4 MPa)
            new() { key = "LT", name = "Latex tubing (VEX 275-1262)", shortName = "latex tubing", source = "VEX 275-1262", material = "35A latex", tubing = true,
                    width = 0.125f, thick = 0.0625f, modulus = 203f, maxStretch = 3f, density = 0.95f, color = Amber },
        };

        // Kinds in builds made before the loops (two-point elastic).
        private static readonly Dictionary<string, string> Old = new() { { "B32", "V32" }, { "B64", "V64" }, { "B117", "RS117" }, { "TUBE", "LT" } };

        public static ElasticKind Of(string key) {
            if (key != null && Old.TryGetValue(key, out var now)) key = now;
            return All.FirstOrDefault(k => k.key == key) ?? All[1];
        }

        public static IEnumerable<string> Keys => All.Select(k => k.key).Concat(Old.Keys);
    }

    /// <summary>One thing a band goes round: a post (a line through the part, a radius) or a point on its edge.</summary>
    public class BandPost {
        public GameObject part;
        public Vector3 local;       // its middle (on its axis) or the point, in the part's space (world when no part)
        public Vector3 axisLocal;   // its axis in the part's space; zero for a point
        public float radius;
        public bool inside;         // pressed in from inside the loop
        public bool hole;           // the band goes through a hole (round the metal beside it)
        public string what;

        public Vector3 Center => part != null ? part.transform.TransformPoint(local) : local;
        public Vector3 Axis => axisLocal.sqrMagnitude < 1e-8f ? Vector3.zero : (part != null ? part.transform.TransformDirection(axisLocal) : axisLocal).normalized;
        public bool HasAxis => axisLocal.sqrMagnitude > 1e-8f;

        public BandPost Copy() => (BandPost)MemberwiseClone();
    }

    /// <summary>The loop round its posts, in world space.</summary>
    public class BandShape {
        public List<Vector3> line = new();
        public readonly List<(Vector3 from, Vector3 to)> spans = new();   // the straight run from post i to post i + 1
        public float length;
        public Vector3 normal;
        public string problem;
        public bool Valid => problem == null && line.Count > 2;

        /// <summary>The loop round posts (world centers, axes, radii, sides). halfThick: the band's centreline
        /// stands off the post by half its thickness.</summary>
        public static BandShape Of(IList<(Vector3 center, Vector3 axis, float radius, bool inside, bool hole)> posts, Vector3 fallback, float halfThick) {
            var shape = new BandShape();
            if (posts.Count < 2) {
                shape.problem = "Pick at least two posts";
                return shape;
            }
            // The plane: square to the posts' axes, through their middles, along any holes it goes through.
            var guess = Vector3.zero;
            Vector3? first = null;
            foreach (var p in posts) {
                if (p.hole || p.axis == Vector3.zero) continue;
                first ??= p.axis;
                guess += Vector3.Dot(p.axis, first.Value) < 0f ? -p.axis : p.axis;
            }
            if (guess.sqrMagnitude < 1e-6f) guess = fallback.sqrMagnitude > 1e-6f ? fallback : Vector3.up;
            guess.Normalize();
            // Holes only turn the band (it goes through them on edge) when there are no posts to lie square to:
            // round a standoff it lies flat and passes through the hole where it is.
            var holesTurn = first == null;
            foreach (var p in posts)
                if (holesTurn && p.hole && p.axis != Vector3.zero) {
                    var square = Vector3.ProjectOnPlane(guess, p.axis);
                    if (square.sqrMagnitude > 1e-4f) guess = square.normalized;
                }
            var middle = Vector3.zero;
            foreach (var p in posts) middle += p.center;
            middle /= posts.Count;
            var m = Matrix4x4.zero;
            var spread = 0.01f;
            foreach (var p in posts) {
                var d = p.center - middle;
                m = Strands.Add(m, Strands.Outer(d, d));
                spread += d.sqrMagnitude;
            }
            // Leaning toward the posts' axes (a 2-post band can turn any way about the line between them).
            var k = 0.02f * spread;
            m = Strands.Add(m, Strands.Add(Strands.Outer(Vector3.right, Vector3.right, k), Strands.Add(Strands.Outer(Vector3.up, Vector3.up, k), Strands.Outer(Vector3.forward, Vector3.forward, k))));
            m = Strands.Add(m, Strands.Outer(guess, guess, -k));
            foreach (var p in posts)
                if (holesTurn && p.hole && p.axis != Vector3.zero) m = Strands.Add(m, Strands.Outer(p.axis, p.axis, spread));
            var normal = Strands.SmallestDirection(m, guess);
            shape.normal = normal;
            var offset = Vector3.Dot(middle, normal);
            var flat = new List<Vector3>();
            foreach (var p in posts) {
                var along = p.hole ? 0f : Vector3.Dot(p.axis, normal);
                flat.Add(Mathf.Abs(along) > 0.3f ? p.center + p.axis * ((offset - Vector3.Dot(p.center, normal)) / along)
                                                 : p.center - normal * (Vector3.Dot(p.center, normal) - offset));
            }
            var u = Vector3.ProjectOnPlane(flat[1] - flat[0], normal);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.right, normal);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.forward, normal);
            u.Normalize();
            var v = Vector3.Cross(normal, u);
            var origin = flat[0];
            Vector2 To2(Vector3 w) => new(Vector3.Dot(w - origin, u), Vector3.Dot(w - origin, v));
            var centers = flat.Select(To2).ToList();
            var area = 0f;
            for (var i = 0; i < centers.Count; i++) {
                var a = centers[i];
                var b = centers[(i + 1) % centers.Count];
                area += a.x * b.y - b.x * a.y;
            }
            var loop = centers.Count > 2 && area < 0f ? -1 : 1;
            var wraps = new List<ChainWrap>();
            for (var i = 0; i < posts.Count; i++)
                wraps.Add(new ChainWrap { center = centers[i], radius = Mathf.Max(0f, posts[i].radius) + halfThick, way = loop * (posts[i].inside ? -1 : 1) });
            var path = ChainPath.Build(wraps, 0.01f, Vector2.down);
            if (!path.Valid) {
                shape.problem = Words(path.problem);
                return shape;
            }
            foreach (var p in path.points) shape.line.Add(origin + u * p.x + v * p.y);
            for (var i = 0; i < posts.Count; i++)
                shape.spans.Add((origin + u * path.spanFrom[i].x + v * path.spanFrom[i].y, origin + u * path.spanTo[i].x + v * path.spanTo[i].y));
            shape.length = path.tight;
            return shape;
        }

        // The chain's wording, for a band.
        private static string Words(string chainProblem) =>
            (chainProblem ?? "").Replace("sprockets", "posts").Replace("Sprockets", "Posts").Replace("sprocket", "post").Replace("Sprocket", "Post")
                                .Replace("a real chain can't", "it can't").Replace("chain", "band").Replace(" (use gears to turn one the other way)", "");
    }

    public class ElasticBand : MonoBehaviour {
        public ElasticKind kind;
        public readonly List<BandPost> posts = new();
        public int bands = 1, wraps = 1;
        public float rest;               // tubing: its natural loop length (as cut); bands: by their size
        private Vector3 fallbackNormal = Vector3.up;

        public static readonly List<ElasticBand> All = new();
        public BandShape Shape { get; private set; }

        private Mesh mesh, hitMesh;
        private MeshCollider hit;
        private string parsedToken, shownKey;
        private long seenVersion = -1;

        private void Awake() {
            All.Add(this);
            mesh = new Mesh { name = "Band" };
            hitMesh = new Mesh { name = "Band (clicks)" };
            GetComponent<MeshFilter>().sharedMesh = mesh;
            // A copy (duplicate) brings its child; give it its own mesh.
            var child = transform.Find("Strands");
            if (child == null) {
                child = new GameObject("Strands") { layer = gameObject.layer }.transform;
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

        /// <summary>V2: another band (or tubing): its part id and color go with it.</summary>
        public void UseKind(ElasticKind k) {
            if (k == null) return;
            var changed = kind != k;
            kind = k;
            var saved = GetComponent<SavedObject>();
            if (saved != null && saved.id != "BAND-" + k.key) saved.id = "BAND-" + k.key;
            if (changed && TryGetComponent(out MeshRenderer r))
                r.sharedMaterial = new Material(ElasticPartGenerator.MaterialFor("Band " + k.key, k.color, k.tubing ? 0.45f : 0.25f));
        }

        // ------------------------------------------------------------------ the numbers

        public float Natural => kind.tubing ? Mathf.Max(0.5f, rest) : kind.NaturalLoop;
        public float Stretch(float loopLength) => loopLength * wraps / Natural;

        /// <summary>The pull along each span between posts (lbf): every strand there.</summary>
        public float SpanPull(float loopLength) => bands * wraps * kind.Tension(Stretch(loopLength));

        public float Grams => bands * (kind.tubing ? kind.Area * Natural : kind.Area * kind.NaturalLoop) * 16.387f * kind.density;

        public string Describe() {
            if (Shape == null || !Shape.Valid) return Shape?.problem ?? "";
            return Describe(kind, Shape.length, bands, wraps, Natural, posts.Count);
        }

        public static string Describe(ElasticKind kind, float loopLength, int bands, int wraps, float natural, int postCount) {
            var stretch = loopLength * wraps / natural;
            var pull = bands * wraps * kind.Tension(stretch);
            var text = stretch <= 1f ? "slack (loop " + Inch.Format(Mathf.Round(loopLength * 8f) / 8f) + ", it's " + Inch.Format(Mathf.Round(natural / wraps * 8f) / 8f) + " unstretched)"
                : "stretched " + stretch.ToString("0.0", CultureInfo.InvariantCulture) + "x · " + (postCount == 2 ? "pulls " + (2f * pull).ToString("0.0", CultureInfo.InvariantCulture) + " lbf"
                                                                                                             : pull.ToString("0.0", CultureInfo.InvariantCulture) + " lbf along each side");
            if (stretch > kind.maxStretch) text += " · over-stretched: it can snap (keep it under " + kind.maxStretch.ToString("0", CultureInfo.InvariantCulture) + "x)";
            return text;
        }

        // ------------------------------------------------------------------ saving

        private static string F(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);

        /// <summary>Its token: posts in world space, relative to the band's position.</summary>
        public string Token() {
            var origin = transform.position;
            var list = posts.Select(p => V(p.Center - origin) + "," + V(p.Axis) + "," + F(p.radius) + "," + ((p.inside ? 1 : 0) | (p.hole ? 2 : 0)));
            return kind.key + "|" + bands + "|" + wraps + "|" + F(rest) + "|" + V(fallbackNormal) + "|" + string.Join("/", list);
        }

        private void Save() {
            var token = Token();
            Dynamics.SetToken(gameObject, "loop", token);
            Dynamics.SetToken(gameObject, "band", null);
            parsedToken = token;
            if (TryGetComponent(out Parts_List.PartName partName)) partName.weightInGrams = Grams;
        }

        /// <summary>Sets it up round these posts (world), then saves it.</summary>
        public void Set(IList<BandPost> newPosts, int bandCount, int wrapCount, float natural, Vector3 normal) {
            posts.Clear();
            foreach (var p in newPosts) posts.Add(p.Copy());
            bands = Mathf.Clamp(bandCount, 1, 12);
            wraps = Mathf.Clamp(wrapCount, 1, 4);
            rest = natural;
            if (normal.sqrMagnitude > 1e-6f) fallbackNormal = normal.normalized;
            if (posts.Count > 0) transform.SetPositionAndRotation(posts[0].Center, Quaternion.identity);
            Save();
            Refresh(true);
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

        // Reads its token (made, loaded or put back by undo): the posts' parts are found where they are.
        private void Load(string token) {
            parsedToken = token;
            posts.Clear();
            var origin = transform.position;
            var fields = token.Split('|');
            if (fields.Length >= 6) {
                UseKind(ElasticKind.Of(fields[0]));
                int.TryParse(fields[1], out bands);
                int.TryParse(fields[2], out wraps);
                float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out rest);
                if (Parse3(fields[4].Split(','), 0, out var n) && n.sqrMagnitude > 1e-6f) fallbackNormal = n.normalized;
                foreach (var entry in fields[5].Split('/')) {
                    var f = entry.Split(',');
                    if (f.Length < 8 || !Parse3(f, 0, out var c) || !Parse3(f, 3, out var axis)) continue;
                    float.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var r);
                    int.TryParse(f[7], out var flags);
                    posts.Add(Attach(origin + c, axis, r, (flags & 1) != 0, (flags & 2) != 0));
                }
            }
            bands = Mathf.Clamp(bands, 1, 12);
            wraps = Mathf.Clamp(wraps, 1, 4);
        }

        // Builds made before the loops: "band:<end B from its start>|loops|rest", a band between two points.
        private void LoadOld(string token) {
            var fields = token.Split('|');
            var start = transform.position;
            var end = start + Vector3.forward;
            if (Parse3(fields[0].Split(','), 0, out var e)) end = start + e;
            wraps = fields.Length > 1 && int.TryParse(fields[1], out var loops) ? Mathf.Clamp(loops, 1, 4) : 1;
            rest = fields.Length > 2 && float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r * 2f : 0f;
            posts.Clear();
            posts.Add(Attach(start, Vector3.zero, 0.1f, false, false));
            posts.Add(Attach(end, Vector3.zero, 0.1f, false, false));
            Save();
        }

        private BandPost Attach(Vector3 center, Vector3 axis, float radius, bool inside, bool hole) {
            var part = Strands.PartAt(center, Mathf.Max(0.25f, radius + 0.15f), p => p == gameObject || p.GetComponent<ElasticBand>() != null || p.GetComponent<PneumaticTube>() != null);
            return new BandPost {
                part = part,
                local = part != null ? part.transform.InverseTransformPoint(center) : center,
                axisLocal = axis.sqrMagnitude < 1e-8f ? Vector3.zero : part != null ? part.transform.InverseTransformDirection(axis) : axis,
                radius = radius, inside = inside, hole = hole,
            };
        }

        // ------------------------------------------------------------------ following its parts

        private Vector3[] lastCenters = new Vector3[0];

        private void LateUpdate() {
            if (kind == null) return;
            var token = Dynamics.Token(gameObject, "loop");
            if (token == null) {
                var old = Dynamics.Token(gameObject, "band");
                if (old != null) LoadOld(old);
                else return;
            }
            else if (token != parsedToken) {
                Load(token);
                shownKey = null;
            }
            // A part it was on that's gone (deleted, undone): look again where it was.
            if (seenVersion != Dynamics.Version) {
                seenVersion = Dynamics.Version;
                for (var i = 0; i < posts.Count; i++)
                    if (posts[i].part != null && posts[i].part.IsDeleted()) posts[i] = Attach(posts[i].Center, posts[i].Axis, posts[i].radius, posts[i].inside, posts[i].hole);
            }
            Refresh(false);
        }

        public void Refresh(bool force) {
            if (posts.Count == 0 || kind == null) return;
            var running = Sandbox.Running;
            var centers = new Vector3[posts.Count];
            var moved = force || centers.Length != lastCenters.Length;
            for (var i = 0; i < posts.Count; i++) {
                var c = posts[i].Center;
                centers[i] = running && posts[i].part != null ? Sandbox.SimPoint(posts[i].part, c) : c;
                if (!moved && (centers[i] - lastCenters[i]).sqrMagnitude > 1e-9f) moved = true;
            }
            var key = bands + "|" + wraps + "|" + kind.key + "|" + string.Join(";", posts.Select(p => p.inside ? "i" : "o"));
            if (!moved && key == shownKey) return;
            lastCenters = centers;
            shownKey = key;
            // The build moved it (not the simulation): its own place and token follow.
            if (!running && posts[0].part != null && (transform.position - centers[0]).sqrMagnitude > 1e-9f) {
                transform.position = centers[0];
                Save();
            }
            var list = new List<(Vector3, Vector3, float, bool, bool)>();
            for (var i = 0; i < posts.Count; i++) {
                var axis = posts[i].Axis;
                if (running && posts[i].part != null && axis != Vector3.zero) axis = Sandbox.SimDirection(posts[i].part, axis);
                list.Add((centers[i], axis, posts[i].radius, posts[i].inside, posts[i].hole));
            }
            Shape = BandShape.Of(list, fallbackNormal, kind.DrawThick / 2f);
            Draw();
        }

        // The strands: bands side by side along the posts, each wrapped round as many times.
        private void Draw() {
            mesh.Clear();
            hitMesh.Clear();
            if (Shape == null || !Shape.Valid) {
                hit.sharedMesh = null;
                return;
            }
            var origin = transform.position;
            BuildMesh(mesh, Shape, kind, bands, wraps, origin);
            var strands = bands * wraps;
            var spacing = kind.DrawWidth * 1.12f;
            // What clicks hit: one fat tube round the loop.
            var hv = new List<Vector3>();
            var hn = new List<Vector3>();
            var ht = new List<int>();
            Strands.Tube(Shape.line.Select(p => p - origin).ToList(), true, Mathf.Max(0.08f, strands * spacing / 2f), 6, hv, hn, ht, Shape.normal);
            hitMesh.SetVertices(hv);
            hitMesh.SetNormals(hn);
            hitMesh.SetTriangles(ht, 0);
            hitMesh.RecalculateBounds();
            hit.sharedMesh = null;
            hit.sharedMesh = hitMesh;
        }

        /// <summary>V2: the strands' mesh round a loop (relative to origin): bands side by side along the posts, each
        /// wrapped round as many times. The band's own drawing and the tools' previews use it.</summary>
        public static void BuildMesh(Mesh mesh, BandShape shape, ElasticKind kind, int bands, int wraps, Vector3 origin) {
            mesh.Clear();
            if (shape == null || !shape.Valid) return;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            var strands = bands * wraps;
            var spacing = kind.DrawWidth * 1.12f;
            for (var s = 0; s < strands; s++) {
                var shift = shape.normal * ((s - (strands - 1) / 2f) * spacing);
                // Each wrap round a band sits a little further out than the one under it.
                var line = shape.line.Select(p => p - origin + shift).ToList();
                if (kind.tubing) Strands.Tube(line, true, kind.width / 2f, 10, vertices, normals, triangles, shape.normal);
                else Strands.Ribbon(line, shape.normal, kind.DrawWidth, kind.DrawThick, vertices, normals, triangles);
            }
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ undo

        public class LoopElement : IElement {
            private readonly ElasticBand band;
            private readonly string token;
            private readonly Vector3 position;

            public LoopElement(ElasticBand band) {
                this.band = band;
                token = Dynamics.Token(band.gameObject, "loop");
                position = band.transform.position;
            }

            public void Load() {
                if (band == null) return;
                band.transform.position = position;
                Dynamics.SetToken(band.gameObject, "loop", token);
            }
        }
    }

    // The part types: "BAND-<kind>" (rubber bands and latex tubing), made at start-up like the chains.
    public class ElasticPartGenerator : PartGenerator {
        public static PartType CreateType() {
            var obj = new GameObject("Elastic");
            obj.SetActive(false);
            Object.DontDestroyOnLoad(obj);
            var type = obj.AddComponent<PartType>();
            type.id = "BAND";
            type.group = PartType.PartGroup.Motion;
            var generator = obj.AddComponent<ElasticPartGenerator>();
            generator.param1 = new Parameter { name = "Elastic", value = "V64" };
            generator.param2 = new Parameter { name = "" };
            return type;
        }

        public override List<string> GetParam1Options() => ElasticKind.Keys.ToList();
        public override List<string> GetParam2Options() => new();

        private static Mesh empty;
        private static Mesh Empty => empty != null ? empty : empty = new Mesh { name = "Elastic (made round its posts)" };
        public override Mesh GetMesh() => Empty;

        private static readonly Dictionary<string, Material> materials = new();

        public static Material MaterialFor(string key, Color color, float smoothness) {
            if (materials.TryGetValue(key, out var material) && material != null) return material;
            var source = Interface.Interface.Instance != null ? Interface.Interface.Instance.plasticMaterial : null;
            material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = key;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return materials[key] = material;
        }

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var kind = ElasticKind.Of(param1.value);
            var obj = new GameObject("Elastic");
            obj.SetActive(false);
            obj.transform.SetPositionAndRotation(position, Quaternion.identity);
            obj.AddComponent<MeshFilter>().sharedMesh = Empty;
            obj.AddComponent<MeshRenderer>().sharedMaterial = new Material(MaterialFor("Band " + kind.key, kind.color, kind.tubing ? 0.45f : 0.25f));
            var name = obj.AddComponent<Parts_List.PartName>();
            name.name = kind.tubing ? "Latex Tubing (1/8\")" : "Rubber Band " + kind.shortName + " (" + kind.source + ")";
            name.weightInGrams = 0.5f;
            obj.AddComponent<ElasticBand>().kind = kind;
            obj.SetActive(true);
            SetId(obj);
            return obj;
        }
    }
}
