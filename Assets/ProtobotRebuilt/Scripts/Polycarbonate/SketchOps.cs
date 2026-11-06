using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: SolidWorks' sketch tools that build from or change what's drawn: shapes made of several pieces (centre
    // and 3-point rectangles, polygons, perimeter circles, 3-point and tangent arcs, midpoint lines), sketch fillets
    // and chamfers, offset entities, mirror, linear and circular patterns, move / copy / rotate / scale, and extend.
    // Each adds the relations SolidWorks would (a fillet is tangent to its lines, a mirror's points are symmetric).
    public partial class SketchData {
        // ------------------------------------------------------------------ shapes

        /// <summary>A rectangle from its centre and a corner, with construction diagonals and a centre point.</summary>
        public List<SketchEntity> CenterRectangle(Vector2 center, Vector2 corner) {
            var h = new Vector2(Mathf.Abs(corner.x - center.x), Mathf.Abs(corner.y - center.y));
            var a = AddPoint(center - h);
            var b = AddPoint(new Vector2(center.x + h.x, center.y - h.y));
            var c = AddPoint(center + h);
            var d = AddPoint(new Vector2(center.x - h.x, center.y + h.y));
            var lines = Explode(Add(EntityKind.Rectangle, a.id, b.id, c.id, d.id));
            var diagonal1 = Add(EntityKind.Line, a.id, c.id);
            var diagonal2 = Add(EntityKind.Line, b.id, d.id);
            diagonal1.construction = diagonal2.construction = true;
            var middle = Add(EntityKind.Point, AddPoint(center).id);
            Relate(RelationKind.Midpoint, diagonal1.id, p1: middle.a);
            Relate(RelationKind.Midpoint, diagonal2.id, p1: middle.a);
            lines.Add(diagonal1);
            lines.Add(diagonal2);
            lines.Add(middle);
            return lines;
        }

        /// <summary>A rectangle at any angle: one side from p1 to p2, then as far as p3 across.</summary>
        public List<SketchEntity> ThreePointRectangle(Vector2 p1, Vector2 p2, Vector2 p3) {
            var d = (p2 - p1).normalized;
            var n = new Vector2(-d.y, d.x);
            var h = Vector2.Dot(p3 - p1, n);
            var a = AddPoint(p1);
            var b = AddPoint(p2);
            var c = AddPoint(p2 + n * h);
            var e = AddPoint(p1 + n * h);
            var l0 = Add(EntityKind.Line, a.id, b.id);
            var l1 = Add(EntityKind.Line, b.id, c.id);
            var l2 = Add(EntityKind.Line, c.id, e.id);
            var l3 = Add(EntityKind.Line, e.id, a.id);
            Relate(RelationKind.Perpendicular, l0.id, l1.id);
            Relate(RelationKind.Parallel, l0.id, l2.id);
            Relate(RelationKind.Parallel, l1.id, l3.id);
            return new List<SketchEntity> { l0, l1, l2, l3 };
        }

        /// <summary>A regular polygon round a centre through a corner (inscribed in a construction circle), its
        /// sides equal and its corners on the circle.</summary>
        public List<SketchEntity> Polygon(Vector2 center, Vector2 corner, int sides) {
            sides = Mathf.Clamp(sides, 3, 40);
            var radius = Vector2.Distance(center, corner);
            var start = Mathf.Atan2(corner.y - center.y, corner.x - center.x);
            var circle = Add(EntityKind.Circle, AddPoint(center).id, radius: radius);
            circle.construction = true;
            var ids = new List<int>();
            for (var i = 0; i < sides; i++) {
                var angle = start + Mathf.PI * 2f * i / sides;
                ids.Add(AddPoint(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius).id);
            }
            var made = new List<SketchEntity> { circle };
            for (var i = 0; i < sides; i++) {
                var line = Add(EntityKind.Line, ids[i], ids[(i + 1) % sides]);
                made.Add(line);
                Relate(RelationKind.Coincident, circle.id, p1: ids[i]);
                if (i > 0) Relate(RelationKind.Equal, made[1].id, line.id);
            }
            return made;
        }

        /// <summary>A circle through three points (false when they're in a line).</summary>
        public SketchEntity PerimeterCircle(Vector2 p1, Vector2 p2, Vector2 p3) {
            if (!Circumcircle(p1, p2, p3, out var center, out var radius)) return null;
            return Add(EntityKind.Circle, AddPoint(center).id, radius: radius);
        }

        /// <summary>An arc from start to end through a third point.</summary>
        public SketchEntity ThreePointArc(Vector2 start, Vector2 end, Vector2 through, int startId = 0, int endId = 0) {
            if (!Circumcircle(start, end, through, out var center, out var radius)) return null;
            float Angle(Vector2 q) => Mathf.Atan2(q.y - center.y, q.x - center.x);
            float Ccw(float from, float to) {
                var x = to - from;
                while (x < 0f) x += Mathf.PI * 2f;
                while (x >= Mathf.PI * 2f) x -= Mathf.PI * 2f;
                return x;
            }
            var s = startId != 0 ? startId : AddPoint(start).id;
            var e = endId != 0 ? endId : AddPoint(end).id;
            // Counter-clockwise from start to end passes through the third point, or the other way round.
            var throughFirst = Ccw(Angle(start), Angle(through)) < Ccw(Angle(start), Angle(end));
            return throughFirst ? Add(EntityKind.Arc, AddPoint(center).id, s, e, radius: radius) : Add(EntityKind.Arc, AddPoint(center).id, e, s, radius: radius);
        }

        /// <summary>An arc leaving a line's end along it (tangent to it) to a point.</summary>
        public SketchEntity TangentArc(int fromPoint, Vector2 direction, Vector2 end, SketchEntity tangentTo) {
            var p = P(fromPoint);
            var n = new Vector2(-direction.y, direction.x).normalized;
            var e = end - p;
            var across = Vector2.Dot(e, n);
            if (Mathf.Abs(across) < 1e-4f) return null;
            var r = e.sqrMagnitude / (2f * across);   // signed: the centre is on the left when positive
            var center = p + n * r;
            var endId = AddPoint(end).id;
            var arc = r > 0f ? Add(EntityKind.Arc, AddPoint(center).id, fromPoint, endId, radius: Mathf.Abs(r))
                             : Add(EntityKind.Arc, AddPoint(center).id, endId, fromPoint, radius: Mathf.Abs(r));
            if (tangentTo != null) Relate(RelationKind.Tangent, tangentTo.id, arc.id);
            return arc;
        }

        public static bool Circumcircle(Vector2 a, Vector2 b, Vector2 c, out Vector2 center, out float radius) {
            center = default;
            radius = 0f;
            var d = 2f * (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));
            if (Mathf.Abs(d) < 1e-7f) return false;
            float a2 = a.sqrMagnitude, b2 = b.sqrMagnitude, c2 = c.sqrMagnitude;
            center = new Vector2((a2 * (b.y - c.y) + b2 * (c.y - a.y) + c2 * (a.y - b.y)) / d,
                                 (a2 * (c.x - b.x) + b2 * (a.x - c.x) + c2 * (b.x - a.x)) / d);
            radius = Vector2.Distance(center, a);
            return radius > 1e-4f;
        }

        // ------------------------------------------------------------------ fillet and chamfer

        // The two lines meeting at a point (for a fillet or chamfer there), each with its far end.
        private bool CornerLines(int point, out SketchEntity l1, out SketchEntity l2, out string why) {
            l1 = l2 = null;
            var lines = entities.Where(e => e.kind == EntityKind.Line && !e.projected && (e.a == point || e.b == point)).ToList();
            // Construction lines at the corner (a centre rectangle's diagonals) stay on the sharp corner, as in SolidWorks.
            var cut = lines.Where(e => !e.construction).ToList();
            if (cut.Count == 2) lines = cut;
            if (lines.Count != 2 || entities.Any(e => e.kind != EntityKind.Line && e.kind != EntityKind.Point && !e.construction && PointsOf(e).Contains(point))) {
                why = "Click a corner where two lines meet";
                return false;
            }
            l1 = lines[0];
            l2 = lines[1];
            var d1 = (P(Far(l1, point)) - P(point)).normalized;
            var d2 = (P(Far(l2, point)) - P(point)).normalized;
            if (Mathf.Abs(Vector2.Dot(d1, d2)) > 0.9995f) {
                why = "Those lines are in a straight line: there's no corner to round";
                return false;
            }
            why = null;
            return true;
        }

        private static int Far(SketchEntity line, int point) => line.a == point ? line.b : line.a;

        private void SetEnd(SketchEntity line, int oldPoint, int newPoint) {
            if (line.a == oldPoint) line.a = newPoint;
            else if (line.b == oldPoint) line.b = newPoint;
        }

        /// <summary>Rounds the corner at a point with an arc of this radius, tangent to both lines (and a radius
        /// dimension holding it), as SolidWorks' sketch fillet.</summary>
        public SketchEntity Fillet(int point, float radius, out string why) {
            if (!CornerLines(point, out var l1, out var l2, out why)) return null;
            var corner = P(point);
            var far1 = P(Far(l1, point));
            var far2 = P(Far(l2, point));
            var d1 = (far1 - corner).normalized;
            var d2 = (far2 - corner).normalized;
            var theta = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1f, 1f));
            var back = radius / Mathf.Tan(theta / 2f);
            if (back >= Vector2.Distance(corner, far1) - 1e-3f || back >= Vector2.Distance(corner, far2) - 1e-3f) {
                why = "That radius is too big for these lines (it needs " + back.ToString("0.###") + "\" of each)";
                return null;
            }
            var t1 = AddPoint(corner + d1 * back);
            var t2 = AddPoint(corner + d2 * back);
            var center = corner + (d1 + d2).normalized * (radius / Mathf.Sin(theta / 2f));
            SetEnd(l1, point, t1.id);
            SetEnd(l2, point, t2.id);
            var ccw = Cross(t1.p - center, t2.p - center) > 0f;
            var arc = Add(EntityKind.Arc, AddPoint(center).id, ccw ? t1.id : t2.id, ccw ? t2.id : t1.id, radius: radius);
            arc.construction = l1.construction && l2.construction;
            Relate(RelationKind.Tangent, l1.id, arc.id);
            Relate(RelationKind.Tangent, l2.id, arc.id);
            dimensions.Add(new SketchDimension { id = nextId++, kind = DimensionKind.Radius, entity = arc.id, value = radius });
            Tidy();
            return arc;
        }

        /// <summary>Cuts the corner at a point straight across, this far back along each line.</summary>
        public SketchEntity Chamfer(int point, float distance, float distance2, out string why) {
            if (!CornerLines(point, out var l1, out var l2, out why)) return null;
            var corner = P(point);
            var far1 = P(Far(l1, point));
            var far2 = P(Far(l2, point));
            if (distance >= Vector2.Distance(corner, far1) - 1e-3f || distance2 >= Vector2.Distance(corner, far2) - 1e-3f) {
                why = "The chamfer is longer than the lines";
                return null;
            }
            var t1 = AddPoint(corner + (far1 - corner).normalized * distance);
            var t2 = AddPoint(corner + (far2 - corner).normalized * distance2);
            SetEnd(l1, point, t1.id);
            SetEnd(l2, point, t2.id);
            var line = Add(EntityKind.Line, t1.id, t2.id);
            line.construction = l1.construction && l2.construction;
            Tidy();
            return line;
        }

        // ------------------------------------------------------------------ offset

        /// <summary>
        /// Offset Entities: copies of lines, arcs and circles a distance to one side (the side <paramref name="toward"/>
        /// is on), joined again where they met (lines meet where their copies cross), lines parallel to the ones they
        /// came from and arcs on the same centres.
        /// </summary>
        public List<SketchEntity> Offset(List<SketchEntity> picked, float distance, Vector2 toward) {
            var made = new List<SketchEntity>();
            var curves = picked.Where(e => (e.kind == EntityKind.Line || e.kind == EntityKind.Arc || e.kind == EntityKind.Circle)).ToList();
            // Circles on their own: bigger or smaller, whichever side the mouse is.
            foreach (var c in curves.Where(e => e.kind == EntityKind.Circle)) {
                var inside = Vector2.Distance(toward, P(c.a)) < c.radius;
                var r = c.radius + (inside ? -distance : distance);
                if (r <= 1e-3f) continue;
                var copy = Add(EntityKind.Circle, c.a, radius: r);
                copy.construction = c.construction;
                made.Add(copy);
            }
            var chain = OrderChain(curves.Where(e => e.kind != EntityKind.Circle).ToList(), out var closed);
            if (chain.Count == 0) return made;

            // Which side: left of the way the chain runs, or right, from the piece nearest the mouse; a closed chain
            // goes out when the mouse is outside it and in when inside.
            float side;
            if (closed) {
                var outline = new List<Vector2>();
                foreach (var step in chain) {
                    var piece = Polyline(step.e, out _);
                    if (step.reversed) piece.Reverse();
                    outline.AddRange(piece);
                }
                var area = 0f;
                for (var i = 0; i < outline.Count; i++) area += Cross(outline[i], outline[(i + 1) % outline.Count]);
                var inside = Geometry.MeshSlicer.Contains(outline, toward);
                // Counter-clockwise (area > 0): the inside is on the left.
                side = (area > 0f) == inside ? 1f : -1f;
            }
            else {
                var nearest = chain.OrderBy(step => DistanceToCurve(step.e, toward)).First();
                var (from, to) = Ends(nearest);
                var tangent = nearest.e.kind == EntityKind.Line ? P(to) - P(from) : Tangent(nearest, toward);
                side = Cross(tangent, toward - P(from)) > 0f ? 1f : -1f;
            }
            var d = distance * side;

            // An arc offset to its inside by its radius or more shrinks to nothing: it's left out and its neighbours
            // meet in a sharp corner (as SolidWorks does).
            bool Collapses(Step step) {
                if (step.e.kind != EntityKind.Arc) return false;
                var r = step.e.radius + (step.reversed ? d : -d);
                return r <= 1e-3f;
            }
            var kept = chain.Where(step => !Collapses(step)).ToList();
            if (kept.Count == 0) return made;
            chain = kept;

            // Each piece's offset ends; then the joins.
            var starts = new Vector2[chain.Count];
            var ends = new Vector2[chain.Count];
            for (var i = 0; i < chain.Count; i++) {
                var (from, to) = Ends(chain[i]);
                starts[i] = OffsetPoint(chain[i], P(from), d);
                ends[i] = OffsetPoint(chain[i], P(to), d);
            }
            var joins = new int[chain.Count + 1];
            var count = closed ? chain.Count : chain.Count + 1;
            for (var j = 0; j < count; j++) {
                Vector2 at;
                if (!closed && j == 0) at = starts[0];
                else if (!closed && j == chain.Count) at = ends[chain.Count - 1];
                else {
                    var before = chain[(j - 1 + chain.Count) % chain.Count];
                    var after = chain[j % chain.Count];
                    var a = ends[(j - 1 + chain.Count) % chain.Count];
                    var b = starts[j % chain.Count];
                    at = (a + b) / 2f;
                    if (before.e.kind == EntityKind.Line && after.e.kind == EntityKind.Line) {
                        var (fa, ta) = Ends(before);
                        var (fb, tb) = Ends(after);
                        if (LineCross(a, a + (P(ta) - P(fa)), b, b + (P(tb) - P(fb)), out var cross)) at = cross;
                    }
                }
                joins[j] = AddPoint(at).id;
            }
            if (closed) joins[chain.Count] = joins[0];
            for (var i = 0; i < chain.Count; i++) {
                var step = chain[i];
                var start = joins[i];
                var end = joins[i + 1];
                SketchEntity copy;
                if (step.e.kind == EntityKind.Line) {
                    copy = Add(EntityKind.Line, start, end);
                    Relate(RelationKind.Parallel, step.e.id, copy.id);
                }
                else {
                    // Its own centre point, kept concentric by a relation (a shared point would let the solver move
                    // the original arc to suit the copy).
                    var center = AddPoint(P(step.e.a)).id;
                    var r = Vector2.Distance(P(start), P(center));
                    // Arcs run counter-clockwise from their start: a reversed step swaps the ends back.
                    copy = step.reversed ? Add(EntityKind.Arc, center, end, start, radius: r) : Add(EntityKind.Arc, center, start, end, radius: r);
                    Relate(RelationKind.Concentric, step.e.id, copy.id);
                }
                copy.construction = step.e.construction;
                made.Add(copy);
            }
            return made;
        }

        private struct Step {
            public SketchEntity e;
            public bool reversed;   // walked from its second end to its first
        }

        private (int from, int to) Ends(Step step) {
            int a = step.e.kind == EntityKind.Line ? step.e.a : step.e.b;
            int b = step.e.kind == EntityKind.Line ? step.e.b : step.e.c;
            return step.reversed ? (b, a) : (a, b);
        }

        private Vector2 Tangent(Step step, Vector2 near) {
            var c = P(step.e.a);
            var radial = (near - c).normalized;
            var ccw = new Vector2(-radial.y, radial.x);
            return step.reversed ? -ccw : ccw;
        }

        // A point on a piece moved square to it by d (to its left as walked).
        private Vector2 OffsetPoint(Step step, Vector2 point, float d) {
            if (step.e.kind == EntityKind.Line) {
                var (from, to) = Ends(step);
                var dir = (P(to) - P(from)).normalized;
                return point + new Vector2(-dir.y, dir.x) * d;
            }
            // An arc walked counter-clockwise has its centre on the left.
            var c = P(step.e.a);
            var outward = (point - c).normalized;
            return point + outward * (step.reversed ? d : -d);
        }

        private float DistanceToCurve(SketchEntity e, Vector2 p) {
            var line = Polyline(e, out var closed);
            var best = float.MaxValue;
            for (var i = 0; i < line.Count - (closed ? 0 : 1); i++)
                best = Mathf.Min(best, DistanceToSegment(p, line[i], line[(i + 1) % line.Count]));
            return best;
        }

        // The pieces in order end to end (each with the way it's walked); closed when the last meets the first.
        private List<Step> OrderChain(List<SketchEntity> pieces, out bool closed) {
            closed = false;
            var result = new List<Step>();
            if (pieces.Count == 0) return result;
            int EndA(SketchEntity e) => e.kind == EntityKind.Line ? e.a : e.b;
            int EndB(SketchEntity e) => e.kind == EntityKind.Line ? e.b : e.c;
            var left = new List<SketchEntity>(pieces);
            // Start at a piece with a free end, if there is one.
            SketchEntity first = null;
            var firstReversed = false;
            foreach (var e in left) {
                var aShared = left.Any(o => o != e && (EndA(o) == EndA(e) || EndB(o) == EndA(e)));
                var bShared = left.Any(o => o != e && (EndA(o) == EndB(e) || EndB(o) == EndB(e)));
                if (!aShared) { first = e; firstReversed = false; break; }
                if (!bShared) { first = e; firstReversed = true; break; }
            }
            first ??= left[0];
            result.Add(new Step { e = first, reversed = firstReversed });
            left.Remove(first);
            while (left.Count > 0) {
                var (_, end) = Ends(result[^1]);
                var next = left.FirstOrDefault(o => EndA(o) == end || EndB(o) == end);
                if (next == null) break;
                result.Add(new Step { e = next, reversed = EndB(next) == end });
                left.Remove(next);
            }
            var (start, _) = Ends(result[0]);
            var (_, last) = Ends(result[^1]);
            closed = result.Count > 1 && start == last;
            return result;
        }

        private static bool LineCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point) {
            point = default;
            var r = b - a;
            var s = d - c;
            var den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-9f) return false;
            point = a + r * (((c.x - a.x) * s.y - (c.y - a.y) * s.x) / den);
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        // ------------------------------------------------------------------ copying (mirror, patterns, move/copy)

        /// <summary>
        /// Copies entities (and the points they use) through a map. Points <paramref name="keep"/> says stay where
        /// they are are shared (a mirror line's own points). Relations among the copies come along (horizontal and
        /// vertical ones only when the map keeps directions). Returns the copies and old point id -> new.
        /// </summary>
        public List<SketchEntity> CopyEntities(IList<SketchEntity> source, Func<Vector2, Vector2> map, bool mirrored, bool keepsAxes,
                                               out Dictionary<int, int> pointMap, Func<int, bool> keep = null) {
            var mapped = new Dictionary<int, int>();
            var made = new List<SketchEntity>();
            var entityMap = new Dictionary<int, int>();
            int Copy(int id) {
                if (id == 0) return 0;
                if (mapped.TryGetValue(id, out var n)) return n;
                n = keep != null && keep(id) ? id : AddPoint(map(P(id))).id;
                mapped[id] = n;
                return n;
            }
            foreach (var e in source) {
                if (e.projected) continue;
                var c = new SketchEntity { id = nextId++, kind = e.kind, construction = e.construction };
                c.a = Copy(e.a);
                c.b = e.kind == EntityKind.Circle || e.kind == EntityKind.Point ? 0 : Copy(e.b);
                c.c = e.kind == EntityKind.Arc ? Copy(e.c) : e.kind == EntityKind.Rectangle ? Copy(e.c) : 0;
                c.d = e.kind == EntityKind.Rectangle ? Copy(e.d) : 0;
                if (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc)
                    c.radius = Vector2.Distance(map(P(e.a)), map(P(e.a) + Vector2.right * e.radius));
                if (mirrored && e.kind == EntityKind.Arc) (c.b, c.c) = (c.c, c.b);
                entities.Add(c);
                made.Add(c);
                entityMap[e.id] = c.id;
            }
            foreach (var r in relations.ToList()) {
                if (r.kind == RelationKind.Fix || r.kind == RelationKind.Symmetric) continue;
                if (!keepsAxes && (r.kind == RelationKind.Horizontal || r.kind == RelationKind.Vertical)) continue;
                if (r.e1 != 0 && !entityMap.ContainsKey(r.e1) || r.e2 != 0 && !entityMap.ContainsKey(r.e2)) continue;
                if (r.p1 != 0 && !mapped.ContainsKey(r.p1) || r.p2 != 0 && !mapped.ContainsKey(r.p2)) continue;
                if (r.e1 == 0 && r.e2 == 0 && r.p1 == 0) continue;
                relations.Add(new SketchRelation {
                    id = nextId++, kind = r.kind,
                    e1 = r.e1 != 0 ? entityMap[r.e1] : 0, e2 = r.e2 != 0 ? entityMap[r.e2] : 0,
                    p1 = r.p1 != 0 ? mapped[r.p1] : 0, p2 = r.p2 != 0 ? mapped[r.p2] : 0,
                });
            }
            pointMap = mapped;
            return made;
        }

        /// <summary>Mirror Entities about a line (copies, symmetric to the originals; points on the line shared).</summary>
        public List<SketchEntity> Mirror(IList<SketchEntity> source, SketchEntity axis) {
            Vector2 a = P(axis.a), b = P(axis.b);
            var d = (b - a).normalized;
            Vector2 Reflect(Vector2 p) {
                var along = a + d * Vector2.Dot(p - a, d);
                return 2f * along - p;
            }
            var keepAxes = Mathf.Abs(d.x) < 1e-4f || Mathf.Abs(d.y) < 1e-4f;
            var made = CopyEntities(source.Where(e => e != axis).ToList(), Reflect, true, keepAxes, out var map,
                id => DistanceToLine(P(id), a, b) < 1e-4f);
            foreach (var (from, to) in map)
                if (from != to && Point(from) != null) Relate(RelationKind.Symmetric, axis.id, p1: from, p2: to);
            return made;
        }

        /// <summary>Linear sketch pattern: copies along one or two directions.</summary>
        public List<SketchEntity> LinearPattern(IList<SketchEntity> source, Vector2 direction1, int count1, float spacing1, Vector2 direction2, int count2, float spacing2) {
            var made = new List<SketchEntity>();
            direction1 = direction1.normalized;
            direction2 = direction2.normalized;
            for (var i = 0; i < Mathf.Max(1, count1); i++)
                for (var j = 0; j < Mathf.Max(1, count2); j++) {
                    if (i == 0 && j == 0) continue;
                    var shift = direction1 * spacing1 * i + direction2 * spacing2 * j;
                    made.AddRange(CopyEntities(source, p => p + shift, false, true, out _));
                }
            return made;
        }

        /// <summary>Circular sketch pattern: copies round a centre, spread over an angle (360: all the way round).</summary>
        public List<SketchEntity> CircularPattern(IList<SketchEntity> source, Vector2 center, int count, float totalDegrees) {
            var made = new List<SketchEntity>();
            count = Mathf.Max(2, count);
            var full = Mathf.Abs(Mathf.Abs(totalDegrees) - 360f) < 1e-3f;
            var step = totalDegrees / (full ? count : count - 1);
            for (var i = 1; i < count; i++) {
                var turn = Quaternion.Euler(0f, 0f, step * i);
                made.AddRange(CopyEntities(source, p => center + (Vector2)(turn * (p - center)), false, false, out _));
            }
            return made;
        }

        /// <summary>Moves (or rotates, or scales) entities' points through a map; their circles' sizes follow.</summary>
        public void TransformEntities(IList<SketchEntity> source, Func<Vector2, Vector2> map) {
            var ids = new HashSet<int>(source.Where(e => !e.projected).SelectMany(PointsOf));
            ids.Remove(origin);
            foreach (var e in source.Where(e => !e.projected && (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc)))
                e.radius = Vector2.Distance(map(P(e.a)), map(P(e.a) + Vector2.right * e.radius));
            foreach (var id in ids) {
                var point = Point(id);
                if (point != null) point.p = map(point.p);
            }
        }

        // ------------------------------------------------------------------ extend

        /// <summary>Extend Entities: the line's end nearest the click runs on to the next curve it meets.</summary>
        public bool Extend(SketchEntity line, Vector2 near, out string why) {
            why = null;
            if (line.kind != EntityKind.Line) {
                why = "Click a line to extend it";
                return false;
            }
            var endIsB = Vector2.Distance(P(line.b), near) < Vector2.Distance(P(line.a), near);
            var end = endIsB ? line.b : line.a;
            var other = endIsB ? line.a : line.b;
            var from = P(end);
            var dir = (from - P(other)).normalized;
            var best = float.MaxValue;
            foreach (var e in entities) {
                if (e == line) continue;
                var poly = Polyline(e, out var closed);
                for (var i = 0; i < poly.Count - (closed ? 0 : 1); i++) {
                    var a = poly[i];
                    var b = poly[(i + 1) % poly.Count];
                    var s = b - a;
                    var den = dir.x * s.y - dir.y * s.x;
                    if (Mathf.Abs(den) < 1e-9f) continue;
                    var t = ((a.x - from.x) * s.y - (a.y - from.y) * s.x) / den;
                    var u = ((a.x - from.x) * dir.y - (a.y - from.y) * dir.x) / den;
                    if (t > 1e-3f && u >= -1e-5f && u <= 1 + 1e-5f && t < best) best = t;
                }
            }
            if (best == float.MaxValue) {
                why = "Nothing to extend to in that direction";
                return false;
            }
            var target = from + dir * best;
            // An end other curves share gets a new point (they stay); a free end just moves.
            var shared = entities.Any(e => e != line && PointsOf(e).Contains(end));
            if (shared || end == origin) {
                var moved = AddPoint(target).id;
                if (endIsB) line.b = moved;
                else line.a = moved;
            }
            else Point(end).p = target;
            return true;
        }
    }
}
