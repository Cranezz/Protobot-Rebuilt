using System.Linq;
using Protobot.Interface;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.Devices {
    // V2: the AI Vision Sensor's card: its live picture with what it finds boxed (colors in their own color, codes
    // orange, AI objects white), the counts, and its color descriptions: click an object in the picture to keep its
    // color (the AI Vision Utility's "Set"), × to drop one.
    public partial class DevicesPanel {
        private void AiVisionDashboard(RectTransform card, AiVisionSensor s) {
            var holder = Box(card, "Picture", Color.black, 6f, true);
            holder.rect.Size(-1, 200f);
            var image = Ui.Rect("Image", holder.rect).Fill(3f, 3f, 3f, 3f);
            var fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)AiVisionSensor.Width / AiVisionSensor.Height;
            var raw = image.gameObject.AddComponent<RawImage>();
            var shapes = ShapeGraphic.Create(image);
            var picker = image.gameObject.AddComponent<PixelPicker>();
            picker.frame = image;
            picker.picked = p => {
                if (s.pixels == null) return;
                var saved = AiVisionSensor.Saved(s.part);
                var free = System.Array.FindIndex(saved, c => !c.HasValue);
                if (free < 0) {
                    Interface.Interface.Say("All 7 colors are used: drop one (×) first.", false);
                    return;
                }
                var c = AiVisionSensor.Make(s.pixels, new RectInt(p.x - 4, p.y - 4, 9, 9), free + 1, "COLOR_" + (free + 1));
                if (!c.HasValue) {
                    Interface.Interface.Say("No color there (too dark or gray): click an object of a clear color.", false);
                    return;
                }
                saved[free] = c;
                AiVisionSensor.Save(s.part, saved);
                Audio.Sounds.Play(Audio.Sounds.Tick);
                Rebuild();
            };
            var row = TileRow(card);
            Tile(row, "Colors", () => s.objects.Count(o => o.kind == AiKind.Color || o.kind == AiKind.Code).ToString(), 1f, "Objects of its color descriptions (and codes) in the picture.");
            Tile(row, "AI objects", () => s.objects.Count(o => o.kind == AiKind.Object).ToString(), 1f,
                "Game pieces its model finds (classes: 0 Red Pin, 1 Blue Pin, 2 Yellow Pin, 3 Cup), with a confidence score.");
            Tile(row, "Largest", () => {
                if (s.objects.Count == 0) return "—";
                var o = s.objects[0];
                var what = o.kind == AiKind.Object ? AiVisionSensor.ClassNames[Mathf.Clamp(o.id, 0, AiVisionSensor.ClassNames.Length - 1)]
                    : o.kind == AiKind.Code ? "code" : "color " + o.id;
                return what + " " + o.width + "×" + o.height;
            }, 1.6f, "The widest object in its 320 × 240 picture.");
            // The colors kept on it.
            var colors = AiVisionSensor.Saved(s.part);
            var list = ButtonRow(card);
            foreach (var c in colors.Where(c => c.HasValue).Select(c => c.Value)) {
                var color = c;
                var chip = Ui.Button(list, "Color " + c.id, c.id + " ×", () => {
                    var all = AiVisionSensor.Saved(s.part);
                    all[color.id - 1] = null;
                    AiVisionSensor.Save(s.part, all);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    Rebuild();
                }, false, 5f, Theme.FontSmall);
                chip.Size(48f, 26f);
                if (chip.TryGetComponent(out Image back)) back.color = color.Color;
                Ui.Tip(chip, "Color " + c.id, null, "R " + c.r + " G " + c.g + " B " + c.b + ", hue ±" + c.hue + "°, saturation ±" + c.saturation + ". Click to drop it.");
            }
            Note(card, colors.Any(c => c.HasValue) ? (s.programSet ? "The running program set its own colors." : "Insert devices writes these as Colordesc lines.")
                : "No colors yet: click an object of a clear color in the picture to keep its color (as the AI Vision Utility does).", Theme.TextFaint, 30f);
            live.Add(() => {
                s.Show();   // V2: shown, not read by a program
                raw.texture = s.Picture;
                raw.color = s.Picture != null ? Color.white : Color.black;
                shapes.Clear();
                var r = image.rect;
                var all = s.Colors;
                foreach (var o in s.objects.Take(AiVisionSensor.MaxObjects)) {
                    var color = o.kind == AiKind.Object ? Color.white : o.kind == AiKind.Code ? Theme.Warning
                        : all[Mathf.Clamp(o.id, 1, 7) - 1].HasValue ? all[o.id - 1].Value.Color : Theme.Accent;
                    var min = new Vector2(r.xMin + (float)o.x / AiVisionSensor.Width * r.width, r.yMax - (float)(o.y + o.height) / AiVisionSensor.Height * r.height);
                    var max = new Vector2(r.xMin + (float)(o.x + o.width) / AiVisionSensor.Width * r.width, r.yMax - (float)o.y / AiVisionSensor.Height * r.height);
                    VisionUtility.Box(shapes, min, max, color, 1.5f);
                }
            });
        }

        /// <summary>A click on a picture: the pixel it's on (picture coordinates, top left 0, 0).</summary>
        private class PixelPicker : MonoBehaviour, IPointerClickHandler {
            public RectTransform frame;
            public System.Action<Vector2Int> picked;

            public void OnPointerClick(PointerEventData e) {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, e.position, e.pressEventCamera, out var local);
                var r = frame.rect;
                var x = Mathf.Clamp(Mathf.RoundToInt((local.x - r.xMin) / r.width * AiVisionSensor.Width), 0, AiVisionSensor.Width - 1);
                var y = Mathf.Clamp(Mathf.RoundToInt((r.yMax - local.y) / r.height * AiVisionSensor.Height), 0, AiVisionSensor.Height - 1);
                picked?.Invoke(new Vector2Int(x, y));
            }
        }
    }
}
