using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: a 2D sketch (inches) for cutting polycarbonate, like a CAD sketch: points shared by lines, rectangles,
    // circles and arcs (moving a point moves everything on it), and dimensions that set lengths and sizes. The
    // closed areas the curves make are its regions (Regions); the chosen ones become the piece.
    [Serializable]
    public class SketchPoint {
        public int id;
        public Vector2 p;
    }

    public enum EntityKind { Line, Rectangle, Circle, Arc }

    [Serializable]
    public class SketchEntity {
        public int id;
        public EntityKind kind;
        public int a, b, c, d;     // points: line a-b; rectangle corners a,b,c,d (a-b along x); circle centre a; arc centre a, start b, end c
        public float radius;       // circle and arc
        public bool construction;  // drawn dashed, not part of any region
    }

    public enum DimensionKind { Length, Width, Height, Diameter, Distance }

    // V2: sketch relations, as in a CAD sketch (SketchSolver keeps them true).
    public enum RelationKind { Horizontal, Vertical, Parallel, Perpendicular, Equal, Coincident, Tangent, Concentric, Midpoint, Fix }

    [Serializable]
    public class SketchRelation {
        public int id;
        public RelationKind kind;
        public int e1, e2;   // entities it's about (0: none)
        public int p1, p2;   // points it's about (0: none)
    }

    // V2: a hole placed by hand on a circle or an arc (half a circle on the edge of a piece still takes a screw).
    [Serializable]
    public class ManualHole {
        public Vector2 center;
        public float radius;
    }

    [Serializable]
    public class SketchDimension {
        public int id;
        public DimensionKind kind;
        public int entity;          // Length (line), Width/Height (rectangle), Diameter (circle/arc)
        public int from, to;        // Distance: two points
        public float offset = 0.35f;
        public float value;         // V2: the size it holds (0 in older sketches: what it measures)
    }

    [Serializable]
    public class SketchData {
        public List<SketchPoint> points = new();
        public List<SketchEntity> entities = new();
        public List<SketchDimension> dimensions = new();
        public List<Vector2> chosen = new();   // a point inside each chosen region
        public List<Vector2> holes = new();    // older sketches: a point inside each shape marked as a hole
        public List<SketchRelation> relations = new();
        public List<ManualHole> manualHoles = new();
        public List<Vector2> notHoles = new();  // holes found in the piece that you said aren't screw holes
        public int nextId = 1;

        public SketchPoint Point(int id) => points.FirstOrDefault(p => p.id == id);
        public Vector2 P(int id) => Point(id)?.p ?? Vector2.zero;
        public SketchEntity Entity(int id) => entities.FirstOrDefault(e => e.id == id);

        public SketchPoint AddPoint(Vector2 p) {
            var point = new SketchPoint { id = nextId++, p = p };
            points.Add(point);
            return point;
        }

        /// <summary>V2: the point at this place if there is one (an end already there), else a new one.</summary>
        public SketchPoint PointAtOrNew(Vector2 p) {
            var centres = new HashSet<int>(entities.Where(e => e.kind == EntityKind.Circle || e.kind == EntityKind.Arc).Select(e => e.a));
            foreach (var existing in points)
                if (!centres.Contains(existing.id) && Vector2.Distance(existing.p, p) < 1e-4f)
                    return existing;
            return AddPoint(p);
        }

        public SketchEntity Add(EntityKind kind, int a, int b = 0, int c = 0, int d = 0, float radius = 0f) {
            var entity = new SketchEntity { id = nextId++, kind = kind, a = a, b = b, c = c, d = d, radius = radius };
            entities.Add(entity);
            return entity;
        }

        public IEnumerable<int> PointsOf(SketchEntity e) => e.kind switch {
            EntityKind.Line => new[] { e.a, e.b },
            EntityKind.Rectangle => new[] { e.a, e.b, e.c, e.d },
            EntityKind.Circle => new[] { e.a },
            _ => new[] { e.a, e.b, e.c },
        };

        /// <summary>Removes an entity, its dimensions and relations, and points nothing else uses.</summary>
        public void Remove(SketchEntity entity) {
            entities.Remove(entity);
            dimensions.RemoveAll(d => d.entity == entity.id);
            relations ??= new List<SketchRelation>();
            relations.RemoveAll(r => r.e1 == entity.id || r.e2 == entity.id);
            Tidy();
        }

        /// <summary>Drops points nothing uses, and dimensions and relations on them.</summary>
        public void Tidy() {
            var used = new HashSet<int>(entities.SelectMany(PointsOf));
            var gone = points.Where(p => !used.Contains(p.id)).Select(p => p.id).ToList();
            points.RemoveAll(p => gone.Contains(p.id));
            dimensions.RemoveAll(d => d.kind == DimensionKind.Distance && (gone.Contains(d.from) || gone.Contains(d.to)));
            relations ??= new List<SketchRelation>();
            relations.RemoveAll(r => gone.Contains(r.p1) || gone.Contains(r.p2));
        }

        public SketchRelation Relate(RelationKind kind, int e1 = 0, int e2 = 0, int p1 = 0, int p2 = 0) {
            relations ??= new List<SketchRelation>();
            var relation = new SketchRelation { id = nextId++, kind = kind, e1 = e1, e2 = e2, p1 = p1, p2 = p2 };
            relations.Add(relation);
            return relation;
        }

        /// <summary>Older sketches: dimensions take the size they show, and the lists exist.</summary>
        public void Upgrade() {
            relations ??= new List<SketchRelation>();
            manualHoles ??= new List<ManualHole>();
            notHoles ??= new List<Vector2>();
            foreach (var d in dimensions)
                if (d.value <= 0f) d.value = Measure(d);
            // V2: rectangles are four lines (with horizontal and vertical relations), as in SolidWorks, so each
            // side can be related, dimensioned and trimmed on its own.
            foreach (var rect in entities.Where(e => e.kind == EntityKind.Rectangle).ToList())
                Explode(rect);
        }

        // ------------------------------------------------------------------ trimming (V2)

        /// <summary>A rectangle as four lines (so one side can be trimmed or related).</summary>
        public List<SketchEntity> Explode(SketchEntity rect) {
            var lines = new List<SketchEntity>();
            if (rect.kind != EntityKind.Rectangle) return lines;
            var corners = new[] { rect.a, rect.b, rect.c, rect.d };
            entities.Remove(rect);
            for (var i = 0; i < 4; i++) {
                var line = Add(EntityKind.Line, corners[i], corners[(i + 1) % 4]);
                line.construction = rect.construction;
                lines.Add(line);
                Relate(i % 2 == 0 ? RelationKind.Horizontal : RelationKind.Vertical, line.id);
            }
            // Its width and height dimensions go on the sides they were drawn on.
            foreach (var d in dimensions.Where(d => d.entity == rect.id)) {
                var side = d.kind == DimensionKind.Width ? (d.from == 1 ? 2 : 0) : (d.from == 1 ? 3 : 1);
                d.kind = DimensionKind.Length;
                d.entity = lines[side].id;
                d.from = 0;
            }
            foreach (var r in relations.Where(r => r.e1 == rect.id || r.e2 == rect.id).ToList()) relations.Remove(r);
            return lines;
        }

        /// <summary>
        /// Trims the piece of an entity between the crossings (with other curves) on either side of
        /// <paramref name="at"/>: a line keeps what's left at its ends, a circle becomes an arc, an arc is cut
        /// short or split in two. False when nothing crosses it (then the whole curve goes).
        /// </summary>
        public bool Trim(SketchEntity e, Vector2 at) {
            if (e.kind == EntityKind.Rectangle) {
                var lines = Explode(e);
                var nearest = lines.OrderBy(l => DistanceToSegment(at, P(l.a), P(l.b))).First();
                return Trim(nearest, at);
            }
            if (e.kind == EntityKind.Line) {
                Vector2 a = P(e.a), b = P(e.b);
                var d = b - a;
                if (d.sqrMagnitude < 1e-10f) return false;
                var ts = Crossings(e).Select(q => Vector2.Dot(q - a, d) / d.sqrMagnitude).Where(t => t > 1e-4f && t < 1 - 1e-4f).OrderBy(t => t).ToList();
                var tAt = Mathf.Clamp01(Vector2.Dot(at - a, d) / d.sqrMagnitude);
                var before = ts.Where(t => t < tAt).DefaultIfEmpty(0f).Max();
                var after = ts.Where(t => t > tAt).DefaultIfEmpty(1f).Min();
                if (before <= 0f && after >= 1f) {
                    Remove(e);
                    return false;
                }
                var construction = e.construction;
                var (ea, eb) = (e.a, e.b);
                entities.Remove(e);
                dimensions.RemoveAll(x => x.entity == e.id);
                relations?.RemoveAll(r => r.e1 == e.id || r.e2 == e.id);
                if (before > 0f) Add(EntityKind.Line, ea, PointAtOrNew(a + d * before).id).construction = construction;
                if (after < 1f) Add(EntityKind.Line, PointAtOrNew(a + d * after).id, eb).construction = construction;
                Tidy();
                return true;
            }
            // Circles and arcs: by angle, counter-clockwise from the arc's start.
            var c = P(e.a);
            float Angle(Vector2 q) => Mathf.Atan2(q.y - c.y, q.x - c.x);
            float From(float angle, float origin) {
                var x = angle - origin;
                while (x < 0f) x += Mathf.PI * 2f;
                while (x >= Mathf.PI * 2f) x -= Mathf.PI * 2f;
                return x;
            }
            var origin2 = e.kind == EntityKind.Arc ? Angle(P(e.b)) : 0f;
            var sweep = e.kind == EntityKind.Arc ? From(Angle(P(e.c)), origin2) : Mathf.PI * 2f;
            if (sweep <= 0f) sweep = Mathf.PI * 2f;
            var cuts = Crossings(e).Select(q => From(Angle(q), origin2)).Where(x => x > 1e-3f && x < sweep - 1e-3f).OrderBy(x => x).ToList();
            var atAngle = From(Angle(at), origin2);
            if (e.kind == EntityKind.Circle) {
                if (cuts.Count < 2) {
                    Remove(e);
                    return false;
                }
                // Keep the circle from the crossing after the click round to the one before it.
                var lo = cuts.Where(x => x < atAngle).DefaultIfEmpty(cuts.Last() - Mathf.PI * 2f).Max();
                var hi = cuts.Where(x => x > atAngle).DefaultIfEmpty(cuts.First() + Mathf.PI * 2f).Min();
                var radius = e.radius;
                var construction = e.construction;
                var center = e.a;
                entities.Remove(e);
                dimensions.RemoveAll(x => x.entity == e.id);
                relations?.RemoveAll(r => r.e1 == e.id || r.e2 == e.id);
                var start = PointAtOrNew(c + new Vector2(Mathf.Cos(hi), Mathf.Sin(hi)) * radius);
                var end = PointAtOrNew(c + new Vector2(Mathf.Cos(lo), Mathf.Sin(lo)) * radius);
                Add(EntityKind.Arc, center, start.id, end.id, radius: radius).construction = construction;
                Tidy();
                return true;
            }
            var beforeA = cuts.Where(x => x < atAngle).DefaultIfEmpty(0f).Max();
            var afterA = cuts.Where(x => x > atAngle).DefaultIfEmpty(sweep).Min();
            if (beforeA <= 0f && afterA >= sweep) {
                Remove(e);
                return false;
            }
            var r2 = e.radius;
            var construction2 = e.construction;
            var (ca, cb, cc) = (e.a, e.b, e.c);
            entities.Remove(e);
            dimensions.RemoveAll(x => x.entity == e.id);
            relations?.RemoveAll(r => r.e1 == e.id || r.e2 == e.id);
            Vector2 OnArc(float x) => c + new Vector2(Mathf.Cos(origin2 + x), Mathf.Sin(origin2 + x)) * r2;
            if (beforeA > 0f) Add(EntityKind.Arc, ca, cb, PointAtOrNew(OnArc(beforeA)).id, radius: r2).construction = construction2;
            if (afterA < sweep) Add(EntityKind.Arc, ca, PointAtOrNew(OnArc(afterA)).id, cc, radius: r2).construction = construction2;
            Tidy();
            return true;
        }

        // Where other curves cross this one.
        public List<Vector2> Crossings(SketchEntity e) {
            var result = new List<Vector2>();
            var mine = Polyline(e, out var closed);
            foreach (var other in entities) {
                if (other == e) continue;
                var theirs = Polyline(other, out var otherClosed);
                for (var i = 0; i < mine.Count - (closed ? 0 : 1); i++)
                    for (var j = 0; j < theirs.Count - (otherClosed ? 0 : 1); j++)
                        if (Intersect(mine[i], mine[(i + 1) % mine.Count], theirs[j], theirs[(j + 1) % theirs.Count], out var q))
                            result.Add(q);
            }
            return result;
        }

        private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point) {
            point = default;
            var r = b - a;
            var s = d - c;
            var denominator = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denominator) < 1e-10f) return false;
            var t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / denominator;
            var u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / denominator;
            if (t < -1e-5f || t > 1 + 1e-5f || u < -1e-5f || u > 1 + 1e-5f) return false;
            point = a + r * t;
            return true;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) {
            var ab = b - a;
            var t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Points at the end of only one curve: where an outline is left open.</summary>
        public List<Vector2> OpenEnds() {
            // V2: counted by place, not by point: two ends at the same spot (a circle trimmed into arcs, pieces
            // drawn to meet) are joined.
            var count = new Dictionary<Vector2Int, (int n, Vector2 p, SketchEntity owner)>();
            var curves = entities.Where(x => !x.construction).ToList();
            foreach (var e in curves.Where(x => x.kind == EntityKind.Line || x.kind == EntityKind.Arc)) {
                var ends = e.kind == EntityKind.Line ? new[] { e.a, e.b } : new[] { e.b, e.c };
                foreach (var id in ends) {
                    var p = P(id);
                    var key = new Vector2Int(Mathf.RoundToInt(p.x * 2000f), Mathf.RoundToInt(p.y * 2000f));
                    count[key] = count.TryGetValue(key, out var v) ? (v.n + 1, p, e) : (1, p, e);
                }
            }
            // An end on another curve (a line trimmed back to a side: a T) is joined there too.
            bool OnAnother(Vector2 p, SketchEntity owner) {
                foreach (var other in curves) {
                    if (other == owner) continue;
                    var line = Polyline(other, out var closed);
                    for (var i = 0; i < line.Count - (closed ? 0 : 1); i++)
                        if (DistanceToSegment(p, line[i], line[(i + 1) % line.Count]) < 4e-3f) return true;
                }
                return false;
            }
            return count.Values.Where(v => v.n == 1 && !OnAnother(v.p, v.owner)).Select(v => v.p).ToList();
        }

        /// <summary>Merges point <paramref name="from"/> into <paramref name="into"/> (snapping an end onto another).</summary>
        public void Merge(int from, int into) {
            if (from == into) return;
            foreach (var e in entities) {
                if (e.a == from) e.a = into;
                if (e.b == from) e.b = into;
                if (e.c == from) e.c = into;
                if (e.d == from) e.d = into;
            }
            foreach (var d in dimensions) {
                if (d.from == from) d.from = into;
                if (d.to == from) d.to = into;
            }
            foreach (var r in relations ?? new List<SketchRelation>()) {
                if (r.p1 == from) r.p1 = into;
                if (r.p2 == from) r.p2 = into;
            }
            points.RemoveAll(p => p.id == from);
        }

        // ------------------------------------------------------------------ editing sizes (what dimensions set)

        public float Measure(SketchDimension dimension) {
            var e = Entity(dimension.entity);
            return dimension.kind switch {
                DimensionKind.Length => e != null ? Vector2.Distance(P(e.a), P(e.b)) : 0f,
                DimensionKind.Width => e != null ? Vector2.Distance(P(e.a), P(e.b)) : 0f,
                DimensionKind.Height => e != null ? Vector2.Distance(P(e.b), P(e.c)) : 0f,
                DimensionKind.Diameter => e != null ? e.radius * 2f : 0f,
                _ => Vector2.Distance(P(dimension.from), P(dimension.to)),
            };
        }

        /// <summary>Sets what a dimension measures: the far end (or side) moves, the near one stays.</summary>
        public void Apply(SketchDimension dimension, float value) {
            value = Mathf.Max(value, 0.01f);
            var e = Entity(dimension.entity);
            switch (dimension.kind) {
                case DimensionKind.Length when e != null: {
                    var a = P(e.a);
                    var dir = (P(e.b) - a).normalized;
                    if (dir.sqrMagnitude < 0.5f) dir = Vector2.right;
                    Point(e.b).p = a + dir * value;
                    break;
                }
                case DimensionKind.Width when e != null: {
                    var dir = (P(e.b) - P(e.a)).normalized;
                    var change = dir * (value - Vector2.Distance(P(e.a), P(e.b)));
                    Point(e.b).p += change;
                    Point(e.c).p += change;
                    break;
                }
                case DimensionKind.Height when e != null: {
                    var dir = (P(e.c) - P(e.b)).normalized;
                    var change = dir * (value - Vector2.Distance(P(e.b), P(e.c)));
                    Point(e.c).p += change;
                    Point(e.d).p += change;
                    break;
                }
                case DimensionKind.Diameter when e != null:
                    e.radius = value / 2f;
                    if (e.kind == EntityKind.Arc) {
                        var center = P(e.a);
                        Point(e.b).p = center + (P(e.b) - center).normalized * e.radius;
                        Point(e.c).p = center + (P(e.c) - center).normalized * e.radius;
                    }
                    break;
                case DimensionKind.Distance: {
                    var from = P(dimension.from);
                    var to = Point(dimension.to);
                    if (to == null) break;
                    var dir = (to.p - from).normalized;
                    if (dir.sqrMagnitude < 0.5f) dir = Vector2.right;
                    to.p = from + dir * value;
                    break;
                }
            }
        }

        /// <summary>A rectangle's corners follow its first corner and the opposite one (axis-aligned).</summary>
        public void SetRectangle(SketchEntity rect, Vector2 corner, Vector2 opposite) {
            Point(rect.a).p = corner;
            Point(rect.b).p = new Vector2(opposite.x, corner.y);
            Point(rect.c).p = opposite;
            Point(rect.d).p = new Vector2(corner.x, opposite.y);
        }

        // ------------------------------------------------------------------ curves as polylines

        public const int CircleSegments = 72;

        /// <summary>The entity as line segments (circles and arcs as fine polylines).</summary>
        public List<Vector2> Polyline(SketchEntity e, out bool closed) {
            closed = false;
            var list = new List<Vector2>();
            switch (e.kind) {
                case EntityKind.Line:
                    list.Add(P(e.a));
                    list.Add(P(e.b));
                    break;
                case EntityKind.Rectangle:
                    list.Add(P(e.a));
                    list.Add(P(e.b));
                    list.Add(P(e.c));
                    list.Add(P(e.d));
                    closed = true;
                    break;
                case EntityKind.Circle: {
                    var c = P(e.a);
                    var segments = Mathf.Clamp(Mathf.RoundToInt(e.radius * 120f), 24, CircleSegments * 2);
                    for (var i = 0; i < segments; i++) {
                        var angle = i * Mathf.PI * 2f / segments;
                        list.Add(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * e.radius);
                    }
                    closed = true;
                    break;
                }
                case EntityKind.Arc: {
                    var c = P(e.a);
                    var start = Mathf.Atan2(P(e.b).y - c.y, P(e.b).x - c.x);
                    var end = Mathf.Atan2(P(e.c).y - c.y, P(e.c).x - c.x);
                    var sweep = end - start;
                    while (sweep <= 0f) sweep += Mathf.PI * 2f;
                    var segments = Mathf.Max(4, Mathf.RoundToInt(sweep / (Mathf.PI * 2f) * Mathf.Clamp(e.radius * 120f, 24, CircleSegments * 2)));
                    list.Add(P(e.b));
                    for (var i = 1; i < segments; i++) {
                        var angle = start + sweep * i / segments;
                        list.Add(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * e.radius);
                    }
                    list.Add(P(e.c));
                    break;
                }
            }
            return list;
        }

        public SketchData Copy() => JsonUtility.FromJson<SketchData>(JsonUtility.ToJson(this));
    }
}
