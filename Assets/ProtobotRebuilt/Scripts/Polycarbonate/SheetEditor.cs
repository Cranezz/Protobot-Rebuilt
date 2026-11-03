using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Custom;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Polycarb {
    // V2: the polycarbonate editor, a full screen of its own over the build: the sheet seen from above, and a
    // sketch on it like a CAD program's. Draw lines, rectangles, circles, arcs and holes (VEX sizes in one click),
    // snapping to the grid (1/16") and to the ends of what's drawn; dimension them and type the sizes. Then
    // "Choose pieces": click the closed areas that are the piece (several join into one), name it, pick the
    // thickness, and "Make piece": it's extruded, its round and square holes become real holes, it's added to the
    // parts browser and you place it on the robot. The sheet is what the rules allow: a 4" x 8" piece (2025 on)
    // or the 12" x 24" sheet (older rules), and the panel shows how much plastic the build already uses.
    public class SheetEditor : MonoBehaviour {
        private enum Tool { Select, Line, Rectangle, Circle, Arc, Hole, Dimension, Pieces }

        private static readonly (Tool tool, string name, string key, string help)[] Tools = {
            (Tool.Select, "Select", "V", "Click a line or shape to select it (Delete removes it). Drag a point to move it."),
            (Tool.Line, "Line", "L", "Click to start, click for each corner. Click the first point to close it; Esc or right-click to stop."),
            (Tool.Rectangle, "Rectangle", "R", "Click one corner, then the opposite one."),
            (Tool.Circle, "Circle", "C", "Click the centre, then a point on the circle."),
            (Tool.Arc, "Arc", "A", "Click the centre, the start, then the end (it goes counter-clockwise)."),
            (Tool.Hole, "Hole", "H", "Pick a hole size, then click where the hole's centre goes."),
            (Tool.Dimension, "Dimension", "D", "Click a line, a rectangle's side or a circle; or two points for the distance between them. Click a dimension's value to type it."),
            (Tool.Pieces, "Choose pieces", "P", "Click the closed areas that make the piece (several join together). Click again to leave one out."),
        };

        public static SheetEditor Instance { get; private set; }
        public static bool IsOpen => Instance != null;

        private SketchData sketch = new();
        private CustomPartData editing;
        private string pieceName;
        private float thickness = 1f / 16f;
        private Tool tool = Tool.Line;
        private bool gridSnap = true;
        private int holeSize = 1;

        // View: canvas units per inch and where the sketch's origin is in the canvas.
        private RectTransform canvasRect, toolColumn, side, statusBar;
        private ShapeGraphic shapes;
        private float zoom = 60f;
        private Vector2 pan;
        private bool panning;
        private Vector2 panMouse;
        private TextMeshProUGUI status;
        private readonly List<TextMeshProUGUI> labels = new();
        private int labelsUsed;
        private readonly List<Button> toolButtons = new();

        // Drawing in progress
        private readonly List<Vector2> clicks = new();
        private int chainStart = -1, chainLast = -1;
        private SketchEntity selected;
        private int draggingPoint = -1;
        private int dimensionFirstPoint = -1;
        private TMP_InputField editField;
        private SketchDimension editingDimension;
        private Regions regions;
        private bool sketchChanged = true;
        private readonly List<SketchData> undo = new();

        public static void Open() => Show(null);

        public static void Edit(CustomPartData part) => Show(part);

        private static void Show(CustomPartData part) {
            if (Instance != null) return;
            PartsFlyout.Close();
            var root = Ui.Rect("Polycarbonate Editor", Interface.Interface.Root).Fill();
            root.SetAsLastSibling();
            Instance = root.gameObject.AddComponent<SheetEditor>();
            Instance.editing = part;
            if (part != null && !string.IsNullOrEmpty(part.sketch)) {
                Instance.sketch = JsonUtility.FromJson<SketchData>(part.sketch) ?? new SketchData();
                Instance.thickness = part.thickness > 0f ? part.thickness : 1f / 16f;
                Instance.pieceName = part.name;
            }
            else
                Instance.pieceName = "Plastic piece " + (CustomParts.OfKind(CustomParts.Polycarbonate).Count() + 1);
            Instance.Build();
        }

        public void Close() {
            CloseEditField();
            Destroy(gameObject);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ building the screen

        private Vector2 SheetSize => SheetUsage.CurrentRules == SheetUsage.Rules.OneSheet ? SheetUsage.LegacySheet : SheetUsage.MaxPiece;

        private void Build() {
            var background = gameObject.AddComponent<Image>();
            background.color = new Color(0.07f, 0.075f, 0.09f, 1f);
            background.raycastTarget = true;

            // Top bar.
            var top = Ui.Image(transform, "Top", Theme.Panel).rectTransform;
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0f, 56f);
            top.anchoredPosition = Vector2.zero;
            Ui.Row(top, 12f, new RectOffset(18, 18, 0, 0));
            Ui.Text(top, "Title", editing != null ? "Edit polycarbonate piece" : "New polycarbonate piece", Theme.FontTitle + 2f, Theme.Text, Theme.Medium).Size(-1, 56f);
            Ui.Divider(top, true).Size(1f, 26f);
            Ui.Text(top, "Name Label", "Name", Theme.FontBody, Theme.TextDim).Size(-1, 56f);
            var name = Ui.Field(top, "Name", pieceName, value => pieceName = value.Trim().Length > 0 ? value.Trim() : pieceName);
            name.Size(220f, 34f);
            var spacer = Ui.Rect("Spacer", top);
            spacer.Size(-1, 10f, 1f);
            var undoButton = Ui.Button(top, "Undo", "Undo", Undo, false, 6f, Theme.FontSmall);
            undoButton.Size(-1, 34f);
            Ui.Tip(undoButton, "Undo", "Ctrl+Z");
            var cancel = Ui.Button(top, "Cancel", "Cancel", () => {
                Close();
                Interface.Interface.Say("Closed the polycarbonate editor", false);
            }, false, 6f);
            cancel.Size(-1, 34f);
            var make = Ui.Button(top, "Make", editing != null ? "Save piece" : "Make piece", MakePiece, true, 6f);
            make.Size(-1, 34f);
            Ui.Tip(make, "Make the piece", "Enter", "From the areas chosen with Choose pieces.");

            // Tools on the left.
            toolColumn = Ui.Panel(transform, "Tools", Theme.Panel, 10f).rectTransform;
            toolColumn.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -70f), new Vector2(170f, 0f));
            toolColumn.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Column(toolColumn, 3f, new RectOffset(6, 6, 6, 6));
            toolColumn.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            foreach (var def in Tools) {
                if (def.tool == Tool.Pieces) Ui.Divider(toolColumn).Size(-1, 1f);
                var button = Ui.Button(toolColumn, def.name, null, () => SetTool(def.tool), false, 6f);
                button.Size(-1, 36f);
                var label = Ui.Text(button.transform, "Label", def.name, Theme.FontBody, Theme.Text, Theme.Medium);
                label.rectTransform.Fill(12, 0, 30, 0);
                var key = Ui.Text(button.transform, "Key", def.key, 12f, Theme.TextFaint, Theme.Medium, TextAlignmentOptions.MidlineRight);
                key.rectTransform.Fill(0, 0, 10, 0);
                Ui.Tip(button, def.name, def.key, def.help);
                toolButtons.Add(button);
            }

            // The drawing area.
            canvasRect = Ui.Rect("Canvas", transform);
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = new Vector2(198f, 40f);
            canvasRect.offsetMax = new Vector2(-344f, -70f);
            canvasRect.gameObject.AddComponent<RectMask2D>();
            var canvasBackground = canvasRect.gameObject.AddComponent<Image>();
            canvasBackground.color = new Color(0.1f, 0.11f, 0.13f, 1f);
            shapes = ShapeGraphic.Create(canvasRect);

            // Settings on the right.
            side = Ui.Panel(transform, "Side", Theme.Panel, 10f).rectTransform;
            side.anchorMin = new Vector2(1f, 0f);
            side.anchorMax = new Vector2(1f, 1f);
            side.pivot = new Vector2(1f, 1f);
            side.offsetMin = new Vector2(-330f, 40f);
            side.offsetMax = new Vector2(-14f, -70f);
            side.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Column(side, 6f, new RectOffset(14, 14, 12, 12));

            // Status line.
            statusBar = Ui.Rect("Status", transform);
            statusBar.anchorMin = new Vector2(0f, 0f);
            statusBar.anchorMax = new Vector2(1f, 0f);
            statusBar.pivot = new Vector2(0.5f, 0f);
            statusBar.sizeDelta = new Vector2(0f, 32f);
            statusBar.anchoredPosition = Vector2.zero;
            status = Ui.Text(statusBar, "Text", "", Theme.FontSmall, Theme.TextDim);
            status.rectTransform.Fill(200, 0, 344, 0);

            Interface.Interface.Instance.StartCoroutine(FitNextFrame());
            SetTool(sketch.entities.Count > 0 ? Tool.Select : Tool.Rectangle);
            Interface.Interface.Say("Draw the piece, then Choose pieces and Make piece");
        }

        private System.Collections.IEnumerator FitNextFrame() {
            yield return null;
            Fit();
        }

        // The sheet (and what's drawn) fills the view.
        private void Fit() {
            if (canvasRect == null) return;
            var rect = canvasRect.rect;
            var min = Vector2.zero;
            var max = SheetSize;
            foreach (var p in sketch.points) {
                min = Vector2.Min(min, p.p);
                max = Vector2.Max(max, p.p);
            }
            var size = max - min + Vector2.one * 1.5f;
            zoom = Mathf.Clamp(Mathf.Min(rect.width / size.x, rect.height / size.y), 4f, 2000f);
            pan = new Vector2(rect.width, rect.height) / 2f - (min + max) / 2f * zoom;
        }

        private void SetTool(Tool newTool) {
            tool = newTool;
            clicks.Clear();
            chainStart = chainLast = -1;
            dimensionFirstPoint = -1;
            if (tool != Tool.Select) selected = null;
            for (var i = 0; i < toolButtons.Count; i++) {
                var on = Tools[i].tool == tool;
                toolButtons[i].targetGraphic.color = on ? Theme.Accent : new Color(1f, 1f, 1f, 0f);
            }
            BuildSide();
        }

        // ------------------------------------------------------------------ the side panel

        private void BuildSide() {
            foreach (Transform child in side)
                if (child.GetSiblingIndex() > 0) Destroy(child.gameObject);
            var def = Tools.First(t => t.tool == tool);
            Dialog.Heading(side, def.name);
            var help = Ui.Text(side, "Help", def.help, Theme.FontSmall, Theme.TextDim);
            help.textWrappingMode = TextWrappingModes.Normal;
            help.Size(-1, 58f);

            if (tool == Tool.Hole) {
                Dialog.Heading(side, "Hole size");
                for (var i = 0; i < HoleSizes.Common.Length; i++) {
                    var index = i;
                    var button = Ui.Button(side, HoleSizes.Common[i].label, HoleSizes.Common[i].label, () => {
                        holeSize = index;
                        BuildSide();
                    }, i == holeSize, 5f, Theme.FontSmall);
                    button.Size(-1, 28f);
                }
            }

            if (selected != null && sketch.entities.Contains(selected)) {
                Dialog.Heading(side, "Selected " + selected.kind.ToString().ToLowerInvariant());
                foreach (var (label, kind) in SizesOf(selected)) {
                    var row = Dialog.Row(side, label, 32f, 90f);
                    var dimension = new SketchDimension { kind = kind, entity = selected.id };
                    var field = Ui.Field(row, label, Inch.Format(sketch.Measure(dimension), false), text => {
                        if (Inch.TryParse(text, out var value)) {
                            Remember();
                            sketch.Apply(dimension, value);
                            Changed();
                        }
                        else Interface.Interface.Say("Type a size like 1 1/2 or 1.5", false);
                    }, Theme.FontBody, TextAlignmentOptions.Center);
                    field.Size(110f, 30f);
                    Ui.Text(row, "Unit", "in", Theme.FontSmall, Theme.TextFaint).Size(-1, 30f);
                }
                var row2 = Dialog.Row(side, "", 32f, 0f);
                var construction = Ui.Button(row2, "Construction", selected.construction ? "Make it a cut line" : "Make it construction", () => {
                    Remember();
                    selected.construction = !selected.construction;
                    Changed();
                }, false, 5f, Theme.FontSmall);
                construction.Size(-1, 30f);
                Ui.Tip(construction, "Construction geometry", null, "Construction lines (dashed) help you draw but aren't cut and don't make areas.");
                var delete = Ui.Button(row2, "Delete", "Delete", DeleteSelected, false, 5f, Theme.FontSmall);
                delete.Size(-1, 30f);
            }

            Dialog.Heading(side, "Sketch");
            ToolOptions.Switch(side, "Snap to 1/16\" grid (Shift: free)", gridSnap, on => gridSnap = on);

            Dialog.Heading(side, "Piece");
            var thicknessRow = Dialog.Row(side, "Thickness", 32f, 90f);
            var index2 = System.Array.FindIndex(PolyPart.Thicknesses, t => Mathf.Abs(t.inches - thickness) < 1e-4f);
            Popup.Choice(thicknessRow, PolyPart.Thicknesses.Select(t => t.label).ToArray(), Mathf.Max(0, index2), i => thickness = PolyPart.Thicknesses[i].inches, 190f);
            var chosen = ChosenFaces();
            if (chosen.Count > 0 && regions != null) {
                var loops = regions.Outline(chosen);
                var hull = PolyPart.Hull(loops.Where(l => Regions.SignedArea(l) > 0f).SelectMany(l => l).ToList());
                var (box, _) = PolyPart.SmallestBox(hull);
                var area = loops.Sum(Regions.SignedArea);
                var fits = SheetUsage.Fits(box, SheetUsage.MaxPiece);
                var info = Ui.Text(side, "Chosen", chosen.Count + (chosen.Count == 1 ? " area" : " areas") + " chosen · " +
                    Inch.Format(Mathf.Min(box.x, box.y)) + " × " + Inch.Format(Mathf.Max(box.x, box.y)) + " · " +
                    area.ToString("0.##", CultureInfo.InvariantCulture) + " sq in", Theme.FontSmall, Theme.Text);
                info.textWrappingMode = TextWrappingModes.Normal;
                info.Size(-1, 38f);
                if (SheetUsage.CurrentRules == SheetUsage.Rules.Current && !fits) {
                    var warn = Ui.Text(side, "Too big", "Bigger than 4\" × 8\": the rules don't allow a piece this size", Theme.FontSmall, Theme.Danger);
                    warn.textWrappingMode = TextWrappingModes.Normal;
                    warn.Size(-1, 36f);
                }
            }
            else {
                var none = Ui.Text(side, "Chosen", "No areas chosen yet: use Choose pieces", Theme.FontSmall, Theme.TextFaint);
                none.Size(-1, 22f);
            }

            Dialog.Heading(side, "Plastic in this build");
            ToolOptions.Segmented(side, new[] { "2025 rules", "One sheet" }, (int)SheetUsage.CurrentRules, i => {
                SheetUsage.CurrentRules = (SheetUsage.Rules)i;
                Fit();
                BuildSide();
            }, 130f);
            var layout = SheetUsage.Plan();
            string usage;
            if (SheetUsage.CurrentRules == SheetUsage.Rules.Current)
                usage = layout.pieces.Count + " of " + SheetUsage.MaxPieces + " pieces used, each up to 4\" × 8\" × 0.070\" (V5RC R25)";
            else
                usage = "Pieces cut from one 12\" × 24\" sheet: " + layout.area.ToString("0.#", CultureInfo.InvariantCulture) + " of 288 sq in used (" +
                        (layout.sheets <= 1 ? "they fit" : "they need " + layout.sheets + " sheets") + ")";
            var usageText = Ui.Text(side, "Usage", usage, Theme.FontSmall, layout.Legal ? Theme.TextDim : Theme.Warning);
            usageText.textWrappingMode = TextWrappingModes.Normal;
            usageText.Size(-1, 40f);
            var sheets = Ui.Button(side, "Sheets", "Show the sheet layout", () => SheetsDialog.Open(), false, 5f, Theme.FontSmall);
            sheets.Size(-1, 30f);
        }

        private static IEnumerable<(string, DimensionKind)> SizesOf(SketchEntity e) => e.kind switch {
            EntityKind.Line => new[] { ("Length", DimensionKind.Length) },
            EntityKind.Rectangle => new[] { ("Width", DimensionKind.Width), ("Height", DimensionKind.Height) },
            _ => new[] { ("Diameter", DimensionKind.Diameter) },
        };

        // ------------------------------------------------------------------ view

        private Vector2 ToCanvas(Vector2 inches) => inches * zoom + pan + canvasRect.rect.min;
        private Vector2 ToSketch(Vector2 canvas) => (canvas - canvasRect.rect.min - pan) / zoom;

        private bool MouseInCanvas(out Vector2 canvas) {
            var mouse = Mouse.current.position.ReadValue();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse, null, out canvas);
            return canvasRect.rect.Contains(canvas) && !OverPanel(mouse);
        }

        private bool OverPanel(Vector2 mouse) =>
            RectTransformUtility.RectangleContainsScreenPoint(side, mouse) || RectTransformUtility.RectangleContainsScreenPoint(toolColumn, mouse)
            || Popup.IsOpen || (editField != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)editField.transform, mouse));

        // ------------------------------------------------------------------ input

        private bool Typing => InputEvents.TextInput.IsTyping;

        private void Update() {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || canvasRect == null) return;
            if (sketchChanged) {
                regions = Regions.Of(sketch);
                sketchChanged = false;
            }

            if (!Typing && keyboard != null && !Popup.IsOpen) Keys(keyboard);
            if (this == null || canvasRect == null) return;

            var inside = MouseInCanvas(out var canvasMouse);
            // Pan: right or middle drag; zoom: wheel about the mouse.
            if (inside && (mouse.middleButton.wasPressedThisFrame || (mouse.rightButton.wasPressedThisFrame && clicks.Count == 0 && chainLast < 0))) {
                panning = true;
                panMouse = canvasMouse;
            }
            if (panning) {
                if (mouse.middleButton.isPressed || mouse.rightButton.isPressed) {
                    pan += canvasMouse - panMouse;
                    panMouse = canvasMouse;
                }
                else panning = false;
            }
            if (inside) {
                var wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) {
                    var before = ToSketch(canvasMouse);
                    zoom = Mathf.Clamp(zoom * (wheel > 0f ? 1.15f : 1f / 1.15f), 4f, 4000f);
                    pan += canvasMouse - ToCanvas(before);
                }
            }

            var free = keyboard != null && keyboard.shiftKey.isPressed;
            var snapped = Snap(ToSketch(canvasMouse), free, out var snapPoint);
            if (inside && !panning) HandleMouse(mouse, snapped, snapPoint, canvasMouse);
            Draw(inside ? snapped : (Vector2?)null, snapPoint, canvasMouse);
            status.text = (inside ? "X " + Inch.Format(snapped.x) + "   Y " + Inch.Format(snapped.y) + "      " : "") +
                          "Scroll: zoom · Right-drag: pan · F: fit · Shift: no snapping · Ctrl+Z: undo";
        }

        private void Keys(Keyboard keyboard) {
            if (keyboard.escapeKey.wasPressedThisFrame) {
                if (editField != null) CloseEditField();
                else if (clicks.Count > 0 || chainLast >= 0 || dimensionFirstPoint >= 0) {
                    clicks.Clear();
                    chainStart = chainLast = -1;
                    dimensionFirstPoint = -1;
                }
                else if (selected != null) {
                    selected = null;
                    BuildSide();
                }
                else if (tool != Tool.Select) SetTool(Tool.Select);
                return;
            }
            if ((keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed) && keyboard.zKey.wasPressedThisFrame) {
                Undo();
                return;
            }
            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) DeleteSelected();
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) MakePiece();
            if (keyboard.fKey.wasPressedThisFrame) Fit();
            if (keyboard.ctrlKey.isPressed) return;
            foreach (var def in Tools)
                if (keyboard[(Key)System.Enum.Parse(typeof(Key), def.key)].wasPressedThisFrame) SetTool(def.tool);
        }

        // Grid and existing points; returns the point id snapped to (or -1).
        private Vector2 Snap(Vector2 p, bool free, out int point) {
            point = -1;
            var best = 10f / zoom;
            foreach (var sp in sketch.points) {
                if (sp.id == draggingPoint) continue;
                var d = Vector2.Distance(sp.p, p);
                if (d < best) {
                    best = d;
                    point = sp.id;
                }
            }
            if (point >= 0) return sketch.P(point);
            if (free || !gridSnap) return p;
            const float step = 1f / 16f;
            return new Vector2(Mathf.Round(p.x / step) * step, Mathf.Round(p.y / step) * step);
        }

        // Horizontal and vertical lines snap straight when they're nearly.
        private Vector2 Straighten(Vector2 start, Vector2 p, int snapPoint) {
            if (snapPoint >= 0) return p;
            var d = p - start;
            if (Mathf.Abs(d.y) * zoom < 7f) p.y = start.y;
            if (Mathf.Abs(d.x) * zoom < 7f) p.x = start.x;
            return p;
        }

        private int PointAt(Vector2 p, int snapPoint) => snapPoint >= 0 ? snapPoint : sketch.AddPoint(p).id;

        private void HandleMouse(UnityEngine.InputSystem.Mouse mouse, Vector2 p, int snapPoint, Vector2 canvasMouse) {
            var pressed = mouse.leftButton.wasPressedThisFrame;
            if (mouse.rightButton.wasPressedThisFrame && (clicks.Count > 0 || chainLast >= 0)) {
                clicks.Clear();
                chainStart = chainLast = -1;
                return;
            }
            switch (tool) {
                case Tool.Select:
                    if (pressed) {
                        var point = HoverPoint(canvasMouse);
                        if (point >= 0) {
                            Remember();
                            draggingPoint = point;
                        }
                        else {
                            selected = HoverEntity(canvasMouse);
                            BuildSide();
                        }
                    }
                    if (draggingPoint >= 0) {
                        if (mouse.leftButton.isPressed) {
                            var target = Snap(ToSketch(canvasMouse), Keyboard.current.shiftKey.isPressed, out _);
                            MovePoint(draggingPoint, target);
                            Changed(false);
                        }
                        else {
                            // Dropped on another point: they become one.
                            var dropped = sketch.Point(draggingPoint);
                            var other = sketch.points.FirstOrDefault(sp => sp.id != draggingPoint && dropped != null && Vector2.Distance(sp.p, dropped.p) < 1e-4f);
                            if (other != null && !IsCircleCenterOnly(draggingPoint)) sketch.Merge(draggingPoint, other.id);
                            draggingPoint = -1;
                            Changed();
                        }
                    }
                    break;
                case Tool.Line:
                    if (!pressed) break;
                    if (chainLast < 0) {
                        Remember();
                        chainStart = chainLast = PointAt(p, snapPoint);
                        break;
                    }
                    var end = Straighten(sketch.P(chainLast), p, snapPoint);
                    if (Vector2.Distance(end, sketch.P(chainLast)) < 1e-4f) break;
                    var endPoint = snapPoint >= 0 ? snapPoint : sketch.AddPoint(end).id;
                    sketch.Add(EntityKind.Line, chainLast, endPoint);
                    Changed();
                    if (endPoint == chainStart || snapPoint >= 0 && endPoint != chainLast) {
                        // Closed, or joined onto something drawn: done.
                        if (endPoint == chainStart) Interface.Interface.Say("Closed the shape");
                        chainStart = chainLast = -1;
                    }
                    else chainLast = endPoint;
                    break;
                case Tool.Rectangle:
                    if (!pressed) break;
                    if (clicks.Count == 0) {
                        clicks.Add(p);
                        break;
                    }
                    if (Mathf.Abs(p.x - clicks[0].x) < 1e-3f || Mathf.Abs(p.y - clicks[0].y) < 1e-3f) {
                        Interface.Interface.Say("A rectangle needs a width and a height", false);
                        break;
                    }
                    Remember();
                    AddRectangle(clicks[0], p);
                    clicks.Clear();
                    Changed();
                    break;
                case Tool.Circle:
                    if (!pressed) break;
                    if (clicks.Count == 0) {
                        clicks.Add(p);
                        break;
                    }
                    var radius = Vector2.Distance(p, clicks[0]);
                    if (radius < 0.01f) break;
                    Remember();
                    sketch.Add(EntityKind.Circle, sketch.AddPoint(clicks[0]).id, radius: radius);
                    clicks.Clear();
                    Changed();
                    break;
                case Tool.Arc:
                    if (!pressed) break;
                    clicks.Add(p);
                    if (clicks.Count < 3) break;
                    var arcRadius = Vector2.Distance(clicks[1], clicks[0]);
                    if (arcRadius < 0.01f) {
                        clicks.Clear();
                        break;
                    }
                    Remember();
                    var endOnCircle = clicks[0] + (clicks[2] - clicks[0]).normalized * arcRadius;
                    var startId = snapPoint >= 0 && clicks.Count == 2 ? snapPoint : sketch.AddPoint(clicks[1]).id;
                    sketch.Add(EntityKind.Arc, sketch.AddPoint(clicks[0]).id, startId, sketch.AddPoint(endOnCircle).id, radius: arcRadius);
                    clicks.Clear();
                    Changed();
                    break;
                case Tool.Hole:
                    if (!pressed) break;
                    Remember();
                    var size = HoleSizes.Common[holeSize];
                    if (size.square) AddRectangle(p - Vector2.one * size.inches / 2f, p + Vector2.one * size.inches / 2f);
                    else sketch.Add(EntityKind.Circle, sketch.AddPoint(p).id, radius: size.inches / 2f);
                    Changed();
                    Interface.Interface.Say("Added a " + size.label.Split('(')[0].Trim() + " hole");
                    break;
                case Tool.Dimension:
                    if (!pressed) break;
                    var hoverPoint = HoverPoint(canvasMouse);
                    if (dimensionFirstPoint >= 0) {
                        if (hoverPoint >= 0 && hoverPoint != dimensionFirstPoint) {
                            Remember();
                            AddDimension(new SketchDimension { kind = DimensionKind.Distance, from = dimensionFirstPoint, to = hoverPoint });
                        }
                        dimensionFirstPoint = -1;
                        break;
                    }
                    var entity = HoverEntity(canvasMouse);
                    if (hoverPoint >= 0 && (entity == null || DistanceToPointPixels(hoverPoint, canvasMouse) < 6f)) {
                        dimensionFirstPoint = hoverPoint;
                        Interface.Interface.Say("Now click the second point");
                        break;
                    }
                    if (entity == null) {
                        Interface.Interface.Say("Click a line, a rectangle's side, a circle, or a point", false);
                        break;
                    }
                    Remember();
                    var kind = entity.kind switch {
                        EntityKind.Line => DimensionKind.Length,
                        EntityKind.Rectangle => NearestRectangleSide(entity, ToSketch(canvasMouse), out _),
                        _ => DimensionKind.Diameter,
                    };
                    var side = 0;
                    if (entity.kind == EntityKind.Rectangle) NearestRectangleSide(entity, ToSketch(canvasMouse), out side);
                    AddDimension(new SketchDimension { kind = kind, entity = entity.id, from = side });
                    break;
                case Tool.Pieces:
                    if (!pressed) break;
                    var sample = ToSketch(canvasMouse);
                    var face = regions?.AreaAt(sample);
                    if (face == null) {
                        Interface.Interface.Say("That's not inside a closed shape: close the outline first", false);
                        break;
                    }
                    Remember();
                    var already = sketch.chosen.FindIndex(c => regions.AreaAt(c) == face);
                    if (already >= 0) sketch.chosen.RemoveAt(already);
                    else sketch.chosen.Add(sample);
                    BuildSide();
                    break;
            }
        }

        private bool IsCircleCenterOnly(int point) => sketch.entities.Any(e => (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc) && e.a == point);

        private void AddRectangle(Vector2 corner, Vector2 opposite) {
            var a = sketch.AddPoint(corner);
            var b = sketch.AddPoint(new Vector2(opposite.x, corner.y));
            var c = sketch.AddPoint(opposite);
            var d = sketch.AddPoint(new Vector2(corner.x, opposite.y));
            sketch.Add(EntityKind.Rectangle, a.id, b.id, c.id, d.id);
        }

        private void AddDimension(SketchDimension dimension) {
            dimension.id = sketch.nextId++;
            sketch.dimensions.Add(dimension);
            Changed();
            OpenEditField(dimension);
        }

        // Which side of a rectangle is nearest: its width (a-b or c-d) or height (b-c or d-a), and which of the
        // two (side 1 = the second, where the dimension is drawn).
        private DimensionKind NearestRectangleSide(SketchEntity rect, Vector2 p, out int side) {
            float Side(Vector2 a, Vector2 b) => Overlay.DistanceToSegment(p, a, b);
            float ab = Side(sketch.P(rect.a), sketch.P(rect.b)), cd = Side(sketch.P(rect.c), sketch.P(rect.d));
            float bc = Side(sketch.P(rect.b), sketch.P(rect.c)), da = Side(sketch.P(rect.d), sketch.P(rect.a));
            if (Mathf.Min(ab, cd) <= Mathf.Min(bc, da)) {
                side = cd < ab ? 1 : 0;
                return DimensionKind.Width;
            }
            side = da < bc ? 1 : 0;
            return DimensionKind.Height;
        }

        // Moving a point: a rectangle stays a rectangle (its neighbours follow), an arc's end stays on its circle.
        private void MovePoint(int id, Vector2 target) {
            var point = sketch.Point(id);
            if (point == null) return;
            foreach (var rect in sketch.entities.Where(e => e.kind == EntityKind.Rectangle)) {
                var corners = new[] { rect.a, rect.b, rect.c, rect.d };
                var index = System.Array.IndexOf(corners, id);
                if (index < 0) continue;
                var opposite = sketch.P(corners[(index + 2) % 4]);
                var map = new Vector2[4];
                // Rebuild from the moved corner and its opposite.
                map[index] = target;
                map[(index + 2) % 4] = opposite;
                map[(index + 1) % 4] = index % 2 == 0 ? new Vector2(opposite.x, target.y) : new Vector2(target.x, opposite.y);
                map[(index + 3) % 4] = index % 2 == 0 ? new Vector2(target.x, opposite.y) : new Vector2(opposite.x, target.y);
                for (var k = 0; k < 4; k++) sketch.Point(corners[k]).p = map[k];
                return;
            }
            foreach (var arc in sketch.entities.Where(e => e.kind == EntityKind.Arc && (e.b == id || e.c == id))) {
                var center = sketch.P(arc.a);
                target = center + (target - center).normalized * arc.radius;
            }
            point.p = target;
        }

        private void DeleteSelected() {
            if (selected == null || !sketch.entities.Contains(selected)) return;
            Remember();
            sketch.Remove(selected);
            selected = null;
            Changed();
            Interface.Interface.Say("Deleted");
        }

        private void Remember() {
            undo.Add(sketch.Copy());
            if (undo.Count > 60) undo.RemoveAt(0);
        }

        private void Undo() {
            if (undo.Count == 0) {
                Interface.Interface.Say("Nothing to undo", false);
                return;
            }
            sketch = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            selected = null;
            clicks.Clear();
            chainStart = chainLast = -1;
            Changed();
        }

        private void Changed(bool rebuildSide = true) {
            sketchChanged = true;
            if (rebuildSide) {
                regions = Regions.Of(sketch);
                sketchChanged = false;
                BuildSide();
            }
        }

        // ------------------------------------------------------------------ hit testing (canvas units)

        private float DistanceToPointPixels(int id, Vector2 canvasMouse) => Vector2.Distance(ToCanvas(sketch.P(id)), canvasMouse);

        private int HoverPoint(Vector2 canvasMouse) {
            var best = -1;
            var bestDistance = 9f;
            foreach (var p in sketch.points) {
                var d = Vector2.Distance(ToCanvas(p.p), canvasMouse);
                if (d < bestDistance) {
                    bestDistance = d;
                    best = p.id;
                }
            }
            return best;
        }

        private SketchEntity HoverEntity(Vector2 canvasMouse) {
            SketchEntity best = null;
            var bestDistance = 8f;
            foreach (var e in sketch.entities) {
                var line = sketch.Polyline(e, out var closed);
                for (var i = 0; i < line.Count - (closed ? 0 : 1); i++) {
                    var d = Overlay.DistanceToSegment(canvasMouse, ToCanvas(line[i]), ToCanvas(line[(i + 1) % line.Count]));
                    if (d < bestDistance) {
                        bestDistance = d;
                        best = e;
                    }
                }
            }
            return best;
        }

        private List<Regions.Face> ChosenFaces() {
            if (regions == null) return new List<Regions.Face>();
            return sketch.chosen.Select(c => regions.AreaAt(c)).Where(f => f != null).Distinct().ToList();
        }

        // ------------------------------------------------------------------ drawing

        private static readonly Color Paper = new(0.16f, 0.18f, 0.21f, 1f);
        private static readonly Color GridFine = new(1f, 1f, 1f, 0.035f);
        private static readonly Color GridInch = new(1f, 1f, 1f, 0.09f);
        private static readonly Color Ink = new(0.92f, 0.94f, 0.97f, 1f);
        private static readonly Color Construction = new(0.55f, 0.6f, 0.68f, 1f);

        private void Draw(Vector2? cursor, int snapPoint, Vector2 canvasMouse) {
            shapes.Clear();
            labelsUsed = 0;
            var rect = canvasRect.rect;
            var sheet = SheetSize;

            // The sheet, its grid, and its size.
            shapes.Rect(ToCanvas(Vector2.zero), ToCanvas(sheet), Paper);
            var min = ToSketch(rect.min);
            var max = ToSketch(rect.max);
            var fine = zoom > 40f ? 0.25f : zoom > 12f ? 1f : 4f;
            for (var x = Mathf.Floor(min.x / fine) * fine; x <= max.x; x += fine) {
                var major = Mathf.Abs(x - Mathf.Round(x)) < 1e-3f;
                shapes.Line(ToCanvas(new Vector2(x, min.y)), ToCanvas(new Vector2(x, max.y)), major ? GridInch : GridFine, 1f);
            }
            for (var y = Mathf.Floor(min.y / fine) * fine; y <= max.y; y += fine) {
                var major = Mathf.Abs(y - Mathf.Round(y)) < 1e-3f;
                shapes.Line(ToCanvas(new Vector2(min.x, y)), ToCanvas(new Vector2(max.x, y)), major ? GridInch : GridFine, 1f);
            }
            var edge = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.7f);
            shapes.Polyline(new[] { ToCanvas(Vector2.zero), ToCanvas(new Vector2(sheet.x, 0f)), ToCanvas(sheet), ToCanvas(new Vector2(0f, sheet.y)) }, edge, 1.5f, true);
            Label(ToCanvas(new Vector2(sheet.x / 2f, sheet.y)) + new Vector2(0f, 14f),
                SheetUsage.CurrentRules == SheetUsage.Rules.Current ? "Largest piece allowed: 4\" × 8\"" : "Sheet: 12\" × 24\"", Theme.TextDim, false);

            // Chosen areas, and the one under the mouse when choosing.
            if (regions != null) {
                foreach (var face in ChosenFaces()) Fill(face, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f));
                if (tool == Tool.Pieces && cursor.HasValue) {
                    var hover = regions.AreaAt(ToSketch(canvasMouse));
                    if (hover != null) Fill(hover, new Color(1f, 1f, 1f, 0.12f));
                }
            }

            // The sketch.
            var hoverEntity = tool == Tool.Select || tool == Tool.Dimension ? HoverEntity(canvasMouse) : null;
            foreach (var e in sketch.entities) {
                var line = sketch.Polyline(e, out var closed).Select(ToCanvas).ToList();
                var color = e == selected ? Theme.Accent : e == hoverEntity ? Theme.AccentHover : e.construction ? Construction : Ink;
                if (e.construction) {
                    for (var i = 0; i < line.Count - (closed ? 0 : 1); i++) shapes.Dashed(line[i], line[(i + 1) % line.Count], color, 1.2f);
                }
                else shapes.Polyline(line, color, e == selected ? 2.5f : 1.8f, closed);
                if (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc) {
                    var c = ToCanvas(sketch.P(e.a));
                    shapes.Line(c - new Vector2(4f, 0f), c + new Vector2(4f, 0f), color, 1f);
                    shapes.Line(c - new Vector2(0f, 4f), c + new Vector2(0f, 4f), color, 1f);
                }
            }
            foreach (var p in sketch.points) {
                if (IsCircleCenterOnly(p.id)) continue;
                var c = ToCanvas(p.p);
                shapes.Rect(c - Vector2.one * 3f, c + Vector2.one * 3f, p.id == dimensionFirstPoint ? Theme.Dimension : Ink);
            }

            // Dimensions.
            foreach (var dimension in sketch.dimensions.ToList()) DrawDimension(dimension);

            // What's being drawn.
            if (cursor.HasValue) {
                var p = cursor.Value;
                var preview = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.9f);
                switch (tool) {
                    case Tool.Line when chainLast >= 0: {
                        var start = sketch.P(chainLast);
                        var end = Straighten(start, p, snapPoint);
                        shapes.Line(ToCanvas(start), ToCanvas(end), preview, 1.8f);
                        Label((ToCanvas(start) + ToCanvas(end)) / 2f + new Vector2(0f, 14f), Inch.Format(Vector2.Distance(start, end)), Theme.Dimension, true);
                        break;
                    }
                    case Tool.Rectangle when clicks.Count == 1: {
                        var a = clicks[0];
                        var corners = new[] { a, new Vector2(p.x, a.y), p, new Vector2(a.x, p.y) }.Select(ToCanvas).ToList();
                        shapes.Polyline(corners, preview, 1.8f, true);
                        Label((corners[0] + corners[1]) / 2f - new Vector2(0f, 14f), Inch.Format(Mathf.Abs(p.x - a.x)), Theme.Dimension, true);
                        Label((corners[1] + corners[2]) / 2f + new Vector2(30f, 0f), Inch.Format(Mathf.Abs(p.y - a.y)), Theme.Dimension, true);
                        break;
                    }
                    case Tool.Circle when clicks.Count == 1: {
                        var r = Vector2.Distance(p, clicks[0]);
                        shapes.Ring(ToCanvas(clicks[0]), r * zoom, preview, 1.8f, 72);
                        Label(ToCanvas(clicks[0]) + new Vector2(0f, r * zoom + 14f), "Ø " + Inch.Format(r * 2f), Theme.Dimension, true);
                        break;
                    }
                    case Tool.Arc when clicks.Count >= 1: {
                        var r = Vector2.Distance(clicks.Count > 1 ? clicks[1] : p, clicks[0]);
                        if (clicks.Count == 1) shapes.Dashed(ToCanvas(clicks[0]), ToCanvas(p), preview, 1f);
                        else {
                            var temp = new SketchData();
                            var c = temp.AddPoint(clicks[0]);
                            var s = temp.AddPoint(clicks[1]);
                            var e = temp.AddPoint(clicks[0] + (p - clicks[0]).normalized * r);
                            shapes.Polyline(temp.Polyline(temp.Add(EntityKind.Arc, c.id, s.id, e.id, radius: r), out _).Select(ToCanvas).ToList(), preview, 1.8f);
                        }
                        break;
                    }
                    case Tool.Hole: {
                        var size = HoleSizes.Common[holeSize];
                        var c = ToCanvas(p);
                        var half = size.inches / 2f * zoom;
                        if (size.square) shapes.Polyline(new[] { c + new Vector2(-half, -half), c + new Vector2(half, -half), c + new Vector2(half, half), c + new Vector2(-half, half) }, preview, 1.5f, true);
                        else shapes.Ring(c, half, preview, 1.5f, 48);
                        break;
                    }
                    case Tool.Dimension when dimensionFirstPoint >= 0:
                        shapes.Dashed(ToCanvas(sketch.P(dimensionFirstPoint)), ToCanvas(p), Theme.Dimension, 1f);
                        break;
                }
                // The snap marker.
                if (snapPoint >= 0) shapes.Ring(ToCanvas(sketch.P(snapPoint)), 7f, Theme.Warning, 1.5f, 20);
                else if (tool != Tool.Select && tool != Tool.Pieces) {
                    var c = ToCanvas(p);
                    shapes.Line(c - new Vector2(6f, 0f), c + new Vector2(6f, 0f), Theme.TextDim, 1f);
                    shapes.Line(c - new Vector2(0f, 6f), c + new Vector2(0f, 6f), Theme.TextDim, 1f);
                }
            }
            for (var i = 0; i < labels.Count; i++) labels[i].transform.parent.gameObject.SetActive(i < labelsUsed);
        }

        private void Fill(Regions.Face face, Color color) {
            // The area with its holes, triangulated.
            var xy = new List<double>();
            var starts = new List<int>();
            var points = new List<Vector2>();
            foreach (var p in face.polygon) { xy.Add(p.x); xy.Add(p.y); points.Add(ToCanvas(p)); }
            foreach (var hole in face.holes) {
                starts.Add(points.Count);
                foreach (var p in hole.polygon) { xy.Add(p.x); xy.Add(p.y); points.Add(ToCanvas(p)); }
            }
            shapes.Triangles(points, Geometry.Earcut.Triangulate(xy, starts), color);
        }

        private void DrawDimension(SketchDimension dimension) {
            var e = sketch.Entity(dimension.entity);
            Vector2 a, b;
            switch (dimension.kind) {
                case DimensionKind.Length when e != null: a = sketch.P(e.a); b = sketch.P(e.b); break;
                case DimensionKind.Width when e != null:
                    if (dimension.from == 1) { a = sketch.P(e.d); b = sketch.P(e.c); }
                    else { a = sketch.P(e.a); b = sketch.P(e.b); }
                    break;
                case DimensionKind.Height when e != null:
                    if (dimension.from == 1) { a = sketch.P(e.a); b = sketch.P(e.d); }
                    else { a = sketch.P(e.b); b = sketch.P(e.c); }
                    break;
                case DimensionKind.Diameter when e != null: {
                    var c = sketch.P(e.a);
                    a = c - Vector2.right * e.radius;
                    b = c + Vector2.right * e.radius;
                    break;
                }
                case DimensionKind.Distance: a = sketch.P(dimension.from); b = sketch.P(dimension.to); break;
                default: return;
            }
            var color = Theme.Dimension;
            var ca = ToCanvas(a);
            var cb = ToCanvas(b);
            var dir = (cb - ca).normalized;
            var normal = new Vector2(-dir.y, dir.x);
            var offset = dimension.kind == DimensionKind.Diameter ? Vector2.zero : normal * 22f;
            // Outside the rectangle, not across it.
            if (e != null && e.kind == EntityKind.Rectangle) {
                var middle = ToCanvas((sketch.P(e.a) + sketch.P(e.c)) / 2f);
                if (Vector2.Dot(offset, (ca + cb) / 2f - middle) < 0f) offset = -offset;
            }
            if (offset != Vector2.zero) {
                shapes.Line(ca, ca + offset * 1.2f, new Color(color.r, color.g, color.b, 0.5f), 1f);
                shapes.Line(cb, cb + offset * 1.2f, new Color(color.r, color.g, color.b, 0.5f), 1f);
            }
            var p = ca + offset;
            var q = cb + offset;
            shapes.Line(p, q, color, 1.2f);
            Arrow(p, dir, color);
            Arrow(q, -dir, color);
            var value = sketch.Measure(dimension);
            var text = (dimension.kind == DimensionKind.Diameter ? "Ø " : "") + Inch.Format(value);
            var label = Label((p + q) / 2f, text, color, true);
            label.transform.parent.GetComponent<Button>().onClick.AddListener(() => OpenEditField(dimension));
        }

        private void Arrow(Vector2 tip, Vector2 inward, Color color) {
            var n = new Vector2(-inward.y, inward.x) * 4f;
            shapes.Triangle(tip, tip + inward * 10f + n, tip + inward * 10f - n, color);
        }

        private TextMeshProUGUI Label(Vector2 at, string text, Color color, bool pill) {
            TextMeshProUGUI label;
            if (labelsUsed < labels.Count) label = labels[labelsUsed];
            else {
                var background = Ui.Image(canvasRect, "Label", Color.black, 10f, true);
                background.rectTransform.anchorMin = background.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                background.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
                label = Ui.Text(background.transform, "Text", "", Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
                label.rectTransform.Fill();
                labels.Add(label);
            }
            labelsUsed++;
            var image = label.transform.parent.GetComponent<Image>();
            image.color = pill ? new Color(0.06f, 0.07f, 0.08f, 0.9f) : new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = pill;
            label.text = text;
            label.color = color;
            var rect = (RectTransform)label.transform.parent;
            rect.anchoredPosition = at - canvasRect.rect.center;
            rect.sizeDelta = new Vector2(label.GetPreferredValues(text).x + 14f, 22f);
            label.transform.parent.GetComponent<Button>().onClick.RemoveAllListeners();
            return label;
        }

        // ------------------------------------------------------------------ typing a dimension

        private void OpenEditField(SketchDimension dimension) {
            CloseEditField();
            editingDimension = dimension;
            editField = Ui.Field(transform, "Dimension", Inch.Format(sketch.Measure(dimension), false), text => {
                if (editingDimension == null) return;
                if (Inch.TryParse(text, out var value)) {
                    Remember();
                    sketch.Apply(editingDimension, value);
                    Changed();
                }
                else if (!string.IsNullOrWhiteSpace(text)) Interface.Interface.Say("Type a size like 1 1/2 or 1.5", false);
                CloseEditField();
            }, Theme.FontBody, TextAlignmentOptions.Center);
            var rect = (RectTransform)editField.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            var mouse = Mouse.current.position.ReadValue();
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, mouse, null, out var local);
            rect.anchoredPosition = local + new Vector2(0f, 30f);
            rect.sizeDelta = new Vector2(110f, 30f);
            editField.onFocusSelectAll = true;
            editField.Select();
            editField.ActivateInputField();
        }

        private void CloseEditField() {
            if (editField != null) Destroy(editField.gameObject);
            editField = null;
            editingDimension = null;
        }

        // ------------------------------------------------------------------ making the piece

        private void MakePiece() {
            var chosen = ChosenFaces();
            if (chosen.Count == 0) {
                if (regions != null && regions.Areas.Count() == 1) chosen = regions.Areas.ToList();  // only one shape: that's it
                else {
                    SetTool(Tool.Pieces);
                    Interface.Interface.Say(regions != null && regions.Areas.Any() ? "Click the areas that make the piece first" : "Draw a closed shape first", false);
                    return;
                }
            }
            var loops = regions.Outline(chosen);
            var part = PolyPart.Make(pieceName, sketch, loops, thickness, editing?.key);
            if (part == null) {
                Interface.Interface.Say("Couldn't make a piece from those areas", false);
                return;
            }
            if (editing != null) part.created = editing.created;
            CustomParts.Add(part);
            Thumbnails.Forget(part.key);
            var updated = editing != null ? CustomParts.UpdateInScene(part) : 0;
            Close();
            var shape = SheetUsage.Shape(part);
            var tooBig = SheetUsage.CurrentRules == SheetUsage.Rules.Current && shape.tooBig;
            if (editing != null) {
                Interface.Interface.Say("Saved " + part.name + (updated > 0 ? " (" + updated + (updated == 1 ? " copy" : " copies") + " in the build updated)" : ""));
                return;
            }
            Interface.Interface.Say(tooBig ? "Made " + part.name + ", but it's bigger than the 4\" × 8\" the rules allow" : "Made " + part.name + ": place it on the robot", !tooBig);
            PartsRecent.Use(part.Id);
            PartsBrowser.Instance?.PlaceCustom(part);
        }
    }
}
