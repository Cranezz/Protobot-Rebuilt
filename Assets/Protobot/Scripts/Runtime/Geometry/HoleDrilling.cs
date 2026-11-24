using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.Geometry
{
    /// <summary>An existing hole found in a flat wall, in the mesh's own space.</summary>
    public struct HoleInfo
    {
        public bool found;
        public string problem;
        /// <summary>Centre of the hole on the wall's front face.</summary>
        public Vector3 center;
        /// <summary>Wall normal pointing out of the front face (towards the drill).</summary>
        public Vector3 normal;
        public float thickness;
        /// <summary>Radius of the smallest circle around the centre that contains the existing hole.</summary>
        public float holeRadius;
        /// <summary>True for a round hole (e.g. one already drilled), false for square or slotted ones.</summary>
        public bool round;
    }

    /// <summary>
    /// The drill: finds an existing hole in a flat wall near where you aim, and widens it into a round hole
    /// by cutting a cylinder through that one wall (walls behind it are left alone). Works on the part's
    /// render mesh, so the result is real geometry, not a decal.
    /// </summary>
    public static class HoleDrilling
    {
        const float SeedRadius = 0.0055f;     // look for a hole within 5.5 mm of the aim point...
        const float CoarseStep = 0.0004f;     // ...on a 0.4 mm grid
        const float Step = 0.0003f;           // the hole itself is traced on a 0.3 mm grid
        const float FillRadius = 0.011f;      // holes reach at most this far from where the search found them
        const float Above = 0.0015f;          // rays start 1.5 mm in front of the surface
        const float FaceTolerance = 0.0006f;

        /// <summary>
        /// Triangles near a point, bucketed in a grid of small cells, so each of the many short test rays only
        /// looks at the few triangles it could possibly hit.
        /// </summary>
        public class Region
        {
            const float Cell = 0.0015f;

            readonly List<(Vector3 a, Vector3 b, Vector3 c)> triangles = new();
            readonly Dictionary<(int, int, int), List<int>> grid = new();
            readonly Vector3 min, max;
            int[] stamps;
            int stamp;

            public int TriangleCount => triangles.Count;

            public Region(Mesh mesh, Vector3 near, float radius)
            {
                min = near - Vector3.one * radius;
                max = near + Vector3.one * radius;
                var v = mesh.vertices;
                var r2 = radius * radius;
                for (var s = 0; s < mesh.subMeshCount; s++)
                {
                    var t = mesh.GetTriangles(s);
                    for (var i = 0; i < t.Length; i += 3)
                    {
                        Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                        var bounds = new Bounds(a, Vector3.zero);
                        bounds.Encapsulate(b);
                        bounds.Encapsulate(c);
                        if (bounds.SqrDistance(near) > r2)
                            continue;
                        var index = triangles.Count;
                        triangles.Add((a, b, c));
                        // Into every cell its box overlaps (only within the region; long slivers are clipped).
                        var lo = CellOf(Vector3.Max(bounds.min, min));
                        var hi = CellOf(Vector3.Min(bounds.max, max));
                        for (var x = lo.Item1; x <= hi.Item1; x++)
                            for (var y = lo.Item2; y <= hi.Item2; y++)
                                for (var z = lo.Item3; z <= hi.Item3; z++)
                                {
                                    if (!grid.TryGetValue((x, y, z), out var list))
                                        grid[(x, y, z)] = list = new List<int>(4);
                                    list.Add(index);
                                }
                    }
                }
                stamps = new int[triangles.Count];
            }

            static (int, int, int) CellOf(Vector3 p) =>
                (Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

            readonly List<int> candidates = new();

            /// <summary>The triangles near the segment from origin along direction (each once), into <see cref="candidates"/>.</summary>
            void Gather(Vector3 origin, Vector3 direction, float maxDistance)
            {
                candidates.Clear();
                stamp++;
                var end = origin + direction * maxDistance;
                var lo = CellOf(Vector3.Max(Vector3.Min(origin, end), min));
                var hi = CellOf(Vector3.Min(Vector3.Max(origin, end), max));
                for (var x = lo.Item1; x <= hi.Item1; x++)
                    for (var y = lo.Item2; y <= hi.Item2; y++)
                        for (var z = lo.Item3; z <= hi.Item3; z++)
                        {
                            if (!grid.TryGetValue((x, y, z), out var list))
                                continue;
                            foreach (var i in list)
                            {
                                if (stamps[i] == stamp)
                                    continue;
                                stamps[i] = stamp;
                                candidates.Add(i);
                            }
                        }
            }

            /// <summary>All hit distances along the ray (both faces count), sorted, up to maxDistance.</summary>
            public List<float> Hits(Vector3 origin, Vector3 direction, float maxDistance)
            {
                var hits = new List<float>();
                Gather(origin, direction, maxDistance);
                foreach (var i in candidates)
                {
                    var (a, b, c) = triangles[i];
                    if (Intersect(origin, direction, a, b, c, out var t) && t >= 0f && t <= maxDistance)
                        hits.Add(t);
                }
                hits.Sort();
                return hits;
            }

            public bool First(Vector3 origin, Vector3 direction, float maxDistance, out float distance)
            {
                distance = float.MaxValue;
                Gather(origin, direction, maxDistance);
                foreach (var i in candidates)
                {
                    var (a, b, c) = triangles[i];
                    if (Intersect(origin, direction, a, b, c, out var t) && t >= 0f && t <= maxDistance && t < distance)
                        distance = t;
                }
                return distance < float.MaxValue;
            }

            /// <summary>Möller–Trumbore, double-sided.</summary>
            static bool Intersect(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
            {
                t = 0f;
                var e1 = b - a;
                var e2 = c - a;
                var p = Vector3.Cross(d, e2);
                var det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-14f)
                    return false;
                var inv = 1f / det;
                var s = o - a;
                var u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f)
                    return false;
                var q = Vector3.Cross(s, e1);
                var v = Vector3.Dot(d, q) * inv;
                if (v < 0f || u + v > 1f)
                    return false;
                t = Vector3.Dot(e2, q) * inv;
                return true;
            }
        }

        static readonly (int, int)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        static List<Vector2> seedOffsets;

        /// <summary>Grid spots around the aim point, nearest first.</summary>
        static List<Vector2> SeedOffsets()
        {
            if (seedOffsets != null)
                return seedOffsets;
            seedOffsets = new List<Vector2>();
            var n = Mathf.CeilToInt(SeedRadius / CoarseStep);
            for (var i = -n; i <= n; i++)
            {
                for (var j = -n; j <= n; j++)
                {
                    var p = new Vector2(i, j) * CoarseStep;
                    if (p.sqrMagnitude <= SeedRadius * SeedRadius)
                        seedOffsets.Add(p);
                }
            }
            seedOffsets.Sort((x, y) => x.sqrMagnitude.CompareTo(y.sqrMagnitude));
            return seedOffsets;
        }

        static void Basis(Vector3 n, out Vector3 a, out Vector3 b)
        {
            a = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            b = Vector3.Cross(n, a);
        }

        /// <summary>Looks for an existing hole in the wall at <paramref name="surfacePoint"/> (mesh space).</summary>
        public static HoleInfo Find(Region region, Vector3 surfacePoint, Vector3 normal)
        {
            var info = new HoleInfo { normal = normal.normalized };
            var n = info.normal;
            Basis(n, out var a, out var b);

            // Is there wall at this spot (2D offset from the aim point, on the wall's surface)?
            bool Solid(Vector2 p)
            {
                var origin = surfacePoint + n * Above + a * p.x + b * p.y;
                return region.First(origin, -n, Above + FaceTolerance, out var t) && Mathf.Abs(t - Above) <= FaceTolerance;
            }

            // The empty spot nearest the aim point.
            Vector2? found = null;
            foreach (var offset in SeedOffsets())
            {
                if (Solid(offset))
                    continue;
                found = offset;
                break;
            }
            if (found == null)
            {
                info.problem = "No hole here — the drill widens existing holes";
                return info;
            }

            // Flood fill the hole from there, testing each spot only when the fill reaches it. If it spreads
            // too far it's the outside of the part (or a long slot), not a hole.
            var seed = found.Value;
            var solid = new Dictionary<(int, int), bool>();
            bool SolidAt((int i, int j) c)
            {
                if (!solid.TryGetValue(c, out var s))
                    solid[c] = s = Solid(seed + new Vector2(c.i, c.j) * Step);
                return s;
            }
            var limit = FillRadius / Step;
            var cells = new List<(int i, int j)>();
            var seen = new HashSet<(int, int)> { (0, 0) };
            var queue = new Queue<(int i, int j)>();
            queue.Enqueue((0, 0));
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                cells.Add(cell);
                foreach (var (di, dj) in Neighbours)
                {
                    var next = (cell.i + di, cell.j + dj);
                    if (seen.Contains(next) || SolidAt(next))
                        continue;
                    if (next.Item1 * next.Item1 + next.Item2 * next.Item2 > limit * limit)
                    {
                        info.problem = "That's the edge of the part, not a hole";
                        return info;
                    }
                    seen.Add(next);
                    queue.Enqueue(next);
                }
            }

            var sum = Vector2.zero;
            foreach (var (i, j) in cells)
                sum += new Vector2(i, j);
            var mean = seed + sum / cells.Count * Step;
            var radius = 0f;
            var edgeNearest = float.MaxValue;  // closest the hole's edge comes to its centre
            foreach (var (i, j) in cells)
            {
                var d = Vector2.Distance(mean, seed + new Vector2(i, j) * Step);
                radius = Mathf.Max(radius, d);
                foreach (var (di, dj) in Neighbours)
                    if (SolidAt((i + di, j + dj)))
                        edgeNearest = Mathf.Min(edgeNearest, d);
            }
            info.round = edgeNearest > radius * 0.88f;  // a square's edge comes in to 0.71 of its corners
            // Square holes: a circle that clears the corners. Round ones: the best estimate of their radius.
            info.holeRadius = info.round ? (edgeNearest + radius) / 2f + Step * 0.5f : radius + Step * 0.75f;

            // Measure the wall just beside the hole (front face, then back face) on every side and keep the
            // thinnest: near an edge a side can miss the wall, and near a bend it cuts the curve at a slant.
            float front = 0f, thickness = float.MaxValue;
            foreach (var side in new[] { a, -a, b, -b })
            {
                var probe = surfacePoint + a * mean.x + b * mean.y + side * (info.holeRadius + 0.0008f) + n * Above;
                var hits = region.Hits(probe, -n, 0.03f);
                if (hits.Count < 2 || Mathf.Abs(hits[0] - Above) > 0.0015f)
                    continue;
                var sideFront = hits[0];
                var sideBack = hits.Find(t => t > sideFront + 0.0002f);
                if (sideBack > sideFront && sideBack - sideFront < thickness)
                {
                    front = sideFront;
                    thickness = sideBack - sideFront;
                }
            }
            if (thickness == float.MaxValue)
            {
                info.problem = "Couldn't find the back of this wall";
                return info;
            }
            if (thickness > 0.008f)
            {
                info.problem = "This wall is too thick to drill";
                return info;
            }
            info.thickness = thickness;
            info.center = surfacePoint + a * mean.x + b * mean.y + n * (Above - front);
            info.found = true;
            return info;
        }

        /// <summary>
        /// Where the new hole's wall will be: for each of its <paramref name="segments"/> sides, whether there's metal
        /// just outside the hole there (near an edge or a bend part of the circle opens into the air).
        /// </summary>
        public static bool[] WallSegments(Region region, HoleInfo hole, float radius, int segments = DrillSegments)
        {
            var n = hole.normal;
            Basis(n, out var a, out var b);
            var result = new bool[segments];
            for (var k = 0; k < segments; k++)
            {
                var angle = (k + 0.5f) * Mathf.PI * 2f / segments;
                var offset = (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * (radius + 0.0003f);
                result[k] = region.First(hole.center + offset + n * Above, -n, Above + hole.thickness * 0.5f, out var t)
                            && Mathf.Abs(t - Above) <= FaceTolerance;
            }
            return result;
        }

        public const int DrillSegments = 32;

        /// <summary>Cuts a round hole of <paramref name="radius"/> through the wall. Returns null if nothing changed.</summary>
        /// <param name="spans">V2: for each side, how far the new round wall runs along the hole (top, bottom; 0 at
        /// the hole's top face, negative into the wall): where there's material just outside the hole, so the wall
        /// never stands out past the part (a flange beside the hole doesn't make it taller all the way round).</param>
        /// <param name="closed">V2: a blind hole (into the end of a shaft): its round bottom is closed at the full depth.</param>
        public static Mesh Drill(Mesh source, HoleInfo hole, float radius, bool[] walls = null, int segments = DrillSegments, Vector2[] spans = null, bool closed = false)
        {
            var n = hole.normal;
            Basis(n, out var a, out var b);
            var depth = hole.thickness;
            var circumscribed = radius / Mathf.Cos(Mathf.PI / segments);
            var edgeNormals = new Vector2[segments];
            var corners = new Vector2[segments];
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                corners[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * circumscribed;
                var mid = (i + 0.5f) * Mathf.PI * 2f / segments;
                edgeNormals[i] = new Vector2(Mathf.Cos(mid), Mathf.Sin(mid));
            }

            var srcV = source.vertices;
            var srcN = source.normals;
            var verts = new List<Vector3>(srcV);
            var normals = new List<Vector3>(srcN != null && srcN.Length == srcV.Length ? srcN : new Vector3[srcV.Length]);
            var submeshes = new List<List<int>>();
            var changed = false;
            var wallSubmesh = 0;

            Vector2 Flat(Vector3 p) => new(Vector3.Dot(p - hole.center, a), Vector3.Dot(p - hole.center, b));
            float Depth(Vector3 p) => Vector3.Dot(p - hole.center, n);

            for (var s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                var output = new List<int>(tris.Length);
                for (var t = 0; t < tris.Length; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    Vector3 p0 = srcV[i0], p1 = srcV[i1], p2 = srcV[i2];
                    var inSlab = InSlab(Depth(p0), depth) && InSlab(Depth(p1), depth) && InSlab(Depth(p2), depth);
                    Vector2 f0 = Flat(p0), f1 = Flat(p1), f2 = Flat(p2);
                    if (!inSlab || !TouchesPrism(f0, f1, f2, edgeNormals, radius))
                    {
                        output.Add(i0);
                        output.Add(i1);
                        output.Add(i2);
                        continue;
                    }

                    changed = true;
                    wallSubmesh = s;
                    // Not .normalized: it returns zero below 1e-5, and a millimetre-sized triangle's cross
                    // product is smaller than that (in metres), which would shade the pieces black.
                    var cross = Vector3.Cross(p1 - p0, p2 - p0);
                    var length = cross.magnitude;
                    var faceNormal = length > 0f ? cross / length : normals[i0];
                    var remaining = new List<(Vector3 p, Vector2 f)> { (p0, f0), (p1, f1), (p2, f2) };
                    for (var e = 0; e < segments && remaining.Count >= 3; e++)
                    {
                        var outside = Clip(remaining, edgeNormals[e], radius, keepOutside: true);
                        if (outside.Count >= 3)
                            AddFan(outside, faceNormal, verts, normals, output);
                        remaining = Clip(remaining, edgeNormals[e], radius, keepOutside: false);
                    }
                    // Whatever is left lies inside the drill: removed.
                }
                submeshes.Add(output);
            }

            if (!changed)
                return null;

            // The new round wall of the hole, facing in towards its centre.
            for (var i = 0; i < segments; i++)
            {
                if (walls != null && i < walls.Length && !walls[i])
                    continue;  // no metal on this side (the hole breaks through an edge)
                var c0 = corners[i];
                var c1 = corners[(i + 1) % segments];
                float top = 0f, bottom = -depth;
                if (spans != null && i < spans.Length)
                {
                    top = Mathf.Clamp(spans[i].x, -depth, 0f);
                    bottom = Mathf.Clamp(spans[i].y, -depth, 0f);
                    if (top - bottom < 0.0005f)
                        continue;
                }
                var f0 = hole.center + a * c0.x + b * c0.y + n * top;
                var f1 = hole.center + a * c1.x + b * c1.y + n * top;
                var b0 = f0 - n * (top - bottom);
                var b1 = f1 - n * (top - bottom);
                var inward = -(a * edgeNormals[i].x + b * edgeNormals[i].y);
                var start = verts.Count;
                verts.AddRange(new[] { f0, f1, b1, b0 });
                normals.AddRange(new[] { inward, inward, inward, inward });
                var winding = Vector3.Dot(Vector3.Cross(f1 - f0, b1 - f0), inward) >= 0f;
                var list = submeshes[wallSubmesh];
                if (winding)
                    list.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                else
                    list.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            if (closed)
            {
                // A disc at the bottom, facing out of the hole (toward its opening).
                var middle = verts.Count;
                var floor = hole.center - n * depth;
                verts.Add(floor);
                normals.Add(n);
                for (var i = 0; i < segments; i++)
                {
                    verts.Add(floor + a * corners[i].x + b * corners[i].y);
                    normals.Add(n);
                }
                var list = submeshes[wallSubmesh];
                for (var i = 0; i < segments; i++)
                {
                    int p = middle + 1 + i, q = middle + 1 + (i + 1) % segments;
                    var up = Vector3.Dot(Vector3.Cross(verts[p] - floor, verts[q] - floor), n) >= 0f;
                    if (up) list.AddRange(new[] { middle, p, q });
                    else list.AddRange(new[] { middle, q, p });
                }
            }

            var mesh = new Mesh { name = source.name };
            if (verts.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = submeshes.Count;
            for (var s = 0; s < submeshes.Count; s++)
                mesh.SetTriangles(submeshes[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }

        static bool InSlab(float d, float depth) => d <= 0.0001f && d >= -depth - 0.0001f;

        /// <summary>Quick reject: the triangle is entirely outside one side of the drill's polygon.</summary>
        static bool TouchesPrism(Vector2 a, Vector2 b, Vector2 c, Vector2[] edgeNormals, float radius)
        {
            foreach (var e in edgeNormals)
                if (Vector2.Dot(a, e) >= radius && Vector2.Dot(b, e) >= radius && Vector2.Dot(c, e) >= radius)
                    return false;
            return true;
        }

        /// <summary>Clips a polygon against the plane dot(f, e) = radius (keeping the outside or the inside).</summary>
        static List<(Vector3 p, Vector2 f)> Clip(List<(Vector3 p, Vector2 f)> poly, Vector2 e, float radius, bool keepOutside)
        {
            var result = new List<(Vector3, Vector2)>(poly.Count + 2);
            for (var i = 0; i < poly.Count; i++)
            {
                var cur = poly[i];
                var next = poly[(i + 1) % poly.Count];
                var dc = Vector2.Dot(cur.f, e) - radius;
                var dn = Vector2.Dot(next.f, e) - radius;
                if (!keepOutside)
                {
                    dc = -dc;
                    dn = -dn;
                }
                if (dc >= 0f)
                    result.Add(cur);
                if ((dc >= 0f) != (dn >= 0f))
                {
                    var k = dc / (dc - dn);
                    result.Add((Vector3.Lerp(cur.p, next.p, k), Vector2.Lerp(cur.f, next.f, k)));
                }
            }
            return result;
        }

        static void AddFan(List<(Vector3 p, Vector2 f)> poly, Vector3 normal, List<Vector3> verts, List<Vector3> normals, List<int> output)
        {
            var start = verts.Count;
            foreach (var (p, _) in poly)
            {
                verts.Add(p);
                normals.Add(normal);
            }
            for (var k = 1; k < poly.Count - 1; k++)
            {
                output.Add(start);
                output.Add(start + k);
                output.Add(start + k + 1);
            }
        }
    }
}
