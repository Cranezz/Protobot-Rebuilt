using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Polycarb {
    // V2: Smart Dimension, as SolidWorks': click a line (its length), a circle (diameter) or an arc (radius), or two
    // things (two points, a point and a line, two lines, circles' centres) for the distance or angle between them;
    // then click where the dimension goes. Two points measure straight across, or level or upright, by where you put
    // it (beside them: up and down; above or below: across). Type the size: it holds (SketchSolver).
    public partial class SheetEditor {
        private readonly List<(int point, SketchEntity entity)> dimensionPicks = new();
        private bool placingDimension;
        private SketchDimension pendingDimension;
        private SketchDimension selectedDimension;

        private static bool IsRoundEntity(SketchEntity e) => e != null && (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc);

        private void HandleDimension(bool pressed, Vector2 canvasMouse) {
            if (placingDimension && pendingDimension != null) {
                Place(pendingDimension, ToSketch(canvasMouse));
                if (!pressed) return;
                // As in SolidWorks: with one item picked, a click on a second item measures between the two.
                if (dimensionPicks.Count == 1) {
                    var (secondPoint, secondEntity) = PickUnder(canvasMouse);
                    var first = dimensionPicks[0];
                    if (secondEntity != null && secondEntity != first.entity || secondPoint >= 0 && secondPoint != first.point && !(first.entity != null && sketch.PointsOf(first.entity).Contains(secondPoint))) {
                        dimensionPicks.Add((secondPoint, secondEntity));
                        StartPlacing();
                        return;
                    }
                }
                Remember();
                pendingDimension.id = sketch.nextId++;
                pendingDimension.value = sketch.Measure(pendingDimension);
                sketch.dimensions.Add(pendingDimension);
                var made = pendingDimension;
                pendingDimension = null;
                placingDimension = false;
                dimensionPicks.Clear();
                Changed();
                OpenEditField(made);
                return;
            }
            if (!pressed) return;
            var (point, entity) = PickUnder(canvasMouse);
            if (point < 0 && entity == null) {
                if (dimensionPicks.Count == 1 && dimensionPicks[0].entity != null) StartPlacing();
                else Interface.Interface.Say("Click a line, circle, arc or point", false);
                return;
            }
            if (dimensionPicks.Count == 1 && (dimensionPicks[0].entity == entity && entity != null || dimensionPicks[0].point == point && point >= 0)) {
                StartPlacing();
                return;
            }
            dimensionPicks.Add((point, entity));
            // A line, circle or arc: its dimension follows the mouse at once (click to put it down, or click a second
            // item to measure between them); a point waits for a second item.
            if (dimensionPicks.Count == 2 || entity != null) StartPlacing();
            else Interface.Interface.Say("Now a second point, line or circle", true);
        }

        // The point (preferred when the mouse is right on it) or curve under the mouse.
        private (int point, SketchEntity entity) PickUnder(Vector2 canvasMouse) {
            var point = HoverPoint(canvasMouse);
            var entity = HoverEntity(canvasMouse);
            if (point >= 0 && (entity == null || DistanceToPointPixels(point, canvasMouse) < 6f)) entity = null;
            else point = -1;
            if (entity != null && entity.kind == EntityKind.Point) {
                point = entity.a;
                entity = null;
            }
            return (point, entity);
        }

        // The dimension the picks make, to be placed with the mouse.
        private void StartPlacing() {
            pendingDimension = MakeDimension();
            if (pendingDimension == null) {
                dimensionPicks.Clear();
                Interface.Interface.Say("Those can't be dimensioned together", false);
                return;
            }
            placingDimension = true;
            Interface.Interface.Say(dimensionPicks.Count == 1 ? "Click where the dimension goes, or a second item to measure to" : "Click where the dimension goes", true);
        }

        private SketchDimension MakeDimension() {
            int PointOf((int point, SketchEntity entity) pick) => pick.point >= 0 ? pick.point : IsRoundEntity(pick.entity) ? pick.entity.a : -1;
            if (dimensionPicks.Count == 1) {
                var e = dimensionPicks[0].entity;
                if (e == null) return null;
                return e.kind switch {
                    EntityKind.Line => new SketchDimension { kind = DimensionKind.Length, entity = e.id, offset = 0.35f },
                    EntityKind.Circle => new SketchDimension { kind = DimensionKind.Diameter, entity = e.id },
                    EntityKind.Arc => new SketchDimension { kind = DimensionKind.Radius, entity = e.id, offset = Mathf.PI / 4f },
                    _ => null,
                };
            }
            var a = dimensionPicks[0];
            var b = dimensionPicks[1];
            var lineA = a.entity != null && a.entity.kind == EntityKind.Line ? a.entity : null;
            var lineB = b.entity != null && b.entity.kind == EntityKind.Line ? b.entity : null;
            if (lineA != null && lineB != null) {
                var da = (sketch.P(lineA.b) - sketch.P(lineA.a)).normalized;
                var db = (sketch.P(lineB.b) - sketch.P(lineB.a)).normalized;
                if (Mathf.Abs(da.x * db.y - da.y * db.x) < 0.02f)
                    return new SketchDimension { kind = DimensionKind.LineLine, entity = lineA.id, entity2 = lineB.id };
                return new SketchDimension { kind = DimensionKind.Angle, entity = lineA.id, entity2 = lineB.id, offset = 0.6f };
            }
            if (lineA != null || lineB != null) {
                var line = lineA ?? lineB;
                var other = lineA != null ? b : a;
                var p = PointOf(other);
                return p < 0 ? null : new SketchDimension { kind = DimensionKind.PointLine, entity = line.id, from = p };
            }
            var pa = PointOf(a);
            var pb = PointOf(b);
            if (pa < 0 || pb < 0 || pa == pb) return null;
            return new SketchDimension { kind = DimensionKind.Distance, from = pa, to = pb, offset = 0.35f };
        }

        // Where it's put sets its offset, and for two points whether it's across, level or upright.
        private void Place(SketchDimension d, Vector2 at) {
            switch (d.kind) {
                case DimensionKind.Distance:
                case DimensionKind.Horizontal:
                case DimensionKind.Vertical: {
                    Vector2 a = sketch.P(d.from), b = sketch.P(d.to);
                    var min = Vector2.Min(a, b);
                    var max = Vector2.Max(a, b);
                    var mid = (a + b) / 2f;
                    var besideX = (at.x < min.x || at.x > max.x) && at.y >= min.y - 0.05f && at.y <= max.y + 0.05f;
                    var besideY = (at.y < min.y || at.y > max.y) && at.x >= min.x - 0.05f && at.x <= max.x + 0.05f;
                    if (besideX && max.y - min.y > 1e-3f) {
                        d.kind = DimensionKind.Vertical;
                        d.offset = at.x - mid.x;
                    }
                    else if (besideY && max.x - min.x > 1e-3f) {
                        d.kind = DimensionKind.Horizontal;
                        d.offset = at.y - mid.y;
                    }
                    else {
                        d.kind = DimensionKind.Distance;
                        var dir = (b - a).normalized;
                        d.offset = Vector2.Dot(at - mid, new Vector2(-dir.y, dir.x));
                    }
                    break;
                }
                case DimensionKind.Length when sketch.Entity(d.entity) is SketchEntity line: {
                    Vector2 a = sketch.P(line.a), b = sketch.P(line.b);
                    var dir = (b - a).normalized;
                    d.offset = Vector2.Dot(at - (a + b) / 2f, new Vector2(-dir.y, dir.x));
                    break;
                }
                case DimensionKind.Angle when sketch.Entity(d.entity) is SketchEntity l1 && sketch.Entity(d.entity2) is SketchEntity l2: {
                    var vertex = AngleVertex(l1, l2);
                    d.offset = Mathf.Max(0.15f, Vector2.Distance(at, vertex));
                    break;
                }
                case DimensionKind.Radius when sketch.Entity(d.entity) is SketchEntity round: {
                    var c = sketch.P(round.a);
                    d.offset = Mathf.Atan2(at.y - c.y, at.x - c.x);
                    break;
                }
                case DimensionKind.Diameter when sketch.Entity(d.entity) is SketchEntity circle: {
                    var c = sketch.P(circle.a);
                    d.offset = Mathf.Atan2(at.y - c.y, at.x - c.x);
                    break;
                }
            }
        }

        private Vector2 AngleVertex(SketchEntity l1, SketchEntity l2) => SketchData.AngleCorner(l1, l2, sketch.Point);

        // ------------------------------------------------------------------ drawing a dimension

        private void DrawDimension(SketchDimension dimension, bool preview = false) {
            var color = dimension == selectedDimension ? Theme.Accent : Theme.Dimension;
            if (preview) color = new Color(color.r, color.g, color.b, 0.8f);
            var faint = new Color(color.r, color.g, color.b, 0.5f);
            var e = sketch.Entity(dimension.entity);
            string text;
            Vector2 labelAt;
            switch (dimension.kind) {
                case DimensionKind.Length when e != null:
                case DimensionKind.Distance:
                case DimensionKind.Width when e != null:
                case DimensionKind.Height when e != null: {
                    Vector2 a, b;
                    if (dimension.kind == DimensionKind.Distance) { a = sketch.P(dimension.from); b = sketch.P(dimension.to); }
                    else if (dimension.kind == DimensionKind.Width) { a = sketch.P(e.a); b = sketch.P(e.b); }
                    else if (dimension.kind == DimensionKind.Height) { a = sketch.P(e.b); b = sketch.P(e.c); }
                    else { a = sketch.P(e.a); b = sketch.P(e.b); }
                    var dir = (b - a).normalized;
                    var normal = new Vector2(-dir.y, dir.x) * (Mathf.Abs(dimension.offset) < 1e-4f ? 0.35f : dimension.offset);
                    labelAt = LinearDimension(a, b, a + normal, b + normal, color, faint);
                    text = Inch.Format(sketch.Measure(dimension));
                    break;
                }
                case DimensionKind.Horizontal: {
                    Vector2 a = sketch.P(dimension.from), b = sketch.P(dimension.to);
                    var y = (a.y + b.y) / 2f + dimension.offset;
                    labelAt = LinearDimension(a, b, new Vector2(a.x, y), new Vector2(b.x, y), color, faint);
                    text = Inch.Format(sketch.Measure(dimension));
                    break;
                }
                case DimensionKind.Vertical: {
                    Vector2 a = sketch.P(dimension.from), b = sketch.P(dimension.to);
                    var x = (a.x + b.x) / 2f + dimension.offset;
                    labelAt = LinearDimension(a, b, new Vector2(x, a.y), new Vector2(x, b.y), color, faint);
                    text = Inch.Format(sketch.Measure(dimension));
                    break;
                }
                case DimensionKind.PointLine when e != null: {
                    var p = sketch.P(dimension.from);
                    var foot = Foot(p, sketch.P(e.a), sketch.P(e.b));
                    labelAt = LinearDimension(p, foot, p, foot, color, faint, true);
                    shapes.Dashed(ToCanvas(foot), ToCanvas(Vector2.Distance(foot, sketch.P(e.a)) < Vector2.Distance(foot, sketch.P(e.b)) ? sketch.P(e.a) : sketch.P(e.b)), faint, 1f);
                    text = Inch.Format(sketch.Measure(dimension));
                    break;
                }
                case DimensionKind.LineLine when e != null && sketch.Entity(dimension.entity2) is SketchEntity l2: {
                    var p = (sketch.P(l2.a) + sketch.P(l2.b)) / 2f;
                    var foot = Foot(p, sketch.P(e.a), sketch.P(e.b));
                    labelAt = LinearDimension(p, foot, p, foot, color, faint, true);
                    text = Inch.Format(sketch.Measure(dimension));
                    break;
                }
                case DimensionKind.Angle when e != null && sketch.Entity(dimension.entity2) is SketchEntity m2: {
                    var (d1, d2) = SketchData.AngleDirections(sketch, e, m2, sketch.Point);
                    var vertex = AngleVertex(e, m2);
                    var r = Mathf.Max(0.15f, dimension.offset);
                    var a1 = Mathf.Atan2(d1.y, d1.x);
                    var a2 = Mathf.Atan2(d2.y, d2.x);
                    var sweep = Mathf.DeltaAngle(a1 * Mathf.Rad2Deg, a2 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    var arc = new List<Vector2>();
                    for (var i = 0; i <= 24; i++) {
                        var t = a1 + sweep * i / 24f;
                        arc.Add(ToCanvas(vertex + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r));
                    }
                    shapes.Polyline(arc, color, 1.2f);
                    if (arc.Count > 2) {
                        Arrow(arc[0], (arc[1] - arc[0]).normalized, color);
                        Arrow(arc[^1], (arc[^2] - arc[^1]).normalized, color);
                    }
                    var mid = a1 + sweep / 2f;
                    labelAt = ToCanvas(vertex + new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * (r + 0.12f));
                    text = sketch.Measure(dimension).ToString("0.#", CultureInfo.InvariantCulture) + "°";
                    break;
                }
                case DimensionKind.Radius when e != null: {
                    var c = sketch.P(e.a);
                    var dir = new Vector2(Mathf.Cos(dimension.offset), Mathf.Sin(dimension.offset));
                    var edge = c + dir * e.radius;
                    shapes.Line(ToCanvas(c), ToCanvas(edge), color, 1.2f);
                    Arrow(ToCanvas(edge), (ToCanvas(c) - ToCanvas(edge)).normalized, color);
                    labelAt = ToCanvas(c + dir * (e.radius + 0.18f));
                    text = "R " + Inch.Format(e.radius);
                    break;
                }
                case DimensionKind.Diameter when e != null: {
                    var c = sketch.P(e.a);
                    var angle = Mathf.Abs(dimension.offset) < 1e-6f ? 0f : dimension.offset;
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var p = ToCanvas(c - dir * e.radius);
                    var q = ToCanvas(c + dir * e.radius);
                    shapes.Line(p, q, color, 1.2f);
                    Arrow(p, (q - p).normalized, color);
                    Arrow(q, (p - q).normalized, color);
                    labelAt = (p + q) / 2f + new Vector2(0f, 12f);
                    text = "Ø " + Inch.Format(e.radius * 2f);
                    break;
                }
                default:
                    return;
            }
            var label = Label(labelAt, text, color, true);
            if (preview) return;
            var keep = dimension;
            label.transform.parent.GetComponent<Button>().onClick.AddListener(() => {
                selectedDimension = keep;
                ClearSelection();
                BuildSide();
                OpenEditField(keep);
            });
        }

        private static Vector2 Foot(Vector2 p, Vector2 a, Vector2 b) {
            var ab = b - a;
            return ab.sqrMagnitude < 1e-12f ? a : a + ab * (Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        }

        // A straight dimension: extension lines from the measured points to the dimension line, the line with its
        // arrows, and where the value goes.
        private Vector2 LinearDimension(Vector2 a, Vector2 b, Vector2 la, Vector2 lb, Color color, Color faint, bool noExtensions = false) {
            var ca = ToCanvas(la);
            var cb = ToCanvas(lb);
            if (!noExtensions) {
                var out1 = (ca - ToCanvas(a));
                var out2 = (cb - ToCanvas(b));
                shapes.Line(ToCanvas(a), ca + (out1.sqrMagnitude > 1f ? out1.normalized * 6f : Vector2.zero), faint, 1f);
                shapes.Line(ToCanvas(b), cb + (out2.sqrMagnitude > 1f ? out2.normalized * 6f : Vector2.zero), faint, 1f);
            }
            shapes.Line(ca, cb, color, 1.2f);
            var dir = (cb - ca).sqrMagnitude > 1e-4f ? (cb - ca).normalized : Vector2.right;
            Arrow(ca, dir, color);
            Arrow(cb, -dir, color);
            return (ca + cb) / 2f;
        }

        // ------------------------------------------------------------------ typing a dimension

        private void OpenEditField(SketchDimension dimension) {
            CloseEditField();
            editingDimension = dimension;
            var angle = dimension.kind == DimensionKind.Angle;
            var shown = angle ? sketch.Measure(dimension).ToString("0.##", CultureInfo.InvariantCulture) : Inch.Format(sketch.Measure(dimension), false);
            editField = Ui.Field(transform, "Dimension", shown, text => {
                if (editingDimension == null) return;
                float value;
                var ok = angle ? float.TryParse(text.Replace("°", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) : Inch.TryParse(text, out value);
                if (ok) {
                    Remember();
                    var keep = editingDimension;
                    var before = keep.value;
                    keep.value = angle ? Mathf.Clamp(value, 0.1f, 179.9f) : Mathf.Max(0.001f, value);
                    if (SketchSolver.Solve(sketch, Anchor(keep))) Changed();
                    else {
                        keep.value = before;
                        undo.RemoveAt(undo.Count - 1);
                        Interface.Interface.Say("That size doesn't work with the sketch's relations and other dimensions", false);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(text)) Interface.Interface.Say(angle ? "Type an angle in degrees, like 45" : "Type a size like 1 1/2 or 1.5", false);
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

        // What stays put when a dimension changes.
        private IEnumerable<int> Anchor(SketchDimension d) {
            var e = sketch.Entity(d.entity);
            return d.kind switch {
                DimensionKind.Length when e != null => new[] { e.a },
                DimensionKind.Width when e != null => new[] { e.a, e.d },
                DimensionKind.Height when e != null => new[] { e.a, e.b },
                DimensionKind.Diameter when e != null => new[] { e.a },
                DimensionKind.Radius when e != null => new[] { e.a },
                DimensionKind.Distance or DimensionKind.Horizontal or DimensionKind.Vertical => new[] { d.from },
                DimensionKind.PointLine when e != null => new[] { e.a, e.b },
                DimensionKind.LineLine or DimensionKind.Angle when e != null => new[] { e.a, e.b },
                _ => new int[0],
            };
        }

        private void DeleteDimension(SketchDimension d) {
            Remember();
            sketch.dimensions.Remove(d);
            if (selectedDimension == d) selectedDimension = null;
            CloseEditField();
            Changed();
            Interface.Interface.Say("Deleted the dimension");
        }
    }
}
