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
    // V2: the polycarbonate editor, a full screen of its own over the build, laid out like a CAD sketch:
    // - A toolbar of icons along the top (hover one for its name and key): sketch tools (select, line, rectangle,
    //   circle, arc, slot), trim, construction, dimension, and the relations (horizontal, vertical, parallel,
    //   perpendicular, equal, coincident, tangent, concentric, midpoint, fix) for what's selected. Shift- or
    //   Ctrl-click selects more than one thing. SketchSolver keeps every relation and dimension true while you
    //   drag and type; a relation that can't hold is refused.
    // - Snapping to the 1/16" grid and to points; dimensions hold the size you type. Undo and redo.
    // - "Make piece": the view tilts into 3D, you click the closed areas that make the piece (several join into
    //   one) and see it extruded as you go, with its screw holes found as you pick (every cut-out up to 1 1/4"
    //   across, its exact shape; click one to leave it out) and "Hole on a curve" to put a hole on any circle or
    //   arc (half a circle at the edge too). Open outlines are marked and explained. Then name it, pick the
    //   thickness and the plastic, and Create: it goes in the parts browser and you place it on the robot.
    // The wheel zooms anywhere on the screen; right or middle drag pans.
    public class SheetEditor : MonoBehaviour {
        private enum Tool { Select, Line, Rectangle, Circle, Arc, Slot, Trim, Dimension }
        private enum Stage { Sketch, Piece }

        private static readonly (Tool tool, string name, string key, string help)[] Tools = {
            (Tool.Select, "Select", "V", "Click a line, shape or point to select it; Shift or Ctrl adds more. Drag a point to move it. Delete removes what's selected."),
            (Tool.Line, "Line", "L", "Click to start, click for each corner. Click the first point to close it; Esc or right-click to stop."),
            (Tool.Rectangle, "Rectangle", "R", "Click one corner, then the opposite one."),
            (Tool.Circle, "Circle", "C", "Click the centre, then a point on the circle."),
            (Tool.Arc, "Arc", "A", "Click the centre, the start, then the end (it goes counter-clockwise)."),
            (Tool.Slot, "Slot", "S", "Click one end's centre, the other end's centre, then how wide it is."),
            (Tool.Trim, "Trim", "T", "Click the part of a curve to cut away: it's trimmed back to where other curves cross it."),
            (Tool.Dimension, "Dimension", "D", "Click a line, a rectangle's side or a circle; or two points for the distance between them. Type the size: it holds."),
        };

        private static readonly (RelationKind kind, string name, string help)[] RelationButtons = {
            (RelationKind.Horizontal, "Horizontal", "Select lines (or two points) to make them horizontal."),
            (RelationKind.Vertical, "Vertical", "Select lines (or two points) to make them vertical."),
            (RelationKind.Parallel, "Parallel", "Select two lines."),
            (RelationKind.Perpendicular, "Perpendicular", "Select two lines."),
            (RelationKind.Equal, "Equal", "Select lines (same length) or circles and arcs (same size)."),
            (RelationKind.Coincident, "Coincident", "Select a point and another point, a line, a circle or an arc."),
            (RelationKind.Tangent, "Tangent", "Select a line and a circle or arc."),
            (RelationKind.Concentric, "Concentric", "Select two circles or arcs."),
            (RelationKind.Midpoint, "Midpoint", "Select a point and a line: the point goes to the line's middle."),
            (RelationKind.Fix, "Fix", "Select points or curves: they stay where they are."),
        };

        public static SheetEditor Instance { get; private set; }
        public static bool IsOpen => Instance != null;

        private SketchData sketch = new();
        private CustomPartData editing;
        private string pieceName;
        private float thickness = 1f / 16f;
        private string finish = PlasticMaterials.Default;
        private Tool tool = Tool.Line;
        private Stage stage = Stage.Sketch;
        private bool gridSnap;   // V2: off unless switched on; Ctrl held snaps
        private bool placingHole;  // piece stage: click a circle or arc for a hole

        // View: canvas units per inch and where the sketch's origin is in the canvas; tilt 0 = from above, 1 = 3D.
        private RectTransform canvasRect, toolbar, side, statusBar;
        private ShapeGraphic shapes;
        private float zoom = 60f;
        private Vector2 pan;
        private float tilt, tiltTarget;
        private bool panning;
        private Vector2 panMouse;
        private TextMeshProUGUI status;
        private readonly List<TextMeshProUGUI> labels = new();
        private int labelsUsed;
        private readonly List<(Tool tool, Image background, ShapeGraphic icon)> toolButtons = new();
        private readonly List<(RelationKind kind, Button button, ShapeGraphic icon)> relationButtons = new();
        private Button makeButton, backButton, redoButton, undoButton;

        // Drawing in progress
        private readonly List<Vector2> clicks = new();
        private int chainStart = -1, chainLast = -1;
        private readonly List<SketchEntity> selectedEntities = new();
        private readonly List<int> selectedPoints = new();
        private int draggingPoint = -1;
        private SketchData dragStart;
        private int dimensionFirstPoint = -1;
        private TMP_InputField editField;
        private SketchDimension editingDimension;
        private Regions regions;
        private bool sketchChanged = true;
        private readonly List<SketchData> undo = new();
        private readonly List<SketchData> redo = new();

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
                Instance.finish = string.IsNullOrEmpty(part.finish) ? PlasticMaterials.Default : part.finish;
                Instance.pieceName = part.name;
            }
            else
                Instance.pieceName = "Plastic piece " + (CustomParts.OfKind(CustomParts.Polycarbonate).Count() + 1);
            Instance.sketch.Upgrade();
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

            // Title bar.
            var top = Ui.Image(transform, "Top", Theme.Panel).rectTransform;
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0f, 52f);
            top.anchoredPosition = Vector2.zero;
            Ui.Row(top, 10f, new RectOffset(18, 18, 0, 0));
            Ui.Text(top, "Title", editing != null ? "Edit polycarbonate piece" : "New polycarbonate piece", Theme.FontTitle + 2f, Theme.Text, Theme.Medium).Size(-1, 52f);
            var spacer = Ui.Rect("Spacer", top);
            spacer.Size(-1, 10f, 1f);
            undoButton = Ui.Button(top, "Undo", "Undo", Undo, false, 6f, Theme.FontSmall);
            undoButton.Size(-1, 32f);
            Ui.Tip(undoButton, "Undo", "Ctrl+Z");
            redoButton = Ui.Button(top, "Redo", "Redo", Redo, false, 6f, Theme.FontSmall);
            redoButton.Size(-1, 32f);
            Ui.Tip(redoButton, "Redo", "Ctrl+Y");
            var cancel = Ui.Button(top, "Cancel", "Cancel", () => {
                Close();
                Interface.Interface.Say("Closed the polycarbonate editor", false);
            }, false, 6f);
            cancel.Size(-1, 32f);
            backButton = Ui.Button(top, "Back", "Back to sketch", () => SetStage(Stage.Sketch), false, 6f);
            backButton.Size(-1, 32f);
            makeButton = Ui.Button(top, "Make", "Make piece", MakeOrCreate, true, 6f);
            makeButton.Size(-1, 32f);
            Ui.Tip(makeButton, "Make piece", "Enter", "Pick the areas that make the piece in 3D, then Create it.");

            // The toolbar of icons, like a CAD sketch's.
            toolbar = Ui.Panel(transform, "Toolbar", Theme.Panel, 10f).rectTransform;
            toolbar.anchorMin = new Vector2(0f, 1f);
            toolbar.anchorMax = new Vector2(1f, 1f);
            toolbar.pivot = new Vector2(0.5f, 1f);
            toolbar.offsetMin = new Vector2(14f, -126f);
            toolbar.offsetMax = new Vector2(-14f, -60f);
            toolbar.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Row(toolbar, 2f, new RectOffset(8, 8, 4, 4));
            Group("Sketch");
            foreach (var def in Tools) {
                if (def.tool == Tool.Trim) {
                    Separator();
                    Group("Modify");
                }
                if (def.tool == Tool.Dimension) {
                    ToolbarButton("Construction", "Construction", "Q", "Turns what's selected into construction lines (dashed): they help you draw but aren't cut.", ToggleConstruction);
                    ToolbarButton("Delete", "Delete", "Del", "Deletes what's selected.", DeleteSelected);
                    Separator();
                    Group("Dimension");
                }
                var tool2 = def.tool;
                var (bg, icon) = ToolbarButton(def.name, def.name, def.key, def.help, () => SetTool(tool2));
                toolButtons.Add((def.tool, bg, icon));
            }
            Separator();
            Group("Relations");
            foreach (var def in RelationButtons) {
                var kind = def.kind;
                var (bg, icon) = ToolbarButton(def.name, def.name, null, def.help, () => AddRelation(kind));
                relationButtons.Add((def.kind, bg.GetComponent<Button>(), icon));
            }
            Separator();
            ToolbarButton("Fit", "Fit", "F", "Fits the sheet and the sketch in the view.", Fit);

            // The drawing area.
            canvasRect = Ui.Rect("Canvas", transform);
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = new Vector2(14f, 40f);
            canvasRect.offsetMax = new Vector2(-344f, -136f);
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
            side.offsetMax = new Vector2(-14f, -136f);
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
            status.rectTransform.Fill(18, 0, 344, 0);

            Interface.Interface.Instance.StartCoroutine(FitNextFrame());
            regions = Regions.Of(sketch);
            sketchChanged = false;
            SetStage(Stage.Sketch);
            SetTool(sketch.entities.Count > 0 ? Tool.Select : Tool.Rectangle);
            Interface.Interface.Say("Draw the piece, then Make piece");
        }

        private void Group(string title) {
            var label = Ui.Text(toolbar, title, title.ToUpperInvariant(), 10f, Theme.TextFaint, Theme.Medium, TextAlignmentOptions.Center);
            label.Size(14f, 58f);
            label.rectTransform.localEulerAngles = new Vector3(0f, 0f, 90f);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        private void Separator() => Ui.Divider(toolbar, true).Size(1f, 46f);

        // An icon over a small name; the tooltip says what it does.
        private (Image background, ShapeGraphic icon) ToolbarButton(string icon, string name, string key, string help, System.Action onClick) {
            var background = Ui.Image(toolbar, name, new Color(1f, 1f, 1f, 0f), 7f, true);
            background.Size(name.Length > 10 ? 74f : 58f, 58f);
            var button = background.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());
            var hover = background.gameObject.AddComponent<HoverTint>();
            hover.target = background;
            var holder = Ui.Rect("Icon", background.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(24f, 24f));
            var graphic = ShapeGraphic.Create(holder);
            SketchIcons.Draw(graphic, icon, Theme.Text);
            var text = Ui.Text(background.transform, "Name", name, 10.5f, Theme.TextDim, Theme.Regular, TextAlignmentOptions.Center);
            text.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(76f, 16f));
            text.enableWordWrapping = false;
            Ui.Tip(background, name, key, help);
            return (background, graphic);
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
            if (stage != Stage.Sketch) return;
            tool = newTool;
            clicks.Clear();
            chainStart = chainLast = -1;
            dimensionFirstPoint = -1;
            if (tool != Tool.Select) ClearSelection();
            foreach (var (t, bg, icon) in toolButtons) {
                var on = t == tool;
                bg.color = on ? Theme.Accent : new Color(1f, 1f, 1f, 0f);
                bg.GetComponent<HoverTint>().enabled = !on;
            }
            BuildSide();
        }

        private void SetStage(Stage newStage) {
            stage = newStage;
            placingHole = false;
            tiltTarget = stage == Stage.Piece ? 1f : 0f;
            clicks.Clear();
            chainStart = chainLast = -1;
            ClearSelection();
            CloseEditField();
            toolbar.gameObject.SetActive(stage == Stage.Sketch);
            // The 3D view takes the toolbar's room.
            var topEdge = stage == Stage.Sketch ? -136f : -66f;
            canvasRect.offsetMax = new Vector2(-344f, topEdge);
            side.offsetMax = new Vector2(-14f, topEdge);
            backButton.gameObject.SetActive(stage == Stage.Piece);
            makeButton.GetComponentInChildren<TextMeshProUGUI>().text = stage == Stage.Sketch ? "Make piece" : editing != null ? "Save piece" : "Create";
            if (stage == Stage.Piece) {
                Interface.Interface.Say(regions != null && regions.Areas.Any()
                    ? "Click the areas that make the piece"
                    : "There's no closed area yet: close the outline first", regions != null && regions.Areas.Any());
                // One area only: it's the piece.
                if (sketch.chosen.Count == 0 && regions != null && regions.Areas.Count() == 1)
                    sketch.chosen.Add(InsidePoint(regions.Areas.First()));
            }
            BuildSide();
        }

        private void MakeOrCreate() {
            if (stage == Stage.Sketch) SetStage(Stage.Piece);
            else CreatePiece();
        }

        // ------------------------------------------------------------------ the side panel

        private void BuildSide() {
            foreach (Transform child in side)
                if (child.GetSiblingIndex() > 0) Destroy(child.gameObject);
            UpdateRelationButtons();
            if (stage == Stage.Piece) {
                BuildPieceSide();
                return;
            }
            var def = Tools.First(t => t.tool == tool);
            Dialog.Heading(side, def.name);
            Wrapped(def.help, Theme.TextDim, 58f);

            var count = selectedEntities.Count + selectedPoints.Count;
            if (count > 0) {
                Dialog.Heading(side, count == 1 && selectedEntities.Count == 1 ? "Selected " + selectedEntities[0].kind.ToString().ToLowerInvariant()
                    : "Selected: " + count + (count == 1 ? " item" : " items"));
                if (selectedEntities.Count == 1 && selectedPoints.Count == 0) {
                    var selected = selectedEntities[0];
                    foreach (var (label, kind) in SizesOf(selected)) {
                        var row = Dialog.Row(side, label, 32f, 90f);
                        var dimension = new SketchDimension { kind = kind, entity = selected.id };
                        var field = Ui.Field(row, label, Inch.Format(sketch.Measure(dimension), false), text => {
                            if (Inch.TryParse(text, out var value)) {
                                Remember();
                                Resize(dimension, value);
                            }
                            else Interface.Interface.Say("Type a size like 1 1/2 or 1.5", false);
                        }, Theme.FontBody, TextAlignmentOptions.Center);
                        field.Size(110f, 30f);
                        Ui.Text(row, "Unit", "in", Theme.FontSmall, Theme.TextFaint).Size(-1, 30f);
                    }
                }
                // Its relations, each removable.
                var related = sketch.relations.Where(r => selectedEntities.Any(e => r.e1 == e.id || r.e2 == e.id)
                                                          || selectedPoints.Any(p => r.p1 == p || r.p2 == p)).ToList();
                if (related.Count > 0) {
                    Ui.Text(side, "Relations", "Relations", Theme.FontSmall, Theme.TextDim, Theme.Medium).Size(-1, 20f);
                    foreach (var relation in related) {
                        var row = Dialog.Row(side, "", 28f, 0f);
                        Ui.Text(row, "Kind", relation.kind.ToString(), Theme.FontSmall, Theme.Text).Size(-1, 28f, 1f);
                        var keep = relation;
                        var remove = Ui.Button(row, "Remove", "Remove", () => {
                            Remember();
                            sketch.relations.Remove(keep);
                            Changed();
                            Interface.Interface.Say("Removed the " + keep.kind.ToString().ToLowerInvariant() + " relation", false);
                        }, false, 5f, Theme.FontSmall);
                        remove.Size(80f, 26f);
                    }
                }
                Wrapped("Pick a relation in the toolbar to add it to what's selected.", Theme.TextFaint, 36f);
            }

            Dialog.Heading(side, "Sketch");
            ToolOptions.Switch(side, "Snap to 1/16\" grid (or hold Ctrl)", gridSnap, on => gridSnap = on);
            var open = sketch.OpenEnds();
            if (open.Count > 0)
                Wrapped(open.Count + (open.Count == 1 ? " open end" : " open ends") + " (red): the outline isn't closed there, so it doesn't make an area yet. Draw to it or drag the end onto the point it should meet.", Theme.Warning, 58f);
            else if (regions != null && regions.Areas.Any())
                Wrapped(regions.Areas.Count() + (regions.Areas.Count() == 1 ? " closed area" : " closed areas") + ": Make piece when you're ready.", Theme.Success, 36f);

            Dialog.Heading(side, "Plastic in this build");
            RulesBlock();
        }

        private void BuildPieceSide() {
            Dialog.Heading(side, "Make piece");
            Wrapped(placingHole ? "Click a circle or an arc (even half a circle on the edge): a screw hole goes at its centre, its size." :
                "Click the areas that make the piece (click again to leave one out). Holes are found as you go: click a blue one to leave it out.", Theme.TextDim, 58f);

            var nameRow = Dialog.Row(side, "Name", 32f, 90f);
            var name = Ui.Field(nameRow, "Name", pieceName, value => pieceName = value.Trim().Length > 0 ? value.Trim() : pieceName);
            name.Size(190f, 30f);
            var thicknessRow = Dialog.Row(side, "Thickness", 32f, 90f);
            var index2 = System.Array.FindIndex(PolyPart.Thicknesses, t => Mathf.Abs(t.inches - thickness) < 1e-4f);
            Popup.Choice(thicknessRow, PolyPart.Thicknesses.Select(t => t.label).ToArray(), Mathf.Max(0, index2), i => thickness = PolyPart.Thicknesses[i].inches, 190f);
            var materialRow = Dialog.Row(side, "Plastic", 34f, 90f);
            var plastic = PlasticMaterials.Find(finish);
            var materialButton = Ui.Button(materialRow, "Plastic", null, OpenPlasticPicker, false, 6f);
            materialButton.Size(190f, 32f);
            var swatch = Ui.Image(materialButton.transform, "Swatch", Swatch(plastic), 4f);
            swatch.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(20f, 20f));
            var plasticName = Ui.Text(materialButton.transform, "Name", plastic.name, Theme.FontSmall, Theme.Text);
            plasticName.rectTransform.Fill(34, 0, 6, 0);
            Ui.Tip(materialButton, "Plastic", null, "What the piece is cut from: its color, finish and weight. Sold by " + plastic.sold + ".");

            var chosen = ChosenFaces();
            if (chosen.Count > 0 && regions != null) {
                var loops = regions.Outline(chosen);
                var hull = PolyPart.Hull(loops.Where(l => Regions.SignedArea(l) > 0f).SelectMany(l => l).ToList());
                var (box, _) = PolyPart.SmallestBox(hull);
                var area = loops.Sum(Regions.SignedArea);
                Wrapped(chosen.Count + (chosen.Count == 1 ? " area" : " areas") + " · " + Inch.Format(Mathf.Min(box.x, box.y)) + " × " +
                        Inch.Format(Mathf.Max(box.x, box.y)) + " · " + area.ToString("0.##", CultureInfo.InvariantCulture) + " sq in", Theme.Text, 22f);
                if (SheetUsage.CurrentRules == SheetUsage.Rules.Current && !SheetUsage.Fits(box, SheetUsage.MaxPiece))
                    Wrapped("Bigger than 4\" × 8\": the rules don't allow a piece this size", Theme.Danger, 36f);
                var holes = Holes(chosen);
                Dialog.Heading(side, "Screw holes");
                Wrapped(holes.Count == 0 ? "None yet: cut-outs up to 1 1/4\" across inside the piece become holes, or use Hole on a curve" :
                    string.Join("\n", holes.Select((h, i) => (i + 1) + ". " + Describe(h))), holes.Count == 0 ? Theme.TextFaint : Theme.Text, Mathf.Max(40f, 18f * holes.Count + 4f));
            }
            else
                Wrapped(regions != null && regions.Areas.Any() ? "No areas picked yet: click them in the view" :
                    "There's no closed area: go back to the sketch and close the outline (open ends show red).", regions != null && regions.Areas.Any() ? Theme.TextFaint : Theme.Warning, 40f);
            var open = sketch.OpenEnds();
            if (open.Count > 0)
                Wrapped(open.Count + (open.Count == 1 ? " end isn't" : " ends aren't") + " joined (red): that outline doesn't make an area.", Theme.Warning, 36f);

            var holeButton = Ui.Button(side, "Hole on a curve", placingHole ? "Click a circle or arc…" : "Hole on a curve", () => {
                placingHole = !placingHole;
                BuildSide();
            }, placingHole, 5f, Theme.FontSmall);
            holeButton.Size(-1, 30f);
            Ui.Tip(holeButton, "Hole on a curve", null, "Puts a screw hole on a circle or arc the finder didn't take (like half a circle on the edge). Click the hole's marker again to remove it.");

            Dialog.Heading(side, "Plastic in this build");
            RulesBlock();
        }

        private void RulesBlock() {
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
            Wrapped(usage, layout.Legal ? Theme.TextDim : Theme.Warning, 40f);
            var sheets = Ui.Button(side, "Sheets", "Show the sheet layout", () => SheetsDialog.Open(), false, 5f, Theme.FontSmall);
            sheets.Size(-1, 30f);
        }

        private void Wrapped(string text, Color color, float height) {
            var label = Ui.Text(side, "Text", text, Theme.FontSmall, color);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.Size(-1, height);
        }

        private static string Describe(CustomHole h) =>
            h.outline != null && h.outline.Length >= 3 ? Inch.Format(Mathf.Min(h.size, h.size2 > 0f ? h.size2 : h.size)) + " × " + Inch.Format(Mathf.Max(h.size, h.size2)) + " (its shape)"
            : h.square ? Inch.Format(h.size) + " square" : Inch.Format(h.size) + " round";

        private static IEnumerable<(string, DimensionKind)> SizesOf(SketchEntity e) => e.kind switch {
            EntityKind.Line => new[] { ("Length", DimensionKind.Length) },
            EntityKind.Rectangle => new[] { ("Width", DimensionKind.Width), ("Height", DimensionKind.Height) },
            _ => new[] { ("Diameter", DimensionKind.Diameter) },
        };

        // ------------------------------------------------------------------ selection and relations

        private void ClearSelection() {
            selectedEntities.Clear();
            selectedPoints.Clear();
        }

        private void UpdateRelationButtons() {
            foreach (var (kind, button, icon) in relationButtons) {
                var ok = RelationFits(kind, out _);
                button.interactable = ok;
                SketchIcons.Draw(icon, kind.ToString(), ok ? Theme.Text : new Color(1f, 1f, 1f, 0.22f));
                var name = button.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
                if (name != null) name.color = ok ? Theme.TextDim : Theme.TextFaint;
            }
        }

        private static bool IsRound(SketchEntity e) => e.kind == EntityKind.Circle || e.kind == EntityKind.Arc;

        // Whether the selection suits a relation; the relations it makes.
        private bool RelationFits(RelationKind kind, out List<SketchRelation> made) {
            made = new List<SketchRelation>();
            var lines = selectedEntities.Where(e => e.kind == EntityKind.Line).ToList();
            var rounds = selectedEntities.Where(IsRound).ToList();
            var points = selectedPoints;
            var others = selectedEntities.Count - lines.Count - rounds.Count;
            switch (kind) {
                case RelationKind.Horizontal:
                case RelationKind.Vertical:
                    if (lines.Count > 0 && rounds.Count == 0 && others == 0 && points.Count == 0)
                        made.AddRange(lines.Select(l => new SketchRelation { kind = kind, e1 = l.id }));
                    else if (points.Count == 2 && selectedEntities.Count == 0)
                        made.Add(new SketchRelation { kind = kind, p1 = points[0], p2 = points[1] });
                    break;
                case RelationKind.Parallel:
                case RelationKind.Perpendicular:
                    if (lines.Count == 2 && selectedEntities.Count == 2 && points.Count == 0)
                        made.Add(new SketchRelation { kind = kind, e1 = lines[0].id, e2 = lines[1].id });
                    break;
                case RelationKind.Equal:
                    if (points.Count == 0 && others == 0 && (lines.Count >= 2 && rounds.Count == 0 || rounds.Count >= 2 && lines.Count == 0)) {
                        var list = lines.Count >= 2 ? lines : rounds;
                        for (var i = 1; i < list.Count; i++) made.Add(new SketchRelation { kind = kind, e1 = list[0].id, e2 = list[i].id });
                    }
                    break;
                case RelationKind.Coincident:
                    if (points.Count == 2 && selectedEntities.Count == 0)
                        made.Add(new SketchRelation { kind = kind, p1 = points[0], p2 = points[1] });
                    else if (points.Count == 1 && selectedEntities.Count == 1 && others == 0)
                        made.Add(new SketchRelation { kind = kind, p1 = points[0], e1 = selectedEntities[0].id });
                    break;
                case RelationKind.Tangent:
                    if (lines.Count == 1 && rounds.Count == 1 && selectedEntities.Count == 2 && points.Count == 0)
                        made.Add(new SketchRelation { kind = kind, e1 = lines[0].id, e2 = rounds[0].id });
                    else if (rounds.Count == 2 && selectedEntities.Count == 2 && points.Count == 0)
                        made.Add(new SketchRelation { kind = kind, e1 = rounds[0].id, e2 = rounds[1].id });
                    break;
                case RelationKind.Concentric:
                    if (rounds.Count == 2 && selectedEntities.Count == 2 && points.Count == 0)
                        made.Add(new SketchRelation { kind = kind, e1 = rounds[0].id, e2 = rounds[1].id });
                    break;
                case RelationKind.Midpoint:
                    if (points.Count == 1 && lines.Count == 1 && selectedEntities.Count == 1)
                        made.Add(new SketchRelation { kind = kind, p1 = points[0], e1 = lines[0].id });
                    break;
                case RelationKind.Fix:
                    made.AddRange(points.Select(p => new SketchRelation { kind = kind, p1 = p }));
                    made.AddRange(selectedEntities.Select(e => new SketchRelation { kind = kind, e1 = e.id }));
                    break;
            }
            return made.Count > 0;
        }

        private void AddRelation(RelationKind kind) {
            if (!RelationFits(kind, out var made)) {
                Interface.Interface.Say(RelationButtons.First(r => r.kind == kind).help, false);
                return;
            }
            Remember();
            foreach (var r in made) {
                r.id = sketch.nextId++;
                sketch.relations.Add(r);
            }
            // Keep a point on what it's made to sit on still: move the rest.
            if (!SketchSolver.Solve(sketch, selectedPoints.Count == 1 && kind != RelationKind.Coincident && kind != RelationKind.Midpoint ? selectedPoints : null)) {
                foreach (var r in made) sketch.relations.Remove(r);
                undo.RemoveAt(undo.Count - 1);
                Interface.Interface.Say("That " + kind.ToString().ToLowerInvariant() + " relation can't hold with the others (or the dimensions)", false);
                return;
            }
            Changed();
            Interface.Interface.Say("Made " + (made.Count == 1 ? "it" : "them") + " " + kind.ToString().ToLowerInvariant());
        }

        private void ToggleConstruction() {
            if (selectedEntities.Count == 0) {
                Interface.Interface.Say("Select lines or shapes first", false);
                return;
            }
            Remember();
            var on = !selectedEntities.All(e => e.construction);
            foreach (var e in selectedEntities) e.construction = on;
            Changed();
            Interface.Interface.Say(on ? "Made construction lines (not cut)" : "Made cut lines");
        }

        // A size typed for something selected: its near end stays, the solver moves the rest.
        private void Resize(SketchDimension dimension, float value) {
            var existing = sketch.dimensions.FirstOrDefault(d => d.kind == dimension.kind && d.entity == dimension.entity);
            var keep = existing ?? dimension;
            var before = keep.value;
            keep.value = Mathf.Max(0.01f, value);
            var temporary = existing == null;
            if (temporary) sketch.dimensions.Add(keep);
            if (!SketchSolver.Solve(sketch, Anchor(keep))) {
                Interface.Interface.Say("That size doesn't work with the relations and dimensions it has", false);
                if (temporary) sketch.dimensions.Remove(keep);
                else keep.value = before;
                undo.RemoveAt(undo.Count - 1);
                return;
            }
            if (temporary) sketch.dimensions.Remove(keep);
            Changed();
        }

        // What stays put when a dimension changes.
        private IEnumerable<int> Anchor(SketchDimension d) {
            var e = sketch.Entity(d.entity);
            return d.kind switch {
                DimensionKind.Length when e != null => new[] { e.a },
                DimensionKind.Width when e != null => new[] { e.a, e.d },
                DimensionKind.Height when e != null => new[] { e.a, e.b },
                DimensionKind.Diameter when e != null => new[] { e.a },
                DimensionKind.Distance => new[] { d.from },
                _ => new int[0],
            };
        }

        // ------------------------------------------------------------------ view

        private const float TiltAngle = 38f;  // degrees the piece stage leans the sheet back

        // The view centre (sketch inches) the tilt leans around, and how far the eye is (inches).
        private Vector2 ViewCenter => ((new Vector2(canvasRect.rect.width, canvasRect.rect.height) / 2f) - pan) / zoom;
        private float EyeDistance => Mathf.Max(canvasRect.rect.width, canvasRect.rect.height) / zoom * 1.6f;

        /// <summary>A sketch point at a height (inches above the sheet) on the canvas: from straight above, or
        /// leaning back in perspective while the piece stage is shown.</summary>
        private Vector2 ToCanvas(Vector2 inches, float height = 0f) {
            if (tilt <= 0.0001f) return inches * zoom + pan + canvasRect.rect.min;
            var theta = tilt * TiltAngle * Mathf.Deg2Rad;
            var c = ViewCenter;
            var q = inches - c;
            var y = q.y * Mathf.Cos(theta) + height * Mathf.Sin(theta);
            var z = -q.y * Mathf.Sin(theta) + height * Mathf.Cos(theta);  // towards the eye
            var f = EyeDistance;
            var s = f / Mathf.Max(f - z, 0.01f);
            return (c + new Vector2(q.x, y) * s) * zoom + pan + canvasRect.rect.min;
        }

        /// <summary>The sketch point (on the sheet) under a canvas point.</summary>
        private Vector2 ToSketch(Vector2 canvas) {
            var flat = (canvas - canvasRect.rect.min - pan) / zoom;
            if (tilt <= 0.0001f) return flat;
            var theta = tilt * TiltAngle * Mathf.Deg2Rad;
            var c = ViewCenter;
            var u = flat.x - c.x;
            var v = flat.y - c.y;
            var f = EyeDistance;
            var denominator = f * Mathf.Cos(theta) - v * Mathf.Sin(theta);
            var qy = Mathf.Abs(denominator) < 1e-5f ? 0f : v * f / denominator;
            var s = f / (f + qy * Mathf.Sin(theta));
            return c + new Vector2(u / s, qy);
        }

        private bool MouseInCanvas(out Vector2 canvas) {
            var mouse = Mouse.current.position.ReadValue();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse, null, out canvas);
            return canvasRect.rect.Contains(canvas) && !OverPanel(mouse);
        }

        private bool OverPanel(Vector2 mouse) =>
            RectTransformUtility.RectangleContainsScreenPoint(side, mouse) || (toolbar.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(toolbar, mouse))
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
            tilt = Mathf.MoveTowards(tilt, tiltTarget, Time.unscaledDeltaTime * 2.5f);
            undoButton.interactable = undo.Count > 0;
            redoButton.interactable = redo.Count > 0;

            if (!Typing && keyboard != null && !Popup.IsOpen && Dialog.OpenCount == 0) Keys(keyboard);
            if (this == null || canvasRect == null) return;

            var inside = MouseInCanvas(out var canvasMouse);
            // Pan: right or middle drag; zoom: the wheel anywhere on the editor (about the mouse over the drawing).
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
            var wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && !Popup.IsOpen && Dialog.OpenCount == 0) {
                var about = inside ? canvasMouse : canvasRect.rect.center;
                var before = ToSketch(about);
                zoom = Mathf.Clamp(zoom * (wheel > 0f ? 1.15f : 1f / 1.15f), 4f, 4000f);
                pan += about - ToCanvas(before);
            }

            // V2: the grid snaps only when switched on or while Ctrl is held (points always catch).
            var ctrlHeld = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed);
            var free = !(gridSnap || ctrlHeld) || keyboard != null && keyboard.shiftKey.isPressed && tool != Tool.Select;
            var snapped = Snap(ToSketch(canvasMouse), free, out var snapPoint);
            if (inside && !panning) {
                if (stage == Stage.Sketch) HandleMouse(mouse, snapped, snapPoint, canvasMouse);
                else HandlePieceMouse(mouse, canvasMouse);
            }
            Draw(inside ? snapped : (Vector2?)null, snapPoint, canvasMouse);
            status.text = (inside && stage == Stage.Sketch ? "X " + Inch.Format(snapped.x) + "   Y " + Inch.Format(snapped.y) + "      " : "") +
                          "Wheel: zoom · Right-drag: pan · F: fit · Ctrl: snap to the grid · Ctrl+Z / Ctrl+Y: undo / redo";
        }

        private void Keys(Keyboard keyboard) {
            var ctrl = keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed;
            if (keyboard.escapeKey.wasPressedThisFrame) {
                if (editField != null) CloseEditField();
                else if (placingHole) {
                    placingHole = false;
                    BuildSide();
                }
                else if (stage == Stage.Piece) SetStage(Stage.Sketch);
                else if (clicks.Count > 0 || chainLast >= 0 || dimensionFirstPoint >= 0) {
                    clicks.Clear();
                    chainStart = chainLast = -1;
                    dimensionFirstPoint = -1;
                }
                else if (selectedEntities.Count + selectedPoints.Count > 0) {
                    ClearSelection();
                    BuildSide();
                }
                else if (tool != Tool.Select) SetTool(Tool.Select);
                return;
            }
            if (ctrl && (keyboard.yKey.wasPressedThisFrame || keyboard.shiftKey.isPressed && keyboard.zKey.wasPressedThisFrame)) {
                Redo();
                return;
            }
            if (ctrl && keyboard.zKey.wasPressedThisFrame) {
                Undo();
                return;
            }
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) MakeOrCreate();
            if (keyboard.fKey.wasPressedThisFrame) Fit();
            if (stage != Stage.Sketch || ctrl) return;
            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) DeleteSelected();
            if (keyboard.qKey.wasPressedThisFrame) ToggleConstruction();
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
            if (free) return p;
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
                case Tool.Select: {
                    var keyboard = Keyboard.current;
                    var add = keyboard != null && (keyboard.shiftKey.isPressed || keyboard.ctrlKey.isPressed);
                    if (pressed) {
                        var point = HoverPoint(canvasMouse);
                        if (point >= 0) {
                            // A point: select it (adding with Shift/Ctrl), and dragging moves it.
                            if (!add && !selectedPoints.Contains(point)) ClearSelection();
                            if (add && selectedPoints.Contains(point)) selectedPoints.Remove(point);
                            else if (!selectedPoints.Contains(point)) selectedPoints.Add(point);
                            draggingPoint = point;
                            dragStart = sketch.Copy();
                            BuildSide();
                        }
                        else {
                            var entity = HoverEntity(canvasMouse);
                            if (!add) ClearSelection();
                            if (entity != null) {
                                if (selectedEntities.Contains(entity)) selectedEntities.Remove(entity);
                                else selectedEntities.Add(entity);
                            }
                            BuildSide();
                        }
                    }
                    if (draggingPoint >= 0) {
                        if (mouse.leftButton.isPressed) {
                            var held = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed);
                            var target = Snap(ToSketch(canvasMouse), !(gridSnap || held) || keyboard != null && keyboard.shiftKey.isPressed, out _);
                            MovePoint(draggingPoint, target);
                            Changed(false);
                        }
                        else {
                            // Dropped on another point: they become one.
                            var dropped = sketch.Point(draggingPoint);
                            var other = sketch.points.FirstOrDefault(sp => sp.id != draggingPoint && dropped != null && Vector2.Distance(sp.p, dropped.p) < 1e-4f);
                            var moved = dragStart != null && dropped != null && Vector2.Distance(dragStart.P(draggingPoint), dropped.p) > 1e-5f;
                            if (moved) {
                                undo.Add(dragStart);
                                redo.Clear();
                            }
                            if (other != null && !IsCircleCenterOnly(draggingPoint)) {
                                sketch.Merge(draggingPoint, other.id);
                                selectedPoints.Remove(draggingPoint);
                            }
                            draggingPoint = -1;
                            dragStart = null;
                            Changed();
                        }
                    }
                    break;
                }
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
                    sketch.Add(EntityKind.Arc, sketch.AddPoint(clicks[0]).id, sketch.AddPoint(clicks[1]).id, sketch.AddPoint(endOnCircle).id, radius: arcRadius);
                    clicks.Clear();
                    Changed();
                    break;
                case Tool.Slot:
                    if (!pressed) break;
                    clicks.Add(p);
                    if (clicks.Count < 3) break;
                    var half = DistanceToLine(clicks[2], clicks[0], clicks[1]);
                    if (half < 0.01f || Vector2.Distance(clicks[0], clicks[1]) < 0.01f) {
                        clicks.Clear();
                        Interface.Interface.Say("A slot needs a length and a width", false);
                        break;
                    }
                    Remember();
                    AddSlot(clicks[0], clicks[1], half);
                    clicks.Clear();
                    Changed();
                    break;
                case Tool.Trim: {
                    // V2: like SolidWorks' power trim: click a curve, or hold the button and drag across the
                    // pieces to cut away.
                    var at = ToSketch(canvasMouse);
                    if (pressed) {
                        Remember();
                        trimming = true;
                        trimmedCount = 0;
                        trimLast = at;
                        var entity = HoverEntity(canvasMouse);
                        if (entity != null) {
                            sketch.Trim(entity, at);
                            trimmedCount++;
                            Changed();
                        }
                        break;
                    }
                    if (!trimming) break;
                    if (mouse.leftButton.isPressed) {
                        if (Vector2.Distance(at, trimLast) * zoom < 2f) break;
                        foreach (var (entity, cross) in CrossedBy(trimLast, at)) {
                            if (!sketch.entities.Contains(entity)) continue;
                            sketch.Trim(entity, cross);
                            trimmedCount++;
                        }
                        trimLast = at;
                        Changed();
                        break;
                    }
                    trimming = false;
                    if (trimmedCount == 0) {
                        undo.RemoveAt(undo.Count - 1);
                        Interface.Interface.Say("Click a curve, or drag across the pieces to cut away", false);
                    }
                    else Interface.Interface.Say("Trimmed " + trimmedCount + (trimmedCount == 1 ? " piece" : " pieces"));
                    break;
                }
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
                    var hovered = HoverEntity(canvasMouse);
                    if (hoverPoint >= 0 && (hovered == null || DistanceToPointPixels(hoverPoint, canvasMouse) < 6f)) {
                        dimensionFirstPoint = hoverPoint;
                        Interface.Interface.Say("Now click the second point");
                        break;
                    }
                    if (hovered == null) {
                        Interface.Interface.Say("Click a line, a rectangle's side, a circle, or a point", false);
                        break;
                    }
                    Remember();
                    var kind = hovered.kind switch {
                        EntityKind.Line => DimensionKind.Length,
                        EntityKind.Rectangle => NearestRectangleSide(hovered, ToSketch(canvasMouse), out _),
                        _ => DimensionKind.Diameter,
                    };
                    var sideIndex = 0;
                    if (hovered.kind == EntityKind.Rectangle) NearestRectangleSide(hovered, ToSketch(canvasMouse), out sideIndex);
                    AddDimension(new SketchDimension { kind = kind, entity = hovered.id, from = sideIndex });
                    break;
            }
        }

        private bool trimming;
        private int trimmedCount;
        private Vector2 trimLast;

        // The curves the mouse crossed between two places, and where (for dragging through pieces to trim).
        private List<(SketchEntity, Vector2)> CrossedBy(Vector2 from, Vector2 to) {
            var result = new List<(SketchEntity, Vector2)>();
            foreach (var e in sketch.entities.ToList()) {
                var line = sketch.Polyline(e, out var closed);
                for (var i = 0; i < line.Count - (closed ? 0 : 1); i++) {
                    var a = line[i];
                    var b = line[(i + 1) % line.Count];
                    var r = to - from;
                    var q = b - a;
                    var den = r.x * q.y - r.y * q.x;
                    if (Mathf.Abs(den) < 1e-12f) continue;
                    var t = ((a.x - from.x) * q.y - (a.y - from.y) * q.x) / den;
                    var u = ((a.x - from.x) * r.y - (a.y - from.y) * r.x) / den;
                    if (t < 0f || t > 1f || u < 0f || u > 1f) continue;
                    result.Add((e, from + r * t));
                    break;
                }
            }
            return result;
        }

        private static float DistanceToLine(Vector2 p, Vector2 a, Vector2 b) {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x);
            return Mathf.Abs(Vector2.Dot(p - a, n));
        }

        // A slot: two half circles joined by two straight sides, sharing their ends.
        private void AddSlot(Vector2 a, Vector2 b, float half) {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * half;
            var ca = sketch.AddPoint(a);
            var cb = sketch.AddPoint(b);
            var a1 = sketch.AddPoint(a + n);
            var a2 = sketch.AddPoint(a - n);
            var b1 = sketch.AddPoint(b + n);
            var b2 = sketch.AddPoint(b - n);
            var top = sketch.Add(EntityKind.Line, a1.id, b1.id);
            var bottom = sketch.Add(EntityKind.Line, b2.id, a2.id);
            var endB = sketch.Add(EntityKind.Arc, cb.id, b2.id, b1.id, radius: half);   // counter-clockwise round the far end
            var endA = sketch.Add(EntityKind.Arc, ca.id, a1.id, a2.id, radius: half);
            sketch.Relate(RelationKind.Parallel, top.id, bottom.id);
            sketch.Relate(RelationKind.Equal, endA.id, endB.id);
            sketch.Relate(RelationKind.Tangent, top.id, endA.id);
            sketch.Relate(RelationKind.Tangent, top.id, endB.id);
            sketch.Relate(RelationKind.Tangent, bottom.id, endA.id);
            Interface.Interface.Say("Added a " + Inch.Format(half * 2f) + " × " + Inch.Format(Vector2.Distance(a, b) + half * 2f) + " slot");
        }

        // The piece stage: click areas to pick them, a blue hole to leave it out, or (placing a hole) a curve.
        private void HandlePieceMouse(UnityEngine.InputSystem.Mouse mouse, Vector2 canvasMouse) {
            if (!mouse.leftButton.wasPressedThisFrame || regions == null || tilt < 0.5f) return;
            var at = ToSketch(canvasMouse);
            if (placingHole) {
                var curve = HoverEntity(canvasMouse);
                if (curve == null || !IsRound(curve)) {
                    Interface.Interface.Say("Click a circle or an arc", false);
                    return;
                }
                Remember();
                var center = sketch.P(curve.a);
                var existing = sketch.manualHoles.FindIndex(h => Vector2.Distance(h.center, center) < 1e-3f);
                if (existing >= 0) {
                    sketch.manualHoles.RemoveAt(existing);
                    Interface.Interface.Say("Removed the hole on that curve", false);
                }
                else {
                    sketch.manualHoles.Add(new ManualHole { center = center, radius = curve.radius });
                    Interface.Interface.Say("Put a " + Inch.Format(curve.radius * 2f) + " hole on it");
                }
                placingHole = false;
                BuildSide();
                return;
            }
            var chosen = ChosenFaces();
            // A hole of the piece: leave it out (or take it back).
            if (chosen.Count > 0) {
                var loops = regions.Outline(chosen);
                var holeLoops = loops.Where(l => Regions.SignedArea(l) <= 0f).ToList();
                var inHole = holeLoops.FirstOrDefault(h => Geometry.MeshSlicer.Contains(h, at));
                if (inHole != null) {
                    Remember();
                    var off = sketch.notHoles.FindIndex(n => Geometry.MeshSlicer.Contains(inHole, n));
                    if (off >= 0) {
                        sketch.notHoles.RemoveAt(off);
                        Interface.Interface.Say("It's a screw hole again");
                    }
                    else {
                        sketch.notHoles.Add(at);
                        Interface.Interface.Say("Left that cut-out out of the screw holes", false);
                    }
                    BuildSide();
                    return;
                }
            }
            var face = regions.AreaAt(at);
            if (face == null) {
                Interface.Interface.Say(sketch.OpenEnds().Count > 0 ? "That's not inside a closed shape: an outline is open (red ends)" : "Click inside a closed shape", false);
                return;
            }
            Remember();
            var already = sketch.chosen.FindIndex(c => regions.AreaAt(c) == face);
            if (already >= 0) sketch.chosen.RemoveAt(already);
            else sketch.chosen.Add(at);
            BuildSide();
        }

        // A point well inside an area (for choosing it without a click).
        private static Vector2 InsidePoint(Regions.Face face) {
            var poly = face.polygon;
            var center = Vector2.zero;
            foreach (var p in poly) center += p;
            center /= Mathf.Max(1, poly.Count);
            if (Geometry.MeshSlicer.Contains(poly, center) && !face.holes.Any(h => Geometry.MeshSlicer.Contains(h.polygon, center))) return center;
            // Else just inside the first edge.
            var a = poly[0];
            var b = poly[1];
            var d = (b - a).normalized;
            return (a + b) / 2f + new Vector2(-d.y, d.x) * 0.01f;
        }

        private bool IsCircleCenterOnly(int point) => sketch.entities.Any(e => (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc) && e.a == point);

        private void AddRectangle(Vector2 corner, Vector2 opposite) {
            var a = sketch.AddPoint(corner);
            var b = sketch.AddPoint(new Vector2(opposite.x, corner.y));
            var c = sketch.AddPoint(opposite);
            var d = sketch.AddPoint(new Vector2(corner.x, opposite.y));
            sketch.Explode(sketch.Add(EntityKind.Rectangle, a.id, b.id, c.id, d.id));
        }

        private void AddDimension(SketchDimension dimension) {
            dimension.id = sketch.nextId++;
            dimension.value = sketch.Measure(dimension);
            sketch.dimensions.Add(dimension);
            Changed();
            OpenEditField(dimension);
        }

        // Which side of a rectangle is nearest: its width (a-b or c-d) or height (b-c or d-a), and which of the
        // two (side 1 = the second, where the dimension is drawn).
        private DimensionKind NearestRectangleSide(SketchEntity rect, Vector2 p, out int sideIndex) {
            float Side(Vector2 a, Vector2 b) => Overlay.DistanceToSegment(p, a, b);
            float ab = Side(sketch.P(rect.a), sketch.P(rect.b)), cd = Side(sketch.P(rect.c), sketch.P(rect.d));
            float bc = Side(sketch.P(rect.b), sketch.P(rect.c)), da = Side(sketch.P(rect.d), sketch.P(rect.a));
            if (Mathf.Min(ab, cd) <= Mathf.Min(bc, da)) {
                sideIndex = cd < ab ? 1 : 0;
                return DimensionKind.Width;
            }
            sideIndex = da < bc ? 1 : 0;
            return DimensionKind.Height;
        }

        // Moving a point: the solver keeps every relation, dimension and shape rule (a rectangle stays square, an
        // arc's ends stay on it); if it can't, the point stays where it was.
        private void MovePoint(int id, Vector2 target) {
            var point = sketch.Point(id);
            if (point == null) return;
            var before = sketch.points.ToDictionary(p => p.id, p => p.p);
            var radii = sketch.entities.ToDictionary(e => e.id, e => e.radius);
            point.p = target;
            if (SketchSolver.Solve(sketch, new[] { id })) return;
            foreach (var p in sketch.points) p.p = before[p.id];
            foreach (var e in sketch.entities) e.radius = radii[e.id];
        }

        private void DeleteSelected() {
            var entities = selectedEntities.Where(e => sketch.entities.Contains(e)).ToList();
            var dimensionsOnPoints = selectedPoints.Count;
            if (entities.Count == 0 && dimensionsOnPoints == 0) {
                Interface.Interface.Say("Select something first", false);
                return;
            }
            Remember();
            foreach (var e in entities) sketch.Remove(e);
            // A selected point takes the curves on it with it.
            foreach (var id in selectedPoints.ToList())
                foreach (var e in sketch.entities.Where(e => sketch.PointsOf(e).Contains(id)).ToList())
                    sketch.Remove(e);
            ClearSelection();
            Changed();
            Interface.Interface.Say("Deleted");
        }

        private void Remember() {
            undo.Add(sketch.Copy());
            if (undo.Count > 80) undo.RemoveAt(0);
            redo.Clear();
        }

        private void Undo() {
            if (undo.Count == 0) {
                Interface.Interface.Say("Nothing to undo", false);
                return;
            }
            redo.Add(sketch.Copy());
            sketch = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            AfterHistory();
            Interface.Interface.Say("Undone");
        }

        private void Redo() {
            if (redo.Count == 0) {
                Interface.Interface.Say("Nothing to redo", false);
                return;
            }
            undo.Add(sketch.Copy());
            sketch = redo[^1];
            redo.RemoveAt(redo.Count - 1);
            AfterHistory();
            Interface.Interface.Say("Redone");
        }

        private void AfterHistory() {
            ClearSelection();
            clicks.Clear();
            chainStart = chainLast = -1;
            Changed();
        }

        private void Changed(bool rebuildSide = true) {
            sketchChanged = true;
            selectedEntities.RemoveAll(e => !sketch.entities.Contains(e));
            selectedPoints.RemoveAll(p => sketch.Point(p) == null);
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

        // The screw holes the piece would get now (sketch coordinates: x, 0, y).
        private List<CustomHole> Holes(List<Regions.Face> chosen) {
            if (chosen.Count == 0 || regions == null) return new List<CustomHole>();
            var loops = regions.Outline(chosen);
            return PolyPart.FindHoles(sketch, loops.Where(l => Regions.SignedArea(l) > 0f).ToList(), loops.Where(l => Regions.SignedArea(l) <= 0f).ToList(), thickness);
        }

        // ------------------------------------------------------------------ drawing

        private static readonly Color Paper = new(0.16f, 0.18f, 0.21f, 1f);
        private static readonly Color GridFine = new(1f, 1f, 1f, 0.035f);
        private static readonly Color GridInch = new(1f, 1f, 1f, 0.09f);
        private static readonly Color Ink = new(0.92f, 0.94f, 0.97f, 1f);
        private static readonly Color Construction = new(0.55f, 0.6f, 0.68f, 1f);
        private static readonly Color HoleBlue = new(0.3f, 0.66f, 1f, 1f);
        private static readonly Color RelationGreen = new(0.45f, 0.85f, 0.55f, 1f);

        private void Draw(Vector2? cursor, int snapPoint, Vector2 canvasMouse) {
            shapes.Clear();
            labelsUsed = 0;
            var rect = canvasRect.rect;
            var sheet = SheetSize;

            // The sheet, its grid, and its size.
            var corners = new[] { ToCanvas(Vector2.zero), ToCanvas(new Vector2(sheet.x, 0f)), ToCanvas(sheet), ToCanvas(new Vector2(0f, sheet.y)) };
            shapes.Quad(corners[0], corners[1], corners[2], corners[3], Paper);
            var min = Vector2.Min(ToSketch(rect.min), ToSketch(new Vector2(rect.xMin, rect.yMax)));
            var max = Vector2.Max(ToSketch(rect.max), ToSketch(new Vector2(rect.xMax, rect.yMin)));
            min = Vector2.Max(min, new Vector2(-200f, -200f));
            max = Vector2.Min(max, new Vector2(200f, 200f));
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
            shapes.Polyline(corners, edge, 1.5f, true);
            Label(ToCanvas(new Vector2(sheet.x / 2f, sheet.y)) + new Vector2(0f, 14f),
                SheetUsage.CurrentRules == SheetUsage.Rules.Current ? "Largest piece allowed: 4\" × 8\"" : "Sheet: 12\" × 24\"", Theme.TextDim, false);

            if (stage == Stage.Piece) {
                DrawPiece(canvasMouse, cursor.HasValue);
                DrawOpenEnds();
                for (var i = 0; i < labels.Count; i++) labels[i].transform.parent.gameObject.SetActive(i < labelsUsed);
                return;
            }

            // The sketch.
            var hoverEntity = tool == Tool.Select || tool == Tool.Dimension || tool == Tool.Trim ? HoverEntity(canvasMouse) : null;
            foreach (var e in sketch.entities) {
                var line = sketch.Polyline(e, out var closed).Select(p => ToCanvas(p)).ToList();
                var selected = selectedEntities.Contains(e);
                var color = selected ? Theme.Accent : e == hoverEntity ? (tool == Tool.Trim ? Theme.Danger : Theme.AccentHover) : e.construction ? Construction : Ink;
                if (e.construction) {
                    for (var i = 0; i < line.Count - (closed ? 0 : 1); i++) shapes.Dashed(line[i], line[(i + 1) % line.Count], color, 1.2f);
                }
                else shapes.Polyline(line, color, selected ? 2.5f : 1.8f, closed);
                if (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc) {
                    var c = ToCanvas(sketch.P(e.a));
                    shapes.Line(c - new Vector2(4f, 0f), c + new Vector2(4f, 0f), color, 1f);
                    shapes.Line(c - new Vector2(0f, 4f), c + new Vector2(0f, 4f), color, 1f);
                }
            }
            var hoverPoint = tool == Tool.Select ? HoverPoint(canvasMouse) : -1;
            foreach (var p in sketch.points) {
                if (IsCircleCenterOnly(p.id) && !selectedPoints.Contains(p.id) && p.id != hoverPoint) continue;
                var c = ToCanvas(p.p);
                var color = selectedPoints.Contains(p.id) ? Theme.Accent : p.id == hoverPoint ? Theme.AccentHover : p.id == dimensionFirstPoint ? Theme.Dimension : Ink;
                var size = selectedPoints.Contains(p.id) || p.id == hoverPoint ? 4.5f : 3f;
                shapes.Rect(c - Vector2.one * size, c + Vector2.one * size, color);
            }
            DrawRelations();
            DrawOpenEnds();

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
                        var box = new[] { a, new Vector2(p.x, a.y), p, new Vector2(a.x, p.y) }.Select(q => ToCanvas(q)).ToList();
                        shapes.Polyline(box, preview, 1.8f, true);
                        Label((box[0] + box[1]) / 2f - new Vector2(0f, 14f), Inch.Format(Mathf.Abs(p.x - a.x)), Theme.Dimension, true);
                        Label((box[1] + box[2]) / 2f + new Vector2(30f, 0f), Inch.Format(Mathf.Abs(p.y - a.y)), Theme.Dimension, true);
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
                            shapes.Polyline(temp.Polyline(temp.Add(EntityKind.Arc, c.id, s.id, e.id, radius: r), out _).Select(q => ToCanvas(q)).ToList(), preview, 1.8f);
                        }
                        break;
                    }
                    case Tool.Slot when clicks.Count >= 1: {
                        if (clicks.Count == 1) {
                            shapes.Dashed(ToCanvas(clicks[0]), ToCanvas(p), preview, 1f);
                            break;
                        }
                        var half = Mathf.Max(DistanceToLine(p, clicks[0], clicks[1]), 0.01f);
                        shapes.Polyline(SketchIcons.Slot(clicks[0], clicks[1], half, 16).Select(q => ToCanvas(q)).ToList(), preview, 1.8f, true);
                        Label(ToCanvas(clicks[1]) + new Vector2(0f, 18f), Inch.Format(half * 2f) + " wide", Theme.Dimension, true);
                        break;
                    }
                    case Tool.Dimension when dimensionFirstPoint >= 0:
                        shapes.Dashed(ToCanvas(sketch.P(dimensionFirstPoint)), ToCanvas(p), Theme.Dimension, 1f);
                        break;
                }
                // The snap marker.
                if (snapPoint >= 0) shapes.Ring(ToCanvas(sketch.P(snapPoint)), 7f, Theme.Warning, 1.5f, 20);
                else if (tool != Tool.Select && tool != Tool.Trim) {
                    var c = ToCanvas(p);
                    shapes.Line(c - new Vector2(6f, 0f), c + new Vector2(6f, 0f), Theme.TextDim, 1f);
                    shapes.Line(c - new Vector2(0f, 6f), c + new Vector2(0f, 6f), Theme.TextDim, 1f);
                }
            }
            for (var i = 0; i < labels.Count; i++) labels[i].transform.parent.gameObject.SetActive(i < labelsUsed);
        }

        // Small green relation marks beside what they hold (like a CAD sketch's).
        private void DrawRelations() {
            var stack = new Dictionary<int, int>();
            foreach (var r in sketch.relations) {
                Vector2 at;
                var e = sketch.Entity(r.e1);
                if (e != null) {
                    var line = sketch.Polyline(e, out _);
                    at = ToCanvas(line[line.Count / 2]);
                    if (e.kind == EntityKind.Line) at = (ToCanvas(sketch.P(e.a)) + ToCanvas(sketch.P(e.b))) / 2f;
                }
                else if (sketch.Point(r.p1) != null) at = ToCanvas(sketch.P(r.p1));
                else continue;
                var key = r.e1 != 0 ? r.e1 : -r.p1;
                stack.TryGetValue(key, out var n);
                stack[key] = n + 1;
                var spot = at + new Vector2(12f + 16f * n, 12f);
                shapes.Rect(spot - Vector2.one * 7f, spot + Vector2.one * 7f, new Color(0.06f, 0.07f, 0.08f, 0.85f));
                SketchIcons.DrawAt(shapes, r.kind.ToString(), RelationGreen, spot, 0.5f);
            }
        }

        // Ends that don't meet anything: the outline isn't closed there.
        private void DrawOpenEnds() {
            foreach (var p in sketch.OpenEnds()) {
                var c = ToCanvas(p);
                shapes.Ring(c, 7f, Theme.Danger, 2f, 20);
                shapes.Disc(c, 3f, Theme.Danger, 12);
            }
        }

        // The piece stage: the sketch faint, the areas picked as the piece extruded in 3D (its walls, then its top
        // in the plastic's color), its screw holes outlined in blue with a dot at the middle.
        private void DrawPiece(Vector2 canvasMouse, bool inside) {
            foreach (var e in sketch.entities.Where(e => !e.construction))
                shapes.Polyline(sketch.Polyline(e, out var closed).Select(p => ToCanvas(p)).ToList(), new Color(Ink.r, Ink.g, Ink.b, 0.35f), 1.2f, closed);
            if (regions == null) return;
            var chosen = ChosenFaces();
            // The area under the mouse.
            if (inside && !placingHole) {
                var hover = regions.AreaAt(ToSketch(canvasMouse));
                if (hover != null && !chosen.Contains(hover)) Fill(hover, new Color(1f, 1f, 1f, 0.12f));
            }
            var h = Mathf.Max(thickness, 0.03f);
            if (chosen.Count > 0) {
                var plastic = PlasticMaterials.Find(finish);
                var top = plastic.color;
                top.a = Mathf.Lerp(0.55f, 0.95f, top.a);
                var wall = Color.Lerp(top, Color.black, 0.35f);
                wall.a = 0.95f;
                var loops = regions.Outline(chosen);
                foreach (var loop in loops)
                    for (var i = 0; i < loop.Count; i++) {
                        var a = loop[i];
                        var b = loop[(i + 1) % loop.Count];
                        shapes.Quad(ToCanvas(a), ToCanvas(b), ToCanvas(b, h), ToCanvas(a, h), wall);
                    }
                foreach (var outer in loops.Where(l => Regions.SignedArea(l) > 0f)) {
                    var holesIn = loops.Where(l => Regions.SignedArea(l) <= 0f && Geometry.MeshSlicer.Contains(outer, l[0])).ToList();
                    var xy = new List<double>();
                    var starts = new List<int>();
                    var points = new List<Vector2>();
                    foreach (var p in outer) { xy.Add(p.x); xy.Add(p.y); points.Add(ToCanvas(p, h)); }
                    foreach (var hole in holesIn) {
                        starts.Add(points.Count);
                        foreach (var p in hole) { xy.Add(p.x); xy.Add(p.y); points.Add(ToCanvas(p, h)); }
                    }
                    shapes.Triangles(points, Geometry.Earcut.Triangulate(xy, starts), top);
                    shapes.Polyline(outer.Select(p => ToCanvas(p, h)).ToList(), Color.Lerp(top, Color.white, 0.5f), 1.4f, true);
                }
                // Screw holes, and cut-outs left out of them.
                foreach (var hole in Holes(chosen)) {
                    var c = new Vector2(hole.center.x, hole.center.z);
                    if (sketch.manualHoles.Any(m => Vector2.Distance(m.center, c) < 1e-3f)) continue;  // drawn below
                    List<Vector2> outline;
                    if (hole.outline != null && hole.outline.Length >= 3) {
                        var up = new Vector2(hole.up.x, hole.up.z);
                        var right = new Vector2(Vector3.Cross(hole.up, Vector3.up).x, Vector3.Cross(hole.up, Vector3.up).z);
                        outline = hole.outline.Select(o => c + right * o.x + up * o.y).ToList();
                    }
                    else outline = Enumerable.Range(0, 40).Select(i => c + new Vector2(Mathf.Cos(i * Mathf.PI / 20f), Mathf.Sin(i * Mathf.PI / 20f)) * hole.size / 2f).ToList();
                    var projected = outline.Select(p => ToCanvas(p, h)).ToList();
                    shapes.Polyline(projected, HoleBlue, 2.2f, true);
                    shapes.Disc(ToCanvas(c, h), 3f, HoleBlue, 12);
                }
                foreach (var off in sketch.notHoles) {
                    var face = loops.Where(l => Regions.SignedArea(l) <= 0f).FirstOrDefault(l => Geometry.MeshSlicer.Contains(l, off));
                    if (face != null) shapes.Polyline(face.Select(p => ToCanvas(p, h)).ToList(), new Color(1f, 1f, 1f, 0.3f), 1f, true);
                }
            }
            // Hand-placed holes on curves.
            // V2: the curve itself in blue (a hole on half a circle at the edge is that half), and its centre.
            foreach (var manual in sketch.manualHoles) {
                var curve = sketch.entities.FirstOrDefault(e => IsRound(e) && Vector2.Distance(sketch.P(e.a), manual.center) < 1e-3f && Mathf.Abs(e.radius - manual.radius) < 1e-3f);
                if (curve != null)
                    shapes.Polyline(sketch.Polyline(curve, out var closedCurve).Select(p => ToCanvas(p, h)).ToList(), HoleBlue, 2.6f, closedCurve);
                else
                    shapes.Ring(ToCanvas(manual.center, h), manual.radius * zoom * 0.95f, HoleBlue, 2.2f, 40);
                shapes.Disc(ToCanvas(manual.center, h), 3f, HoleBlue, 12);
            }
            if (placingHole && inside) {
                var curve = HoverEntity(canvasMouse);
                if (curve != null && IsRound(curve))
                    shapes.Polyline(sketch.Polyline(curve, out var closed).Select(p => ToCanvas(p)).ToList(), Theme.AccentHover, 2.5f, closed);
            }
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
                    var keep = editingDimension;
                    var before = keep.value;
                    keep.value = Mathf.Max(0.01f, value);
                    if (SketchSolver.Solve(sketch, Anchor(keep))) Changed();
                    else {
                        keep.value = before;
                        undo.RemoveAt(undo.Count - 1);
                        Interface.Interface.Say("That size doesn't work with the sketch's relations and other dimensions", false);
                    }
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

        private void CreatePiece() {
            var chosen = ChosenFaces();
            if (chosen.Count == 0) {
                Interface.Interface.Say(regions != null && regions.Areas.Any() ? "Click the areas that make the piece first" :
                    sketch.OpenEnds().Count > 0 ? "The outline isn't closed (red ends): go back to the sketch and join them" : "Draw a closed shape first", false);
                return;
            }
            var loops = regions.Outline(chosen);
            var part = PolyPart.Make(pieceName, sketch, loops, thickness, editing?.key, finish);
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
            Interface.Interface.Say(tooBig ? "Made " + part.name + ", but it's bigger than the 4\" × 8\" the rules allow" : "Made " + part.name + " with " + part.holes.Length + (part.holes.Length == 1 ? " hole" : " holes") + ": place it on the robot", !tooBig);
            PartsRecent.Use(part.Id);
            PartsBrowser.Instance?.PlaceCustom(part);
        }

        // ------------------------------------------------------------------ the plastic

        // A swatch color that shows the plastic on the dark panel (see-through ones a little stronger).
        private static Color Swatch(PlasticMaterials.Plastic plastic) {
            var c = plastic.color;
            return plastic.Opaque ? c : new Color(c.r, c.g, c.b, Mathf.Lerp(0.55f, 1f, c.a));
        }

        // A window of every plastic: a swatch (with its grain), its name and who sells it.
        private void OpenPlasticPicker() {
            var dialog = Dialog.Open("Plastic for this piece", new Vector2(860f, 580f));
            var scroll = Ui.Rect("Scroll", dialog.Body).Fill();
            scroll.gameObject.AddComponent<RectMask2D>();
            scroll.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var grid = Ui.Rect("Grid", scroll);
            grid.anchorMin = new Vector2(0f, 1f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.pivot = new Vector2(0.5f, 1f);
            grid.sizeDelta = Vector2.zero;
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(254f, 96f);
            layout.spacing = new Vector2(10f, 10f);
            layout.padding = new RectOffset(4, 4, 4, 4);
            grid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = scroll;
            scrollRect.content = grid;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            foreach (var plastic in PlasticMaterials.All) {
                var choice = plastic;
                var tile = Ui.Button(grid, plastic.id, null, () => {
                    finish = choice.id;
                    dialog.Close();
                    BuildSide();
                    Interface.Interface.Say("The piece is " + choice.name.ToLowerInvariant() + " (" + choice.plastic + ")");
                }, plastic.id == finish, 8f);
                // The swatch: a paper square under see-through plastic, so the tint shows like it would.
                var back = Ui.Image(tile.transform, "Back", plastic.Opaque ? Theme.Field : new Color(0.85f, 0.87f, 0.9f, 1f), 6f);
                back.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(72f, 72f));
                if (plastic.surface != PlasticMaterials.Surface.Gloss) {
                    var grain = new GameObject("Grain", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                    grain.transform.SetParent(back.transform, false);
                    var raw = grain.GetComponent<RawImage>();
                    raw.texture = PlasticMaterials.Texture(plastic.surface);
                    raw.uvRect = new Rect(0f, 0f, 1.6f, 1.6f);
                    // Dark plastics a little lighter here, so the grain shows on the small swatch.
                    raw.color = Color.Lerp(plastic.color, Color.white, plastic.color.grayscale < 0.2f ? 0.3f : 0f);
                    raw.raycastTarget = false;
                    ((RectTransform)grain.transform).Fill(3, 3, 3, 3);
                }
                else {
                    var tint = Ui.Image(back.transform, "Tint", plastic.color, 4f);
                    tint.rectTransform.Fill(3, 3, 3, 3);
                }
                var name = Ui.Text(tile.transform, "Name", plastic.name, Theme.FontBody, Theme.Text, Theme.Medium);
                name.textWrappingMode = TextWrappingModes.Normal;
                name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                name.rectTransform.anchorMax = new Vector2(1f, 1f);
                name.rectTransform.offsetMin = new Vector2(92f, 0f);
                name.rectTransform.offsetMax = new Vector2(-8f, -8f);
                name.alignment = TextAlignmentOptions.BottomLeft;
                var sold = Ui.Text(tile.transform, "Sold", plastic.plastic + " · " + plastic.sold, Theme.FontSmall, Theme.TextDim);
                sold.textWrappingMode = TextWrappingModes.Normal;
                sold.rectTransform.anchorMin = new Vector2(0f, 0f);
                sold.rectTransform.anchorMax = new Vector2(1f, 0.5f);
                sold.rectTransform.offsetMin = new Vector2(92f, 6f);
                sold.rectTransform.offsetMax = new Vector2(-8f, -2f);
                sold.alignment = TextAlignmentOptions.TopLeft;
                Ui.Tip(tile, plastic.name, null, "Sold by " + plastic.sold + ".");
            }
            var note = Ui.Text(dialog.Footer, "Note", "Any color is legal; each piece up to 0.070\" thick (V5RC R24/R25).", Theme.FontSmall, Theme.TextFaint);
            note.Size(-1, 36f, 1f);
            var close = Ui.Button(dialog.Footer, "Close", "Close", dialog.Close, false, 6f);
            close.Size(-1, 36f);
        }
    }
}
