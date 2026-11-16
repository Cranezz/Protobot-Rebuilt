using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Scripting;
using UnityEngine;
using static Protobot.RobotCode.VexModule;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9): the V5 Brain's touch screen as VEXcode V5 Python draws on it: 480 x 240 pixels, origin top left.
    /// Shapes (pixel, line, rectangle, circle) are drawn into a pixel buffer; text (print in the 12 x 48 grid of
    /// MONO20, or print_at a pixel) is kept as text items over it (a newer text on the same spot replaces the older,
    /// a filled shape over a text covers it, clear_screen clears both). Pen/fill colors, pen width, fonts, origin,
    /// clip region, render() double buffering, and touch (pressing, x/y_position, pressed/released callbacks: the
    /// screen is clicked in the code window's Brain screen tab) work as on the robot.
    /// Nothing here runs unless the program draws: a drawing call is a few microseconds; the view uploads the
    /// texture at most once a frame and only when it changed and is shown.
    /// </summary>
    public sealed class BrainCanvas : VexObject {
        public const int Width = 480, Height = 240;
        public override string TypeName => "BrainScreen";

        public struct TextItem {
            public float x, y;   // top left, pixels
            public string text;
            public float size;   // font height, pixels
            public bool mono;
            public Color32 color, background;   // background alpha 0: see-through
            public float width;
        }

        public sealed class Layer {
            public readonly Color32[] pixels = new Color32[Width * Height];
            public readonly List<TextItem> texts = new();

            public void CopyTo(Layer other) {
                Array.Copy(pixels, other.pixels, pixels.Length);
                other.texts.Clear();
                other.texts.AddRange(texts);
            }
        }

        /// <summary>What's shown (with double buffering, what was last render()ed).</summary>
        public readonly Layer front = new();
        private Layer back;   // drawing goes here once render() was called
        private Layer Draw => back ?? front;
        /// <summary>Goes up on every change shown (the view redraws when it moves).</summary>
        public int Version { get; private set; }
        public static event Action Changed;

        private Color32 pen = White, fill = Black;
        private int penWidth = 1;
        private float fontSize = 20f;
        private bool mono = true;
        private int originX, originY;
        private RectInt clip = new(0, 0, Width, Height);
        private int row, col;   // cursor, 0-based, in the current font's cells

        // Touch (set by the view).
        public bool Pressing { get; private set; }
        public int TouchX { get; private set; }
        public int TouchY { get; private set; }
        private readonly List<object> onPressed = new(), onReleased = new();
        private readonly RobotProgram program;

        private static readonly Color32 White = new(255, 255, 255, 255), Black = new(0, 0, 0, 255), Clear = new(0, 0, 0, 0);

        internal BrainCanvas(RobotProgram p) {
            program = p;
            Fill(front.pixels, Black);
            Method("print", a => { Print(Text(a), true); return PyNone.Value; });
            Method("print_at", a => {
                var x = a.Number(-1, "x", 0) + originX;
                var y = a.Number(-1, "y", 0) + originY;
                var opaque = !a.Has(-1, "opaque") || Ops.Truthy(a.Get(-1, "opaque"));
                // print_at's y is the text's baseline.
                AddText(new TextItem { x = (float)x, y = (float)y - fontSize * 0.8f, text = Text(a), size = fontSize, mono = mono, color = pen, background = opaque ? fill : Clear });
                return PyNone.Value;
            });
            Method("set_cursor", a => {
                row = Math.Max(0, (int)a.Number(0, "row", 1) - 1);
                col = Math.Max(0, (int)a.Number(1, "column", 1) - 1);
                return PyNone.Value;
            });
            Method("next_row", _ => { NewLine(); return PyNone.Value; });
            Method("new_line", _ => { NewLine(); return PyNone.Value; });
            Method("row", _ => (long)row + 1);
            Method("column", _ => (long)col + 1);
            Method("clear_screen", a => {
                var color = a.Count > 0 ? ColorOf(a.pos[0]) : Black;
                Fill(Draw.pixels, color);
                Draw.texts.Clear();
                row = col = 0;
                Touched();
                return PyNone.Value;
            });
            Method("clear_row", a => ClearRow(a));
            Method("clear_line", a => ClearRow(a));
            Method("set_font", a => {
                var f = a.Need(0, "font") as PyEnum;
                var name = f?.name ?? "MONO20";
                mono = name.StartsWith("MONO");
                fontSize = Mathf.Clamp(f?.value ?? 20, 8, 80);
                return PyNone.Value;
            });
            Method("set_pen_width", a => { penWidth = Mathf.Clamp((int)a.Number(0, "width", 1), 0, 32); return PyNone.Value; });
            Method("set_pen_color", a => { pen = ColorOf(a.Need(0, "color")); return PyNone.Value; });
            Method("set_fill_color", a => { fill = ColorOf(a.Need(0, "color")); return PyNone.Value; });
            Method("set_origin", a => {
                originX = (int)a.Number(0, "x", 0);
                originY = (int)a.Number(1, "y", 0);
                return PyNone.Value;
            });
            Method("set_clip_region", a => {
                var x = (int)a.Number(0, "x", 0);
                var y = (int)a.Number(1, "y", 0);
                var w = (int)a.Number(2, "width", Width);
                var h = (int)a.Number(3, "height", Height);
                var xMin = Mathf.Clamp(x, 0, Width);
                var yMin = Mathf.Clamp(y, 0, Height);
                clip = new RectInt(xMin, yMin, Mathf.Clamp(x + w, 0, Width) - xMin, Mathf.Clamp(y + h, 0, Height) - yMin);
                return PyNone.Value;
            });
            Method("draw_pixel", a => {
                Plot((int)a.Number(0, "x", 0) + originX, (int)a.Number(1, "y", 0) + originY, pen);
                Touched();
                return PyNone.Value;
            });
            Method("draw_line", a => {
                Line((int)a.Number(0, "start_x", 0) + originX, (int)a.Number(1, "start_y", 0) + originY,
                     (int)a.Number(2, "end_x", 0) + originX, (int)a.Number(3, "end_y", 0) + originY);
                Touched();
                return PyNone.Value;
            });
            Method("draw_rectangle", a => {
                var color = a.Get(4, "color");
                Rectangle((int)a.Number(0, "x", 0) + originX, (int)a.Number(1, "y", 0) + originY,
                          (int)a.Number(2, "width", 0), (int)a.Number(3, "height", 0), color == null || color is PyNone ? fill : ColorOf(color));
                Touched();
                return PyNone.Value;
            });
            Method("draw_circle", a => {
                var color = a.Get(3, "color");
                Circle((int)a.Number(0, "x", 0) + originX, (int)a.Number(1, "y", 0) + originY, (int)a.Number(2, "radius", 0),
                       color == null || color is PyNone ? fill : ColorOf(color));
                Touched();
                return PyNone.Value;
            });
            Method("draw_image_from_file", _ => {
                program.Warn("draw_image_from_file", "brain.screen.draw_image_from_file needs the Brain's SD card: the simulated Brain has none (nothing is drawn)");
                return false;
            });
            Method("render", _ => {
                // VEXcode: the first render() turns double buffering on; each one after shows what was drawn.
                if (back == null) {
                    back = new Layer();
                    front.CopyTo(back);
                }
                else {
                    back.CopyTo(front);
                    Version++;
                    Changed?.Invoke();
                }
                return true;
            });
            Method("get_string_width", a => (long)Mathf.RoundToInt(TextWidth(a.vm.StrOf(a.Need(0, "string")), fontSize, mono)));
            Method("get_string_height", _ => (long)Mathf.RoundToInt(fontSize));
            Method("pressing", _ => Pressing);
            Method("x_position", _ => (long)TouchX);
            Method("y_position", _ => (long)TouchY);
            Method("pressed", a => { onPressed.Add(Callback(a)); KeepAlive(); return PyNone.Value; });
            Method("released", a => { onReleased.Add(Callback(a)); KeepAlive(); return PyNone.Value; });
        }

        // A program waiting for touches keeps running (as on the robot) after its main code ends.
        private bool keptAlive;

        private void KeepAlive() {
            if (keptAlive) return;
            keptAlive = true;
            program.pollers.Add(() => { });
        }

        private static object Callback(Args a) {
            var cb = a.Need(0, "callback");
            var args = a.Get(1, "arg");
            if (args == null || args is PyNone) return cb;
            var list = Ops.Iterate(args).ToArray();
            return new PyBuiltin("callback", b => b.vm.CallSync(cb, list));
        }

        // ------------------------------------------------------------------ touch (from the view)

        public void Touch(bool down, int x, int y) {
            TouchX = Mathf.Clamp(x, 0, Width - 1);
            TouchY = Mathf.Clamp(y, 0, Height - 1);
            if (down == Pressing) return;
            Pressing = down;
            foreach (var cb in down ? onPressed : onReleased) program.vm.Start(cb, null, down ? "screen pressed" : "screen released");
        }

        // ------------------------------------------------------------------ text

        private string Text(Args a) {
            var sep = a.kw != null && a.kw.TryGetValue("sep", out var s) ? Ops.Str(s) : "";
            var precision = a.kw != null && a.kw.TryGetValue("precision", out var p) ? (int)Ops.ToLong(p) : 2;
            return string.Join(sep, a.pos.Select(o => o is double d ? d.ToString("F" + Math.Max(0, precision), Ops.Inv) : a.vm.StrOf(o)));
        }

        private float CellWidth => mono ? fontSize * 0.5f : fontSize * 0.55f;

        public static float TextWidth(string text, float size, bool mono) => text.Length * (mono ? size * 0.5f : size * 0.5f);

        private void Print(string text, bool atCursor) {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++) {
                if (i > 0) NewLine();
                if (lines[i].Length == 0) continue;
                AddText(new TextItem { x = col * CellWidth, y = row * fontSize, text = lines[i], size = fontSize, mono = mono, color = pen, background = Clear });
                col += lines[i].Length;
            }
        }

        private void NewLine() {
            row++;
            col = 0;
            // Past the bottom: the screen scrolls up a row, as the Brain's does.
            var rows = Mathf.Max(1, Mathf.FloorToInt(Height / fontSize));
            if (row < rows) return;
            row = rows - 1;
            var texts = Draw.texts;
            for (var i = texts.Count - 1; i >= 0; i--) {
                var t = texts[i];
                t.y -= fontSize;
                if (t.y < -t.size * 0.5f) texts.RemoveAt(i);
                else texts[i] = t;
            }
            Touched();
        }

        private void AddText(TextItem item) {
            item.width = TextWidth(item.text, item.size, item.mono);
            var texts = Draw.texts;
            // A text over the same spot replaces what was there (a loop printing a value in place stays one text).
            for (var i = texts.Count - 1; i >= 0; i--) {
                var t = texts[i];
                var sameRow = Mathf.Abs(t.y - item.y) < Mathf.Min(t.size, item.size) * 0.5f;
                var overlaps = t.x < item.x + item.width - 0.5f && item.x < t.x + t.width - 0.5f;
                if (sameRow && overlaps) {
                    // Keep the part of the old text left of the new one (printing over the end of a line).
                    var keep = item.mono && t.mono && Mathf.Approximately(t.size, item.size) ? Mathf.FloorToInt((item.x - t.x) / CellWidthOf(t) + 0.01f) : 0;
                    var after = Mathf.CeilToInt((item.x + item.width - t.x) / CellWidthOf(t) - 0.01f);
                    var tail = item.mono && t.mono && after < t.text.Length ? t.text.Substring(Mathf.Max(0, after)) : "";
                    texts.RemoveAt(i);
                    if (keep > 0) texts.Add(new TextItem { x = t.x, y = t.y, text = t.text.Substring(0, Mathf.Min(keep, t.text.Length)), size = t.size, mono = t.mono, color = t.color, background = t.background, width = keep * CellWidthOf(t) });
                    if (tail.Length > 0) {
                        var tx = t.x + after * CellWidthOf(t);
                        texts.Add(new TextItem { x = tx, y = t.y, text = tail, size = t.size, mono = t.mono, color = t.color, background = t.background, width = tail.Length * CellWidthOf(t) });
                    }
                }
            }
            if (item.background.a > 0) FillRect(Mathf.RoundToInt(item.x), Mathf.RoundToInt(item.y), Mathf.CeilToInt(item.width), Mathf.CeilToInt(item.size), item.background);
            if (item.x < clip.xMax && item.y < clip.yMax && item.x + item.width > clip.xMin && item.y + item.size > clip.yMin) texts.Add(item);
            if (texts.Count > 600) texts.RemoveRange(0, texts.Count - 600);   // a runaway print loop
            Touched();
        }

        private static float CellWidthOf(TextItem t) => t.mono ? t.size * 0.5f : t.size * 0.5f;

        private object ClearRow(Args a) {
            var r = a.Count > 0 && !(a.pos[0] is PyNone) ? Math.Max(0, (int)a.Number(0, "row", 1) - 1) : row;
            var color = a.Count > 1 ? ColorOf(a.pos[1]) : Black;
            var top = r * fontSize;
            FillRect(0, Mathf.RoundToInt(top), Width, Mathf.RoundToInt(fontSize), color);
            Draw.texts.RemoveAll(t => t.y + t.size * 0.5f > top && t.y + t.size * 0.5f < top + fontSize);
            Touched();
            return PyNone.Value;
        }

        // ------------------------------------------------------------------ shapes

        private void Touched() {
            if (back != null) return;   // double buffered: shown at the next render()
            Version++;
            Changed?.Invoke();
        }

        private static void Fill(Color32[] pixels, Color32 c) {
            for (var i = 0; i < pixels.Length; i++) pixels[i] = c;
        }

        private void Plot(int x, int y, Color32 c) {
            if (c.a == 0 || x < clip.xMin || x >= clip.xMax || y < clip.yMin || y >= clip.yMax) return;
            Draw.pixels[(Height - 1 - y) * Width + x] = c;   // (textures count rows from the bottom)
        }

        private void FillRect(int x, int y, int w, int h, Color32 c) {
            if (c.a == 0) return;
            var x0 = Mathf.Max(x, clip.xMin);
            var x1 = Mathf.Min(x + w, clip.xMax);
            var y0 = Mathf.Max(y, clip.yMin);
            var y1 = Mathf.Min(y + h, clip.yMax);
            var px = Draw.pixels;
            for (var yy = y0; yy < y1; yy++) {
                var line = (Height - 1 - yy) * Width;
                for (var xx = x0; xx < x1; xx++) px[line + xx] = c;
            }
        }

        private void Rectangle(int x, int y, int w, int h, Color32 inside) {
            if (w < 0) { x += w; w = -w; }
            if (h < 0) { y += h; h = -h; }
            FillRect(x, y, w, h, inside);
            if (inside.a > 0) CoverTexts(x, y, w, h);
            if (penWidth <= 0 || pen.a == 0) return;
            var p = Mathf.Min(penWidth, Mathf.Max(1, Mathf.Min(w, h) / 2 + 1));
            FillRect(x, y, w, p, pen);
            FillRect(x, y + h - p, w, p, pen);
            FillRect(x, y, p, h, pen);
            FillRect(x + w - p, y, p, h, pen);
        }

        private void Circle(int cx, int cy, int r, Color32 inside) {
            if (r <= 0) return;
            var outer = r + 0.5f;
            var inner = penWidth > 0 && pen.a > 0 ? Mathf.Max(0f, r + 0.5f - penWidth) : outer;
            for (var y = -r - 1; y <= r + 1; y++)
                for (var x = -r - 1; x <= r + 1; x++) {
                    var d = Mathf.Sqrt(x * x + y * y);
                    if (d > outer) continue;
                    Plot(cx + x, cy + y, d >= inner ? pen : inside);
                }
            if (inside.a > 0) CoverTexts(cx - r * 0.7f, cy - r * 0.7f, r * 1.4f, r * 1.4f);
        }

        private void Line(int x0, int y0, int x1, int y1) {
            var w = Mathf.Max(1, penWidth);
            var dx = Mathf.Abs(x1 - x0);
            var dy = -Mathf.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;
            var guard = 0;
            while (guard++ < 4000) {
                if (w == 1) Plot(x0, y0, pen);
                else FillRect(x0 - w / 2, y0 - w / 2, w, w, pen);
                if (x0 == x1 && y0 == y1) break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // A filled shape drawn over a text covers it (texts sit over the pixels).
        private void CoverTexts(float x, float y, float w, float h) {
            Draw.texts.RemoveAll(t => t.x >= x - 1 && t.y >= y - 1 && t.x + t.width <= x + w + 1 && t.y + t.size <= y + h + 1);
        }

        // ------------------------------------------------------------------ colors

        /// <summary>A VEXcode color: Color.RED, Color(r, g, b), Color(0xFF0000) or "#FF0000".</summary>
        public static Color32 ColorOf(object o) {
            switch (o) {
                case PyEnum e when e.type == "Color":
                    return e.name switch {
                        "RED" => new Color32(255, 0, 0, 255),
                        "GREEN" => new Color32(0, 255, 0, 255),
                        "BLUE" => new Color32(0, 0, 255, 255),
                        "YELLOW" => new Color32(255, 255, 0, 255),
                        "ORANGE" => new Color32(255, 165, 0, 255),
                        "PURPLE" => new Color32(255, 0, 255, 255),
                        "CYAN" => new Color32(0, 255, 255, 255),
                        "WHITE" => White,
                        "BLACK" => Black,
                        "TRANSPARENT" => Clear,
                        _ => White,
                    };
                case VexColorValue v:
                    return v.color;
                case string s when s.StartsWith("#") && s.Length == 7 && int.TryParse(s.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out var hex):
                    return new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);
                case long l:
                    return new Color32((byte)(l >> 16), (byte)(l >> 8), (byte)l, 255);
                default:
                    throw new PyError("TypeError", "a color is Color.RED, Color(255, 0, 0), Color(0xFF0000) or \"#FF0000\", not " + Ops.Describe(o));
            }
        }
    }

    /// <summary>V2 (Phase 9): Color(r, g, b) / Color(0xRRGGBB) / Color("#RRGGBB"), as VEXcode makes custom colors.</summary>
    public sealed class VexColorValue : VexObject {
        public readonly Color32 color;
        public override string TypeName => "Color";

        public VexColorValue(Color32 color) {
            this.color = color;
            Method("rgb", _ => (long)((color.r << 16) | (color.g << 8) | color.b));
            Method("is_transparent", _ => color.a == 0);
        }

        public static VexColorValue Make(Args a) {
            if (a.Count >= 3) return new VexColorValue(new Color32((byte)Mathf.Clamp((int)a.Number(0, "r", 0), 0, 255), (byte)Mathf.Clamp((int)a.Number(1, "g", 0), 0, 255),
                                                                   (byte)Mathf.Clamp((int)a.Number(2, "b", 0), 0, 255), 255));
            if (a.Count == 1) return new VexColorValue(BrainCanvas.ColorOf(a.pos[0]));
            return new VexColorValue(new Color32(0, 0, 0, 255));
        }
    }
}
