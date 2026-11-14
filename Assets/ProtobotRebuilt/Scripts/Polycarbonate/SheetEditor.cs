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
    // V2: the polycarbonate editor, laid out like a SolidWorks sketch (the tools are in SheetEditor.Tools, the
    // PropertyManager panel in SheetEditor.Side, drawing in SheetEditor.Draw, Smart Dimension in
    // SheetEditor.Dimension, sketching on a part in the build in SheetEditor.Place):
    // - A toolbar of icons: sketch tools (line, centerline, midpoint line, corner / centre / 3-point rectangle, slot,
    //   circle, perimeter circle, centrepoint / tangent / 3-point arc, polygon, point; the variants under ▾), modify
    //   tools (trim, extend, fillet, chamfer, offset, mirror, linear and circular patterns, move / copy / rotate /
    //   scale), construction, smart dimension, relations, Convert Entities, view.
    // - Snaps and automatic relations as you draw; SketchSolver keeps every relation and dimension true; blue is
    //   under defined, white fully defined. Box selection, dragging, copy and paste, undo and redo.
    // - Make piece: the view tilts into 3D (or, on a part, stays in the build), you click the closed areas that make
    //   the piece and see it extruded with its screw holes found; then name it, pick the thickness and the plastic,
    //   and Create (on a part, it's made right there on the face).
    // The wheel zooms anywhere on the screen; right or middle drag pans.
    public partial class SheetEditor : MonoBehaviour {
        private enum Tool {
            Select, Line, Centerline, MidpointLine, Rectangle, CenterRectangle, ThreePointRectangle, Circle, PerimeterCircle,
            Arc, TangentArc, ThreePointArc, Slot, Polygon, Point, Trim, Extend, Fillet, Chamfer, Offset, Mirror,
            LinearPattern, CircularPattern, Move, Copy, Rotate, Scale, Dimension, Project,
        }
        private enum Stage { Sketch, Piece }

        private static readonly (RelationKind kind, string name, string help)[] RelationButtons = {
            (RelationKind.Horizontal, "Horizontal", "Lines (or two points) level."),
            (RelationKind.Vertical, "Vertical", "Lines (or two points) upright."),
            (RelationKind.Collinear, "Collinear", "Two lines on one straight line."),
            (RelationKind.Parallel, "Parallel", "Two lines parallel."),
            (RelationKind.Perpendicular, "Perpendicular", "Two lines square to each other."),
            (RelationKind.Equal, "Equal", "Lines the same length, or circles and arcs the same size."),
            (RelationKind.Coincident, "Coincident", "A point on another point, a line, a circle or an arc."),
            (RelationKind.Midpoint, "Midpoint", "A point at the middle of a line."),
            (RelationKind.Tangent, "Tangent", "A line and a circle or arc (or two circles) touching smoothly."),
            (RelationKind.Concentric, "Concentric", "Two circles or arcs on one centre."),
            (RelationKind.Coradial, "Coradial", "Two circles or arcs on one centre and the same size."),
            (RelationKind.Symmetric, "Symmetric", "Two points mirror each other across a line (select the points and the line)."),
            (RelationKind.Fix, "Fix", "Points or curves stay where they are."),
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
            Instance.AnalyzeDefinition();
            Instance.BuildSide();
        }

        public void Close() {
            CloseEditField();
            RestoreScene();
            Destroy(gameObject);
        }

        private void OnDestroy() {
            // Leaving Play mode tears everything down: nothing to put back then.
            if (!quitting) RestoreScene();
            if (Instance == this) Instance = null;
        }

        private static bool quitting;

        [RuntimeInitializeOnLoadMethod]
        private static void WatchQuitting() {
            quitting = false;
            Application.quitting += () => quitting = true;
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
            BuildPlaceButton(top);
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
            Ui.Row(toolbar, 1f, new RectOffset(8, 8, 4, 4));
            BuildToolbar();

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
            BuildSidePanel();

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
            Interface.Interface.Say("Draw the piece, then Make piece (or Sketch on a part to draw it on the robot)");
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
            var button = background.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());
            var hover = background.gameObject.AddComponent<HoverTint>();
            hover.target = background;
            var holder = Ui.Rect("Icon", background.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(24f, 24f));
            var graphic = ShapeGraphic.Create(holder);
            SketchIcons.Draw(graphic, icon, Theme.Text);
            var text = Ui.Text(background.transform, "Name", name, 10.5f, Theme.TextDim, Theme.Regular, TextAlignmentOptions.Center);
            text.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(72f, 16f));
            text.enableWordWrapping = false;
            // Each button as wide as its label (labels never run into each other).
            var width = Mathf.Max(52f, Mathf.Ceil(text.GetPreferredValues(name).x) + 10f);
            background.Size(width, 58f);
            text.rectTransform.sizeDelta = new Vector2(width, 16f);
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
            if (inPlace) {
                NormalTo();
                return;
            }
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

        private void SetStage(Stage newStage) {
            stage = newStage;
            placingHole = false;
            tiltTarget = stage == Stage.Piece && !inPlace ? 1f : 0f;
            ResetToolState();
            clicks.Clear();
            chainStart = chainLast = -1;
            ClearSelection();
            CloseEditField();
            toolbar.gameObject.SetActive(stage == Stage.Sketch);
            // The 3D view takes the toolbar's room.
            var topEdge = stage == Stage.Sketch ? -136f : -66f;
            canvasRect.offsetMax = new Vector2(-344f, topEdge);
            sidePanel.offsetMax = new Vector2(-14f, topEdge);
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

        // ------------------------------------------------------------------ the side panel (piece stage)

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

        private static string Describe(CustomHole h) =>
            h.outline != null && h.outline.Length >= 3 ? Inch.Format(Mathf.Min(h.size, h.size2 > 0f ? h.size2 : h.size)) + " × " + Inch.Format(Mathf.Max(h.size, h.size2)) + " (its shape)"
            : h.square ? Inch.Format(h.size) + " square" : Inch.Format(h.size) + " round";

        private static IEnumerable<(string, DimensionKind)> SizesOf(SketchEntity e) => e.projected ? new (string, DimensionKind)[0] : e.kind switch {
            EntityKind.Line => new[] { ("Length", DimensionKind.Length) },
            EntityKind.Rectangle => new[] { ("Width", DimensionKind.Width), ("Height", DimensionKind.Height) },
            EntityKind.Arc => new[] { ("Radius", DimensionKind.Radius) },
            EntityKind.Point => new (string, DimensionKind)[0],
            _ => new[] { ("Diameter", DimensionKind.Diameter) },
        };

        // ------------------------------------------------------------------ selection and relations

        private void ClearSelection() {
            selectedEntities.Clear();
            selectedPoints.Clear();
        }

        private static bool IsRound(SketchEntity e) => e.kind == EntityKind.Circle || e.kind == EntityKind.Arc;

        // Whether the selection suits a relation; the relations it makes.
        private bool RelationFits(RelationKind kind, out List<SketchRelation> made) {
            made = new List<SketchRelation>();
            var lines = selectedEntities.Where(e => e.kind == EntityKind.Line).ToList();
            var rounds = selectedEntities.Where(IsRound).ToList();
            // Point entities count as their points.
            var points = selectedPoints.Concat(selectedEntities.Where(e => e.kind == EntityKind.Point).Select(e => e.a)).Distinct().ToList();
            if (selectedEntities.Any(e => e.kind == EntityKind.Point)) {
                var keep = selectedEntities.Where(e => e.kind != EntityKind.Point).ToList();
                return RelationFitsWith(kind, lines, rounds, points, keep, out made);
            }
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
                case RelationKind.Collinear:
                    if (lines.Count == 2 && selectedEntities.Count == 2 && points.Count == 0)
                        made.Add(new SketchRelation { kind = kind, e1 = lines[0].id, e2 = lines[1].id });
                    break;
                case RelationKind.Coradial:
                    if (rounds.Count == 2 && selectedEntities.Count == 2 && points.Count == 0)
                        made.Add(new SketchRelation { kind = kind, e1 = rounds[0].id, e2 = rounds[1].id });
                    break;
                case RelationKind.Symmetric:
                    if (points.Count == 2 && lines.Count == 1 && selectedEntities.Count == 1)
                        made.Add(new SketchRelation { kind = kind, e1 = lines[0].id, p1 = points[0], p2 = points[1] });
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

        private bool RelationFitsWith(RelationKind kind, List<SketchEntity> lines, List<SketchEntity> rounds, List<int> points, List<SketchEntity> entities, out List<SketchRelation> made) {
            var keepEntities = selectedEntities.ToList();
            var keepPoints = selectedPoints.ToList();
            selectedEntities.Clear();
            selectedEntities.AddRange(entities);
            selectedPoints.Clear();
            selectedPoints.AddRange(points);
            var ok = RelationFits(kind, out made);
            selectedEntities.Clear();
            selectedEntities.AddRange(keepEntities);
            selectedPoints.Clear();
            selectedPoints.AddRange(keepPoints);
            return ok;
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

        // ------------------------------------------------------------------ view

        private const float TiltAngle = 38f;  // degrees the piece stage leans the sheet back

        // The view centre (sketch inches) the tilt leans around, and how far the eye is (inches).
        private Vector2 ViewCenter => ((new Vector2(canvasRect.rect.width, canvasRect.rect.height) / 2f) - pan) / zoom;
        private float EyeDistance => Mathf.Max(canvasRect.rect.width, canvasRect.rect.height) / zoom * 1.6f;

        /// <summary>A sketch point at a height (inches above the sheet) on the canvas: from straight above, or
        /// leaning back in perspective while the piece stage is shown.</summary>
        private Vector2 ToCanvas(Vector2 inches, float height = 0f) {
            if (inPlace) return PlaneToCanvas(inches, height);
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
            if (inPlace) return CanvasToPlane(canvas);
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
            RectTransformUtility.RectangleContainsScreenPoint(sidePanel, mouse) || (toolbar.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(toolbar, mouse))
            || Popup.IsOpen || (editField != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)editField.transform, mouse))
            || RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform.Find("Top"), mouse);

        // ------------------------------------------------------------------ input

        private bool Typing => InputEvents.TextInput.IsTyping;

        private void Update() {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || canvasRect == null) return;
            if (pickingFace) {
                UpdateFacePick();
                return;
            }
            if (sketchChanged) {
                regions = Regions.Of(sketch);
                sketchChanged = false;
            }
            tilt = Mathf.MoveTowards(tilt, tiltTarget, Time.unscaledDeltaTime * 2.5f);
            undoButton.interactable = undo.Count > 0;
            redoButton.interactable = redo.Count > 0;

            if (!Typing && keyboard != null && !Popup.IsOpen && Dialog.OpenCount == 0) Keys(keyboard);
            if (this == null || canvasRect == null || pickingFace) return;

            var inside = MouseInCanvas(out var canvasMouse);
            if (inPlace) {
                // In the build: the scene's camera buttons turn and pan, the wheel zooms.
                SceneCamera();
                UpdatePlaneZoom();
            }
            else {
                // Pan: right or middle drag; zoom: the wheel anywhere on the editor (about the mouse over the drawing).
                if (inside && (mouse.middleButton.wasPressedThisFrame || (mouse.rightButton.wasPressedThisFrame && !Drawing))) {
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
                if (Mathf.Abs(wheel) > 0.01f && !Popup.IsOpen && Dialog.OpenCount == 0 && !OverSideScroll(mouse.position.ReadValue())) {
                    var about = inside ? canvasMouse : canvasRect.rect.center;
                    var before = ToSketch(about);
                    zoom = Mathf.Clamp(zoom * (wheel > 0f ? 1.15f : 1f / 1.15f), 4f, 4000f);
                    pan += about - ToCanvas(before);
                }
            }

            // V2: the grid snaps only when switched on or while Ctrl is held (points always catch).
            var ctrlHeld = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed);
            var snap = SnapAt(ToSketch(canvasMouse), (gridSnap || ctrlHeld) && !inPlace || ctrlHeld);
            if (inside && !panning) {
                if (stage == Stage.Sketch) HandleMouse(mouse, snap, canvasMouse);
                else HandlePieceMouse(mouse, canvasMouse);
            }
            if (this == null || canvasRect == null) return;
            Draw(snap, canvasMouse, inside && !panning);
            var where = inside && stage == Stage.Sketch ? "X " + Inch.Format(snap.p.x) + "   Y " + Inch.Format(snap.p.y) + "      " : "";
            status.text = where + (inPlace
                ? "Scene buttons: turn and pan · Wheel: zoom · Normal to: face the sketch · Ctrl: grid · Ctrl+Z / Ctrl+Y: undo / redo"
                : "Wheel: zoom · Right-drag: pan · F: fit · Ctrl: snap to the grid · Ctrl+Z / Ctrl+Y: undo / redo · Ctrl+C / Ctrl+V: copy / paste");
        }

        // The wheel over the side panel scrolls it.
        private bool OverSideScroll(Vector2 mouse) => sidePanel != null && RectTransformUtility.RectangleContainsScreenPoint(sidePanel, mouse);

        private void Keys(Keyboard keyboard) {
            var ctrl = keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed;
            if (keyboard.escapeKey.wasPressedThisFrame) {
                if (editField != null) CloseEditField();
                else if (placingHole) {
                    placingHole = false;
                    BuildSide();
                }
                else if (stage == Stage.Piece) SetStage(Stage.Sketch);
                else if (Drawing || placingDimension || opStep != OpStep.Entities) {
                    CancelDrawing();
                    pendingDimension = null;
                }
                else if (selectedEntities.Count + selectedPoints.Count > 0 || selectedDimension != null) {
                    ClearSelection();
                    selectedDimension = null;
                    BuildSide();
                }
                else if (tool != Tool.Select) SetTool(Tool.Select);
                return;
            }
            // V2: keys from Edit > Keyboard shortcuts (Tools.Keybinds).
            if (global::Protobot.Tools.Keybinds.Pressed("SketchRedo") || ctrl && keyboard.shiftKey.isPressed && keyboard.zKey.wasPressedThisFrame) {
                Redo();
                return;
            }
            if (global::Protobot.Tools.Keybinds.Pressed("SketchUndo")) {
                Undo();
                return;
            }
            if (stage == Stage.Sketch) {
                if (global::Protobot.Tools.Keybinds.Pressed("SketchSelectAll")) { SelectAll(); return; }
                if (global::Protobot.Tools.Keybinds.Pressed("SketchCopy")) { CopySelection(); return; }
                if (global::Protobot.Tools.Keybinds.Pressed("SketchPaste")) { Paste(); return; }
            }
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) {
                // The tool's Apply, else Make piece.
                if (stage == Stage.Sketch && tool is Tool.LinearPattern or Tool.CircularPattern or Tool.Rotate or Tool.Scale or Tool.Move or Tool.Copy or Tool.Mirror && OperationSource().Count > 0)
                    ApplyOperation();
                else MakeOrCreate();
                return;
            }
            if (global::Protobot.Tools.Keybinds.Pressed("SketchFit")) Fit();
            if (stage != Stage.Sketch) return;
            if (!ctrl && (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)) DeleteSelected();
            if (global::Protobot.Tools.Keybinds.Pressed("SketchConstruction")) ToggleConstruction();
            foreach (var def in Tools)
                if (global::Protobot.Tools.Keybinds.Pressed(ToolKeyId(def.tool))) SetTool(def.tool);
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
            if (!mouse.leftButton.wasPressedThisFrame || regions == null || tilt < 0.5f && !inPlace) return;
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

        // Its first and opposite corners' points.
        private (int, int) AddRectangle(Vector2 corner, Vector2 opposite) {
            var a = sketch.AddPoint(corner);
            var b = sketch.AddPoint(new Vector2(opposite.x, corner.y));
            var c = sketch.AddPoint(opposite);
            var d = sketch.AddPoint(new Vector2(corner.x, opposite.y));
            sketch.Explode(sketch.Add(EntityKind.Rectangle, a.id, b.id, c.id, d.id));
            return (a.id, c.id);
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
            selectedPoints.Remove(sketch.origin);
            var dimensionsOnPoints = selectedPoints.Count;
            if (entities.Count == 0 && dimensionsOnPoints == 0) {
                if (selectedDimension != null && sketch.dimensions.Contains(selectedDimension)) {
                    DeleteDimension(selectedDimension);
                    return;
                }
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
            version++;
            selectedEntities.RemoveAll(e => !sketch.entities.Contains(e));
            selectedPoints.RemoveAll(p => sketch.Point(p) == null);
            if (selectedDimension != null && !sketch.dimensions.Contains(selectedDimension)) selectedDimension = null;
            if (mirrorAxis != null && !sketch.entities.Contains(mirrorAxis)) mirrorAxis = null;
            if (rebuildSide) {
                regions = Regions.Of(sketch);
                sketchChanged = false;
                AnalyzeDefinition();
                BuildSide();
            }
        }

        // What can still move (blue) and whether the sketch is fully defined, as SolidWorks shows it.
        private void AnalyzeDefinition() {
            if (sketch.points.Count > 600) {
                freePoints = new HashSet<int>(sketch.points.Select(p => p.id));
                freeRadii = new HashSet<int>();
                fullyDefined = false;
                return;
            }
            (freePoints, freeRadii) = SketchSolver.Analyze(sketch);
            fullyDefined = freePoints.Count == 0 && freeRadii.Count == 0 && sketch.entities.Any(e => !e.projected);
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

        // Small green relation marks beside what they hold (like a CAD sketch's).
        private void DrawRelations() {
            var stack = new Dictionary<int, int>();
            foreach (var r in sketch.relations) {
                Vector2 at;
                var e = sketch.Entity(r.e1);
                if (e != null && e.projected || r.p1 == sketch.origin && r.kind == RelationKind.Fix) continue;
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
                    shapes.Polyline(CircleOnCanvas(manual.center, manual.radius * 0.95f), HoleBlue, 2.2f, true);
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
            var onPart = inPlace;
            if (onPart) PlaceOnFace(part, loops);
            Close();
            if (onPart) {
                Interface.Interface.Say((editing != null ? "Saved " : "Made ") + part.name + " on the part, with " + part.holes.Length + (part.holes.Length == 1 ? " hole" : " holes")
                                        + (updated > 1 ? " (" + updated + " copies updated)" : ""));
                return;
            }
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
