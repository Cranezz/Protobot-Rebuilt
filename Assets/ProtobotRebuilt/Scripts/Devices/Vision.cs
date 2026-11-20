using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Motion;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.Devices {
    /// <summary>V2: a color signature, as VEX's Vision Utility makes it (VEXcode's Signature(index, uMin, uMax, uMean,
    /// vMin, vMax, vMean, range, type)): u and v are the pixel's (r - g) and (b - g) over its brightness r + g + b,
    /// times 2^15 (Pixy's color model: the V5 Vision Sensor is a Pixy); range widens min/max round the mean.</summary>
    public struct VisionSignature {
        public int id;            // 1-7
        public string name;
        public int uMin, uMax, uMean, vMin, vMax, vMean;
        public float range;
        public int type;          // 0 normal, 1 for a signature used in a color code

        public bool Valid => id >= 1 && id <= 7 && uMax >= uMin && vMax >= vMin;

        /// <summary>The bounds the sensor uses: mean + (bound - mean) x range.</summary>
        public (float u0, float u1, float v0, float v1) Bounds() {
            var r = range > 0f ? range : 2.5f;
            return (uMean + (uMin - uMean) * r, uMean + (uMax - uMean) * r, vMean + (vMin - vMean) * r, vMean + (vMax - vMean) * r);
        }

        public string Token => string.Join(",", id, Clean(name), uMin, uMax, uMean, vMin, vMax, vMean, range.ToString("0.0##", CultureInfo.InvariantCulture), type);

        public static VisionSignature? Parse(string text) {
            var p = text.Split(',');
            if (p.Length < 9) return null;
            var s = new VisionSignature { id = Int(p[0]), name = p[1], uMin = Int(p[2]), uMax = Int(p[3]), uMean = Int(p[4]),
                vMin = Int(p[5]), vMax = Int(p[6]), vMean = Int(p[7]), type = p.Length > 9 ? Int(p[9]) : 0 };
            float.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out s.range);
            return s.Valid ? s : null;
        }

        private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        /// <summary>A name as code can use it (VEXcode: vision_5__RED_BOX).</summary>
        public static string Clean(string name) {
            var chars = (name ?? "").Trim().Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray();
            var s = new string(chars).Trim('_');
            if (s.Length == 0) return "SIG";
            return char.IsDigit(s[0]) ? "SIG_" + s : s;
        }
    }

    /// <summary>An object a snapshot found: its box in the sensor's 316 x 212 picture (0,0 at the top left).</summary>
    public struct VisionBlob {
        public int id;            // the signature (1-7), or a color code's number (its signatures as octal digits)
        public int x, y, width, height;
        public float angle;       // a color code's turn, 0-359
        public int area;          // pixels of its color(s)
        public bool code;
        public int CenterX => x + width / 2;
        public int CenterY => y + height / 2;
    }

    /// <summary>
    /// V2: the V5 Vision Sensor (276-4850). It draws what is in front of its lens (64.6 x 46 degrees, VEX's figures)
    /// at its own 316 x 212 pixels, sorts each pixel into the signatures it fits (Pixy's u/v color model), joins
    /// touching pixels of a signature into objects (boxes, largest first) and side-by-side objects of a color code
    /// into one turned object, up to 50 times a second while something reads it. Signatures are kept on the sensor
    /// (its saved state, "vsig:", made in the Vision Utility); a program's own Signature(...) replace them while it runs.
    /// </summary>
    public class VisionSensor : V5Device {
        public const int Width = 316, Height = 212;
        public const float VerticalFov = 46f, Interval = 0.02f;
        private const int MinArea = 20;   // Pixy's smallest object (pixels)

        public readonly VisionSignature?[] programSigs = new VisionSignature?[7];
        public readonly List<int[]> codes = new();
        public int brightness = 50;      // 1-100: the picture's exposure (VEXcode's Vision(port, brightness, ...))
        public bool programSet;          // the running program gave its own signatures

        public RenderTexture Picture { get; private set; }
        public Color32[] pixels;          // the last picture, top row first
        public List<VisionBlob> blobs = new();
        public float takenAt = -1f;       // Time.time of the last finished picture
        public int frames;

        private float wantedUntil = -1f, lastRender = -10f;
        private bool pending;
        private Color32[] readback;

        public VisionSensor(GameObject part) : base(part, DeviceKind.Vision) { }

        public override bool AlwaysSample => true;   // (Sample does nothing unless something asked for pictures)
        public override string Reading => takenAt < 0f ? "No picture" : blobs.Count == 0 ? "Nothing seen" : blobs.Count + (blobs.Count == 1 ? " object" : " objects");
        public override Color Tint => new(0.42f, 0.36f, 0.78f);

        /// <summary>Keeps it taking pictures for a second (a program's snapshot, the panel's picture).</summary>
        public void Want(float seconds = 1f) => wantedUntil = Mathf.Max(wantedUntil, Time.unscaledTime + seconds);

        // ------------------------------------------------------------------ signatures

        /// <summary>The signatures kept on the sensor (the Vision Utility's), by id.</summary>
        public static VisionSignature?[] Saved(GameObject part) {
            var sigs = new VisionSignature?[7];
            var token = Dynamics.Token(part, "vsig");
            if (string.IsNullOrEmpty(token)) return sigs;
            foreach (var entry in token.Split('|')) {
                var s = VisionSignature.Parse(entry);
                if (s.HasValue) sigs[s.Value.id - 1] = s;
            }
            return sigs;
        }

        public static void Save(GameObject part, VisionSignature?[] sigs) {
            var list = sigs.Where(s => s.HasValue).Select(s => s.Value.Token).ToList();
            Dynamics.SetToken(part, "vsig", list.Count > 0 ? string.Join("|", list) : null);
        }

        public static int SavedBrightness(GameObject part) =>
            int.TryParse(Dynamics.Token(part, "vbright"), out var b) ? Mathf.Clamp(b, 1, 100) : 50;

        public VisionSignature?[] Signatures => programSet ? programSigs : Saved(part);

        public override void PowerOn() {
            for (var i = 0; i < 7; i++) programSigs[i] = null;
            codes.Clear();
            programSet = false;
            brightness = SavedBrightness(part);
            blobs.Clear();
            takenAt = -1f;
        }

        public void SetSignature(VisionSignature s) {
            if (!s.Valid) return;
            if (!programSet) {
                var saved = Saved(part);
                for (var i = 0; i < 7; i++) programSigs[i] = saved[i];
                programSet = true;
            }
            programSigs[s.id - 1] = s;
        }

        // ------------------------------------------------------------------ pictures

        public override void Sample(float dt) {
            if (Time.unscaledTime > wantedUntil || part == null) return;
            if (pending || Time.unscaledTime - lastRender < Interval) return;
            lastRender = Time.unscaledTime;
            Render(true);
        }

        private void Render(bool async) {
            if (Picture == null) {
                Picture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Vision Sensor", antiAliasing = 1 };
                Picture.Create();
            }
            var (origin, look, up) = Lens();
            var camera = VisionCamera.Get();
            camera.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(look, up));
            camera.targetTexture = Picture;
            camera.cullingMask = VisionCamera.Mask;
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
                    Finish(readback);
                });
            }
            else {
                var previous = RenderTexture.active;
                RenderTexture.active = Picture;
                var texture = VisionCamera.Reader();
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                RenderTexture.active = previous;
                Finish(texture.GetPixels32());
            }
        }

        // Where the lens is and which way the picture's top is (the sensor's longer side across the picture; the top
        // the side that is up when the robot stands as built).
        private Vector3 upLocal;

        public (Vector3 origin, Vector3 look, Vector3 up) Lens() {
            var (o, d) = SensorFrame.Look(part);
            if (upLocal == Vector3.zero) {
                var size = part.TryGetComponent(out MeshFilter f) && f.sharedMesh != null ? f.sharedMesh.bounds.size : Vector3.one;
                var axes = new[] { Vector3.right, Vector3.up, Vector3.forward }.Where(a => Mathf.Abs(Vector3.Dot(a, d)) < 0.5f).ToArray();
                // the shorter of the two sides across the lens is the picture's up
                var across = axes.OrderBy(a => Mathf.Abs(Vector3.Dot(size, a))).First();
                if (Vector3.Dot(part.transform.TransformDirection(across), Vector3.up) < 0f) across = -across;
                upLocal = across;
            }
            var look = Pose.Dir(part, d);
            var up = Vector3.ProjectOnPlane(Pose.Dir(part, upLocal), look);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            return (Pose.Point(part, o) + look * 0.05f, look, up.normalized);
        }

        // ------------------------------------------------------------------ finding objects

        private int[] labels;

        private void Finish(Color32[] raw) {
            // The picture comes bottom row first: turn it so row 0 is the top (as the sensor counts y).
            if (pixels == null || pixels.Length != raw.Length) pixels = new Color32[raw.Length];
            for (var row = 0; row < Height; row++)
                System.Array.Copy(raw, (Height - 1 - row) * Width, pixels, row * Width, Width);
            var gain = brightness / 50f;
            if (!Mathf.Approximately(gain, 1f))
                for (var i = 0; i < pixels.Length; i++) {
                    var c = pixels[i];
                    pixels[i] = new Color32((byte)Mathf.Min(255, c.r * gain), (byte)Mathf.Min(255, c.g * gain), (byte)Mathf.Min(255, c.b * gain), 255);
                }
            blobs = Find(pixels, Signatures, codes, ref labels);
            takenAt = Time.time;
            frames++;
        }

        /// <summary>Pixy's color model: u = (r - g) / (r + g + b), v = (b - g) / (r + g + b), times 2^15; too dark
        /// a pixel (under a tenth of full brightness) has no color.</summary>
        public static bool UV(Color32 c, out int u, out int v) {
            int sum = c.r + c.g + c.b;
            if (sum < 77) { u = v = 0; return false; }
            u = ((c.r - c.g) << 15) / sum;
            v = ((c.b - c.g) << 15) / sum;
            return true;
        }

        public static List<VisionBlob> Find(Color32[] image, VisionSignature?[] sigs, List<int[]> codes, ref int[] labels) {
            var found = new List<VisionBlob>();
            var n = Width * Height;
            if (labels == null || labels.Length != n) labels = new int[n];
            var bounds = new (float u0, float u1, float v0, float v1)[7];
            var any = false;
            for (var s = 0; s < 7; s++)
                if (sigs[s].HasValue) { bounds[s] = sigs[s].Value.Bounds(); any = true; }
            if (!any) return found;
            for (var i = 0; i < n; i++) {
                labels[i] = 0;
                if (!UV(image[i], out var u, out var v)) continue;
                for (var s = 0; s < 7; s++) {
                    if (!sigs[s].HasValue) continue;
                    var b = bounds[s];
                    if (u >= b.u0 && u <= b.u1 && v >= b.v0 && v <= b.v1) { labels[i] = s + 1; break; }
                }
            }
            // Joined pixels of one signature (4-connected) become one object.
            var parent = new int[n];
            for (var i = 0; i < n; i++) parent[i] = i;
            int Root(int i) {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++) {
                    var i = y * Width + x;
                    var l = labels[i];
                    if (l == 0) continue;
                    if (x > 0 && labels[i - 1] == l) parent[Root(i)] = Root(i - 1);
                    if (y > 0 && labels[i - Width] == l) parent[Root(i)] = Root(i - Width);
                }
            var boxes = new Dictionary<int, (int sig, int x0, int y0, int x1, int y1, int area)>();
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++) {
                    var i = y * Width + x;
                    if (labels[i] == 0) continue;
                    var r = Root(i);
                    boxes[r] = boxes.TryGetValue(r, out var b)
                        ? (b.sig, Mathf.Min(b.x0, x), Mathf.Min(b.y0, y), Mathf.Max(b.x1, x), Mathf.Max(b.y1, y), b.area + 1)
                        : (labels[i], x, y, x, y, 1);
                }
            foreach (var b in boxes.Values)
                if (b.area >= MinArea)
                    found.Add(new VisionBlob { id = b.sig, x = b.x0, y = b.y0, width = b.x1 - b.x0 + 1, height = b.y1 - b.y0 + 1, area = b.area });
            // Color codes: objects of the code's signatures side by side (boxes touching or within 4 pixels) make one
            // object; its angle is the turn of the line from the first signature's object to the next one's.
            foreach (var code in codes) {
                var parts = found.Where(f => !f.code && code.Contains(f.id)).ToList();
                var used = new HashSet<int>();
                for (var a = 0; a < parts.Count; a++) {
                    if (used.Contains(a) || parts[a].id != code[0]) continue;
                    var group = new List<int> { a };
                    for (var k = 1; k < code.Length; k++) {
                        var last = parts[group[group.Count - 1]];
                        var next = Enumerable.Range(0, parts.Count).Where(j => !used.Contains(j) && !group.Contains(j) && parts[j].id == code[k] && Near(last, parts[j]))
                            .OrderBy(j => Gap(last, parts[j])).DefaultIfEmpty(-1).First();
                        if (next < 0) break;
                        group.Add(next);
                    }
                    if (group.Count < 2) continue;
                    foreach (var g in group) used.Add(g);
                    var gs = group.Select(g => parts[g]).ToList();
                    var x0 = gs.Min(g => g.x); var y0 = gs.Min(g => g.y);
                    var x1 = gs.Max(g => g.x + g.width); var y1 = gs.Max(g => g.y + g.height);
                    var first = gs[0]; var second = gs[1];
                    var angle = Mathf.Atan2(-(second.CenterY - first.CenterY), second.CenterX - first.CenterX) * Mathf.Rad2Deg;
                    found.Add(new VisionBlob { id = CodeId(code), x = x0, y = y0, width = x1 - x0, height = y1 - y0, area = gs.Sum(g => g.area),
                        angle = (angle + 360f) % 360f, code = true });
                }
            }
            found.Sort((p, q) => q.area.CompareTo(p.area));
            return found;
        }

        private static int[] spare;

        /// <summary>The objects in a picture (the Vision Utility's frozen one).</summary>
        public static List<VisionBlob> FindIn(Color32[] image, VisionSignature?[] sigs, List<int[]> codes) => Find(image, sigs, codes, ref spare);

        private static int Gap(VisionBlob a, VisionBlob b) {
            var gx = Mathf.Max(0, Mathf.Max(a.x, b.x) - Mathf.Min(a.x + a.width, b.x + b.width));
            var gy = Mathf.Max(0, Mathf.Max(a.y, b.y) - Mathf.Min(a.y + a.height, b.y + b.height));
            return Mathf.Max(gx, gy);
        }

        private static bool Near(VisionBlob a, VisionBlob b) => Gap(a, b) <= 4;

        /// <summary>A color code's number: its signatures as octal digits (signatures 1 and 2: 10, octal 12).</summary>
        public static int CodeId(int[] code) => code.Aggregate(0, (n, s) => n * 8 + s);

        /// <summary>The objects of one signature (or color code, by its number), largest first, at most count.</summary>
        public List<VisionBlob> Of(int id, int count = 16) => blobs.Where(b => b.id == id).Take(Mathf.Clamp(count, 1, 16)).ToList();

        /// <summary>A program's snapshot: the latest picture's objects of one signature or code (the sensor keeps
        /// taking pictures, 50 a second, while a program reads it; the first is taken at once).</summary>
        public List<VisionBlob> Snapshot(int id, int count) {
            Want();
            if (takenAt < 0f && part != null) RenderNow();
            return Of(id, count);
        }

        /// <summary>A picture now, waiting for it (the first one, the Vision Utility's freeze).</summary>
        public void RenderNow() {
            Render(false);
            lastRender = Time.unscaledTime;
        }

        /// <summary>Pixels the signature takes in a picture (a box over it in the Vision Utility): the middle of their
        /// u and v (mean), and the range holding the middle 80% of them (min/max), as VEX's utility gives them.</summary>
        public static VisionSignature? Make(Color32[] image, RectInt box, int id, string name) {
            var us = new List<int>();
            var vs = new List<int>();
            for (var y = Mathf.Max(0, box.yMin); y < Mathf.Min(Height, box.yMax); y++)
                for (var x = Mathf.Max(0, box.xMin); x < Mathf.Min(Width, box.xMax); x++)
                    if (UV(image[y * Width + x], out var u, out var v)) { us.Add(u); vs.Add(v); }
            if (us.Count < 8) return null;
            // Keep the pixels of the box's main color: those near the median (the box's edges catch the background).
            var mu = Median(us); var mv = Median(vs);
            var near = Enumerable.Range(0, us.Count).OrderBy(i => (long)(us[i] - mu) * (us[i] - mu) + (long)(vs[i] - mv) * (vs[i] - mv)).Take(Mathf.Max(8, us.Count * 6 / 10)).ToList();
            var su = near.Select(i => us[i]).OrderBy(x => x).ToList();
            var sv = near.Select(i => vs[i]).OrderBy(x => x).ToList();
            int At(List<int> s, float f) => s[Mathf.Clamp(Mathf.RoundToInt(f * (s.Count - 1)), 0, s.Count - 1)];
            // At least 250 each side of the mean (VEX's own signatures span ~300): a flatly lit face gives one value,
            // and a signature that narrow loses the object as soon as it turns or the light changes.
            var um = (int)su.Average();
            var vm = (int)sv.Average();
            return new VisionSignature { id = id, name = name, uMin = Mathf.Min(At(su, 0.1f), um - 250), uMax = Mathf.Max(At(su, 0.9f), um + 250), uMean = um,
                vMin = Mathf.Min(At(sv, 0.1f), vm - 250), vMax = Mathf.Max(At(sv, 0.9f), vm + 250), vMean = vm, range = 2.5f, type = 0 };
        }

        private static int Median(List<int> values) => values.OrderBy(x => x).ElementAt(values.Count / 2);
    }

    /// <summary>The one camera every Vision Sensor draws with (switched off: drawn on request).</summary>
    internal static class VisionCamera {
        private static Camera camera;
        private static Texture2D reader;

        public static Camera Get() {
            if (camera != null) return camera;
            var go = new GameObject("Vision Sensor Camera") { hideFlags = HideFlags.HideAndDontSave };
            camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = VisionSensor.VerticalFov;
            camera.aspect = (float)VisionSensor.Width / VisionSensor.Height;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 2000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.08f, 0.09f);   // a dim room past the field
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.targetTexture = RenderTexture.GetTemporary(VisionSensor.Width, VisionSensor.Height, 24);
            GraphicsQuality.GraphicsSettingsManager.ApplyToCamera(camera);   // (no shadows/AO/post for a texture camera)
            RenderTexture.ReleaseTemporary(camera.targetTexture);
            camera.targetTexture = null;
            return camera;
        }

        /// <summary>What the main camera draws, less the interface, grid, hole and placing layers.</summary>
        public static int Mask {
            get {
                var main = Camera.main != null ? Camera.main.cullingMask : ~0;
                foreach (var l in new[] { 5, 8, 9, 10, 11, 12, 14, 15 }) main &= ~(1 << l);
                return main | 1;
            }
        }

        public static Texture2D Reader() => reader != null ? reader : reader = new Texture2D(VisionSensor.Width, VisionSensor.Height, TextureFormat.RGBA32, false);
    }
}
