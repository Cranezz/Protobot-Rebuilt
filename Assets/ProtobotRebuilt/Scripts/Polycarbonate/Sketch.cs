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

    [Serializable]
    public class SketchDimension {
        public int id;
        public DimensionKind kind;
        public int entity;          // Length (line), Width/Height (rectangle), Diameter (circle/arc)
        public int from, to;        // Distance: two points
        public float offset = 0.35f;
    }

    [Serializable]
    public class SketchData {
        public List<SketchPoint> points = new();
        public List<SketchEntity> entities = new();
        public List<SketchDimension> dimensions = new();
        public List<Vector2> chosen = new();   // a point inside each chosen region
        public int nextId = 1;

        public SketchPoint Point(int id) => points.FirstOrDefault(p => p.id == id);
        public Vector2 P(int id) => Point(id)?.p ?? Vector2.zero;
        public SketchEntity Entity(int id) => entities.FirstOrDefault(e => e.id == id);

        public SketchPoint AddPoint(Vector2 p) {
            var point = new SketchPoint { id = nextId++, p = p };
            points.Add(point);
            return point;
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

        /// <summary>Removes an entity, its dimensions, and points nothing else uses.</summary>
        public void Remove(SketchEntity entity) {
            entities.Remove(entity);
            dimensions.RemoveAll(d => d.entity == entity.id);
            var used = new HashSet<int>(entities.SelectMany(PointsOf));
            var gone = points.Where(p => !used.Contains(p.id)).Select(p => p.id).ToList();
            points.RemoveAll(p => gone.Contains(p.id));
            dimensions.RemoveAll(d => d.kind == DimensionKind.Distance && (gone.Contains(d.from) || gone.Contains(d.to)));
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
