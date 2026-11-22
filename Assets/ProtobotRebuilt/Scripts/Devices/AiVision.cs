using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Game;
using Protobot.Motion;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.Devices {
    // V2: the V5 AI Vision Sensor (276-8659): a 320 x 240 camera (74° across, 63° up and down: the picture's top and
    // bottom are cut to 4:3 here, 59°) that finds three kinds of things, each switchable (VEXcode's tag_detection /
    // color_detection / model_detection, PROS's enable_detection_types):
    //   colors: up to 7 color descriptions (an RGB color, a hue range 1-40° and a saturation range 0.1-1, as the AI Vision
    //     Utility makes them), pixels within both join into objects; color codes: 2-5 colors side by side (angle from
    //     the first to the second);
    //   AI objects: the season's game pieces, found by its trained model: here the pieces in view (not hidden behind
    //     something), with a confidence score that falls with distance. Classes (this app's for Override): 0 Red Pin,
    //     1 Blue Pin, 2 Yellow Pin (both halves yellow), 3 Cup;
    //   AprilTags: the classroom tags (none on a V5RC field: nothing is found).
    // Objects come sorted by width, largest first, at most 24 (AIVISION_MAX_OBJECT_COUNT).

    /// <summary>An AI Vision color description: VEXcode's Colordesc(index, r, g, b, hangle, hdsat).</summary>
    public struct AiColor {
        public int id;           // 1-7
        public string name;
        public int r, g, b;
        public float hue, saturation;   // ranges: degrees 1-40, 0.1-1

        public bool Valid => id >= 1 && id <= 7;
        public Color Color => new Color32((byte)r, (byte)g, (byte)b, 255);
        public string Token => string.Join(",", id, VisionSignature.Clean(name), r, g, b, hue.ToString("0.##", CultureInfo.InvariantCulture), saturation.ToString("0.###", CultureInfo.InvariantCulture));

        public static AiColor? Parse(string text) {
            var p = text.Split(',');
            if (p.Length < 7) return null;
            float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            var c = new AiColor { id = I(p[0]), name = p[1], r = I(p[2]), g = I(p[3]), b = I(p[4]), hue = F(p[5]), saturation = F(p[6]) };
            return c.Valid ? c : (AiColor?)null;
        }

        /// <summary>Whether a pixel is this color: its hue within the range, its saturation within the range of the
        /// color's, and bright enough to have a color at all.</summary>
        public bool Matches(float h, float s, float v) {
            Color.RGBToHSV(Color, out var h0, out var s0, out _);
            if (v < 0.12f || s < 0.08f) return false;
            var dh = Mathf.Abs(Mathf.DeltaAngle(h * 360f, h0 * 360f));
            return dh <= Mathf.Clamp(hue, 1f, 40f) && Mathf.Abs(s - s0) <= Mathf.Clamp(saturation, 0.1f, 1f);
        }
    }

    public enum AiKind { Color = 1, Code = 2, Object = 4, Tag = 8 }

    /// <summary>One thing the AI Vision Sensor found.</summary>
    public struct AiObject {
        public AiKind kind;
        public int id;             // the color (1-7), the code's number, the AI class, the tag
        public int x, y, width, height;
        public float angle;        // codes and tags: 0-359
        public int score;          // AI objects: 1-100
        public int CenterX => x + width / 2;
        public int CenterY => y + height / 2;
    }

    public class AiVisionSensor : V5Device {
        public const int Width = 320, Height = 240, MaxObjects = 24;
        public const float HorizontalFov = 74f, Interval = 0.02f;
        public static readonly float VerticalFov = 2f * Mathf.Atan(Mathf.Tan(HorizontalFov * 0.5f * Mathf.Deg2Rad) * Height / Width) * Mathf.Rad2Deg;
        public static readonly string[] ClassNames = { "Red Pin", "Blue Pin", "Yellow Pin", "Cup" };
        private const int MinArea = 20;

        public readonly AiColor?[] programColors = new AiColor?[7];
        public bool programSet;
        public readonly List<int[]> codes = new();
        public bool tags = true, colors = true, model = true, merge;

        public RenderTexture Picture { get; private set; }
        public Color32[] pixels;           // top row first
        public List<AiObject> objects = new();
        public float takenAt = -1f;
        private float wantedUntil = -1f, lastRender = -1f;
        private bool pending;
        private Color32[] readback;
        private List<AiObject> pendingModel = new();
        private static Texture2D reader;

        public AiVisionSensor(GameObject part) : base(part, DeviceKind.AiVision) { }

        public override bool AlwaysSample => true;
        public override string Reading => takenAt < 0f ? "No picture" : objects.Count == 0 ? "Nothing seen" : objects.Count + (objects.Count == 1 ? " object" : " objects");
        public override Color Tint => new(0.36f, 0.5f, 0.86f);

        public void Want(float seconds = 1f) => wantedUntil = Mathf.Max(wantedUntil, Time.unscaledTime + seconds);

        /// <summary>V2: keeps its picture coming for a card that shows it (the Devices panel): pictures only looked at
        /// share VisionCamera.ShownPerSecond between every sensor shown (nine open cards made nine full renders of the
        /// field 50 times a second each), while a program's reads stay at 50 a second.</summary>
        public void Show() {
            shownUntil = Time.unscaledTime + 1f;
            VisionCamera.Shown(this);
        }

        private float shownUntil;

        // ------------------------------------------------------------------ colors kept on the sensor

        public static AiColor?[] Saved(GameObject part) {
            var all = new AiColor?[7];
            var token = Motion.Dynamics.Token(part, "aicolor");
            if (string.IsNullOrEmpty(token)) return all;
            foreach (var piece in token.Split('|')) {
                var c = AiColor.Parse(piece);
                if (c.HasValue) all[c.Value.id - 1] = c;
            }
            return all;
        }

        public static void Save(GameObject part, AiColor?[] all) {
            var list = all.Where(c => c.HasValue).Select(c => c.Value.Token).ToList();
            Motion.Dynamics.SetToken(part, "aicolor", list.Count > 0 ? string.Join("|", list) : null);
        }

        public AiColor?[] Colors => programSet ? programColors : Saved(part);

        public void SetColor(AiColor c) {
            if (!c.Valid) return;
            if (!programSet) {
                var saved = Saved(part);
                for (var i = 0; i < 7; i++) programColors[i] = saved[i];
                programSet = true;
            }
            programColors[c.id - 1] = c;
        }

        public override void PowerOn() {
            for (var i = 0; i < 7; i++) programColors[i] = null;
            programSet = false;
            codes.Clear();
            tags = colors = model = true;
            merge = false;
            objects.Clear();
            takenAt = -1f;
        }

        // ------------------------------------------------------------------ pictures

        public override void Sample(float dt) {
            if (part == null) return;
            if (pending) return;
            var program = Time.unscaledTime <= wantedUntil;
            if (!program && Time.unscaledTime > shownUntil) return;
            if (Time.unscaledTime - lastRender < (program ? Interval : VisionCamera.ShownInterval)) return;
            if (!VisionCamera.TakeRender()) return;   // V2: a few renders a frame at most
            lastRender = Time.unscaledTime;
            Render(true);
        }

        public void RenderNow() {
            Render(false);
            lastRender = Time.unscaledTime;
        }

        private Vector3 upLocal;

        public (Vector3 origin, Vector3 look, Vector3 up) Lens() {
            var (o, d) = SensorFrame.Look(part);
            if (upLocal == Vector3.zero) {
                var size = part.TryGetComponent(out MeshFilter f) && f.sharedMesh != null ? f.sharedMesh.bounds.size : Vector3.one;
                var axes = new[] { Vector3.right, Vector3.up, Vector3.forward }.Where(a => Mathf.Abs(Vector3.Dot(a, d)) < 0.5f).ToArray();
                var across = axes.OrderBy(a => Mathf.Abs(Vector3.Dot(size, a))).First();
                if (Vector3.Dot(part.transform.TransformDirection(across), Vector3.up) < 0f) across = -across;
                upLocal = across;
            }
            var look = Pose.Dir(part, d);
            var up = Vector3.ProjectOnPlane(Pose.Dir(part, upLocal), look);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            return (Pose.Point(part, o) + look * 0.05f, look, up.normalized);
        }

        private void Render(bool async) {
            if (Picture == null) {
                Picture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "AI Vision Sensor", antiAliasing = 1 };
                Picture.Create();
            }
            var (origin, look, up) = Lens();
            var camera = VisionCamera.Get();
            camera.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(look, up));
            camera.fieldOfView = VerticalFov;
            camera.aspect = (float)Width / Height;
            camera.targetTexture = Picture;
            camera.cullingMask = VisionCamera.Mask;
            var found = model ? Pieces(camera, origin) : new List<AiObject>();
            try { camera.Render(); }
            finally { camera.targetTexture = null; }
            if (async && SystemInfo.supportsAsyncGPUReadback) {
                pending = true;
                AsyncGPUReadback.Request(Picture, 0, TextureFormat.RGBA32, request => {
                    pending = false;
                    if (request.hasError || part == null) return;
                    var data = request.GetData<Color32>();
                    if (readback == null || readback.Length != data.Length) readback = new Color32[data.Length];
                    data.CopyTo(readback);
                    Finish(readback, found);
                });
            }
            else {
                var previous = RenderTexture.active;
                RenderTexture.active = Picture;
                if (reader == null) reader = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                reader.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                RenderTexture.active = previous;
                Finish(reader.GetPixels32(), found);
            }
        }

        private int[] labels;

        private void Finish(Color32[] raw, List<AiObject> found) {
            if (pixels == null || pixels.Length != raw.Length) pixels = new Color32[raw.Length];
            for (var row = 0; row < Height; row++)
                System.Array.Copy(raw, (Height - 1 - row) * Width, pixels, row * Width, Width);
            var all = new List<AiObject>();
            if (colors) all.AddRange(FindColors(pixels, Colors, codes, merge, ref labels));
            all.AddRange(found);
            objects = all.OrderByDescending(o => o.width).Take(MaxObjects).ToList();
            takenAt = Time.time;
        }

        // ------------------------------------------------------------------ colors

        public static List<AiObject> FindColors(Color32[] image, AiColor?[] all, List<int[]> codes, bool merge, ref int[] labels) {
            var result = new List<AiObject>();
            var n = Width * Height;
            if (labels == null || labels.Length != n) labels = new int[n];
            var lab = labels;   // (a ref parameter can't be used in the local function below)
            var hsv = new Vector3[n];
            for (var i = 0; i < n; i++) {
                Color.RGBToHSV(image[i], out var h, out var s, out var v);
                hsv[i] = new Vector3(h, s, v);
            }
            var blobs = new List<AiObject>();
            var stack = new Stack<int>();
            foreach (var c in all.Where(c => c.HasValue).Select(c => c.Value)) {
                var color = c;
                System.Array.Clear(lab, 0, n);
                for (var start = 0; start < n; start++) {
                    if (lab[start] != 0) continue;
                    var p = hsv[start];
                    if (!color.Matches(p.x, p.y, p.z)) { lab[start] = -1; continue; }
                    int minX = Width, minY = Height, maxX = 0, maxY = 0, area = 0;
                    stack.Push(start);
                    lab[start] = 1;
                    while (stack.Count > 0) {
                        var i = stack.Pop();
                        var x = i % Width;
                        var y = i / Width;
                        area++;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        void Visit(int j) {
                            if (lab[j] != 0) return;
                            var q = hsv[j];
                            if (!color.Matches(q.x, q.y, q.z)) { lab[j] = -1; return; }
                            lab[j] = 1;
                            stack.Push(j);
                        }
                        if (x > 0) Visit(i - 1);
                        if (x < Width - 1) Visit(i + 1);
                        if (y > 0) Visit(i - Width);
                        if (y < Height - 1) Visit(i + Width);
                    }
                    if (area >= MinArea) blobs.Add(new AiObject { kind = AiKind.Color, id = color.id, x = minX, y = minY, width = maxX - minX + 1, height = maxY - minY + 1 });
                }
            }
            if (merge) blobs = Merge(blobs);
            result.AddRange(blobs);
            // Codes: their colors' objects side by side (within 4 px), in the code's order.
            foreach (var code in codes) {
                var firsts = blobs.Where(b => b.id == code[0]);
                foreach (var a in firsts) {
                    var chain = new List<AiObject> { a };
                    for (var k = 1; k < code.Length; k++) {
                        var last = chain[chain.Count - 1];
                        var next = blobs.Where(b => b.id == code[k] && Gap(last, b) <= 4).OrderBy(b => Gap(last, b)).Cast<AiObject?>().FirstOrDefault();
                        if (next == null) { chain = null; break; }
                        chain.Add(next.Value);
                    }
                    if (chain == null) continue;
                    int x0 = chain.Min(b => b.x), y0 = chain.Min(b => b.y), x1 = chain.Max(b => b.x + b.width), y1 = chain.Max(b => b.y + b.height);
                    var d = new Vector2(chain[1].CenterX - chain[0].CenterX, -(chain[1].CenterY - chain[0].CenterY));
                    result.Add(new AiObject { kind = AiKind.Code, id = CodeId(code), x = x0, y = y0, width = x1 - x0, height = y1 - y0,
                        angle = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 360f) });
                }
            }
            return result;
        }

        public static int CodeId(int[] code) => code.Aggregate(0, (n, s) => n * 8 + s);

        private static int Gap(AiObject a, AiObject b) {
            var dx = Mathf.Max(0, Mathf.Max(a.x, b.x) - Mathf.Min(a.x + a.width, b.x + b.width));
            var dy = Mathf.Max(0, Mathf.Max(a.y, b.y) - Mathf.Min(a.y + a.height, b.y + b.height));
            return Mathf.Max(dx, dy);
        }

        // Color merge: objects of one color that touch become one.
        private static List<AiObject> Merge(List<AiObject> list) {
            var changed = true;
            while (changed) {
                changed = false;
                for (var i = 0; i < list.Count && !changed; i++)
                    for (var j = i + 1; j < list.Count && !changed; j++) {
                        if (list[i].id != list[j].id || Gap(list[i], list[j]) > 4) continue;
                        var a = list[i];
                        var b = list[j];
                        int x0 = Mathf.Min(a.x, b.x), y0 = Mathf.Min(a.y, b.y), x1 = Mathf.Max(a.x + a.width, b.x + b.width), y1 = Mathf.Max(a.y + a.height, b.y + b.height);
                        list[i] = new AiObject { kind = AiKind.Color, id = a.id, x = x0, y = y0, width = x1 - x0, height = y1 - y0 };
                        list.RemoveAt(j);
                        changed = true;
                    }
            }
            return list;
        }

        // ------------------------------------------------------------------ the AI model: game pieces

        private List<AiObject> Pieces(Camera camera, Vector3 origin) {
            var found = new List<AiObject>();
            foreach (var (root, cls) in GamePieces()) {
                if (root == null || !root.gameObject.activeInHierarchy) continue;
                var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
                if (renderers.Count == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                var center = camera.WorldToViewportPoint(b.center);
                if (center.z <= 0.5f || center.x < 0f || center.x > 1f || center.y < 0f || center.y > 1f) continue;
                if (center.z > 120f) continue;   // too far to classify
                if (!Visible(origin, b, root)) continue;
                float x0 = 1f, y0 = 1f, x1 = 0f, y1 = 0f;
                for (var i = 0; i < 8; i++) {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var v = camera.WorldToViewportPoint(corner);
                    if (v.z <= 0f) continue;
                    x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x);
                    y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
                }
                x0 = Mathf.Clamp01(x0); x1 = Mathf.Clamp01(x1); y0 = Mathf.Clamp01(y0); y1 = Mathf.Clamp01(y1);
                var w = Mathf.RoundToInt((x1 - x0) * Width);
                var h = Mathf.RoundToInt((y1 - y0) * Height);
                if (w < 4 || h < 4) continue;
                found.Add(new AiObject { kind = AiKind.Object, id = cls, x = Mathf.RoundToInt(x0 * Width), y = Mathf.RoundToInt((1f - y1) * Height), width = w, height = h,
                    score = Mathf.Clamp(Mathf.RoundToInt(99f - center.z * 0.3f), 40, 99) });
            }
            return found;
        }

        // Seen when a ray from the lens to its middle or either end of its box reaches it before anything else.
        private bool Visible(Vector3 origin, Bounds b, Transform root) {
            var targets = new[] { b.center, b.center + new Vector3(0f, b.extents.y * 0.6f, 0f), b.center - new Vector3(b.extents.x, 0f, b.extents.z) * 0.6f, b.center + new Vector3(b.extents.x, 0f, b.extents.z) * 0.6f };
            var mask = Pose.Mask | (1 << 18) | (1 << 19);
            foreach (var t in targets) {
                var d = t - origin;
                var length = d.magnitude;
                var blocked = false;
                foreach (var hit in Physics.RaycastAll(origin, d / length, length, mask, QueryTriggerInteraction.Ignore)) {
                    if (hit.collider.transform.IsChildOf(root)) continue;
                    if (!Sandbox.Running && hit.collider.transform.IsChildOf(part.transform)) continue;
                    if (hit.distance < length - 1.5f) { blocked = true; break; }
                }
                if (!blocked) return true;
            }
            return false;
        }

        /// <summary>The game pieces in the scene and their AI classes: the simulation's (while a run goes) or the field
        /// model's own.</summary>
        public static IEnumerable<(Transform root, int cls)> GamePieces() {
            var pins = Object.FindObjectsByType<Pin>(FindObjectsSortMode.None);
            var cups = Object.FindObjectsByType<Cup>(FindObjectsSortMode.None);
            if (pins.Length > 0 || cups.Length > 0) {
                foreach (var p in pins) {
                    var colorful = p.halves.Select(h => h.color).FirstOrDefault(c => c == PieceColor.Red || c == PieceColor.Blue);
                    yield return (p.transform, colorful == PieceColor.Red ? 0 : colorful == PieceColor.Blue ? 1 : 2);
                }
                foreach (var c in cups) yield return (c.transform, 3);
                yield break;
            }
            var field = SkillsGame.Current != null ? SkillsGame.Current.field : null;
            if (field == null) yield break;
            foreach (var t in field.GetComponentsInChildren<Transform>()) {
                if (t.name.StartsWith("276-9250-80x")) yield return (t, PinClass(t));
                else if (t.name.StartsWith("276-9250-81x")) yield return (t, 3);
            }
        }

        // A field-model pin's class by the colors of its materials (red or blue half, else yellow).
        private static int PinClass(Transform t) {
            foreach (var r in t.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials) {
                    var c = Pose.MaterialColor(m);
                    Color.RGBToHSV(c, out var h, out var s, out _);
                    if (s < 0.35f) continue;
                    if (h < 0.04f || h > 0.93f) return 0;
                    if (h > 0.55f && h < 0.72f) return 1;
                }
            return 2;
        }

        // ------------------------------------------------------------------ for programs

        /// <summary>A program's snapshot: objects of one color, code, every AI object or every tag (largest first).</summary>
        public List<AiObject> Snapshot(System.Func<AiObject, bool> which, int count) {
            Want();
            if (takenAt < 0f && part != null) RenderNow();
            return objects.Where(which).Take(Mathf.Clamp(count, 1, MaxObjects)).ToList();
        }

        /// <summary>A color description from a box over an object (the AI Vision Utility's): the box's most common color
        /// (the pixels nearest its median), hue range 10°, saturation range 0.2.</summary>
        public static AiColor? Make(Color32[] image, RectInt box, int id, string name) {
            var list = new List<Color32>();
            for (var y = Mathf.Max(0, box.yMin); y < Mathf.Min(Height, box.yMax); y++)
                for (var x = Mathf.Max(0, box.xMin); x < Mathf.Min(Width, box.xMax); x++) {
                    var c = image[y * Width + x];
                    Color.RGBToHSV(c, out _, out var s, out var v);
                    if (s > 0.1f && v > 0.12f) list.Add(c);
                }
            if (list.Count < 8) return null;
            int Med(System.Func<Color32, int> f) => list.Select(f).OrderBy(v => v).ElementAt(list.Count / 2);
            var mr = Med(c => c.r);
            var mg = Med(c => c.g);
            var mb = Med(c => c.b);
            var near = list.OrderBy(c => (c.r - mr) * (c.r - mr) + (c.g - mg) * (c.g - mg) + (c.b - mb) * (c.b - mb)).Take(Mathf.Max(8, list.Count / 2)).ToList();
            return new AiColor { id = id, name = name, r = (int)near.Average(c => c.r), g = (int)near.Average(c => c.g), b = (int)near.Average(c => c.b), hue = 10f, saturation = 0.2f };
        }
    }
}
