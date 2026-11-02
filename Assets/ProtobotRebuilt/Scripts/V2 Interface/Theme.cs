using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Protobot.Interface {
    // V2: colours, sizes and shapes shared by the new interface (tool rail, tool options, saw, measure, paint,
    // parts browser). Dark graphite panels with the original's blue as the accent.
    public static class Theme {
        public static readonly Color Backdrop = new(0f, 0f, 0f, 0.45f);
        public static readonly Color Panel = new(0.106f, 0.114f, 0.133f, 0.97f);
        public static readonly Color PanelRaised = new(0.149f, 0.161f, 0.188f, 1f);
        public static readonly Color Field = new(0.078f, 0.086f, 0.102f, 1f);
        public static readonly Color Hover = new(0.212f, 0.231f, 0.271f, 1f);
        public static readonly Color Pressed = new(0.255f, 0.278f, 0.325f, 1f);
        public static readonly Color Border = new(1f, 1f, 1f, 0.08f);
        public static readonly Color Divider = new(1f, 1f, 1f, 0.06f);
        public static readonly Color Accent = new(0.157f, 0.522f, 0.957f, 1f);
        public static readonly Color AccentHover = new(0.255f, 0.6f, 1f, 1f);
        public static readonly Color AccentSoft = new(0.157f, 0.522f, 0.957f, 0.18f);
        public static readonly Color Text = new(0.93f, 0.94f, 0.96f, 1f);
        public static readonly Color TextDim = new(0.62f, 0.65f, 0.7f, 1f);
        public static readonly Color TextFaint = new(0.45f, 0.48f, 0.53f, 1f);
        public static readonly Color Danger = new(0.93f, 0.33f, 0.31f, 1f);
        public static readonly Color Success = new(0.27f, 0.75f, 0.45f, 1f);
        public static readonly Color Warning = new(0.98f, 0.73f, 0.23f, 1f);
        public static readonly Color AxisX = new(0.93f, 0.29f, 0.29f, 1f);
        public static readonly Color AxisY = new(0.38f, 0.8f, 0.36f, 1f);
        public static readonly Color AxisZ = new(0.27f, 0.55f, 0.98f, 1f);
        public static readonly Color Dimension = new(1f, 0.78f, 0.2f, 1f);

        public static Color Axis(int axis) => axis == 0 ? AxisX : axis == 1 ? AxisY : AxisZ;

        public const float Radius = 8f;
        public const float RadiusSmall = 5f;
        public const float RowHeight = 32f;
        public const float FontBody = 16f;
        public const float FontSmall = 14f;
        public const float FontTitle = 18f;

        public static TMP_FontAsset Regular, Medium, Bold;

        // A white rounded rectangle, 9-sliced (Image.type = Sliced); pixelsPerUnitMultiplier sets the radius.
        private static Sprite rounded, circle;
        private const int SpriteRadius = 32;

        public static Sprite Rounded => rounded ??= MakeRounded(SpriteRadius);
        public static Sprite Circle => circle ??= MakeCircle(64);

        /// <summary>pixelsPerUnitMultiplier for a corner radius in canvas pixels.</summary>
        public static float RadiusMultiplier(float radius) => SpriteRadius / Mathf.Max(radius, 0.5f);

        private static Sprite MakeRounded(int r) {
            var size = r * 2 + 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "V2 Rounded"
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++) {
                    // Distance outside the rounded square (corners only), anti-aliased over one pixel.
                    var cx = Mathf.Clamp(x + 0.5f, r, size - r);
                    var cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    var a = Mathf.Clamp01(r - d + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
        }

        private static Sprite MakeCircle(int size) {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "V2 Circle"
            };
            var pixels = new Color32[size * size];
            var c = size / 2f;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++) {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(c - d) * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // Recently used colours (paint), newest first, kept between sessions.
        private const string RecentKey = "V2.RecentColors";
        private static List<Color> recentColors;

        public static List<Color> RecentColors {
            get {
                if (recentColors != null)
                    return recentColors;
                recentColors = new List<Color>();
                foreach (var code in PlayerPrefs.GetString(RecentKey, "").Split(';'))
                    if (ColorUtility.TryParseHtmlString("#" + code, out var c))
                        recentColors.Add(c);
                return recentColors;
            }
        }

        public static void AddRecentColor(Color color) {
            var list = RecentColors;
            list.RemoveAll(c => ColorUtility.ToHtmlStringRGB(c) == ColorUtility.ToHtmlStringRGB(color));
            list.Insert(0, color);
            if (list.Count > 16)
                list.RemoveRange(16, list.Count - 16);
            PlayerPrefs.SetString(RecentKey, string.Join(";", list.ConvertAll(ColorUtility.ToHtmlStringRGB)));
        }
    }
}
