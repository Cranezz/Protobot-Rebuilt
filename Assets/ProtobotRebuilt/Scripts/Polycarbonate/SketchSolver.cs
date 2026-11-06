using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: what keeps a sketch's relations and dimensions true, like a CAD sketch solver. Every point's x and y and
    // every circle's and arc's radius is a variable; every relation (horizontal, parallel, tangent...), dimension
    // and built-in rule (a rectangle's sides stay square, an arc's ends stay on its circle) is an equation. After
    // a change, a Gauss-Newton solve with the smallest change finds the nearest sketch where they all hold, so the
    // things you didn't touch stay put (drag a rectangle's corner: only its neighbours follow). Points being
    // dragged and fixed points don't move. If they can't all hold, the sketch is left as it was.
    public static class SketchSolver {
        private const int MaxIterations = 30;
        private const float Tolerance = 1e-5f;

        private class Variables {
            public readonly List<(SketchPoint point, int axis)> coords = new();
            public readonly List<SketchEntity> radii = new();
            public int Count => coords.Count + radii.Count;

            public float Get(int i) => i < coords.Count ? coords[i].point.p[coords[i].axis] : radii[i - coords.Count].radius;

            public void Set(int i, float value) {
                if (i < coords.Count) {
                    var p = coords[i].point.p;
                    p[coords[i].axis] = value;
                    coords[i].point.p = p;
                }
                else radii[i - coords.Count].radius = Mathf.Max(0.001f, value);
            }
        }

        /// <summary>Solves the sketch with <paramref name="pinned"/> points held where they are. False (and the sketch
        /// unchanged) when the relations and dimensions can't all be met.</summary>
        public static bool Solve(SketchData sketch, IEnumerable<int> pinned = null) {
            var hold = new HashSet<int>(pinned ?? Enumerable.Empty<int>());
            foreach (var r in sketch.relations)
                if (r.kind == RelationKind.Fix) {
                    if (r.p1 != 0) hold.Add(r.p1);
                    var e = sketch.Entity(r.e1);
                    if (e != null) foreach (var id in sketch.PointsOf(e)) hold.Add(id);
                }

            var equations = Equations(sketch);
            if (equations.Count == 0) return true;

            var vars = new Variables();
            var used = new HashSet<int>();
            foreach (var e in sketch.entities) foreach (var id in sketch.PointsOf(e)) used.Add(id);
            foreach (var p in sketch.points)
                if (used.Contains(p.id) && !hold.Contains(p.id)) {
                    vars.coords.Add((p, 0));
                    vars.coords.Add((p, 1));
                }
            foreach (var e in sketch.entities)
                if ((e.kind == EntityKind.Circle || e.kind == EntityKind.Arc) && !sketch.relations.Any(r => r.kind == RelationKind.Fix && r.e1 == e.id))
                    vars.radii.Add(e);
            var n = vars.Count;
            var m = equations.Count;
            if (n == 0) return Residual(equations) < 1e-4f;

            var start = new float[n];
            for (var i = 0; i < n; i++) start[i] = vars.Get(i);

            var r0 = new float[m];
            var jacobian = new float[m, n];
            for (var iteration = 0; iteration < MaxIterations; iteration++) {
                for (var k = 0; k < m; k++) r0[k] = equations[k]();
                if (r0.Max(Mathf.Abs) < Tolerance) return true;
                // Numeric Jacobian.
                const float h = 1e-4f;
                for (var j = 0; j < n; j++) {
                    var keep = vars.Get(j);
                    vars.Set(j, keep + h);
                    for (var k = 0; k < m; k++) jacobian[k, j] = (equations[k]() - r0[k]) / h;
                    vars.Set(j, keep);
                }
                // The smallest step that fixes the residuals: dx = -J^T (J J^T + lambda I)^-1 r.
                var jjt = new double[m, m];
                for (var a = 0; a < m; a++)
                    for (var b = a; b < m; b++) {
                        double sum = 0;
                        for (var j = 0; j < n; j++) sum += jacobian[a, j] * jacobian[b, j];
                        jjt[a, b] = jjt[b, a] = sum;
                    }
                for (var a = 0; a < m; a++) jjt[a, a] += 1e-8;
                var y = SolveLinear(jjt, r0.Select(v => (double)v).ToArray());
                if (y == null) break;
                var maxStep = 0f;
                for (var j = 0; j < n; j++) {
                    double dx = 0;
                    for (var k = 0; k < m; k++) dx -= jacobian[k, j] * y[k];
                    vars.Set(j, vars.Get(j) + (float)dx);
                    maxStep = Mathf.Max(maxStep, Mathf.Abs((float)dx));
                }
                if (maxStep < 1e-7f) break;
            }
            if (Residual(equations) < 1e-3f) return true;
            for (var i = 0; i < n; i++) vars.Set(i, start[i]);
            return false;
        }

        /// <summary>
        /// V2: how defined the sketch is, as SolidWorks colours it: the points and circles that can still move or grow
        /// without breaking a relation or dimension (under-defined, blue) and whether it's fully defined. Worked out
        /// from the equations' Jacobian: what isn't fixed by them is in its null space.
        /// </summary>
        public static (HashSet<int> freePoints, HashSet<int> freeRadii) Analyze(SketchData sketch) {
            var freePoints = new HashSet<int>();
            var freeRadii = new HashSet<int>();
            var hold = new HashSet<int>();
            foreach (var r in sketch.relations)
                if (r.kind == RelationKind.Fix) {
                    if (r.p1 != 0) hold.Add(r.p1);
                    var e = sketch.Entity(r.e1);
                    if (e != null) foreach (var id in sketch.PointsOf(e)) hold.Add(id);
                }
            var vars = new Variables();
            var used = new HashSet<int>();
            foreach (var e in sketch.entities) foreach (var id in sketch.PointsOf(e)) used.Add(id);
            foreach (var p in sketch.points)
                if (used.Contains(p.id) && !hold.Contains(p.id)) {
                    vars.coords.Add((p, 0));
                    vars.coords.Add((p, 1));
                }
            foreach (var e in sketch.entities)
                if ((e.kind == EntityKind.Circle || e.kind == EntityKind.Arc) && !sketch.relations.Any(r => r.kind == RelationKind.Fix && r.e1 == e.id))
                    vars.radii.Add(e);
            var n = vars.Count;
            if (n == 0) return (freePoints, freeRadii);
            var equations = Equations(sketch);
            var m = equations.Count;
            var free = new bool[n];
            if (m == 0) {
                for (var j = 0; j < n; j++) free[j] = true;
            }
            else {
                // Numeric Jacobian, then row-reduced: columns without a pivot are free; a pivot column is free when its
                // row still depends on a free column.
                var J = new double[m, n];
                var r0 = new float[m];
                for (var k = 0; k < m; k++) r0[k] = equations[k]();
                const float h = 1e-4f;
                for (var j = 0; j < n; j++) {
                    var keep = vars.Get(j);
                    vars.Set(j, keep + h);
                    for (var k = 0; k < m; k++) J[k, j] = (equations[k]() - r0[k]) / h;
                    vars.Set(j, keep);
                }
                var pivotOfRow = new List<int>();
                var row = 0;
                for (var col = 0; col < n && row < m; col++) {
                    var best = row;
                    for (var k = row + 1; k < m; k++)
                        if (Math.Abs(J[k, col]) > Math.Abs(J[best, col])) best = k;
                    if (Math.Abs(J[best, col]) < 1e-6) continue;
                    if (best != row)
                        for (var c = 0; c < n; c++) (J[row, c], J[best, c]) = (J[best, c], J[row, c]);
                    var pivot = J[row, col];
                    for (var c = 0; c < n; c++) J[row, c] /= pivot;
                    for (var k = 0; k < m; k++) {
                        if (k == row || Math.Abs(J[k, col]) < 1e-12) continue;
                        var f = J[k, col];
                        for (var c = 0; c < n; c++) J[k, c] -= f * J[row, c];
                    }
                    pivotOfRow.Add(col);
                    row++;
                }
                var isPivot = new bool[n];
                foreach (var c in pivotOfRow) isPivot[c] = true;
                for (var j = 0; j < n; j++) free[j] = !isPivot[j];
                for (var r = 0; r < pivotOfRow.Count; r++)
                    for (var c = 0; c < n; c++)
                        if (!isPivot[c] && Math.Abs(J[r, c]) > 1e-5) {
                            free[pivotOfRow[r]] = true;
                            break;
                        }
            }
            for (var j = 0; j < n; j++) {
                if (!free[j]) continue;
                if (j < vars.coords.Count) freePoints.Add(vars.coords[j].point.id);
                else freeRadii.Add(vars.radii[j - vars.coords.Count].id);
            }
            return (freePoints, freeRadii);
        }

        private static float Residual(List<Func<float>> equations) => equations.Count == 0 ? 0f : equations.Max(e => Mathf.Abs(e()));

        private static double[] SolveLinear(double[,] a, double[] b) {
            var n = b.Length;
            var m = (double[,])a.Clone();
            var x = (double[])b.Clone();
            for (var col = 0; col < n; col++) {
                var pivot = col;
                for (var row = col + 1; row < n; row++)
                    if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;
                if (Math.Abs(m[pivot, col]) < 1e-14) continue;  // redundant: the damping keeps the rest solvable
                if (pivot != col) {
                    for (var k = 0; k < n; k++) (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                    (x[col], x[pivot]) = (x[pivot], x[col]);
                }
                for (var row = 0; row < n; row++) {
                    if (row == col) continue;
                    var f = m[row, col] / m[col, col];
                    if (f == 0) continue;
                    for (var k = col; k < n; k++) m[row, k] -= f * m[col, k];
                    x[row] -= f * x[col];
                }
            }
            for (var i = 0; i < n; i++) x[i] = Math.Abs(m[i, i]) < 1e-14 ? 0 : x[i] / m[i, i];
            return x;
        }

        // ------------------------------------------------------------------ the equations

        private static List<Func<float>> Equations(SketchData s) {
            var list = new List<Func<float>>();
            SketchPoint P(int id) => s.Point(id);

            // Built in: rectangles stay square to the sketch, arcs' ends stay on their circle.
            foreach (var e in s.entities) {
                if (e.kind == EntityKind.Rectangle) {
                    var (a, b, c, d) = (P(e.a), P(e.b), P(e.c), P(e.d));
                    if (a == null || b == null || c == null || d == null) continue;
                    list.Add(() => b.p.y - a.p.y);
                    list.Add(() => c.p.x - b.p.x);
                    list.Add(() => d.p.y - c.p.y);
                    list.Add(() => a.p.x - d.p.x);
                }
                else if (e.kind == EntityKind.Arc) {
                    var (c, start, end) = (P(e.a), P(e.b), P(e.c));
                    if (c == null || start == null || end == null) continue;
                    var arc = e;
                    list.Add(() => Vector2.Distance(start.p, c.p) - arc.radius);
                    list.Add(() => Vector2.Distance(end.p, c.p) - arc.radius);
                }
            }

            foreach (var r in s.relations) {
                var e1 = s.Entity(r.e1);
                var e2 = s.Entity(r.e2);
                var p1 = P(r.p1);
                var p2 = P(r.p2);
                switch (r.kind) {
                    case RelationKind.Horizontal:
                        if (Ends(s, e1, out var ha, out var hb)) list.Add(() => hb.p.y - ha.p.y);
                        else if (p1 != null && p2 != null) list.Add(() => p2.p.y - p1.p.y);
                        break;
                    case RelationKind.Vertical:
                        if (Ends(s, e1, out var va, out var vb)) list.Add(() => vb.p.x - va.p.x);
                        else if (p1 != null && p2 != null) list.Add(() => p2.p.x - p1.p.x);
                        break;
                    case RelationKind.Parallel:
                        if (Ends(s, e1, out var a1, out var b1) && Ends(s, e2, out var a2, out var b2))
                            list.Add(() => Cross((b1.p - a1.p).normalized, (b2.p - a2.p).normalized));
                        break;
                    case RelationKind.Perpendicular:
                        if (Ends(s, e1, out var c1, out var d1) && Ends(s, e2, out var c2, out var d2))
                            list.Add(() => Vector2.Dot((d1.p - c1.p).normalized, (d2.p - c2.p).normalized));
                        break;
                    case RelationKind.Equal:
                        if (IsRound(e1) && IsRound(e2)) {
                            var (r1, r2) = (e1, e2);
                            list.Add(() => r1.radius - r2.radius);
                        }
                        else if (Ends(s, e1, out var f1, out var g1) && Ends(s, e2, out var f2, out var g2))
                            list.Add(() => Vector2.Distance(f1.p, g1.p) - Vector2.Distance(f2.p, g2.p));
                        break;
                    case RelationKind.Coincident:
                        if (p1 != null && p2 != null) {
                            list.Add(() => p2.p.x - p1.p.x);
                            list.Add(() => p2.p.y - p1.p.y);
                        }
                        else if (p1 != null && IsRound(e1)) {
                            var (circle, center) = (e1, P(e1.a));
                            if (center != null) list.Add(() => Vector2.Distance(p1.p, center.p) - circle.radius);
                        }
                        else if (p1 != null && Ends(s, e1, out var la, out var lb))
                            list.Add(() => Cross((lb.p - la.p).normalized, p1.p - la.p));
                        break;
                    case RelationKind.Midpoint:
                        if (p1 != null && Ends(s, e1, out var ma, out var mb)) {
                            list.Add(() => p1.p.x - (ma.p.x + mb.p.x) / 2f);
                            list.Add(() => p1.p.y - (ma.p.y + mb.p.y) / 2f);
                        }
                        break;
                    case RelationKind.Concentric:
                        if (IsRound(e1) && IsRound(e2) && P(e1.a) != null && P(e2.a) != null) {
                            var (o1, o2) = (P(e1.a), P(e2.a));
                            list.Add(() => o2.p.x - o1.p.x);
                            list.Add(() => o2.p.y - o1.p.y);
                        }
                        break;
                    case RelationKind.Collinear:
                        if (Ends(s, e1, out var ka, out var kb) && Ends(s, e2, out var kc, out var kd)) {
                            list.Add(() => Cross((kb.p - ka.p).normalized, kc.p - ka.p));
                            list.Add(() => Cross((kb.p - ka.p).normalized, kd.p - ka.p));
                        }
                        break;
                    case RelationKind.Symmetric:
                        // Two points mirror each other across a line: their middle is on it, and they're square to it.
                        if (p1 != null && p2 != null && Ends(s, e1, out var sa, out var sb)) {
                            list.Add(() => Cross((sb.p - sa.p).normalized, (p1.p + p2.p) / 2f - sa.p));
                            list.Add(() => Vector2.Dot((sb.p - sa.p).normalized, p2.p - p1.p));
                        }
                        break;
                    case RelationKind.Coradial:
                        if (IsRound(e1) && IsRound(e2) && P(e1.a) != null && P(e2.a) != null) {
                            var (cc1, cc2, kk1, kk2) = (P(e1.a), P(e2.a), e1, e2);
                            list.Add(() => cc2.p.x - cc1.p.x);
                            list.Add(() => cc2.p.y - cc1.p.y);
                            list.Add(() => kk1.radius - kk2.radius);
                        }
                        break;
                    case RelationKind.Tangent: {
                        if (IsRound(e1) && IsRound(e2) && P(e1.a) != null && P(e2.a) != null) {
                            // Two circles or arcs: touching outside or inside, whichever they're nearer now.
                            var (k1, k2, q1, q2) = (e1, e2, P(e1.a), P(e2.a));
                            var apart = Vector2.Distance(q1.p, q2.p);
                            var outside = Mathf.Abs(apart - (k1.radius + k2.radius)) <= Mathf.Abs(apart - Mathf.Abs(k1.radius - k2.radius));
                            if (outside) list.Add(() => Vector2.Distance(q1.p, q2.p) - (k1.radius + k2.radius));
                            else list.Add(() => Vector2.Distance(q1.p, q2.p) - Mathf.Abs(k1.radius - k2.radius));
                            break;
                        }
                        var line = IsRound(e1) ? e2 : e1;
                        var round = IsRound(e1) ? e1 : e2;
                        if (IsRound(round) && Ends(s, line, out var ta, out var tb) && P(round.a) != null) {
                            var center = P(round.a);
                            var rr = round;
                            list.Add(() => Mathf.Abs(Cross((tb.p - ta.p).normalized, center.p - ta.p)) - rr.radius);
                        }
                        break;
                    }
                }
            }

            foreach (var d in s.dimensions) {
                if (d.value <= 0f) continue;
                var e = s.Entity(d.entity);
                var value = d.value;
                switch (d.kind) {
                    case DimensionKind.Length when e != null && P(e.a) != null && P(e.b) != null: {
                        var (a, b) = (P(e.a), P(e.b));
                        list.Add(() => Vector2.Distance(a.p, b.p) - value);
                        break;
                    }
                    case DimensionKind.Width when e != null && P(e.a) != null && P(e.b) != null: {
                        var (a, b) = (P(e.a), P(e.b));
                        list.Add(() => Vector2.Distance(a.p, b.p) - value);
                        break;
                    }
                    case DimensionKind.Height when e != null && P(e.b) != null && P(e.c) != null: {
                        var (b, c) = (P(e.b), P(e.c));
                        list.Add(() => Vector2.Distance(b.p, c.p) - value);
                        break;
                    }
                    case DimensionKind.Diameter when e != null: {
                        var round = e;
                        list.Add(() => round.radius * 2f - value);
                        break;
                    }
                    case DimensionKind.Distance when P(d.from) != null && P(d.to) != null: {
                        var (a, b) = (P(d.from), P(d.to));
                        list.Add(() => Vector2.Distance(a.p, b.p) - value);
                        break;
                    }
                    case DimensionKind.Horizontal when P(d.from) != null && P(d.to) != null: {
                        var (a, b) = (P(d.from), P(d.to));
                        list.Add(() => Mathf.Abs(b.p.x - a.p.x) - value);
                        break;
                    }
                    case DimensionKind.Vertical when P(d.from) != null && P(d.to) != null: {
                        var (a, b) = (P(d.from), P(d.to));
                        list.Add(() => Mathf.Abs(b.p.y - a.p.y) - value);
                        break;
                    }
                    case DimensionKind.Radius when e != null: {
                        var round = e;
                        list.Add(() => round.radius - value);
                        break;
                    }
                    case DimensionKind.PointLine when P(d.from) != null && Ends(s, e, out var la, out var lb): {
                        var q = P(d.from);
                        list.Add(() => Mathf.Abs(Cross((lb.p - la.p).normalized, q.p - la.p)) - value);
                        break;
                    }
                    case DimensionKind.LineLine when Ends(s, e, out var la, out var lb) && Ends(s, s.Entity(d.entity2), out var lc, out _): {
                        list.Add(() => Mathf.Abs(Cross((lb.p - la.p).normalized, lc.p - la.p)) - value);
                        break;
                    }
                    case DimensionKind.Angle when e != null && s.Entity(d.entity2) is SketchEntity other && Ends(s, e, out _, out _) && Ends(s, other, out _, out _): {
                        var (l1, l2) = (e, other);
                        list.Add(() => {
                            var (d1, d2) = SketchData.AngleDirections(s, l1, l2, s.Point);
                            return (Vector2.Angle(d1, d2) - value) * Mathf.Deg2Rad;
                        });
                        break;
                    }
                }
            }
            return list;
        }

        private static bool IsRound(SketchEntity e) => e != null && (e.kind == EntityKind.Circle || e.kind == EntityKind.Arc);

        private static bool Ends(SketchData s, SketchEntity e, out SketchPoint a, out SketchPoint b) {
            a = b = null;
            if (e == null || e.kind != EntityKind.Line) return false;
            a = s.Point(e.a);
            b = s.Point(e.b);
            return a != null && b != null;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
