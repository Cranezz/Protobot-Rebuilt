using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Geometry
{
    /// <summary>
    /// Finds the holes in a part by looking at it from its six sides. Each view is a depth image that keeps
    /// every surface the view ray passes (front to back), made by rasterising the triangles. A hole is a
    /// patch of the image that goes deeper than the flat face around it, fully ringed by that face, and
    /// hole-sized. The rim's second surface gives the wall's thickness; a hole with an empty gap behind the
    /// wall goes through, otherwise it's blind (e.g. the threaded hole in a standoff's end). Holes seen from
    /// both sides of a wall are merged. Only holes square to the part's own axes are found, which is how
    /// VEX parts are modelled.
    /// </summary>
    public static class HoleScanner
    {
        public struct Found
        {
            public Vector3 entry;   // centre of the hole where it meets the face (scan space)
            public Vector3 axis;    // into the material
            public float size;      // square side or round diameter (m)
            public bool square;
            public float depth;     // wall thickness for a through hole, how deep for a blind one
            public bool through;
        }

        const float DefaultCell = 0.0004f;
        const float MinHole = 0.0019f;          // 0.075 in
        const float MaxHole = 0.0175f;          // 0.69 in
        const float LevelTolerance = 0.00015f;
        const float Deeper = 0.0003f;           // a hole goes at least this much deeper than its rim
        const float MinFaceArea = 1.2e-5f;      // 12 mm² of flat face counts as a surface level
        const int MaxLevels = 16;
        const int MaxCells = 2_500_000;

        /// <summary>All holes in the triangles (vertices already in the space the result should be in).</summary>
        public static List<Found> Scan(IList<Vector3> vertices, IList<int> indices)
        {
            var found = new List<Found>();
            if (vertices.Count == 0 || indices.Count < 3)
                return found;
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var v in vertices)
                bounds.Encapsulate(v);
            for (var k = 0; k < 3; k++)
            {
                ScanView(vertices, indices, bounds, k, 1, found);
                ScanView(vertices, indices, bounds, k, -1, found);
            }
            return Merge(found);
        }

        static void ScanView(IList<Vector3> vertices, IList<int> indices, Bounds bounds, int k, int sign, List<Found> found)
        {
            int ua = (k + 1) % 3, va = (k + 2) % 3;
            var min = bounds.min;
            var size = bounds.size;
            var cell = DefaultCell;
            while ((size[ua] / cell + 2) * (size[va] / cell + 2) > MaxCells)
                cell *= 1.5f;
            var nu = Mathf.CeilToInt(size[ua] / cell) + 2;
            var nv = Mathf.CeilToInt(size[va] / cell) + 2;
            float Depth(Vector3 p) => sign > 0 ? p[k] - min[k] : bounds.max[k] - p[k];

            // Rasterise every triangle: one (cell, depth) sample per cell centre it covers.
            var samples = new List<(int cell, float depth)>();
            for (var t = 0; t + 2 < indices.Count; t += 3)
            {
                Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
                float ax = (a[ua] - min[ua]) / cell, ay = (a[va] - min[va]) / cell;
                float bx = (b[ua] - min[ua]) / cell, by = (b[va] - min[va]) / cell;
                float cx = (c[ua] - min[ua]) / cell, cy = (c[va] - min[va]) / cell;
                var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (Mathf.Abs(area) < 1e-6f)
                    continue;  // edge-on to this view
                float da = Depth(a), db = Depth(b), dc = Depth(c);
                var i0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx)) - 0.5f));
                var i1 = Mathf.Min(nu - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx)) - 0.5f));
                var j0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy)) - 0.5f));
                var j1 = Mathf.Min(nv - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy)) - 0.5f));
                var tolerance = -1e-5f * Mathf.Abs(area);
                for (var j = j0; j <= j1; j++)
                {
                    var y = j + 0.5f;
                    for (var i = i0; i <= i1; i++)
                    {
                        var x = i + 0.5f;
                        var w0 = ((bx - x) * (cy - y) - (by - y) * (cx - x)) / area;
                        var w1 = ((cx - x) * (ay - y) - (cy - y) * (ax - x)) / area;
                        var w2 = 1f - w0 - w1;
                        if (w0 * Mathf.Abs(area) < tolerance || w1 * Mathf.Abs(area) < tolerance || w2 * Mathf.Abs(area) < tolerance)
                            continue;
                        samples.Add((j * nu + i, w0 * da + w1 * db + w2 * dc));
                    }
                }
            }
            if (samples.Count == 0)
                return;

            // Per cell: its surfaces front to back (duplicates from shared edges removed).
            samples.Sort((x, y) => x.cell != y.cell ? x.cell.CompareTo(y.cell) : x.depth.CompareTo(y.depth));
            var start = new int[nu * nv + 1];
            var depths = new List<float>(samples.Count);
            var owner = 0;
            for (var s = 0; s < samples.Count; s++)
            {
                var (c, d) = samples[s];
                while (owner <= c)
                    start[owner++] = depths.Count;
                if (depths.Count > start[c] && d - depths[depths.Count - 1] < 2e-5f)
                    continue;
                depths.Add(d);
            }
            while (owner <= nu * nv)
                start[owner++] = depths.Count;
            float First(int c) => start[c + 1] > start[c] ? depths[start[c]] : float.PositiveInfinity;
            float Second(int c) => start[c + 1] > start[c] + 1 ? depths[start[c] + 1] : float.NaN;

            // Flat faces looking this way: depths shared by enough cells.
            var bins = new Dictionary<int, int>();
            for (var c = 0; c < nu * nv; c++)
            {
                var f = First(c);
                if (float.IsInfinity(f))
                    continue;
                var q = Mathf.RoundToInt(f / 0.0001f);
                bins.TryGetValue(q, out var n);
                bins[q] = n + 1;
            }
            var minCells = MinFaceArea / (cell * cell);
            var levels = new List<float>();
            var ordered = new List<KeyValuePair<int, int>>(bins);
            ordered.Sort((x, y) => y.Value.CompareTo(x.Value));
            foreach (var pair in ordered)
            {
                if (pair.Value < minCells || levels.Count >= MaxLevels)
                    break;
                var level = pair.Key * 0.0001f;
                var close = false;
                foreach (var l in levels)
                    close |= Mathf.Abs(l - level) < 0.0003f;
                if (!close)
                    levels.Add(level);
            }

            var direction = Vector3.zero;
            direction[k] = sign;
            var seen = new int[nu * nv];
            var queue = new Queue<int>();
            var component = new List<int>();
            var rimThickness = new List<float>();
            var inside = new List<float>();
            for (var li = 0; li < levels.Count; li++)
            {
                var level = levels[li];
                var mark = li + 1;
                bool AtLevel(int c) => Mathf.Abs(First(c) - level) <= LevelTolerance;
                bool Below(int c) => First(c) > level + Deeper;

                for (var seed = 0; seed < nu * nv; seed++)
                {
                    if (seen[seed] == mark || !Below(seed))
                        continue;
                    // One connected patch deeper than this face.
                    component.Clear();
                    rimThickness.Clear();
                    inside.Clear();
                    var valid = true;
                    int rim = 0, foreign = 0;
                    int minI = int.MaxValue, maxI = int.MinValue, minJ = int.MaxValue, maxJ = int.MinValue;
                    seen[seed] = mark;
                    queue.Enqueue(seed);
                    while (queue.Count > 0)
                    {
                        var c = queue.Dequeue();
                        component.Add(c);
                        int i = c % nu, j = c / nu;
                        minI = Mathf.Min(minI, i);
                        maxI = Mathf.Max(maxI, i);
                        minJ = Mathf.Min(minJ, j);
                        maxJ = Mathf.Max(maxJ, j);
                        if (i == 0 || j == 0 || i == nu - 1 || j == nv - 1)
                        {
                            valid = false;  // runs off the image: the outside, not a hole
                            continue;
                        }
                        var f = First(c);
                        if (!float.IsInfinity(f))
                            inside.Add(f);
                        for (var n = 0; n < 4; n++)
                        {
                            var next = n == 0 ? c + 1 : n == 1 ? c - 1 : n == 2 ? c + nu : c - nu;
                            if (Below(next))
                            {
                                if (seen[next] != mark)
                                {
                                    seen[next] = mark;
                                    queue.Enqueue(next);
                                }
                            }
                            else if (First(next) >= level - LevelTolerance)
                            {
                                // The face, or a chamfer or countersink sloping into the hole.
                                rim++;
                                var second = Second(next);
                                if (AtLevel(next) && !float.IsNaN(second))
                                    rimThickness.Add(second - First(next));
                            }
                            else
                            {
                                foreign++;  // something standing out of the face right at the hole
                            }
                        }
                    }
                    if (!valid || rim == 0 || foreign > rim * 0.1f)
                        continue;
                    var w = (maxI - minI + 1) * cell;
                    var h = (maxJ - minJ + 1) * cell;
                    if (Mathf.Min(w, h) < MinHole || Mathf.Max(w, h) > MaxHole || Mathf.Max(w, h) > Mathf.Min(w, h) * 1.6f)
                        continue;

                    var fill = component.Count * cell * cell / (w * h);
                    var square = fill >= 0.86f;
                    if (fill < 0.6f)
                        continue;  // not a plain square or round hole
                    var holeSize = square ? (w + h) / 2f : Mathf.Sqrt(4f * component.Count / Mathf.PI) * cell;
                    rimThickness.Sort();
                    var thickness = rimThickness.Count > 0 ? rimThickness[rimThickness.Count / 2] : 0f;
                    // Through when the view passes the wall (nothing behind, or only far behind it).
                    var through = true;
                    var depth = thickness;
                    if (inside.Count > component.Count / 2)
                    {
                        inside.Sort();
                        var behind = inside[inside.Count / 2] - level;
                        if (thickness <= 0f || behind < thickness + 0.0002f)
                        {
                            through = false;
                            depth = behind;
                        }
                    }
                    if (depth <= 0f)
                        continue;

                    var entry = Vector3.zero;
                    entry[k] = sign > 0 ? min[k] + level : bounds.max[k] - level;
                    entry[ua] = min[ua] + (minI + maxI + 1) * 0.5f * cell;
                    entry[va] = min[va] + (minJ + maxJ + 1) * 0.5f * cell;
                    found.Add(new Found { entry = entry, axis = direction, size = holeSize, square = square, depth = depth, through = through });
                }
            }
        }

        /// <summary>A through hole is seen from both sides of its wall: keep one of each.</summary>
        static List<Found> Merge(List<Found> found)
        {
            var result = new List<Found>();
            foreach (var hole in found)
            {
                if (!hole.through && hole.depth < 0.0008f)
                    continue;  // a shallow recess (a counterbore, a nut's teeth), not a hole
                var duplicate = false;
                foreach (var kept in result)
                {
                    if (Mathf.Abs(Vector3.Dot(kept.axis, hole.axis)) < 0.99f)
                        continue;
                    var offset = hole.entry - kept.entry;
                    var along = Vector3.Dot(offset, kept.axis);
                    var lateral = (offset - kept.axis * along).magnitude;
                    if (lateral > 0.001f)
                        continue;
                    // Same face, or the other face of the same wall.
                    var sameFace = Vector3.Dot(kept.axis, hole.axis) > 0f && Mathf.Abs(along) < 0.0004f;
                    var otherFace = Vector3.Dot(kept.axis, hole.axis) < 0f && Mathf.Abs(along - kept.depth) < 0.0005f;
                    if (sameFace || otherFace)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    result.Add(hole);
            }
            return result;
        }
    }
}
