using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: drawing the sketch as SolidWorks colours it: geometry that can still move blue (under defined), what can't
    // white (fully defined), converted part edges purple, construction dashed; the origin's arrows; the snap under the
    // mouse (a square for a point, a triangle for a midpoint, a cross where curves meet...) with its name and the
    // dotted lines in line with other points; and a faint preview of what each tool will make.
    public partial class SheetEditor {
        private static readonly Color UnderDefined = new(0.45f, 0.72f, 1f, 1f);
        private static readonly Color Projected = new(0.78f, 0.52f, 1f, 1f);
        private static readonly Color PreviewInk = new(1f, 1f, 1f, 0.45f);
        private HashSet<int> freePoints = new();
        private HashSet<int> freeRadii = new();
        private bool fullyDefined;

        private bool Defined(SketchEntity e) => !e.projected && !sketch.PointsOf(e).Any(freePoints.Contains) && !freeRadii.Contains(e.id);

        private Color EntityColor(SketchEntity e) => e.projected ? Projected : Defined(e) ? Ink : UnderDefined;

        private List<Vector2> CircleOnCanvas(Vector2 center, float radius, int segments = 64) {
            var list = new List<Vector2>(segments);
            for (var i = 0; i < segments; i++) {
                var a = i * Mathf.PI * 2f / segments;
                list.Add(ToCanvas(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius));
            }
            return list;
        }

        private void Draw(SnapInfo snap, Vector2 canvasMouse, bool inside) {
            shapes.Clear();
            labelsUsed = 0;
            if (!inPlace) DrawSheet();
            if (stage == Stage.Piece) {
                DrawPiece(canvasMouse, inside);
                DrawOpenEnds();
                HideUnusedLabels();
                return;
            }
            if (inPlace && tool == Tool.Project) DrawProjectHover();
            DrawOrigin();

            var hoverTools = tool is Tool.Select or Tool.Dimension or Tool.Trim or Tool.Extend or Tool.Fillet or Tool.Chamfer or Tool.Mirror
                or Tool.LinearPattern or Tool.CircularPattern or Tool.Move or Tool.Copy or Tool.Rotate or Tool.Scale or Tool.Offset;
            var hoverEntity = inside && hoverTools ? HoverEntity(canvasMouse) : null;
            if (tool == Tool.Mirror && opStep == OpStep.Axis && hoverEntity != null && hoverEntity.kind != EntityKind.Line) hoverEntity = null;
            var picked = dimensionPicks.Select(d => d.entity).Where(e => e != null).ToList();
            foreach (var e in sketch.entities) {
                var line = sketch.Polyline(e, out var closed).Select(p => ToCanvas(p)).ToList();
                var selected = selectedEntities.Contains(e) || picked.Contains(e) || e == mirrorAxis || e == filletFirstLine;
                var color = selected ? Theme.Accent : e == hoverEntity ? (tool == Tool.Trim && !e.projected ? Theme.Danger : Theme.AccentHover) : EntityColor(e);
                if (e.kind == EntityKind.Point) {
                    var c = line[0];
                    shapes.Disc(c, selected || e == hoverEntity ? 4.5f : 3.5f, color, 12);
                    continue;
                }
                if (e.construction) {
                    var faint = e.projected ? new Color(color.r, color.g, color.b, 0.8f) : color;
                    for (var i = 0; i < line.Count - (closed ? 0 : 1); i++) shapes.Dashed(line[i], line[(i + 1) % line.Count], faint, e.projected ? 1.4f : 1.2f);
                }
                else shapes.Polyline(line, color, selected ? 2.6f : e.projected ? 1.6f : 1.9f, closed);
                if (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc) {
                    var c = ToCanvas(sketch.P(e.a));
                    shapes.Line(c - new Vector2(4f, 0f), c + new Vector2(4f, 0f), color, 1f);
                    shapes.Line(c - new Vector2(0f, 4f), c + new Vector2(0f, 4f), color, 1f);
                }
            }
            // Points: the ends of lines and arcs (circle centres only when picked or under the mouse).
            var hoverPoint = inside && (tool == Tool.Select || tool == Tool.Dimension || tool == Tool.Fillet || tool == Tool.Chamfer) ? HoverPoint(canvasMouse) : -1;
            var projectedOnly = new HashSet<int>(sketch.entities.Where(e => e.projected).SelectMany(sketch.PointsOf));
            projectedOnly.ExceptWith(sketch.entities.Where(e => !e.projected).SelectMany(sketch.PointsOf));
            foreach (var p in sketch.points) {
                if (p.id == sketch.origin) continue;
                var picked2 = dimensionPicks.Any(d => d.point == p.id);
                if (IsCircleCenterOnly(p.id) && !selectedPoints.Contains(p.id) && p.id != hoverPoint && !picked2) continue;
                if (sketch.entities.Any(e => e.kind == EntityKind.Point && e.a == p.id)) continue;   // drawn as the point entity
                var c = ToCanvas(p.p);
                var color = selectedPoints.Contains(p.id) || picked2 ? Theme.Accent : p.id == hoverPoint ? Theme.AccentHover
                    : projectedOnly.Contains(p.id) ? Projected : freePoints.Contains(p.id) ? UnderDefined : Ink;
                var size = selectedPoints.Contains(p.id) || p.id == hoverPoint ? 4.5f : projectedOnly.Contains(p.id) ? 2.2f : 3f;
                shapes.Rect(c - Vector2.one * size, c + Vector2.one * size, color);
            }
            if (showRelations) DrawRelations();
            DrawOpenEnds();
            foreach (var dimension in sketch.dimensions.ToList()) DrawDimension(dimension);
            if (placingDimension && pendingDimension != null) DrawDimension(pendingDimension, true);

            // What a modify tool will make, faint.
            if (tool == Tool.Offset && offsetPreview != null && previewSketch != null && inside)
                foreach (var e in offsetPreview) shapes.Polyline(previewSketch.Polyline(e, out var closed).Select(p => ToCanvas(p)).ToList(), PreviewInk, 1.6f, closed);
            foreach (var (line, closed) in OperationPreview()) shapes.Polyline(line.Select(p => ToCanvas(p)).ToList(), PreviewInk, 1.6f, closed);
            if (tool is Tool.Rotate or Tool.Scale or Tool.CircularPattern && (opCenterSet || tool == Tool.CircularPattern)) {
                var c = ToCanvas(opCenterSet ? opCenter : Vector2.zero);
                shapes.Ring(c, 6f, Theme.Warning, 1.5f, 16);
                shapes.Disc(c, 2f, Theme.Warning, 8);
            }
            if (boxing) {
                var a = boxStart;
                var b = boxEnd;
                var crossing = b.x < a.x;
                var color = crossing ? new Color(0.45f, 0.9f, 0.55f, 0.9f) : new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.9f);
                var box = new[] { a, new Vector2(b.x, a.y), b, new Vector2(a.x, b.y) };
                shapes.Quad(box[0], box[1], box[2], box[3], new Color(color.r, color.g, color.b, 0.08f));
                if (crossing) for (var i = 0; i < 4; i++) shapes.Dashed(box[i], box[(i + 1) % 4], color, 1.2f);
                else shapes.Polyline(box, color, 1.2f, true);
            }

            if (inside) DrawToolPreview(snap);
            HideUnusedLabels();
        }

        private void HideUnusedLabels() {
            for (var i = 0; i < labels.Count; i++) labels[i].transform.parent.gameObject.SetActive(i < labelsUsed);
        }

        // The flat sheet, its grid and its size.
        private void DrawSheet() {
            var rect = canvasRect.rect;
            var sheet = SheetSize;
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
        }

        // The sketch's origin: a red x arrow and a green y arrow, as SolidWorks shows it.
        private void DrawOrigin() {
            var o = ToCanvas(Vector2.zero);
            var x = ToCanvas(Vector2.right * (28f / Mathf.Max(zoom, 0.01f)));
            var y = ToCanvas(Vector2.up * (28f / Mathf.Max(zoom, 0.01f)));
            shapes.Line(o, x, Theme.AxisX, 1.6f);
            shapes.Line(o, y, Theme.AxisY, 1.6f);
            if ((x - o).sqrMagnitude > 1f) Arrow(x, (o - x).normalized, Theme.AxisX);
            if ((y - o).sqrMagnitude > 1f) Arrow(y, (o - y).normalized, Theme.AxisY);
            shapes.Disc(o, 2.5f, Theme.AxisX, 10);
        }

        // What the tool will make where the mouse is, the snap there, and the dotted lines in line with points.
        private void DrawToolPreview(SnapInfo snap) {
            var p = snap.p;
            var preview = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.9f);
            switch (tool) {
                case Tool.Line when chainLast >= 0:
                case Tool.Centerline when chainLast >= 0: {
                    var start = sketch.P(chainLast);
                    var (end, level) = Straighten(start, snap);
                    if (tool == Tool.Centerline) shapes.Dashed(ToCanvas(start), ToCanvas(end), preview, 1.4f);
                    else shapes.Line(ToCanvas(start), ToCanvas(end), preview, 1.8f);
                    Label((ToCanvas(start) + ToCanvas(end)) / 2f + new Vector2(0f, 14f), Inch.Format(Vector2.Distance(start, end)) + (level == 1 ? "  ─" : level == 2 ? "  │" : ""), Theme.Dimension, true);
                    break;
                }
                case Tool.MidpointLine when clicks.Count == 1: {
                    var a = clicks[0] * 2f - p;
                    shapes.Line(ToCanvas(a), ToCanvas(p), preview, 1.8f);
                    Label(ToCanvas(clicks[0]) + new Vector2(0f, 14f), Inch.Format(Vector2.Distance(a, p)), Theme.Dimension, true);
                    break;
                }
                case Tool.Rectangle when clicks.Count == 1:
                case Tool.CenterRectangle when clicks.Count == 1: {
                    var a = tool == Tool.Rectangle ? clicks[0] : clicks[0] * 2f - p;
                    var box = new[] { a, new Vector2(p.x, a.y), p, new Vector2(a.x, p.y) }.Select(q => ToCanvas(q)).ToList();
                    shapes.Polyline(box, preview, 1.8f, true);
                    if (tool == Tool.CenterRectangle) {
                        shapes.Dashed(box[0], box[2], preview, 1f);
                        shapes.Dashed(box[1], box[3], preview, 1f);
                    }
                    Label((box[0] + box[1]) / 2f - new Vector2(0f, 14f), Inch.Format(Mathf.Abs(p.x - a.x)), Theme.Dimension, true);
                    Label((box[1] + box[2]) / 2f + new Vector2(30f, 0f), Inch.Format(Mathf.Abs(p.y - a.y)), Theme.Dimension, true);
                    break;
                }
                case Tool.ThreePointRectangle when clicks.Count >= 1: {
                    if (clicks.Count == 1) {
                        shapes.Line(ToCanvas(clicks[0]), ToCanvas(p), preview, 1.8f);
                        Label((ToCanvas(clicks[0]) + ToCanvas(p)) / 2f + new Vector2(0f, 14f), Inch.Format(Vector2.Distance(clicks[0], p)), Theme.Dimension, true);
                        break;
                    }
                    var d = (clicks[1] - clicks[0]).normalized;
                    var n = new Vector2(-d.y, d.x);
                    var h = Vector2.Dot(p - clicks[0], n);
                    var box = new[] { clicks[0], clicks[1], clicks[1] + n * h, clicks[0] + n * h }.Select(q => ToCanvas(q)).ToList();
                    shapes.Polyline(box, preview, 1.8f, true);
                    Label((box[1] + box[2]) / 2f + new Vector2(30f, 0f), Inch.Format(Mathf.Abs(h)), Theme.Dimension, true);
                    break;
                }
                case Tool.Circle when clicks.Count == 1: {
                    var r = Vector2.Distance(p, clicks[0]);
                    shapes.Polyline(CircleOnCanvas(clicks[0], r), preview, 1.8f, true);
                    Label(ToCanvas(clicks[0] + Vector2.up * r) + new Vector2(0f, 14f), "Ø " + Inch.Format(r * 2f), Theme.Dimension, true);
                    break;
                }
                case Tool.PerimeterCircle when clicks.Count >= 1: {
                    if (clicks.Count == 1) shapes.Dashed(ToCanvas(clicks[0]), ToCanvas(p), preview, 1f);
                    else if (SketchData.Circumcircle(clicks[0], clicks[1], p, out var c, out var r)) {
                        shapes.Polyline(CircleOnCanvas(c, r), preview, 1.8f, true);
                        Label(ToCanvas(c) + new Vector2(0f, 14f), "Ø " + Inch.Format(r * 2f), Theme.Dimension, true);
                    }
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
                case Tool.TangentArc when clicks.Count == 1 && clickSnaps.Count == 1: {
                    if (!TangentStart(clickSnaps[0].point, out var dir, out _)) break;
                    var temp = new SketchData();
                    var start = temp.AddPoint(clicks[0]);
                    var arc = temp.TangentArc(start.id, dir, p, null);
                    if (arc != null) shapes.Polyline(temp.Polyline(arc, out _).Select(q => ToCanvas(q)).ToList(), preview, 1.8f);
                    break;
                }
                case Tool.ThreePointArc when clicks.Count >= 1: {
                    if (clicks.Count == 1) {
                        shapes.Dashed(ToCanvas(clicks[0]), ToCanvas(p), preview, 1f);
                        break;
                    }
                    var temp = new SketchData();
                    var arc = temp.ThreePointArc(clicks[0], clicks[1], p);
                    if (arc != null) {
                        shapes.Polyline(temp.Polyline(arc, out _).Select(q => ToCanvas(q)).ToList(), preview, 1.8f);
                        Label(ToCanvas(p) + new Vector2(0f, 16f), "R " + Inch.Format(arc.radius), Theme.Dimension, true);
                    }
                    break;
                }
                case Tool.Slot when clicks.Count >= 1: {
                    if (clicks.Count == 1) {
                        shapes.Dashed(ToCanvas(clicks[0]), ToCanvas(p), preview, 1f);
                        break;
                    }
                    var half = Mathf.Max(SketchData.DistanceToLine(p, clicks[0], clicks[1]), 0.01f);
                    shapes.Polyline(SketchIcons.Slot(clicks[0], clicks[1], half, 16).Select(q => ToCanvas(q)).ToList(), preview, 1.8f, true);
                    Label(ToCanvas(clicks[1]) + new Vector2(0f, 18f), Inch.Format(half * 2f) + " wide", Theme.Dimension, true);
                    break;
                }
                case Tool.Polygon when clicks.Count == 1: {
                    var r = Vector2.Distance(p, clicks[0]);
                    if (r < 1e-3f) break;
                    var start = Mathf.Atan2(p.y - clicks[0].y, p.x - clicks[0].x);
                    var poly = Enumerable.Range(0, polygonSides).Select(i => ToCanvas(clicks[0] + new Vector2(Mathf.Cos(start + Mathf.PI * 2f * i / polygonSides), Mathf.Sin(start + Mathf.PI * 2f * i / polygonSides)) * r)).ToList();
                    shapes.Polyline(poly, preview, 1.8f, true);
                    shapes.Polyline(CircleOnCanvas(clicks[0], r), new Color(preview.r, preview.g, preview.b, 0.35f), 1f, true);
                    Label(ToCanvas(clicks[0]) + new Vector2(0f, -16f), polygonSides + " sides · " + Inch.Format(r * 2f) + " across corners", Theme.Dimension, true);
                    break;
                }
                case Tool.Move when opBase.HasValue:
                case Tool.Copy when opBase.HasValue:
                    shapes.Dashed(ToCanvas(opBase.Value), ToCanvas(p), Theme.Dimension, 1.2f);
                    Label((ToCanvas(opBase.Value) + ToCanvas(p)) / 2f + new Vector2(0f, 14f), Inch.Format(p.x - opBase.Value.x) + ", " + Inch.Format(p.y - opBase.Value.y), Theme.Dimension, true);
                    break;
            }
            var drawing = tool != Tool.Select && tool != Tool.Trim && tool != Tool.Dimension && tool != Tool.Project && tool != Tool.Extend && tool != Tool.Offset;
            if (!drawing && !(tool is Tool.Move or Tool.Copy && opStep != OpStep.Entities) && !(opStep == OpStep.Center)) return;
            DrawSnap(snap);
        }

        private void DrawSnap(SnapInfo snap) {
            var c = ToCanvas(snap.p);
            var color = Theme.Warning;
            string name = null;
            switch (snap.kind) {
                case SnapKind.Point:
                    shapes.Rect(c - Vector2.one * 5.5f, c + Vector2.one * 5.5f, new Color(0f, 0f, 0f, 0.5f));
                    shapes.Ring(c, 7f, color, 1.5f, 4);
                    name = snap.point == sketch.origin ? "Origin" : "Endpoint";
                    break;
                case SnapKind.Center:
                    shapes.Ring(c, 7f, color, 1.5f, 20);
                    shapes.Disc(c, 2f, color, 8);
                    name = "Centre";
                    break;
                case SnapKind.Midpoint:
                    shapes.Triangle(c + new Vector2(0f, 7f), c + new Vector2(-6f, -4f), c + new Vector2(6f, -4f), color);
                    name = "Midpoint";
                    break;
                case SnapKind.Quadrant:
                    shapes.Polyline(new[] { c + new Vector2(0f, 7f), c + new Vector2(7f, 0f), c + new Vector2(0f, -7f), c + new Vector2(-7f, 0f) }, color, 1.5f, true);
                    name = "Quadrant";
                    break;
                case SnapKind.Intersection:
                    shapes.Line(c + new Vector2(-6f, -6f), c + new Vector2(6f, 6f), color, 1.8f);
                    shapes.Line(c + new Vector2(-6f, 6f), c + new Vector2(6f, -6f), color, 1.8f);
                    name = "Intersection";
                    break;
                case SnapKind.OnCurve:
                    shapes.Disc(c, 3f, color, 10);
                    name = "On " + (snap.entity.kind == EntityKind.Line ? "line" : snap.entity.kind == EntityKind.Circle ? "circle" : "arc");
                    break;
                default:
                    shapes.Line(c - new Vector2(6f, 0f), c + new Vector2(6f, 0f), Theme.TextDim, 1f);
                    shapes.Line(c - new Vector2(0f, 6f), c + new Vector2(0f, 6f), Theme.TextDim, 1f);
                    break;
            }
            var infer = new Color(Theme.Warning.r, Theme.Warning.g, Theme.Warning.b, 0.6f);
            if (snap.alignX >= 0) shapes.Dashed(ToCanvas(sketch.P(snap.alignX)), c, infer, 1f);
            if (snap.alignY >= 0) shapes.Dashed(ToCanvas(sketch.P(snap.alignY)), c, infer, 1f);
            if (name != null) Label(c + new Vector2(0f, -20f), name, Theme.Warning, false);
        }
    }
}
