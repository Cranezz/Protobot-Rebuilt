using System.Globalization;
using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Polycarb {
    // V2: the plastic in the build (top bar > Plastic): every polycarbonate piece laid out on stock sheets as
    // tightly as they go (turned to fit; a piece that doesn't fit starts the next sheet), with the counts and the
    // rules they're checked against. Deleting a piece from the build takes it off the sheets. V2: the wheel zooms
    // in on the sheets (about the mouse), a drag pans, F or a double-click fits them again.
    public class SheetsDialog : MonoBehaviour {
        private Dialog dialog;
        private ShapeGraphic shapes;
        private RectTransform view, info;
        private float viewZoom = 1f;
        private Vector2 viewPan;
        private bool dragging;
        private Vector2 lastMouse;
        private float lastClick;

        public static void Open() {
            var dialog = Dialog.Open("Plastic sheets", new Vector2(1100f, 680f));
            var sheets = dialog.gameObject.AddComponent<SheetsDialog>();
            sheets.dialog = dialog;
            sheets.Build();
        }

        private void Build() {
            var frame = Ui.Image(dialog.Body, "View", new Color(0.1f, 0.11f, 0.13f, 1f), 8f);
            view = frame.rectTransform;
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = Vector2.zero;
            view.offsetMax = new Vector2(-330f, 0f);
            view.gameObject.AddComponent<RectMask2D>();
            shapes = ShapeGraphic.Create(view);

            info = Ui.Rect("Info", dialog.Body);
            info.anchorMin = new Vector2(1f, 0f);
            info.anchorMax = new Vector2(1f, 1f);
            info.pivot = new Vector2(1f, 1f);
            info.sizeDelta = new Vector2(310f, 0f);
            info.anchoredPosition = Vector2.zero;
            Ui.Column(info, 6f);

            var add = Ui.Button(dialog.Footer, "New", "New piece", () => {
                dialog.Close();
                SheetEditor.Open();
            }, false, 6f);
            add.Size(-1f, 36f);
            var close = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            close.Size(-1f, 36f);
            SheetUsage.Changed += Rebuild;
            StartCoroutine(Later());
        }

        private System.Collections.IEnumerator Later() {
            yield return null;  // the view's size is known after a frame
            Rebuild();
        }

        private void OnDestroy() => SheetUsage.Changed -= Rebuild;

        // Where a point of the fitted drawing is after zooming and panning.
        private Vector2 V(Vector2 p) => (p - view.rect.center) * viewZoom + view.rect.center + viewPan;

        private void Update() {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || view == null) return;
            var screen = mouse.position.ReadValue();
            var over = RectTransformUtility.RectangleContainsScreenPoint(view, screen) && !Popup.IsOpen;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(view, screen, null, out var local);
            var changed = false;
            var wheel = mouse.scroll.ReadValue().y;
            if (over && Mathf.Abs(wheel) > 0.01f) {
                var before = (local - view.rect.center - viewPan) / viewZoom;
                viewZoom = Mathf.Clamp(viewZoom * (wheel > 0f ? 1.15f : 1f / 1.15f), 0.5f, 12f);
                viewPan = local - view.rect.center - before * viewZoom;
                changed = true;
            }
            var pressed = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            if (over && pressed) {
                if (mouse.leftButton.wasPressedThisFrame && Time.unscaledTime - lastClick < 0.3f) {
                    viewZoom = 1f;
                    viewPan = Vector2.zero;
                    changed = true;
                }
                lastClick = mouse.leftButton.wasPressedThisFrame ? Time.unscaledTime : lastClick;
                dragging = true;
                lastMouse = local;
            }
            if (dragging) {
                if (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed) {
                    if ((local - lastMouse).sqrMagnitude > 0.01f) {
                        viewPan += local - lastMouse;
                        lastMouse = local;
                        changed = true;
                    }
                }
                else dragging = false;
            }
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && Tools.Keybinds.Pressed("SheetsFit") && !InputEvents.TextInput.IsTyping) {
                viewZoom = 1f;
                viewPan = Vector2.zero;
                changed = true;
            }
            if (changed) DrawSheets(SheetUsage.Plan());
        }

        private void Rebuild() {
            if (this == null || shapes == null) return;
            var layout = SheetUsage.Plan();
            DrawSheets(layout);
            BuildInfo(layout);
        }

        private void DrawSheets(SheetUsage.Layout layout) {
            shapes.Clear();
            foreach (Transform child in view)
                if (child != shapes.transform) Destroy(child.gameObject);
            var rect = view.rect;
            var sheet = SheetUsage.Sheet;
            var count = Mathf.Max(1, layout.sheets);
            // Sheets side by side (standing up), scaled to fit.
            var gap = 24f;
            var scale = Mathf.Min((rect.width - gap * (count + 1)) / (count * sheet.x), (rect.height - 70f) / sheet.y);
            scale = Mathf.Max(scale, 1f);
            var total = count * sheet.x * scale + (count - 1) * gap;
            var origin = new Vector2(rect.xMin + (rect.width - total) / 2f, rect.yMin + 30f);
            for (var s = 0; s < count; s++) {
                var corner = origin + new Vector2(s * (sheet.x * scale + gap), 0f);
                shapes.Quad(V(corner), V(corner + new Vector2(sheet.x * scale, 0f)), V(corner + sheet * scale), V(corner + new Vector2(0f, sheet.y * scale)), new Color(0.2f, 0.24f, 0.29f, 1f));
                var over = SheetUsage.CurrentRules == SheetUsage.Rules.OneSheet && s > 0;
                shapes.Polyline(new[] { V(corner), V(corner + new Vector2(sheet.x * scale, 0f)), V(corner + sheet * scale), V(corner + new Vector2(0f, sheet.y * scale)) },
                    over ? Theme.Danger : new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.8f), 1.5f, true);
                Label(V(corner + new Vector2(sheet.x * scale / 2f, sheet.y * scale)) + new Vector2(0f, 16f),
                    "Sheet " + (s + 1) + " · " + Inch.Format(sheet.x) + " × " + Inch.Format(sheet.y), over ? Theme.Danger : Theme.TextDim);
            }
            var palette = new[] { new Color(0.55f, 0.75f, 1f, 0.75f), new Color(0.6f, 0.9f, 0.75f, 0.75f), new Color(1f, 0.82f, 0.5f, 0.75f), new Color(0.9f, 0.65f, 0.95f, 0.75f) };
            var keys = layout.pieces.Select(p => p.part.key).Distinct().ToList();
            foreach (var piece in layout.pieces.Where(p => p.sheet >= 0)) {
                var corner = origin + new Vector2(piece.sheet * (sheet.x * scale + gap), 0f);
                var points = piece.points.Select(p => V(corner + SheetUsage.OnSheet(piece, p) * scale)).ToList();
                var color = piece.tooBig && SheetUsage.CurrentRules == SheetUsage.Rules.Current ? new Color(Theme.Danger.r, Theme.Danger.g, Theme.Danger.b, 0.75f) : palette[keys.IndexOf(piece.part.key) % palette.Length];
                shapes.Triangles(points, piece.top, color);
                var hull = piece.hull.Select(p => V(corner + SheetUsage.OnSheet(piece, p) * scale)).ToList();
                if (hull.Count > 2) {
                    var center = hull.Aggregate(Vector2.zero, (a, b) => a + b) / hull.Count;
                    if (piece.size.x * scale * viewZoom > 50f && piece.size.y * scale * viewZoom > 18f) Label(center, piece.part.name, Theme.Text);
                }
            }
            if (layout.pieces.Count == 0) Label(rect.center, "No polycarbonate pieces in this build yet", Theme.TextFaint);
            else Label(new Vector2(rect.xMin + 150f, rect.yMin + 12f), "Wheel: zoom · drag: pan · F: fit", Theme.TextFaint);
        }

        private void Label(Vector2 at, string text, Color color) {
            var label = Ui.Text(view, "Label", text, 12.5f, color, Theme.Medium, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = at;
            label.rectTransform.sizeDelta = new Vector2(220f, 20f);
        }

        private void BuildInfo(SheetUsage.Layout layout) {
            foreach (Transform child in info) Destroy(child.gameObject);
            info.DetachChildren();
            Dialog.Heading(info, "Rules");
            ToolOptions.Segmented(info, new[] { "2025 rules", "One sheet" }, (int)SheetUsage.CurrentRules, i => SheetUsage.CurrentRules = (SheetUsage.Rules)i, 140f);
            var rules = SheetUsage.CurrentRules == SheetUsage.Rules.Current
                ? "V5RC 2025-26 and 2026-27 (R25 / R24): up to 12 pieces of non-shattering plastic, each no bigger than 4\" × 8\" × 0.070\"."
                : "Earlier seasons: all plastic parts must be cut from one 12\" × 24\" sheet, up to 0.070\" thick.";
            var rulesText = Ui.Text(info, "Rules text", rules, Theme.FontSmall, Theme.TextDim);
            rulesText.textWrappingMode = TextWrappingModes.Normal;
            rulesText.Size(-1, 56f);

            Dialog.Heading(info, "This build");
            var pieces = layout.pieces.Count;
            var line1 = SheetUsage.CurrentRules == SheetUsage.Rules.Current
                ? pieces + " of " + SheetUsage.MaxPieces + " pieces"
                : pieces + (pieces == 1 ? " piece" : " pieces");
            Ui.Text(info, "Pieces", line1, Theme.FontTitle, layout.OverCount ? Theme.Danger : Theme.Text, Theme.Medium).Size(-1, 28f);
            var sheetArea = SheetUsage.Sheet.x * SheetUsage.Sheet.y;
            var area = Ui.Text(info, "Area", layout.area.ToString("0.#", CultureInfo.InvariantCulture) + " sq in of plastic · " +
                layout.sheets + (layout.sheets == 1 ? " sheet" : " sheets") + " (" + (layout.sheets > 0 ? (layout.area / (layout.sheets * sheetArea) * 100f).ToString("0") : "0") + "% used)",
                Theme.FontSmall, Theme.TextDim);
            area.textWrappingMode = TextWrappingModes.Normal;
            area.Size(-1, 38f);
            if (!layout.Legal) {
                var problem = Ui.Text(info, "Problem", SheetUsage.Problem(layout), Theme.FontSmall, Theme.Danger);
                problem.textWrappingMode = TextWrappingModes.Normal;
                problem.Size(-1, 40f);
            }
            else if (pieces > 0) Ui.Text(info, "Legal", "Within the limits", Theme.FontSmall, Theme.Success).Size(-1, 22f);

            if (SheetUsage.CurrentRules == SheetUsage.Rules.Current) {
                Dialog.Heading(info, "Stock sheet to cut from");
                var labels = SheetUsage.StockSheets.Select(s => Inch.Format(s.x) + " × " + Inch.Format(s.y)).ToArray();
                Popup.Choice(info, labels, SheetUsage.StockIndex, i => SheetUsage.StockIndex = i, 180f);
            }

            Dialog.Heading(info, "Pieces");
            foreach (var group in layout.pieces.GroupBy(p => p.part.key)) {
                var first = group.First();
                var text = group.Count() + " × " + first.part.name + "  (" + Inch.Format(Mathf.Min(first.size.x, first.size.y)) + " × " + Inch.Format(Mathf.Max(first.size.x, first.size.y)) + ")";
                var row = Ui.Text(info, "Piece", text, Theme.FontSmall, first.tooBig && SheetUsage.CurrentRules == SheetUsage.Rules.Current ? Theme.Danger : Theme.Text);
                row.Size(-1, 22f);
            }
        }
    }
}
