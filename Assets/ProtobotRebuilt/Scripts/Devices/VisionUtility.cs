using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.Devices {
    /// <summary>
    /// V2: the Vision Utility (as VEXcode's): what a Vision Sensor sees, live or frozen; drag a box over a colored
    /// object and Set a signature (1-7) from it; each signature's name, range (how far round its color it reaches) and
    /// Clear; the sensor's brightness. Signatures are kept on the sensor (the part's "vsig:" state), so Insert devices
    /// writes them into the program (vision_5__RED = Signature(...)) and PROS programs find them on the sensor.
    /// </summary>
    public static class VisionUtility {
        private const float Scale = 2f;   // the picture at twice the sensor's 316 x 212

        public static void Open(VisionSensor sensor) {
            if (sensor == null || sensor.part == null) return;
            var part = sensor.part;
            var dialog = Dialog.Open("Vision Utility · port " + sensor.PortLabel, new Vector2(1060f, 600f));
            var body = dialog.Body;
            var w = VisionSensor.Width * Scale;
            var h = VisionSensor.Height * Scale;

            // The picture (left), with the found objects' boxes and the box being dragged over it.
            var frame = Ui.Image(body, "Picture", Color.black, 6f, true).rectTransform;
            frame.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(w, h));
            var raw = Ui.Rect("Image", frame).Fill().gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var overlay = Ui.Rect("Boxes", frame).Fill();
            overlay.gameObject.AddComponent<CanvasRenderer>();
            var shapes = ShapeGraphic.Create(overlay);
            shapes.raycastTarget = false;
            var labels = Ui.Rect("Labels", frame).Fill();
            var input = frame.gameObject.AddComponent<BoxDrag>();
            input.frame = frame;

            var status = Ui.Text(body, "Status", "", Theme.FontSmall, Theme.TextDim);
            status.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -h - 8f), new Vector2(w, 40f));

            Texture2D frozenTexture = null;
            Color32[] frozen = null;
            void Freeze(bool on) {
                if (on) {
                    sensor.RenderNow();
                    if (sensor.pixels == null) return;
                    frozen = (Color32[])sensor.pixels.Clone();
                    if (frozenTexture == null) frozenTexture = new Texture2D(VisionSensor.Width, VisionSensor.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    var flipped = new Color32[frozen.Length];   // (a texture's row 0 is its bottom)
                    for (var row = 0; row < VisionSensor.Height; row++)
                        System.Array.Copy(frozen, row * VisionSensor.Width, flipped, (VisionSensor.Height - 1 - row) * VisionSensor.Width, VisionSensor.Width);
                    frozenTexture.SetPixels32(flipped);
                    frozenTexture.Apply();
                }
                else frozen = null;
            }

            // The signatures (right).
            var side = Ui.Rect("Signatures", body).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(1020f - w - 24f, h + 48f));
            var column = Ui.Column(side, 4f);
            column.childForceExpandWidth = true;
            Dialog.Heading(side, "Signatures");
            System.Action rebuild = null;
            var live = new System.Collections.Generic.List<System.Action>();
            rebuild = () => {
                live.Clear();
                for (var i = side.childCount - 1; i >= 1; i--) Object.Destroy(side.GetChild(i).gameObject);
                var sigs = VisionSensor.Saved(part);
                for (var i = 0; i < 7; i++) {
                    var id = i + 1;
                    var s = sigs[i];
                    var row = Ui.Rect("Signature " + id, side);
                    Ui.Row(row, 5f);
                    row.Size(-1, 32f);
                    var swatch = Ui.Image(row, "Color", s.HasValue ? SigColor(s.Value) : Theme.Field, 5f);
                    swatch.Size(26f, 26f);
                    var number = Ui.Text(swatch.transform, "Id", id.ToString(), Theme.FontSmall, s.HasValue ? Color.white : Theme.TextFaint, Theme.Medium, TextAlignmentOptions.Center);
                    number.rectTransform.Fill();
                    if (s.HasValue) {
                        var name = Ui.Field(row, "Name", s.Value.name, value => {
                            var all = VisionSensor.Saved(part);
                            if (!all[id - 1].HasValue) return;
                            var v = all[id - 1].Value;
                            v.name = VisionSignature.Clean(value);
                            all[id - 1] = v;
                            VisionSensor.Save(part, all);
                            rebuild();
                        }, Theme.FontSmall);
                        name.Size(-1, 26f, 1f);
                        Ui.Tip(name, "Name", null, "The signature's name in code: vision_" + sensor.port + "__" + s.Value.name + ".");
                        void Range(float by) {
                            var all = VisionSensor.Saved(part);
                            var v = all[id - 1].Value;
                            v.range = Mathf.Clamp(Mathf.Round((v.range + by) * 10f) / 10f, 0.5f, 11f);
                            all[id - 1] = v;
                            VisionSensor.Save(part, all);
                            Audio.Sounds.Play(Audio.Sounds.Tick);
                            rebuild();
                        }
                        var less = Ui.Button(row, "Narrower", "−", () => Range(-0.5f), false, 5f, Theme.FontBody);
                        less.Size(24f, 26f);
                        var range = Ui.Text(row, "Range", s.Value.range.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
                        range.Size(30f, 26f);
                        Ui.Tip(range, "Range", null, "How far round its color it reaches (VEX's range, 0.5-11; 2.5 to start): wider finds the object in more light and shade, but also other colors near it.");
                        var more = Ui.Button(row, "Wider", "+", () => Range(0.5f), false, 5f, Theme.FontBody);
                        more.Size(24f, 26f);
                    }
                    else {
                        var empty = Ui.Text(row, "Empty", "Empty", Theme.FontSmall, Theme.TextFaint);
                        empty.Size(-1, 26f, 1f);
                    }
                    var set = Ui.Button(row, "Set", "Set", () => {
                        var pixels = frozen ?? sensor.pixels;
                        if (pixels == null) { Interface.Interface.Say("The sensor has no picture yet.", false); return; }
                        if (!input.box.HasValue) { Interface.Interface.Say("Drag a box over the object's color in the picture first.", false); return; }
                        var all = VisionSensor.Saved(part);
                        var made = VisionSensor.Make(pixels, input.box.Value, id, all[id - 1]?.name ?? "SIG_" + id);
                        if (!made.HasValue) { Interface.Interface.Say("Not enough color in that box: drag it over the object (dark pixels have no color).", false); return; }
                        all[id - 1] = made;
                        VisionSensor.Save(part, all);
                        input.box = null;
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                        rebuild();
                    }, !s.HasValue, 5f, Theme.FontSmall);
                    set.Size(42f, 26f);
                    Ui.Tip(set, "Set signature " + id, null, "Makes it from the colors in the box dragged over the picture.");
                    if (s.HasValue) {
                        var clear = Ui.Button(row, "Clear", "×", () => {
                            var all = VisionSensor.Saved(part);
                            all[id - 1] = null;
                            VisionSensor.Save(part, all);
                            rebuild();
                        }, false, 5f, Theme.FontBody);
                        clear.Size(26f, 26f);
                        Ui.Tip(clear, "Clear", null, "Removes signature " + id + ".");
                    }
                }
                // Brightness (the sensor's exposure).
                var bright = Ui.Rect("Brightness", side);
                Ui.Row(bright, 5f);
                bright.Size(-1, 34f);
                Ui.Text(bright, "Label", "Brightness", Theme.FontSmall, Theme.TextDim).Size(-1, 30f, 1f);
                void Bright(int by) {
                    var b = Mathf.Clamp(VisionSensor.SavedBrightness(part) + by, 1, 100);
                    Motion.Dynamics.SetToken(part, "vbright", b == 50 ? null : b.ToString());
                    sensor.brightness = b;
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    rebuild();
                }
                Ui.Button(bright, "Darker", "−", () => Bright(-5), false, 5f, Theme.FontBody).Size(24f, 26f);
                Ui.Text(bright, "Value", VisionSensor.SavedBrightness(part).ToString(), Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.Center).Size(34f, 26f);
                Ui.Button(bright, "Brighter", "+", () => Bright(5), false, 5f, Theme.FontBody).Size(24f, 26f);
                var help = Ui.Text(side, "Help", "Freeze the picture, drag a box over an object, then Set a signature. Insert devices (code window) writes them into the program; PROS finds them on the sensor.", Theme.FontSmall - 1f, Theme.TextFaint);
                help.Size(-1, 52f);
                help.textWrappingMode = TextWrappingModes.Normal;
            };
            rebuild();

            // Footer: freeze / live, done.
            Button freeze = null;
            freeze = Ui.Button(dialog.Footer, "Freeze", "Freeze", () => {
                Freeze(frozen == null);
                freeze.GetComponentInChildren<TextMeshProUGUI>().text = frozen != null ? "Live" : "Freeze";
            }, false);
            freeze.Size(96f, 34f);
            Ui.Tip(freeze, "Freeze", null, "Holds the picture still so a box can be dragged over a moving object.");
            Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true).Size(96f, 34f);

            // Live: the picture, the boxes, the status line.
            var updater = dialog.gameObject.AddComponent<Updater>();
            updater.tick = () => {
                if (part == null) { dialog.Close(); return; }
                sensor.Want();
                raw.texture = frozen != null ? frozenTexture : sensor.Picture;
                raw.color = raw.texture != null ? Color.white : Color.black;
                var r = overlay.rect;
                Vector2 At(float x, float y) => new(r.xMin + x / VisionSensor.Width * r.width, r.yMax - y / VisionSensor.Height * r.height);
                shapes.Clear();
                var sigs = sensor.Signatures;
                var blobs = frozen != null ? VisionSensor.FindIn(frozen, sigs, sensor.codes) : sensor.blobs;
                foreach (var b in blobs.Take(24)) {
                    var color = b.code ? Theme.Warning : sigs[Mathf.Clamp(b.id, 1, 7) - 1].HasValue ? SigColor(sigs[b.id - 1].Value) : Theme.Accent;
                    Box(shapes, At(b.x, b.y + b.height), At(b.x + b.width, b.y), color, 2f);
                }
                if (input.box.HasValue) {
                    var bx = input.box.Value;
                    Outline(shapes, At(bx.xMin, bx.yMax), At(bx.xMax, bx.yMin), Color.white, 1.5f);
                }
                status.text = (frozen != null ? "Frozen. " : "Live. ") + (blobs.Count == 0 ? "No objects of its signatures." : blobs.Count + " object" + (blobs.Count == 1 ? "" : "s") + ": " +
                    string.Join(", ", blobs.Take(4).Select(b => (b.code ? "code " + System.Convert.ToString(b.id, 8) : "SIG " + b.id) + " " + b.width + "×" + b.height + " at " + b.CenterX + "," + b.CenterY)));
            };
            dialog.OnClose += () => { if (frozenTexture != null) Object.Destroy(frozenTexture); };
        }

        /// <summary>A signature's color as a swatch: its mean u/v at middle brightness.</summary>
        public static Color SigColor(VisionSignature s) {
            // u = (r - g) / sum, v = (b - g) / sum, sum = r + g + b: solve with sum = 1.
            var u = s.uMean / 32768f;
            var v = s.vMean / 32768f;
            var g = (1f - u - v) / 3f;
            var c = new Color(g + u, g, g + v);
            var most = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return most > 0f ? new Color(Mathf.Clamp01(c.r / most * 0.85f), Mathf.Clamp01(c.g / most * 0.85f), Mathf.Clamp01(c.b / most * 0.85f)) : Color.gray;
        }

        /// <summary>An object's box: a white outline just outside it (a box in its own color vanished against a flat
        /// object) with a tag in its signature's color at the top left.</summary>
        public static void Box(ShapeGraphic s, Vector2 min, Vector2 max, Color color, float width) {
            min -= new Vector2(2f, 2f);
            max += new Vector2(2f, 2f);
            Outline(s, min, max, new Color(1f, 1f, 1f, 0.9f), width);
            s.Rect(new Vector2(min.x, max.y - 9f), new Vector2(min.x + 9f, max.y), color);
        }

        private static void Outline(ShapeGraphic s, Vector2 min, Vector2 max, Color color, float width) {
            s.Line(new Vector2(min.x, min.y), new Vector2(max.x, min.y), color, width);
            s.Line(new Vector2(max.x, min.y), new Vector2(max.x, max.y), color, width);
            s.Line(new Vector2(max.x, max.y), new Vector2(min.x, max.y), color, width);
            s.Line(new Vector2(min.x, max.y), new Vector2(min.x, min.y), color, width);
        }

        /// <summary>Dragging a box over the picture (in the sensor's pixels).</summary>
        private class BoxDrag : MonoBehaviour, IPointerDownHandler, IDragHandler {
            public RectTransform frame;
            public RectInt? box;
            private Vector2Int start;

            private Vector2Int Pixel(PointerEventData e) {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, e.position, e.pressEventCamera, out var local);
                var r = frame.rect;
                var x = Mathf.Clamp(Mathf.RoundToInt((local.x - r.xMin) / r.width * VisionSensor.Width), 0, VisionSensor.Width);
                var y = Mathf.Clamp(Mathf.RoundToInt((r.yMax - local.y) / r.height * VisionSensor.Height), 0, VisionSensor.Height);
                return new Vector2Int(x, y);
            }

            public void OnPointerDown(PointerEventData e) {
                start = Pixel(e);
                box = null;
            }

            public void OnDrag(PointerEventData e) {
                var p = Pixel(e);
                var min = Vector2Int.Min(start, p);
                var max = Vector2Int.Max(start, p);
                box = max.x - min.x >= 2 && max.y - min.y >= 2 ? new RectInt(min, max - min) : null;
            }
        }

        private class Updater : MonoBehaviour {
            public System.Action tick;
            private void LateUpdate() => tick?.Invoke();
        }
    }
}
