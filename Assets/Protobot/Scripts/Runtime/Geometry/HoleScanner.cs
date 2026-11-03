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
    /// both sides of a wall are merged. Faces at other angles are scanned too (the part turned to look at each
    /// big slanted face), and a square hole can be turned within its face (its box is found at any angle).
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
            public Vector3 side;    // along one side of a square hole or a slot (zero for round ones)
            public float size2;     // a slot's length, along side (0: square or round, size both ways)
        }

        const float DefaultCell = 0.0004f;
        const float MinHole = 0.0019f;          // 0.075 in
        const float MaxHole = 0.0175f;          // 0.69 in (across)
        const float MaxSlot = 0.0406f;          // 1.6 in (a slot's length)
        const float SlotAspect = 3.5f;
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
            // Flat faces at other angles (a bent gusset's leg, an imported part turned in its file): the part is
            // turned so the face looks along a view, scanned, and what's found turned back.
            foreach (var normal in SlantedFaces(vertices, indices))
            {
                var turn = Quaternion.FromToRotation(normal, Vector3.forward);
                var back = Quaternion.Inverse(turn);
                var turned = new List<Vector3>(vertices.Count);
                foreach (var v in vertices)
                    turned.Add(turn * v);
                var turnedBounds = new Bounds(turned[0], Vector3.zero);
                foreach (var v in turned)
                    turnedBounds.Encapsulate(v);
                var more = new List<Found>();
                ScanView(turned, indices, turnedBounds, 2, 1, more);
                ScanView(turned, indices, turnedBounds, 2, -1, more);
                foreach (var hole in more)
                {
                    var h = hole;
                    h.entry = back * hole.entry;
                    h.axis = back * hole.axis;
                    h.side = back * hole.side;
                    found.Add(h);
                }
            }
            return Merge(found);
        }

        /// <summary>The normals of the biggest flat faces that aren't square to the axes (one per direction).</summary>
        static List<Vector3> SlantedFaces(IList<Vector3> vertices, IList<int> indices)
        {
            var groups = new List<(Vector3 normal, float area)>();
            for (var t = 0; t + 2 < indices.Count; t += 3)
            {
                var cross = Vector3.Cross(vertices[indices[t + 1]] - vertices[indices[t]], vertices[indices[t + 2]] - vertices[indices[t]]);
                var area = cross.magnitude / 2f;
                if (area < 1e-12f)
                    continue;
                var n = cross / (2f * area);
                if (Mathf.Abs(n.x) > 0.999f || Mathf.Abs(n.y) > 0.999f || Mathf.Abs(n.z) > 0.999f)
                    continue;  // the six views see these
                // One side and the other of a wall are the same direction (both are scanned).
                if (n.x < -1e-4f || (Mathf.Abs(n.x) <= 1e-4f && (n.y < -1e-4f || (Mathf.Abs(n.y) <= 1e-4f && n.z < 0f))))
                    n = -n;
                var match = -1;
                for (var g = 0; g < groups.Count; g++)
                    if (Vector3.Dot(groups[g].normal, n) > 0.9995f)
                    {
                        match = g;
                        break;
                    }
                if (match < 0)
                    groups.Add((n, area));
                else
                    groups[match] = (groups[match].normal, groups[match].area + area);
            }
            groups.Sort((a, b) => b.area.CompareTo(a.area));
            var result = new List<Vector3>();
            foreach (var (normal, area) in groups)
            {
                if (area < MinFaceArea * 4f || result.Count >= 6)
                    break;
                result.Add(normal);
            }
            return result;
        }

        /// <summary>
        /// The smallest box around a patch of cells at any turn: the angle (degrees, 0 to 90), its size in cells
        /// and its centre (cell units, from the image's corner).
        /// </summary>
        static (float angle, float w, float h, float cu, float cv) SmallestBox(List<int> cells, int nu)
        {
            var best = (angle: 0f, w: 0f, h: 0f, cu: 0f, cv: 0f);
            var bestArea = float.MaxValue;
            void Try(float degrees)
            {
                var c = Mathf.Cos(degrees * Mathf.Deg2Rad);
                var s = Mathf.Sin(degrees * Mathf.Deg2Rad);
                float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
                foreach (var cell in cells)
                {
                    float x = cell % nu + 0.5f, y = cell / nu + 0.5f;
                    var a = x * c + y * s;
                    var b = -x * s + y * c;
                    if (a < minA) minA = a;
                    if (a > maxA) maxA = a;
                    if (b < minB) minB = b;
                    if (b > maxB) maxB = b;
                }
                // Cell centres: the patch reaches about half a cell past them (a full cell when square to it).
                var pad = Mathf.Abs(c) + Mathf.Abs(s);
                var w = maxA - minA + pad;
                var h = maxB - minB + pad;
                if (w * h < bestArea - 1e-4f)
                {
                    bestArea = w * h;
                    var ma = (minA + maxA) / 2f;
                    var mb = (minB + maxB) / 2f;
                    best = (degrees, w, h, ma * c - mb * s, ma * s + mb * c);
                }
            }
            for (var d = 0f; d < 90f; d += 3f)
                Try(d);
            var coarse = best.angle;
            for (var d = coarse - 2.5f; d <= coarse + 2.5f; d += 0.5f)
                Try((d + 90f) % 90f);
            return best;
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
                    // A square turned in the face (a gusset's angled holes) has a bigger box around it: at 45° it's
                    // 1.41 times as wide.
                    if (Mathf.Min(w, h) < MinHole || Mathf.Max(w, h) > MaxSlot * 1.45f || Mathf.Max(w, h) > Mathf.Min(w, h) * SlotAspect)
                        continue;
                    // The smallest box around the patch, at any turn (degrees, 0 = square to the view): w along
                    // the turned axis (side), h across it.
                    var (angle, bw, bh, bcu, bcv) = SmallestBox(component, nu);
                    w = bw * cell;
                    h = bh * cell;
                    var across = Mathf.Min(w, h);
                    var length = Mathf.Max(w, h);
                    if (across < MinHole || across > MaxHole || length > MaxSlot || length > across * SlotAspect)
                        continue;

                    var fill = component.Count * cell * cell / (w * h);
                    if (fill < 0.6f)
                        continue;  // not a plain square, round hole or slot
                    // Longer one way: a slot (a screw slides along it), drawn as the box around it.
                    var slot = length > across * 1.2f;
                    var square = slot || fill >= 0.86f;
                    var holeSize = slot ? across : square ? (w + h) / 2f : Mathf.Sqrt(4f * component.Count / Mathf.PI) * cell;
                    // The side runs along the slot's length.
                    if (slot && h > w)
                        angle += 90f;
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
                    entry[ua] = min[ua] + bcu * cell;
                    entry[va] = min[va] + bcv * cell;
                    var side = Vector3.zero;
                    side[ua] = Mathf.Cos(angle * Mathf.Deg2Rad);
                    side[va] = Mathf.Sin(angle * Mathf.Deg2Rad);
                    if (!square) side = Vector3.zero;
                    found.Add(new Found { entry = entry, axis = direction, side = side, size = holeSize, size2 = slot ? length : 0f, square = square, depth = depth, through = through });
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
