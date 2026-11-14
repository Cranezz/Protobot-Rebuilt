using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Motion {
    // V2: parts-list pictures for bands and tubing (they have no model to render): a band stretched round two posts
    // in its color (tubing round), and a pneumatic tube curving between two red-ended fittings.
    public static class StrandIcons {
        private const int Size = 128;
        private static readonly Dictionary<string, Texture2D> made = new();

        public static Texture2D For(string action) {
            if (action == null) return null;
            if (made.TryGetValue(action, out var texture) && texture != null) return texture;
            if (action == "ptube") texture = Tube();
            else if (action == "wire:V5") texture = Cable(false);
            else if (action == "wire:3W") texture = Cable(true);
            else if (action.StartsWith("band:")) texture = Band(ElasticKind.Of(action.Substring(5)));
            else return null;
            texture.name = "Icon " + action;
            return made[action] = texture;
        }

        // Distance from p to the segment ab.
        private static float Segment(Vector2 p, Vector2 a, Vector2 b) {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        private static Color Over(Color under, Color over, float alpha) {
            alpha = Mathf.Clamp01(alpha) * over.a;
            return new Color(Mathf.Lerp(under.r, over.r, alpha), Mathf.Lerp(under.g, over.g, alpha), Mathf.Lerp(under.b, over.b, alpha), Mathf.Max(under.a, alpha));
        }

        private static Texture2D Paint(System.Func<Vector2, Color> shade) {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[Size * Size];
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    pixels[y * Size + x] = shade(new Vector2(x + 0.5f, y + 0.5f));
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D Band(ElasticKind kind) {
            // The band's own color, brightened for the interface (part colors are about half the real ones).
            var color = new Color(Mathf.Min(1f, kind.color.r * 2.1f + 0.05f), Mathf.Min(1f, kind.color.g * 2.1f + 0.05f), Mathf.Min(1f, kind.color.b * 2.1f + 0.05f), 1f);
            if (kind.color.maxColorComponent < 0.1f) color = new Color(0.22f, 0.22f, 0.24f, 1f);
            var post = new Color(0.72f, 0.74f, 0.78f, 1f);
            Vector2 a = new(30f, 64f), b = new(98f, 64f);
            const float PostRadius = 19f;
            var width = kind.tubing ? 10f : 6f + kind.width * 18f;
            var r = PostRadius + width / 2f;
            return Paint(p => {
                var c = new Color(0f, 0f, 0f, 0f);
                // Posts (with a darker hole), then the loop round them.
                foreach (var center in new[] { a, b }) {
                    var d = Vector2.Distance(p, center);
                    c = Over(c, post, PostRadius - d + 0.5f);
                    c = Over(c, new Color(0.4f, 0.42f, 0.46f, 1f), 7f - d + 0.5f);
                }
                // The loop: two straight sides and a half circle round each post.
                float loop;
                if (p.x < a.x) loop = Mathf.Abs(Vector2.Distance(p, a) - r);
                else if (p.x > b.x) loop = Mathf.Abs(Vector2.Distance(p, b) - r);
                else loop = Mathf.Min(Mathf.Abs(p.y - (a.y + r)), Mathf.Abs(p.y - (a.y - r)));
                c = Over(c, color, width / 2f - loop + 0.5f);
                return c;
            });
        }

        // A cable curving between two plugs: flat black with clear plugs (V5), or black/red/white with black plugs.
        private static Texture2D Cable(bool threeWire) {
            var black = new Color(0.13f, 0.13f, 0.15f, 1f);
            var red = new Color(0.85f, 0.18f, 0.16f, 1f);
            var white = new Color(0.92f, 0.92f, 0.9f, 1f);
            var plug = threeWire ? new Color(0.2f, 0.2f, 0.22f, 1f) : new Color(0.82f, 0.86f, 0.9f, 0.85f);
            var edge = new Color(0.5f, 0.52f, 0.56f, 1f);
            var curve = new List<Vector2>();
            for (var i = 0; i <= 40; i++) {
                var t = i / 40f;
                curve.Add(new Vector2(Mathf.Lerp(30f, 98f, t), 64f + 30f * Mathf.Cos(t * Mathf.PI)));
            }
            Vector2 a = new(22f, 94f), b = new(106f, 34f);
            return Paint(p => {
                var c = new Color(0f, 0f, 0f, 0f);
                var d = float.MaxValue;
                var along = 0f;
                for (var i = 0; i + 1 < curve.Count; i++) {
                    var s = Segment(p, curve[i], curve[i + 1]);
                    if (s < d) {
                        d = s;
                        along = i / (float)(curve.Count - 1);
                    }
                }
                if (threeWire) {
                    // Three wires: the distance across picks the color.
                    c = Over(c, black, 9f - d + 0.5f);
                    var n = curve[Mathf.Clamp((int)(along * (curve.Count - 1)), 0, curve.Count - 2)];
                    var m = curve[Mathf.Clamp((int)(along * (curve.Count - 1)) + 1, 0, curve.Count - 1)];
                    var side = Vector2.Perpendicular((m - n).normalized);
                    var across = Vector2.Dot(p - n, side);
                    if (d < 9f && across > 3f) c = Over(c, red, 9f - d + 0.5f);
                    if (d < 9f && across < -3f) c = Over(c, white, 9f - d + 0.5f);
                }
                else c = Over(c, black, 7f - d + 0.5f);
                foreach (var end in new[] { a, b }) {
                    var q = new Vector2(Mathf.Abs(p.x - end.x), Mathf.Abs(p.y - end.y));
                    var inside = Mathf.Min(14f - q.x, 11f - q.y);
                    c = Over(c, edge, inside + 2.5f);
                    c = Over(c, plug, inside + 0.5f);
                }
                return c;
            });
        }

        private static Texture2D Tube() {
            var tube = new Color(0.16f, 0.16f, 0.18f, 1f);
            var edge = new Color(0.45f, 0.46f, 0.5f, 1f);
            var red = new Color(0.86f, 0.14f, 0.14f, 1f);
            var body = new Color(0.7f, 0.72f, 0.76f, 1f);
            // A curve from lower left to upper right.
            var curve = new List<Vector2>();
            for (var i = 0; i <= 40; i++) {
                var t = i / 40f;
                curve.Add(new Vector2(Mathf.Lerp(20f, 108f, t), 64f + 34f * Mathf.Sin((t - 0.5f) * Mathf.PI) * -1f + 8f * Mathf.Sin(t * Mathf.PI * 2f)));
            }
            return Paint(p => {
                var c = new Color(0f, 0f, 0f, 0f);
                var d = float.MaxValue;
                for (var i = 0; i + 1 < curve.Count; i++) d = Mathf.Min(d, Segment(p, curve[i], curve[i + 1]));
                c = Over(c, edge, 9.5f - d + 0.5f);
                c = Over(c, tube, 7.5f - d + 0.5f);
                // Fittings at both ends: a gray body and a red collar.
                foreach (var (end, dir) in new[] { (curve[0], (curve[0] - curve[2]).normalized), (curve[^1], (curve[^1] - curve[^3]).normalized) }) {
                    var collar = Segment(p, end - dir * 2f, end + dir * 8f);
                    var fitting = Segment(p, end + dir * 8f, end + dir * 22f);
                    c = Over(c, body, 10f - fitting + 0.5f);
                    c = Over(c, red, 12f - collar + 0.5f);
                }
                return c;
            });
        }
    }
}
