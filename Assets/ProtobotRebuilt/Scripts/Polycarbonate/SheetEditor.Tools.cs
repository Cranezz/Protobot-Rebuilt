using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Polycarb {
    // V2: the sketch tools, as SolidWorks has them: the toolbar (one button per family, its variants under ▾),
    // snapping and inferencing (endpoints, midpoints, centres, quadrants, intersections, points on curves, lines in
    // line with points) with the relations SolidWorks adds as you draw, and what each tool does with the mouse.
    public partial class SheetEditor {
        private static readonly (Tool tool, string name, string key, string icon, string help)[] Tools = {
            (Tool.Select, "Select", "V", "Select", "Click a line, shape or point to select it; Shift or Ctrl adds more. Drag on empty space to box-select (left to right: what's fully inside; right to left: what it touches). Drag points, lines and circles to move them. Delete removes what's selected."),
            (Tool.Line, "Line", "L", "Line", "Click to start, click for each corner. Click the first point to close it; Esc or right-click to stop. Lines drawn level or upright become horizontal or vertical."),
            (Tool.Centerline, "Centerline", null, "Centerline", "A construction line (dashed): for mirroring, patterns and dimensions. It isn't cut."),
            (Tool.MidpointLine, "Midpoint line", null, "MidpointLine", "Click the middle of the line, then one end: the other end mirrors it."),
            (Tool.Rectangle, "Corner rectangle", "R", "Rectangle", "Click one corner, then the opposite one."),
            (Tool.CenterRectangle, "Center rectangle", null, "CenterRectangle", "Click the centre, then a corner (it gets construction diagonals and a centre point)."),
            (Tool.ThreePointRectangle, "3 point rectangle", null, "ThreePointRectangle", "Click two corners for one side (at any angle), then how far across."),
            (Tool.Circle, "Circle", "C", "Circle", "Click the centre (a point or a hole's centre works), then a point on the circle."),
            (Tool.PerimeterCircle, "Perimeter circle", null, "PerimeterCircle", "Click three points the circle goes through."),
            (Tool.Arc, "Centerpoint arc", "A", "Arc", "Click the centre, the start, then the end (it goes counter-clockwise)."),
            (Tool.TangentArc, "Tangent arc", null, "TangentArc", "Click the end of a line or arc, then where the arc ends: it leaves smoothly (tangent)."),
            (Tool.ThreePointArc, "3 point arc", null, "ThreePointArc", "Click the start, the end, then a point the arc passes through."),
            (Tool.Slot, "Straight slot", "S", "Slot", "Click one end's centre, the other end's centre, then how wide it is."),
            (Tool.Polygon, "Polygon", null, "Polygon", "Click the centre, then a corner. Set how many sides on the right."),
            (Tool.Point, "Point", null, "Point", "Click to put a sketch point (a reference to dimension to, or a hole's place)."),
            (Tool.Trim, "Trim", "T", "Trim", "Click the part of a curve to cut away, or drag across pieces: each is trimmed back to where other curves cross it."),
            (Tool.Extend, "Extend", null, "Extend", "Click a line near the end to lengthen: it runs on to the next curve."),
            (Tool.Fillet, "Sketch fillet", null, "Fillet", "Click corners where two lines meet: each is rounded with the radius on the right."),
            (Tool.Chamfer, "Sketch chamfer", null, "Chamfer", "Click corners where two lines meet: each is cut across, the distance on the right back along each line."),
            (Tool.Offset, "Offset entities", "O", "Offset", "Point at a line or curve (its whole chain is used, or what's selected), move the mouse to the side, and click. The distance is on the right."),
            (Tool.Mirror, "Mirror entities", "M", "Mirror", "Select what to mirror (click it), then press Mirror about and click the line (a centerline is best)."),
            (Tool.LinearPattern, "Linear pattern", null, "LinearPattern", "Select what to copy, set how many and how far apart on the right, then Apply."),
            (Tool.CircularPattern, "Circular pattern", null, "CircularPattern", "Select what to copy, set the centre (the origin unless you pick one), how many and the angle, then Apply."),
            (Tool.Move, "Move entities", null, "Move", "Select what to move, then click a point to move from and a point to move to (or type the distances and Apply)."),
            (Tool.Copy, "Copy entities", null, "Copy", "Select what to copy, then click a point to copy from and where to put it (or type the distances and Apply)."),
            (Tool.Rotate, "Rotate entities", null, "Rotate", "Select what to turn, set the centre and angle on the right, then Apply."),
            (Tool.Scale, "Scale entities", null, "Scale", "Select what to scale, set the centre and how much on the right, then Apply."),
            (Tool.Dimension, "Smart dimension", "D", "Dimension", "Click a line (length), a circle (diameter) or arc (radius); or two things for the distance or angle between them; then click where the dimension goes and type the size: it holds."),
            (Tool.Project, "Convert entities", null, "Project", "Sketching on a part: click an edge, a hole or a face of the parts around to bring it into the sketch (purple, fixed in place) to draw from and snap to. Holes come in as holes: a piece drawn over them gets them."),
        };

        private static (Tool tool, string name, string key, string icon, string help) Def(Tool t) => Tools.First(x => x.tool == t);

        // V2: every tool can have a key (Edit > Keyboard shortcuts); the table's keys are the defaults.
        private static string ToolKeyId(Tool t) => "Sketch." + t;

        [RuntimeInitializeOnLoadMethod]
        private static void RegisterKeys() {
            foreach (var def in Tools)
                global::Protobot.Tools.Keybinds.Add(ToolKeyId(def.tool), "Sketch editor tools", def.name, def.key ?? "");
        }

        private static string FamilyName(Tool t) => t switch {
            Tool.Rectangle => "Rectangle",
            Tool.Arc => "Arc",
            Tool.Slot => "Slot",
            Tool.Fillet => "Fillet",
            Tool.Offset => "Offset",
            Tool.Mirror => "Mirror",
            Tool.LinearPattern => "Pattern",
            Tool.Move => "Move",
            Tool.Dimension => "Dimension",
            Tool.Project => "Convert",
            _ => Def(t).name,
        };

        // One button per family; its variants under ▾ (as SolidWorks' flyouts).
        private static readonly (string group, Tool[] tools)[] ToolGroups = {
            ("Sketch", new[] { Tool.Select }),
            ("Sketch", new[] { Tool.Line, Tool.Centerline, Tool.MidpointLine }),
            ("Sketch", new[] { Tool.Rectangle, Tool.CenterRectangle, Tool.ThreePointRectangle }),
            ("Sketch", new[] { Tool.Slot }),
            ("Sketch", new[] { Tool.Circle, Tool.PerimeterCircle }),
            ("Sketch", new[] { Tool.Arc, Tool.TangentArc, Tool.ThreePointArc }),
            ("Sketch", new[] { Tool.Polygon }),
            ("Sketch", new[] { Tool.Point }),
            ("Modify", new[] { Tool.Trim }),
            ("Modify", new[] { Tool.Extend }),
            ("Modify", new[] { Tool.Fillet, Tool.Chamfer }),
            ("Modify", new[] { Tool.Offset }),
            ("Modify", new[] { Tool.Mirror }),
            ("Modify", new[] { Tool.LinearPattern, Tool.CircularPattern }),
            ("Modify", new[] { Tool.Move, Tool.Copy, Tool.Rotate, Tool.Scale }),
            ("Dimension", new[] { Tool.Dimension }),
            ("Convert", new[] { Tool.Project }),
        };

        private readonly List<(Tool[] tools, Image background, ShapeGraphic icon, TextMeshProUGUI name)> toolGroupButtons = new();
        private readonly Dictionary<int, Tool> groupChoice = new();
        private bool showRelations = true;

        private void BuildToolbar() {
            string last = null;
            for (var i = 0; i < ToolGroups.Length; i++) {
                var (group, tools) = ToolGroups[i];
                if (group != last) {
                    if (last == "Modify") {
                        ToolbarButton("Construction", "Construction", global::Protobot.Tools.Keybinds.Label("SketchConstruction"), "Turns what's selected into construction geometry (dashed): it helps you draw but isn't cut.", ToggleConstruction);
                        ToolbarButton("Delete", "Delete", "Del", "Deletes what's selected.", DeleteSelected);
                    }
                    if (last != null) Separator();
                    Group(group);
                    last = group;
                }
                var index = i;
                var def = Def(tools[0]);
                // Named by its family (the icon shows which variant), as SolidWorks' toolbar.
                var family = FamilyName(tools[0]);
                var (bg, icon) = ToolbarButton(def.icon, family, global::Protobot.Tools.Keybinds.Label(ToolKeyId(def.tool)), def.help, () => SetTool(groupChoice.TryGetValue(index, out var t) ? t : ToolGroups[index].tools[0]));
                var nameText = bg.transform.Find("Name").GetComponent<TextMeshProUGUI>();
                if (tools.Length > 1) {
                    var arrow = Ui.Image(bg.transform, "More", new Color(1f, 1f, 1f, 0f), 4f, true);
                    arrow.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(16f, 18f));
                    var chevron = ShapeGraphic.Create(Ui.Rect("Chevron", arrow.transform).Fill());
                    chevron.Triangle(new Vector2(-4f, 2f), new Vector2(4f, 2f), new Vector2(0f, -3f), Theme.TextDim);
                    var button = arrow.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    button.onClick.AddListener(() => Popup.Open(arrow.rectTransform, ToolGroups[index].tools.Select(t => Def(t).name).ToArray(),
                        System.Array.IndexOf(ToolGroups[index].tools, groupChoice.TryGetValue(index, out var c) ? c : ToolGroups[index].tools[0]),
                        k => SetTool(ToolGroups[index].tools[k])));
                    Ui.Tip(arrow, def.name + " and more", null, string.Join(", ", tools.Select(t => Def(t).name)));
                }
                toolGroupButtons.Add((tools, bg, icon, nameText));
            }
            Separator();
            Group("Relations");
            ToolbarButton("Relations", "Relations", null, "Select the entities: the relations that suit them are listed on the right, as in SolidWorks' Add Relations.", () => {
                SetTool(Tool.Select);
                Interface.Interface.Say("Select entities: the relations that suit them are on the right", true);
            });
            var (show, _) = ToolbarButton("ShowRelations", "Show marks", null, "Shows or hides the small relation marks beside the sketch.", () => {
                showRelations = !showRelations;
                Interface.Interface.Say(showRelations ? "Showing sketch relations" : "Hiding sketch relations", true);
            });
            Separator();
            Group("View");
            ToolbarButton("Fit", "Fit", "F", "Fits the sketch in the view.", Fit);
            ToolbarButton("NormalTo", "Normal to", null, "Sketching on a part: looks straight at the sketch again (as SolidWorks' Normal To).", NormalTo);
        }

        private void SetTool(Tool newTool) {
            if (stage != Stage.Sketch) return;
            if (newTool == Tool.Project && !inPlace) {
                Interface.Interface.Say("Convert entities brings a part's edges into the sketch: use Sketch on a part first", false);
                return;
            }
            var keepSelection = newTool is Tool.Select or Tool.Mirror or Tool.LinearPattern or Tool.CircularPattern or Tool.Move or Tool.Copy or Tool.Rotate or Tool.Scale or Tool.Offset;
            tool = newTool;
            ResetToolState();
            if (!keepSelection) ClearSelection();
            for (var i = 0; i < toolGroupButtons.Count; i++) {
                var (tools, bg, icon, name) = toolGroupButtons[i];
                var on = tools.Contains(tool);
                if (on) {
                    groupChoice[i] = tool;
                    var def = Def(tool);
                    SketchIcons.Draw(icon, def.icon, Theme.Text);
                }
                bg.color = on ? Theme.Accent : new Color(1f, 1f, 1f, 0f);
                bg.GetComponent<HoverTint>().enabled = !on;
            }
            BuildSide();
        }

        private void ResetToolState() {
            clicks.Clear();
            clickSnaps.Clear();
            chainStart = chainLast = -1;
            dimensionPicks.Clear();
            placingDimension = false;
            opStep = OpStep.Entities;
            opBase = null;
            filletFirstLine = null;
            offsetPreview = null;
        }

        // ------------------------------------------------------------------ snapping and inferencing

        private enum SnapKind { None, Grid, Point, Center, Midpoint, Quadrant, Intersection, OnCurve }

        private struct SnapInfo {
            public Vector2 p;
            public int point;
            public SnapKind kind;
            public SketchEntity entity, entity2;
            public int alignX, alignY;   // points it's in line with (inference): same x, same y
        }

        private readonly List<SnapInfo> clickSnaps = new();
        private List<(Vector2 p, SketchEntity a, SketchEntity b)> intersections;
        private int intersectionsVersion = -1;
        private int version;

        private List<(Vector2 p, SketchEntity a, SketchEntity b)> Intersections() {
            if (intersections != null && intersectionsVersion == version) return intersections;
            intersectionsVersion = version;
            intersections = new List<(Vector2, SketchEntity, SketchEntity)>();
            var lines = sketch.entities.Where(e => e.kind != EntityKind.Point).Select(e => (e, poly: sketch.Polyline(e, out var closed), closed)).ToList();
            if (lines.Count > 120) return intersections;
            for (var i = 0; i < lines.Count; i++)
                for (var j = i + 1; j < lines.Count; j++) {
                    var (ea, pa, ca) = lines[i];
                    var (eb, pb, cb) = lines[j];
                    for (var x = 0; x < pa.Count - (ca ? 0 : 1); x++)
                        for (var y = 0; y < pb.Count - (cb ? 0 : 1); y++)
                            if (SegmentCross(pa[x], pa[(x + 1) % pa.Count], pb[y], pb[(y + 1) % pb.Count], out var q)
                                && !sketch.points.Any(sp => Vector2.Distance(sp.p, q) < 1e-3f))
                                intersections.Add((q, ea, eb));
                }
            return intersections;
        }

        private static bool SegmentCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point) {
            point = default;
            var r = b - a;
            var s = d - c;
            var den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-10f) return false;
            var t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / den;
            var u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / den;
            if (t < 1e-4f || t > 1 - 1e-4f || u < 1e-4f || u > 1 - 1e-4f) return false;
            point = a + r * t;
            return true;
        }

        private readonly HashSet<int> snapSkip = new();   // points being moved or made: not snapped to

        /// <summary>Where the mouse snaps, like SolidWorks' sketch snaps: a point, a line's midpoint, a circle's
        /// centre or quadrant, where curves cross, a point on a curve; else in line with points (inference), or the
        /// grid when it's on.</summary>
        private SnapInfo SnapAt(Vector2 raw, bool grid) {
            var s = new SnapInfo { p = raw, point = -1, alignX = -1, alignY = -1 };
            var mouse = ToCanvas(raw);
            const float PointPixels = 10f, CurvePixels = 7f, InferPixels = 6f;
            var best = PointPixels;
            foreach (var sp in sketch.points) {
                if (sp.id == draggingPoint || snapSkip.Contains(sp.id)) continue;
                var d = Vector2.Distance(ToCanvas(sp.p), mouse);
                if (d < best) {
                    best = d;
                    s.point = sp.id;
                    s.p = sp.p;
                    s.kind = IsCircleCenterOnly(sp.id) ? SnapKind.Center : SnapKind.Point;
                }
            }
            if (s.point >= 0) return s;
            best = PointPixels;
            foreach (var e in sketch.entities) {
                if (e.kind == EntityKind.Line) {
                    var m = (sketch.P(e.a) + sketch.P(e.b)) / 2f;
                    var d = Vector2.Distance(ToCanvas(m), mouse);
                    if (d < best) { best = d; s.p = m; s.kind = SnapKind.Midpoint; s.entity = e; }
                }
                else if (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc) {
                    var c = sketch.P(e.a);
                    foreach (var dir in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down }) {
                        var q = c + dir * e.radius;
                        if (e.kind == EntityKind.Arc && !OnArc(e, q)) continue;
                        var d = Vector2.Distance(ToCanvas(q), mouse);
                        if (d < best) { best = d; s.p = q; s.kind = SnapKind.Quadrant; s.entity = e; }
                    }
                }
            }
            foreach (var (q, a, b) in Intersections()) {
                var d = Vector2.Distance(ToCanvas(q), mouse);
                if (d < best) { best = d; s.p = q; s.kind = SnapKind.Intersection; s.entity = a; s.entity2 = b; }
            }
            if (s.kind != SnapKind.None) return s;
            best = CurvePixels;
            foreach (var e in sketch.entities) {
                if (e.kind == EntityKind.Point) continue;
                var line = sketch.Polyline(e, out var closed);
                for (var i = 0; i < line.Count - (closed ? 0 : 1); i++) {
                    var a = line[i];
                    var b = line[(i + 1) % line.Count];
                    var ab = b - a;
                    var t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(raw - a, ab) / ab.sqrMagnitude) : 0f;
                    var q = a + ab * t;
                    var d = Vector2.Distance(ToCanvas(q), mouse);
                    if (d < best) {
                        best = d;
                        s.p = q;
                        s.kind = SnapKind.OnCurve;
                        s.entity = e;
                    }
                }
            }
            if (s.kind == SnapKind.OnCurve) {
                // Exactly on a circle (its polyline is a little inside).
                if (s.entity.kind == EntityKind.Circle || s.entity.kind == EntityKind.Arc) {
                    var c = sketch.P(s.entity.a);
                    s.p = c + (raw - c).normalized * s.entity.radius;
                }
                else if (s.entity.kind == EntityKind.Line) {
                    var a = sketch.P(s.entity.a);
                    var b = sketch.P(s.entity.b);
                    var ab = b - a;
                    s.p = a + ab * Mathf.Clamp01(Vector2.Dot(raw - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                }
                return s;
            }
            if (grid) {
                const float step = 1f / 16f;
                s.p = new Vector2(Mathf.Round(raw.x / step) * step, Mathf.Round(raw.y / step) * step);
                s.kind = SnapKind.Grid;
            }
            // In line with a point (dotted inference lines, as SolidWorks draws).
            var bestX = InferPixels;
            var bestY = InferPixels;
            foreach (var sp in sketch.points) {
                if (sp.id == draggingPoint || snapSkip.Contains(sp.id)) continue;
                var dx = Mathf.Abs(sp.p.x - raw.x) * zoom;
                var dy = Mathf.Abs(sp.p.y - raw.y) * zoom;
                if (dx < bestX) { bestX = dx; s.alignX = sp.id; }
                if (dy < bestY) { bestY = dy; s.alignY = sp.id; }
            }
            if (s.alignX >= 0) s.p.x = sketch.P(s.alignX).x;
            if (s.alignY >= 0) s.p.y = sketch.P(s.alignY).y;
            return s;
        }

        private bool OnArc(SketchEntity arc, Vector2 q) {
            var c = sketch.P(arc.a);
            float Angle(Vector2 v) => Mathf.Atan2(v.y - c.y, v.x - c.x);
            float Ccw(float from, float to) {
                var x = to - from;
                while (x < 0f) x += Mathf.PI * 2f;
                while (x >= Mathf.PI * 2f) x -= Mathf.PI * 2f;
                return x;
            }
            var start = Angle(sketch.P(arc.b));
            return Ccw(start, Angle(q)) <= Ccw(start, Angle(sketch.P(arc.c))) + 1e-4f;
        }

        /// <summary>The point a click makes: the point snapped to, or a new one with the relation its snap implies
        /// (SolidWorks' automatic relations: on a curve, at a midpoint, where two curves cross).</summary>
        private int PointFor(SnapInfo s) {
            if (s.point >= 0) return s.point;
            var id = sketch.AddPoint(s.p).id;
            switch (s.kind) {
                case SnapKind.Midpoint:
                    sketch.Relate(RelationKind.Midpoint, s.entity.id, p1: id);
                    break;
                case SnapKind.OnCurve:
                case SnapKind.Quadrant:
                    sketch.Relate(RelationKind.Coincident, s.entity.id, p1: id);
                    break;
                case SnapKind.Intersection:
                    sketch.Relate(RelationKind.Coincident, s.entity.id, p1: id);
                    sketch.Relate(RelationKind.Coincident, s.entity2.id, p1: id);
                    break;
            }
            return id;
        }

        private static bool Exact(SnapInfo s) => s.kind is SnapKind.Point or SnapKind.Center or SnapKind.Midpoint or SnapKind.Quadrant or SnapKind.Intersection or SnapKind.OnCurve;

        // Lines nearly level or upright snap straight (level: 1 horizontal, 2 vertical, 0 neither).
        private (Vector2 p, int level) Straighten(Vector2 start, SnapInfo s) {
            if (Exact(s)) return (s.p, 0);
            var p = s.p;
            var d = p - start;
            if (Mathf.Abs(d.y) * zoom < 7f && Mathf.Abs(d.x) > 1e-4f) return (new Vector2(p.x, start.y), 1);
            if (Mathf.Abs(d.x) * zoom < 7f && Mathf.Abs(d.y) > 1e-4f) return (new Vector2(start.x, p.y), 2);
            return (p, 0);
        }

        // ------------------------------------------------------------------ the mouse, by tool

        private enum OpStep { Entities, Axis, Base, Target, Center }
        private OpStep opStep = OpStep.Entities;
        private Vector2? opBase;
        private SketchEntity filletFirstLine;
        private List<SketchEntity> offsetPreview;
        private SketchData previewSketch;

        // Options (the PropertyManager's fields).
        private int polygonSides = 6;
        private float filletRadius = 0.25f, chamferDistance = 0.125f, offsetDistance = 0.125f;
        private bool offsetChain = true;
        private int pattern1 = 3, pattern2 = 1, circularCount = 6;
        private float spacing1 = 1f, spacing2 = 1f, patternAngle, circularAngle = 360f, rotateAngle = 90f, scaleFactor = 2f, moveX = 1f, moveY;
        private bool transformCopy;
        private Vector2 opCenter;
        private bool opCenterSet;
        private SketchEntity mirrorAxis;

        private void CancelDrawing() {
            clicks.Clear();
            clickSnaps.Clear();
            chainStart = chainLast = -1;
            dimensionPicks.Clear();
            placingDimension = false;
            opBase = null;
            filletFirstLine = null;
            if (opStep != OpStep.Entities) {
                opStep = OpStep.Entities;
                BuildSide();
            }
        }

        private bool Drawing => clicks.Count > 0 || chainLast >= 0 || dimensionPicks.Count > 0 || opBase.HasValue || filletFirstLine != null;

        private void HandleMouse(Mouse mouse, SnapInfo snap, Vector2 canvasMouse) {
            var pressed = mouse.leftButton.wasPressedThisFrame;
            var p = snap.p;
            if (mouse.rightButton.wasPressedThisFrame && Drawing) {
                CancelDrawing();
                return;
            }
            switch (tool) {
                case Tool.Select:
                    HandleSelect(mouse, canvasMouse);
                    break;
                case Tool.Line:
                case Tool.Centerline: {
                    if (!pressed) break;
                    if (chainLast < 0) {
                        Remember();
                        chainStart = chainLast = PointFor(snap);
                        break;
                    }
                    var start = sketch.P(chainLast);
                    var (end, level) = Straighten(start, snap);
                    if (Vector2.Distance(end, start) < 1e-4f) break;
                    var endSnap = snap;
                    endSnap.p = end;
                    if (level != 0 && !Exact(snap)) endSnap.kind = SnapKind.None;
                    var endPoint = PointFor(endSnap);
                    if (endPoint == chainLast) break;
                    var line = sketch.Add(EntityKind.Line, chainLast, endPoint);
                    line.construction = tool == Tool.Centerline;
                    if (level == 1) sketch.Relate(RelationKind.Horizontal, line.id);
                    else if (level == 2) sketch.Relate(RelationKind.Vertical, line.id);
                    Changed();
                    if (endPoint == chainStart || snap.point >= 0 || tool == Tool.Centerline) {
                        if (endPoint == chainStart) Interface.Interface.Say("Closed the shape");
                        chainStart = chainLast = -1;
                    }
                    else chainLast = endPoint;
                    break;
                }
                case Tool.MidpointLine: {
                    if (!pressed) break;
                    if (clicks.Count == 0) {
                        clicks.Add(p);
                        clickSnaps.Add(snap);
                        break;
                    }
                    var mid = clicks[0];
                    if (Vector2.Distance(mid, p) < 1e-3f) break;
                    Remember();
                    var line = sketch.Add(EntityKind.Line, sketch.AddPoint(mid * 2f - p).id, PointFor(snap));
                    // Its middle stays where it was clicked (on what it snapped to).
                    var middle = sketch.Add(EntityKind.Point, PointFor(clickSnaps[0]));
                    sketch.Relate(RelationKind.Midpoint, line.id, p1: middle.a);
                    clicks.Clear();
                    clickSnaps.Clear();
                    Changed();
                    break;
                }
                case Tool.Rectangle:
                case Tool.CenterRectangle: {
                    if (!pressed) break;
                    if (clicks.Count == 0) {
                        clicks.Add(p);
                        clickSnaps.Add(snap);
                        break;
                    }
                    if (Mathf.Abs(p.x - clicks[0].x) < 1e-3f || Mathf.Abs(p.y - clicks[0].y) < 1e-3f) {
                        Interface.Interface.Say("A rectangle needs a width and a height", false);
                        break;
                    }
                    Remember();
                    if (tool == Tool.Rectangle) {
                        var (a, c) = AddRectangle(clicks[0], p);
                        if (clickSnaps[0].point >= 0) sketch.Merge(a, clickSnaps[0].point);
                        if (snap.point >= 0) sketch.Merge(c, snap.point);
                    }
                    else {
                        var made = sketch.CenterRectangle(clicks[0], p);
                        if (clickSnaps[0].point >= 0) sketch.Merge(made[^1].a, clickSnaps[0].point);
                    }
                    clicks.Clear();
                    clickSnaps.Clear();
                    SolveQuietly();
                    Changed();
                    break;
                }
                case Tool.ThreePointRectangle: {
                    if (!pressed) break;
                    clicks.Add(p);
                    clickSnaps.Add(snap);
                    if (clicks.Count < 3) break;
                    var across = Mathf.Abs(SketchData.DistanceToLine(clicks[2], clicks[0], clicks[1]));
                    if (across < 1e-3f || Vector2.Distance(clicks[0], clicks[1]) < 1e-3f) {
                        Interface.Interface.Say("A rectangle needs a width and a height", false);
                        clicks.Clear();
                        clickSnaps.Clear();
                        break;
                    }
                    Remember();
                    var lines = sketch.ThreePointRectangle(clicks[0], clicks[1], clicks[2]);
                    if (clickSnaps[0].point >= 0) sketch.Merge(lines[0].a, clickSnaps[0].point);
                    if (clickSnaps[1].point >= 0) sketch.Merge(lines[0].b, clickSnaps[1].point);
                    clicks.Clear();
                    clickSnaps.Clear();
                    SolveQuietly();
                    Changed();
                    break;
                }
                case Tool.Circle: {
                    if (!pressed) break;
                    if (clicks.Count == 0) {
                        clicks.Add(p);
                        clickSnaps.Add(snap);
                        break;
                    }
                    var radius = Vector2.Distance(p, clicks[0]);
                    if (radius < 0.005f) break;
                    Remember();
                    var circle = sketch.Add(EntityKind.Circle, PointFor(clickSnaps[0]), radius: radius);
                    if (snap.point >= 0) sketch.Relate(RelationKind.Coincident, circle.id, p1: snap.point);
                    clicks.Clear();
                    clickSnaps.Clear();
                    Changed();
                    break;
                }
                case Tool.PerimeterCircle: {
                    if (!pressed) break;
                    clicks.Add(p);
                    clickSnaps.Add(snap);
                    if (clicks.Count < 3) break;
                    Remember();
                    var circle = sketch.PerimeterCircle(clicks[0], clicks[1], clicks[2]);
                    if (circle == null) {
                        undo.RemoveAt(undo.Count - 1);
                        Interface.Interface.Say("Those three points are in a line: no circle goes through them", false);
                    }
                    else {
                        foreach (var cs in clickSnaps.Where(c => c.point >= 0)) sketch.Relate(RelationKind.Coincident, circle.id, p1: cs.point);
                        Changed();
                    }
                    clicks.Clear();
                    clickSnaps.Clear();
                    break;
                }
                case Tool.Arc: {
                    if (!pressed) break;
                    clicks.Add(p);
                    clickSnaps.Add(snap);
                    if (clicks.Count < 3) break;
                    var arcRadius = Vector2.Distance(clicks[1], clicks[0]);
                    if (arcRadius < 0.005f) {
                        clicks.Clear();
                        clickSnaps.Clear();
                        break;
                    }
                    Remember();
                    var center = PointFor(clickSnaps[0]);
                    var start = clickSnaps[1].point >= 0 ? clickSnaps[1].point : sketch.AddPoint(clicks[1]).id;
                    var endOnCircle = clicks[0] + (clicks[2] - clicks[0]).normalized * arcRadius;
                    var end = snap.point >= 0 && Mathf.Abs(Vector2.Distance(snap.p, clicks[0]) - arcRadius) < 2f / zoom ? snap.point : sketch.AddPoint(endOnCircle).id;
                    sketch.Add(EntityKind.Arc, center, start, end, radius: arcRadius);
                    clicks.Clear();
                    clickSnaps.Clear();
                    SolveQuietly();
                    Changed();
                    break;
                }
                case Tool.TangentArc: {
                    if (!pressed) break;
                    if (clicks.Count == 0) {
                        if (snap.point < 0 || !TangentStart(snap.point, out _, out _)) {
                            Interface.Interface.Say("Start a tangent arc on the end of a line or an arc", false);
                            break;
                        }
                        clicks.Add(snap.p);
                        clickSnaps.Add(snap);
                        break;
                    }
                    TangentStart(clickSnaps[0].point, out var dir, out var from);
                    Remember();
                    var arc = sketch.TangentArc(clickSnaps[0].point, dir, p, from);
                    if (arc == null) {
                        undo.RemoveAt(undo.Count - 1);
                        Interface.Interface.Say("That's straight on along the line: draw a line there instead", false);
                        break;
                    }
                    var newEnd = arc.b == clickSnaps[0].point ? arc.c : arc.b;
                    if (snap.point >= 0) sketch.Merge(newEnd, snap.point);
                    clicks.Clear();
                    clickSnaps.Clear();
                    SolveQuietly();
                    Changed();
                    break;
                }
                case Tool.ThreePointArc: {
                    if (!pressed) break;
                    clicks.Add(p);
                    clickSnaps.Add(snap);
                    if (clicks.Count < 3) break;
                    Remember();
                    var startId = clickSnaps[0].point >= 0 ? clickSnaps[0].point : PointFor(clickSnaps[0]);
                    var endId = clickSnaps[1].point >= 0 ? clickSnaps[1].point : PointFor(clickSnaps[1]);
                    var arc = sketch.ThreePointArc(clicks[0], clicks[1], clicks[2], startId, endId);
                    if (arc == null) {
                        undo.RemoveAt(undo.Count - 1);
                        Interface.Interface.Say("Those points are in a line: no arc goes through them", false);
                    }
                    else Changed();
                    clicks.Clear();
                    clickSnaps.Clear();
                    break;
                }
                case Tool.Slot: {
                    if (!pressed) break;
                    clicks.Add(p);
                    if (clicks.Count < 3) break;
                    var half = SketchData.DistanceToLine(clicks[2], clicks[0], clicks[1]);
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
                }
                case Tool.Polygon: {
                    if (!pressed) break;
                    if (clicks.Count == 0) {
                        clicks.Add(p);
                        clickSnaps.Add(snap);
                        break;
                    }
                    if (Vector2.Distance(p, clicks[0]) < 0.01f) break;
                    Remember();
                    var made = sketch.Polygon(clicks[0], p, polygonSides);
                    if (clickSnaps[0].point >= 0) sketch.Merge(made[0].a, clickSnaps[0].point);
                    clicks.Clear();
                    clickSnaps.Clear();
                    SolveQuietly();
                    Changed();
                    Interface.Interface.Say("Added a " + polygonSides + "-sided polygon");
                    break;
                }
                case Tool.Point: {
                    if (!pressed) break;
                    if (snap.point >= 0) {
                        Interface.Interface.Say("There's a point there already", false);
                        break;
                    }
                    Remember();
                    sketch.Add(EntityKind.Point, PointFor(snap));
                    Changed();
                    break;
                }
                case Tool.Trim:
                    HandleTrim(mouse, canvasMouse);
                    break;
                case Tool.Extend: {
                    if (!pressed) break;
                    var line = HoverEntity(canvasMouse);
                    if (line == null || line.projected) {
                        Interface.Interface.Say(line == null ? "Click a line near the end to lengthen" : "Converted edges stay as they are", false);
                        break;
                    }
                    Remember();
                    if (sketch.Extend(line, ToSketch(canvasMouse), out var why)) {
                        SolveQuietly();
                        Changed();
                        Interface.Interface.Say("Extended it to the next curve");
                    }
                    else {
                        undo.RemoveAt(undo.Count - 1);
                        Interface.Interface.Say(why, false);
                    }
                    break;
                }
                case Tool.Fillet:
                case Tool.Chamfer: {
                    if (!pressed) break;
                    var corner = HoverPoint(canvasMouse);
                    if (corner < 0) {
                        // Or two lines that meet.
                        var line = HoverEntity(canvasMouse);
                        if (line == null || line.kind != EntityKind.Line) {
                            Interface.Interface.Say("Click a corner, or two lines that meet", false);
                            break;
                        }
                        if (filletFirstLine == null || filletFirstLine == line) {
                            filletFirstLine = line;
                            Interface.Interface.Say("Now the other line", true);
                            break;
                        }
                        corner = new[] { line.a, line.b }.FirstOrDefault(id => id == filletFirstLine.a || id == filletFirstLine.b);
                        filletFirstLine = null;
                        if (corner == 0) {
                            Interface.Interface.Say("Those lines don't meet: their ends have to be one point", false);
                            break;
                        }
                    }
                    Remember();
                    string reason;
                    var ok = tool == Tool.Fillet ? sketch.Fillet(corner, filletRadius, out reason) != null : sketch.Chamfer(corner, chamferDistance, chamferDistance, out reason) != null;
                    if (!ok) {
                        undo.RemoveAt(undo.Count - 1);
                        Interface.Interface.Say(reason, false);
                        break;
                    }
                    SolveQuietly();
                    Changed();
                    Interface.Interface.Say(tool == Tool.Fillet ? "Rounded the corner (R " + Inch.Format(filletRadius) + ")" : "Cut the corner (" + Inch.Format(chamferDistance) + ")");
                    break;
                }
                case Tool.Offset:
                    HandleOffset(pressed, canvasMouse);
                    break;
                case Tool.Mirror:
                case Tool.LinearPattern:
                case Tool.CircularPattern:
                case Tool.Move:
                case Tool.Copy:
                case Tool.Rotate:
                case Tool.Scale:
                    HandleSelectionTool(pressed, snap, canvasMouse);
                    break;
                case Tool.Dimension:
                    HandleDimension(pressed, canvasMouse);
                    break;
                case Tool.Project:
                    HandleProject(pressed);
                    break;
            }
        }

        // The direction a tangent arc leaves a point: along the line (or arc) that ends there, outwards.
        private bool TangentStart(int point, out Vector2 direction, out SketchEntity from) {
            direction = default;
            from = sketch.entities.FirstOrDefault(e => (e.kind == EntityKind.Line && (e.a == point || e.b == point)) || (e.kind == EntityKind.Arc && (e.b == point || e.c == point)));
            if (from == null) return false;
            var p = sketch.P(point);
            if (from.kind == EntityKind.Line) direction = (p - sketch.P(from.a == point ? from.b : from.a)).normalized;
            else {
                var radial = (p - sketch.P(from.a)).normalized;
                var ccw = new Vector2(-radial.y, radial.x);
                direction = from.c == point ? ccw : -ccw;
            }
            return true;
        }

        // Settles relations added with new geometry (they hold already; this tidies rounding).
        private void SolveQuietly() => SketchSolver.Solve(sketch);

        // ------------------------------------------------------------------ select: pick, box, drag

        private bool boxing;
        private Vector2 boxStart;
        private SketchEntity draggingEntity;
        private Vector2 entityDragFrom;
        private bool entityDragRadius;
        private Dictionary<int, Vector2> entityDragStart;
        private float entityDragStartRadius;
        private bool entityMoved;

        private void HandleSelect(Mouse mouse, Vector2 canvasMouse) {
            var keyboard = Keyboard.current;
            var add = keyboard != null && (keyboard.shiftKey.isPressed || keyboard.ctrlKey.isPressed);
            if (mouse.leftButton.wasPressedThisFrame) {
                var point = HoverPoint(canvasMouse);
                if (point >= 0) {
                    if (!add && !selectedPoints.Contains(point)) ClearSelection();
                    if (add && selectedPoints.Contains(point)) selectedPoints.Remove(point);
                    else if (!selectedPoints.Contains(point)) selectedPoints.Add(point);
                    selectedDimension = null;
                    draggingPoint = point;
                    dragStart = sketch.Copy();
                    BuildSide();
                }
                else {
                    var entity = HoverEntity(canvasMouse);
                    if (entity != null) {
                        if (!add && !selectedEntities.Contains(entity)) ClearSelection();
                        if (add && selectedEntities.Contains(entity)) selectedEntities.Remove(entity);
                        else if (!selectedEntities.Contains(entity)) selectedEntities.Add(entity);
                        selectedDimension = null;
                        if (!entity.projected) {
                            draggingEntity = entity;
                            entityDragFrom = ToSketch(canvasMouse);
                            entityDragStart = sketch.PointsOf(entity).Distinct().ToDictionary(id => id, id => sketch.P(id));
                            entityDragRadius = entity.kind == EntityKind.Circle || entity.kind == EntityKind.Arc;
                            entityDragStartRadius = entity.radius;
                            entityMoved = false;
                            dragStart = sketch.Copy();
                        }
                    }
                    else {
                        if (!add) ClearSelection();
                        selectedDimension = null;
                        boxing = true;
                        boxStart = canvasMouse;
                    }
                    BuildSide();
                }
            }
            if (draggingPoint >= 0) {
                if (mouse.leftButton.isPressed) {
                    var held = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed);
                    var target = SnapAt(ToSketch(canvasMouse), gridSnap || held).p;
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
            if (draggingEntity != null) {
                if (mouse.leftButton.isPressed) {
                    var now = ToSketch(canvasMouse);
                    if (!entityMoved && Vector2.Distance(ToCanvas(now), ToCanvas(entityDragFrom)) < 3f) return;
                    entityMoved = true;
                    MoveEntity(draggingEntity, now);
                    Changed(false);
                }
                else {
                    if (entityMoved && dragStart != null) {
                        undo.Add(dragStart);
                        redo.Clear();
                    }
                    draggingEntity = null;
                    dragStart = null;
                    if (entityMoved) Changed();
                }
            }
            if (boxing) {
                if (mouse.leftButton.isPressed) {
                    boxEnd = canvasMouse;
                    return;
                }
                boxing = false;
                if (Vector2.Distance(boxStart, canvasMouse) < 4f) return;
                // Left to right: what's fully inside the box; right to left: what it crosses, as SolidWorks.
                var crossing = canvasMouse.x < boxStart.x;
                var rect = Rect.MinMaxRect(Mathf.Min(boxStart.x, canvasMouse.x), Mathf.Min(boxStart.y, canvasMouse.y), Mathf.Max(boxStart.x, canvasMouse.x), Mathf.Max(boxStart.y, canvasMouse.y));
                foreach (var e in sketch.entities) {
                    var line = sketch.Polyline(e, out _).Select(q => ToCanvas(q)).ToList();
                    var inside = crossing ? line.Any(rect.Contains) : line.All(rect.Contains);
                    if (inside && !selectedEntities.Contains(e)) selectedEntities.Add(e);
                }
                BuildSide();
                Interface.Interface.Say("Selected " + selectedEntities.Count + (selectedEntities.Count == 1 ? " item" : " items"), true);
            }
        }

        private Vector2 boxEnd;

        // Dragging a line moves it; dragging a circle's edge (or an arc's) changes its size.
        private void MoveEntity(SketchEntity e, Vector2 now) {
            var before = sketch.points.ToDictionary(p => p.id, p => p.p);
            var radii = sketch.entities.ToDictionary(x => x.id, x => x.radius);
            if (entityDragRadius) {
                e.radius = Mathf.Max(0.01f, Vector2.Distance(now, sketch.P(e.a)));
                if (e.kind == EntityKind.Arc) {
                    var c = sketch.P(e.a);
                    sketch.Point(e.b).p = c + (sketch.P(e.b) - c).normalized * e.radius;
                    sketch.Point(e.c).p = c + (sketch.P(e.c) - c).normalized * e.radius;
                }
                if (SketchSolver.Solve(sketch, new[] { e.a })) return;
            }
            else {
                var delta = now - entityDragFrom;
                foreach (var (id, start) in entityDragStart) sketch.Point(id).p = start + delta;
                if (SketchSolver.Solve(sketch, entityDragStart.Keys)) return;
            }
            foreach (var p in sketch.points) p.p = before[p.id];
            foreach (var x in sketch.entities) x.radius = radii[x.id];
        }

        // ------------------------------------------------------------------ trim

        private void HandleTrim(Mouse mouse, Vector2 canvasMouse) {
            // As SolidWorks' power trim: click a curve, or hold the button and drag across the pieces to cut away.
            var at = ToSketch(canvasMouse);
            if (mouse.leftButton.wasPressedThisFrame) {
                Remember();
                trimming = true;
                trimmedCount = 0;
                trimLast = at;
                var entity = HoverEntity(canvasMouse);
                if (entity != null && !entity.projected) {
                    sketch.Trim(entity, at);
                    trimmedCount++;
                    Changed();
                }
                return;
            }
            if (!trimming) return;
            if (mouse.leftButton.isPressed) {
                if (Vector2.Distance(at, trimLast) * zoom < 2f) return;
                foreach (var (entity, cross) in CrossedBy(trimLast, at)) {
                    if (!sketch.entities.Contains(entity) || entity.projected) continue;
                    sketch.Trim(entity, cross);
                    trimmedCount++;
                }
                trimLast = at;
                Changed();
                return;
            }
            trimming = false;
            if (trimmedCount == 0) {
                undo.RemoveAt(undo.Count - 1);
                Interface.Interface.Say("Click a curve, or drag across the pieces to cut away", false);
            }
            else Interface.Interface.Say("Trimmed " + trimmedCount + (trimmedCount == 1 ? " piece" : " pieces"));
        }

        // ------------------------------------------------------------------ offset

        private SketchEntity offsetHover;
        private Vector2 offsetMouse;

        // The curves an offset uses: what's selected, else the chain the curve under the mouse is part of.
        private List<SketchEntity> OffsetSource(SketchEntity hovered) {
            if (selectedEntities.Count > 0) return selectedEntities.Where(e => !e.projected).ToList();
            if (hovered == null) return new List<SketchEntity>();
            if (!offsetChain || hovered.kind == EntityKind.Circle) return new List<SketchEntity> { hovered };
            var chain = new List<SketchEntity> { hovered };
            var frontier = new Queue<SketchEntity>();
            frontier.Enqueue(hovered);
            int[] Ends(SketchEntity e) => e.kind == EntityKind.Line ? new[] { e.a, e.b } : e.kind == EntityKind.Arc ? new[] { e.b, e.c } : new int[0];
            while (frontier.Count > 0) {
                var e = frontier.Dequeue();
                foreach (var end in Ends(e))
                    foreach (var o in sketch.entities)
                        if (!chain.Contains(o) && !o.projected && o.construction == hovered.construction && Ends(o).Contains(end)) {
                            chain.Add(o);
                            frontier.Enqueue(o);
                        }
            }
            return chain;
        }

        private void HandleOffset(bool pressed, Vector2 canvasMouse) {
            var hovered = HoverEntity(canvasMouse) ?? offsetHover;
            if (HoverEntity(canvasMouse) != null) offsetHover = hovered;
            var at = ToSketch(canvasMouse);
            // The preview: the offset made on a copy of the sketch.
            if (Time.frameCount % 3 == 0 || offsetMouse != at) {
                offsetMouse = at;
                var source = OffsetSource(hovered);
                offsetPreview = null;
                if (source.Count > 0) {
                    previewSketch = sketch.Copy();
                    var copies = previewSketch.Offset(source.Select(e => previewSketch.Entity(e.id)).Where(e => e != null).ToList(), offsetDistance, at);
                    offsetPreview = copies;
                }
            }
            if (!pressed) return;
            var picked = OffsetSource(hovered);
            if (picked.Count == 0) {
                Interface.Interface.Say("Point at a line or curve (or select some), move to the side, and click", false);
                return;
            }
            Remember();
            var made = sketch.Offset(picked, offsetDistance, at);
            if (made.Count == 0) {
                undo.RemoveAt(undo.Count - 1);
                Interface.Interface.Say("Couldn't offset that", false);
                return;
            }
            SolveQuietly();
            ClearSelection();
            offsetHover = null;
            Changed();
            Interface.Interface.Say("Offset " + made.Count + (made.Count == 1 ? " curve " : " curves ") + Inch.Format(offsetDistance));
        }

        // ------------------------------------------------------------------ mirror, patterns, move / copy / rotate / scale

        private void HandleSelectionTool(bool pressed, SnapInfo snap, Vector2 canvasMouse) {
            if (!pressed) return;
            switch (opStep) {
                case OpStep.Axis: {
                    var line = HoverEntity(canvasMouse);
                    if (line == null || line.kind != EntityKind.Line) {
                        Interface.Interface.Say("Click the line to mirror about", false);
                        return;
                    }
                    mirrorAxis = line;
                    ApplyOperation();
                    return;
                }
                case OpStep.Center:
                    opCenter = snap.p;
                    opCenterSet = true;
                    opStep = OpStep.Entities;
                    BuildSide();
                    Interface.Interface.Say("Centre set: Apply when ready", true);
                    return;
                case OpStep.Base:
                    opBase = snap.p;
                    opStep = OpStep.Target;
                    Interface.Interface.Say("Now click where it goes", true);
                    return;
                case OpStep.Target:
                    if (!opBase.HasValue) return;
                    moveX = snap.p.x - opBase.Value.x;
                    moveY = snap.p.y - opBase.Value.y;
                    opBase = null;
                    opStep = OpStep.Entities;
                    ApplyOperation();
                    return;
            }
            // Picking what it acts on.
            var entity = HoverEntity(canvasMouse);
            if (entity == null) {
                if ((tool == Tool.Move || tool == Tool.Copy) && selectedEntities.Count > 0) {
                    opBase = snap.p;
                    opStep = OpStep.Target;
                    Interface.Interface.Say("Moving from here: click where it goes", true);
                }
                return;
            }
            if (selectedEntities.Contains(entity)) selectedEntities.Remove(entity);
            else selectedEntities.Add(entity);
            BuildSide();
        }

        private List<SketchEntity> OperationSource() => selectedEntities.Where(e => sketch.entities.Contains(e) && !e.projected).ToList();

        private Vector2 SelectionCenter(IList<SketchEntity> source) {
            var points = source.SelectMany(sketch.PointsOf).Distinct().Select(sketch.P).ToList();
            if (points.Count == 0) return Vector2.zero;
            var min = points.Aggregate(Vector2.Min);
            var max = points.Aggregate(Vector2.Max);
            return (min + max) / 2f;
        }

        /// <summary>Runs the mirror, pattern or move / copy / rotate / scale on the selection (or on a copy of the
        /// sketch, for the preview).</summary>
        private List<SketchEntity> RunOperation(SketchData target, List<SketchEntity> source) {
            var mapped = source.Select(e => target.Entity(e.id)).Where(e => e != null).ToList();
            if (mapped.Count == 0) return new List<SketchEntity>();
            var center = opCenterSet ? opCenter : tool == Tool.CircularPattern ? Vector2.zero : SelectionCenter(mapped);
            switch (tool) {
                case Tool.Mirror:
                    return mirrorAxis != null && target.Entity(mirrorAxis.id) is SketchEntity axis ? target.Mirror(mapped, axis) : new List<SketchEntity>();
                case Tool.LinearPattern: {
                    var dir1 = new Vector2(Mathf.Cos(patternAngle * Mathf.Deg2Rad), Mathf.Sin(patternAngle * Mathf.Deg2Rad));
                    var dir2 = new Vector2(-dir1.y, dir1.x);
                    return target.LinearPattern(mapped, dir1, pattern1, spacing1, dir2, pattern2, spacing2);
                }
                case Tool.CircularPattern:
                    return target.CircularPattern(mapped, center, circularCount, circularAngle);
                case Tool.Move:
                case Tool.Copy: {
                    var shift = new Vector2(moveX, moveY);
                    if (tool == Tool.Copy) return target.CopyEntities(mapped, q => q + shift, false, true, out _);
                    target.TransformEntities(mapped, q => q + shift);
                    return mapped;
                }
                case Tool.Rotate: {
                    var turn = Quaternion.Euler(0f, 0f, rotateAngle);
                    Vector2 Map(Vector2 q) => center + (Vector2)(turn * (q - center));
                    if (transformCopy) return target.CopyEntities(mapped, Map, false, Mathf.Abs(Mathf.Repeat(rotateAngle, 90f)) < 1e-3f, out _);
                    target.TransformEntities(mapped, Map);
                    return mapped;
                }
                case Tool.Scale: {
                    Vector2 Map(Vector2 q) => center + (q - center) * scaleFactor;
                    if (transformCopy) return target.CopyEntities(mapped, Map, false, true, out _);
                    target.TransformEntities(mapped, Map);
                    return mapped;
                }
            }
            return new List<SketchEntity>();
        }

        private void ApplyOperation() {
            var source = OperationSource();
            if (source.Count == 0) {
                Interface.Interface.Say("Select what to " + Def(tool).name.Split(' ')[0].ToLowerInvariant() + " first (click it)", false);
                return;
            }
            if (tool == Tool.Mirror && mirrorAxis == null) {
                opStep = OpStep.Axis;
                Interface.Interface.Say("Click the line to mirror about", true);
                return;
            }
            Remember();
            var made = RunOperation(sketch, source);
            var moving = tool is Tool.Move or Tool.Rotate or Tool.Scale && !(transformCopy && tool != Tool.Move);
            // Moving things keeps their relations to the rest: the solver puts back what they hold on to.
            if (moving && !SketchSolver.Solve(sketch, source.SelectMany(sketch.PointsOf).Distinct())) {
                sketch = undo[^1];
                undo.RemoveAt(undo.Count - 1);
                Interface.Interface.Say("Relations or dimensions hold that in place: delete them first", false);
                Changed();
                return;
            }
            if (!moving) SolveQuietly();
            mirrorAxis = null;
            opStep = OpStep.Entities;
            ClearSelection();
            Changed();
            Interface.Interface.Say(tool switch {
                Tool.Mirror => "Mirrored " + source.Count + (source.Count == 1 ? " entity" : " entities"),
                Tool.LinearPattern => "Made " + made.Count + " copies in a row",
                Tool.CircularPattern => "Made " + made.Count + " copies round the centre",
                Tool.Move => "Moved " + Inch.Format(moveX) + ", " + Inch.Format(moveY),
                Tool.Copy => "Copied " + source.Count + (source.Count == 1 ? " entity" : " entities"),
                Tool.Rotate => "Turned " + rotateAngle.ToString("0.#", CultureInfo.InvariantCulture) + "°",
                _ => "Scaled " + scaleFactor.ToString("0.###", CultureInfo.InvariantCulture) + "×",
            });
        }

        // The preview of the operation (on a copy of the sketch), drawn faint.
        private List<(List<Vector2> line, bool closed)> OperationPreview() {
            var result = new List<(List<Vector2>, bool)>();
            if (stage != Stage.Sketch || tool is not (Tool.Mirror or Tool.LinearPattern or Tool.CircularPattern or Tool.Move or Tool.Copy or Tool.Rotate or Tool.Scale)) return result;
            if (tool == Tool.Mirror && mirrorAxis == null) return result;
            var source = OperationSource();
            if (source.Count == 0) return result;
            if ((tool == Tool.Move || tool == Tool.Copy) && opBase.HasValue && MouseInCanvas(out var mouse)) {
                var keepX = moveX;
                var keepY = moveY;
                var now = ToSketch(mouse);
                moveX = now.x - opBase.Value.x;
                moveY = now.y - opBase.Value.y;
                var copy1 = sketch.Copy();
                foreach (var e in RunOperation(copy1, source)) result.Add((copy1.Polyline(e, out var c1), c1));
                moveX = keepX;
                moveY = keepY;
                return result;
            }
            var copy = sketch.Copy();
            foreach (var e in RunOperation(copy, source)) result.Add((copy.Polyline(e, out var closed), closed));
            return result;
        }

        // ------------------------------------------------------------------ copy and paste

        private readonly List<int> clipboard = new();

        private void CopySelection() {
            clipboard.Clear();
            clipboard.AddRange(selectedEntities.Where(e => !e.projected).Select(e => e.id));
            Interface.Interface.Say(clipboard.Count > 0 ? "Copied " + clipboard.Count + (clipboard.Count == 1 ? " entity" : " entities") : "Select something to copy", clipboard.Count > 0);
        }

        private void Paste() {
            var source = clipboard.Select(sketch.Entity).Where(e => e != null).ToList();
            if (source.Count == 0) {
                Interface.Interface.Say("Nothing to paste: copy something first (Ctrl+C)", false);
                return;
            }
            var at = MouseInCanvas(out var mouse) ? ToSketch(mouse) : SelectionCenter(source) + Vector2.one * 0.5f;
            var shift = at - SelectionCenter(source);
            Remember();
            var made = sketch.CopyEntities(source, q => q + shift, false, true, out _);
            ClearSelection();
            selectedEntities.AddRange(made);
            Changed();
            Interface.Interface.Say("Pasted " + made.Count + (made.Count == 1 ? " entity" : " entities"));
        }

        private void SelectAll() {
            ClearSelection();
            selectedEntities.AddRange(sketch.entities.Where(e => !e.projected));
            BuildSide();
            Interface.Interface.Say("Selected everything (" + selectedEntities.Count + ")", true);
        }
    }
}
