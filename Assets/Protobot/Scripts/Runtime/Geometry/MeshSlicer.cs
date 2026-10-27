using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.Geometry
{
    /// <summary>
    /// Cuts a mesh with a plane and keeps one side, closing the open end with a flat cap so the part
    /// stays solid (the hack saw). Also finds the outline where a plane crosses a mesh, which is used
    /// to work out a metal part's cross-section.
    /// </summary>
    public static class MeshSlicer
    {
        const float OnPlaneEpsilon = 1e-7f;
        const float WeldTolerance = 1e-5f;    // CAD tessellations put the "same" outline point a few µm apart
        const float HealTolerance = 2.5e-4f;  // small gaps between outline pieces get bridged
        const float CollinearTolerance = 2e-7f;

        /// <summary>A closed outline where a plane crosses a mesh, in the plane's 2D coordinates.</summary>
        public class Loop
        {
            public List<Vector3> points3D = new();
            public List<Vector2> points2D = new();
            public float signedArea;
        }

        /// <summary>Basis on a plane: two perpendicular in-plane directions.</summary>
        public static void PlaneBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            v = Vector3.Cross(normal, u);
        }

        /// <summary>
        /// Keeps the part of <paramref name="source"/> on the side the plane normal points to, with the cut capped.
        /// Returns null if nothing would be left.
        /// </summary>
        public static Mesh Slice(Mesh source, Plane plane)
        {
            SliceBoth(source, plane, false, out var kept, out _, out _);
            return kept;
        }

        /// <summary>
        /// Both pieces of one cut in a single pass: <paramref name="positive"/> is the side the plane normal points
        /// to, <paramref name="negative"/> the other (only built when <paramref name="bothSides"/>; null when nothing
        /// is on that side). The outline is found and triangulated once and caps both pieces; it's returned too
        /// (e.g. to draw the kerf).
        /// </summary>
        public static void SliceBoth(Mesh source, Plane plane, bool bothSides, out Mesh positive, out Mesh negative, out List<Loop> outline)
        {
            var srcVerts = source.vertices;
            var srcNormals = source.normals;
            var hasNormals = srcNormals != null && srcNormals.Length == srcVerts.Length;
            var distances = new float[srcVerts.Length];
            for (var i = 0; i < srcVerts.Length; i++)
                distances[i] = plane.GetDistanceToPoint(srcVerts[i]);
            var triangles = new int[source.subMeshCount][];
            for (var s = 0; s < triangles.Length; s++)
                triangles[s] = source.GetTriangles(s);

            var segments = new List<(Vector3 a, Vector3 b)>();
            var positiveSide = new Side(srcVerts.Length);
            positiveSide.Clip(srcVerts, srcNormals, hasNormals, distances, 1f, triangles, plane.normal, segments);
            Side negativeSide = null;
            if (bothSides)
            {
                negativeSide = new Side(srcVerts.Length);
                negativeSide.Clip(srcVerts, srcNormals, hasNormals, distances, -1f, triangles, plane.normal, null);
            }

            // Cap each cut with the triangulated outline, facing out of its piece.
            outline = BuildLoops(segments, plane.normal);
            List<Vector3> capPoints = null;
            List<int> capTriangles = null;
            if (outline.Count > 0)
                (_, capPoints, capTriangles) = Triangulate(outline);
            positive = positiveSide.Build(source.name + " (cut)", capPoints, capTriangles, -plane.normal);
            negative = negativeSide?.Build(source.name + " (cut)", capPoints, capTriangles, plane.normal);
        }

        /// <summary>One piece of a cut while it's being built.</summary>
        class Side
        {
            readonly List<Vector3> verts;
            readonly List<Vector3> normals;
            readonly int[] remap;
            readonly List<List<int>> submeshes = new();

            public Side(int sourceVertexCount)
            {
                verts = new List<Vector3>(sourceVertexCount);
                normals = new List<Vector3>(sourceVertexCount);
                remap = new int[sourceVertexCount];
                for (var i = 0; i < remap.Length; i++)
                    remap[i] = -1;
            }

            int Keep(int index, Vector3[] srcVerts, Vector3[] srcNormals, bool hasNormals)
            {
                var mapped = remap[index];
                if (mapped >= 0)
                    return mapped;
                mapped = verts.Count;
                verts.Add(srcVerts[index]);
                normals.Add(hasNormals ? srcNormals[index] : Vector3.up);
                remap[index] = mapped;
                return mapped;
            }

            /// <summary>
            /// Keeps the triangles on the <paramref name="sign"/> side of the plane, clipping those that cross it.
            /// When <paramref name="segments"/> isn't null, collects the cut outline's pieces there.
            /// </summary>
            public void Clip(Vector3[] srcVerts, Vector3[] srcNormals, bool hasNormals, float[] distances, float sign,
                int[][] triangles, Vector3 planeNormal, List<(Vector3 a, Vector3 b)> segments)
            {
                var poly = new List<(Vector3 pos, Vector3 normal, int original, bool onPlane)>(4);
                var corner = new int[3];
                var indices = new int[4];
                foreach (var tris in triangles)
                {
                    var output = new List<int>(tris.Length);
                    for (var t = 0; t < tris.Length; t += 3)
                    {
                        int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                        float d0 = distances[i0] * sign, d1 = distances[i1] * sign, d2 = distances[i2] * sign;
                        var in0 = d0 >= -OnPlaneEpsilon;
                        var in1 = d1 >= -OnPlaneEpsilon;
                        var in2 = d2 >= -OnPlaneEpsilon;
                        if (!in0 && !in1 && !in2)
                            continue;
                        if (Mathf.Abs(d0) <= OnPlaneEpsilon && Mathf.Abs(d1) <= OnPlaneEpsilon && Mathf.Abs(d2) <= OnPlaneEpsilon)
                            continue;  // lies in the cut plane: the cap replaces it

                        var along = segments != null ? Along(planeNormal, srcVerts[i0], srcVerts[i1], srcVerts[i2]) : Vector3.zero;
                        if (in0 && in1 && in2)
                        {
                            output.Add(Keep(i0, srcVerts, srcNormals, hasNormals));
                            output.Add(Keep(i1, srcVerts, srcNormals, hasNormals));
                            output.Add(Keep(i2, srcVerts, srcNormals, hasNormals));
                            if (segments != null)
                            {
                                // An edge lying exactly on the plane is part of the cut outline.
                                AddOnPlaneEdge(srcVerts, distances, i0, i1, along, segments);
                                AddOnPlaneEdge(srcVerts, distances, i1, i2, along, segments);
                                AddOnPlaneEdge(srcVerts, distances, i2, i0, along, segments);
                            }
                            continue;
                        }

                        // Clip the triangle against the plane (Sutherland-Hodgman, one plane).
                        poly.Clear();
                        corner[0] = i0;
                        corner[1] = i1;
                        corner[2] = i2;
                        for (var k = 0; k < 3; k++)
                        {
                            int a = corner[k], b = corner[(k + 1) % 3];
                            float da = distances[a] * sign, db = distances[b] * sign;
                            var aIn = da >= -OnPlaneEpsilon;
                            var bIn = db >= -OnPlaneEpsilon;
                            if (aIn)
                                poly.Add((srcVerts[a], hasNormals ? srcNormals[a] : Vector3.up, a, Mathf.Abs(da) <= OnPlaneEpsilon));
                            if (aIn != bIn && Mathf.Abs(da) > OnPlaneEpsilon && Mathf.Abs(db) > OnPlaneEpsilon)
                            {
                                // From the plane's own distances, so both pieces get bit-identical points.
                                var pos = EdgeCrossing(srcVerts[a], srcVerts[b], distances[a], distances[b], out var f, out var swapped);
                                var nrm = Vector3.up;
                                if (hasNormals)
                                {
                                    nrm = (swapped ? Vector3.Lerp(srcNormals[b], srcNormals[a], f) : Vector3.Lerp(srcNormals[a], srcNormals[b], f)).normalized;
                                    if (nrm == Vector3.zero)
                                        nrm = srcNormals[a];  // the two normals cancel out
                                }
                                poly.Add((pos, nrm, -1, true));
                            }
                        }
                        if (poly.Count < 3)
                            continue;

                        for (var k = 0; k < poly.Count; k++)
                        {
                            if (poly[k].original >= 0)
                            {
                                indices[k] = Keep(poly[k].original, srcVerts, srcNormals, hasNormals);
                                continue;
                            }
                            verts.Add(poly[k].pos);
                            normals.Add(poly[k].normal);
                            indices[k] = verts.Count - 1;
                        }
                        for (var k = 1; k < poly.Count - 1; k++)
                        {
                            output.Add(indices[0]);
                            output.Add(indices[k]);
                            output.Add(indices[k + 1]);
                        }

                        if (segments == null)
                            continue;
                        // The two on-plane points of the clipped polygon form one segment of the cut outline.
                        Vector3? first = null;
                        foreach (var p in poly)
                        {
                            if (!p.onPlane)
                                continue;
                            if (first == null)
                                first = p.pos;
                            else if (p.pos != first.Value)
                            {
                                segments.Add(Oriented(first.Value, p.pos, along));
                                break;
                            }
                        }
                    }
                    submeshes.Add(output);
                }
            }

            /// <summary>The finished mesh with its cap (null if nothing was on this side).</summary>
            public Mesh Build(string name, List<Vector3> capPoints, List<int> capTriangles, Vector3 outward)
            {
                if (verts.Count == 0)
                    return null;
                if (capPoints != null && capTriangles.Count >= 3)
                    AddCap(capPoints, capTriangles, outward, verts, normals, submeshes[0]);
                var mesh = new Mesh { name = name };
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
        }

        /// <summary>
        /// Where an edge crosses the plane. Always computed from the same end of the edge (by position), so the
        /// two triangles sharing an edge produce bit-identical points and the outline chains up exactly.
        /// </summary>
        static Vector3 EdgeCrossing(Vector3 a, Vector3 b, float da, float db, out float f, out bool swapped)
        {
            swapped = a.x > b.x || (a.x == b.x && (a.y > b.y || (a.y == b.y && a.z > b.z)));
            if (swapped)
            {
                (a, b) = (b, a);
                (da, db) = (db, da);
            }
            f = da / (da - db);
            return Vector3.Lerp(a, b, f);
        }

        static void AddOnPlaneEdge(Vector3[] verts, float[] distances, int a, int b, Vector3 along, List<(Vector3, Vector3)> segments)
        {
            if (Mathf.Abs(distances[a]) <= OnPlaneEpsilon && Mathf.Abs(distances[b]) <= OnPlaneEpsilon)
                segments.Add(Oriented(verts[a], verts[b], along));
        }

        /// <summary>
        /// The direction an outline runs where the plane crosses a triangle (from the triangle's facing), so
        /// every outer outline runs one way round and every hole the other.
        /// </summary>
        static Vector3 Along(Vector3 planeNormal, Vector3 a, Vector3 b, Vector3 c) =>
            Vector3.Cross(planeNormal, Vector3.Cross(b - a, c - a));

        static (Vector3, Vector3) Oriented(Vector3 a, Vector3 b, Vector3 along) =>
            Vector3.Dot(b - a, along) >= 0f ? (a, b) : (b, a);

        /// <summary>Outline loops where the plane crosses the given triangles (no new mesh is built).</summary>
        public static List<Loop> Section(IList<Vector3> vertices, IList<int> triangles, Plane plane)
        {
            var segments = new List<(Vector3, Vector3)>();
            var pts = new List<Vector3>(2);
            for (var t = 0; t < triangles.Count; t += 3)
            {
                pts.Clear();
                for (var k = 0; k < 3; k++)
                {
                    var a = vertices[triangles[t + k]];
                    var b = vertices[triangles[t + (k + 1) % 3]];
                    var da = plane.GetDistanceToPoint(a);
                    var db = plane.GetDistanceToPoint(b);
                    if ((da > 0f) != (db > 0f) && Mathf.Abs(da - db) > 1e-12f)
                        pts.Add(EdgeCrossing(a, b, da, db, out _, out _));
                }
                if (pts.Count == 2 && pts[0] != pts[1])
                {
                    var along = Along(plane.normal, vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]]);
                    segments.Add(Oriented(pts[0], pts[1], along));
                }
            }
            return BuildLoops(segments, plane.normal);
        }

        /// <summary>
        /// Chains the cut segments (each running in its outline's direction) into closed loops. Endpoints a few
        /// µm apart are welded, segments that collapse or cancel out are dropped, and small gaps are bridged,
        /// because CAD tessellations are rarely perfectly watertight.
        /// </summary>
        public static List<Loop> BuildLoops(List<(Vector3 a, Vector3 b)> segments, Vector3 normal)
        {
            PlaneBasis(normal, out var u, out var v);

            // Weld endpoints into shared nodes.
            var nodes = new List<Vector3>();
            var grid = new Dictionary<(long, long, long), List<int>>();
            (long, long, long) Cell(Vector3 p) => ((long)Mathf.Floor(p.x / WeldTolerance), (long)Mathf.Floor(p.y / WeldTolerance), (long)Mathf.Floor(p.z / WeldTolerance));
            int Node(Vector3 p)
            {
                var c = Cell(p);
                for (var x = -1; x <= 1; x++)
                    for (var y = -1; y <= 1; y++)
                        for (var z = -1; z <= 1; z++)
                            if (grid.TryGetValue((c.Item1 + x, c.Item2 + y, c.Item3 + z), out var near))
                                foreach (var n in near)
                                    if ((nodes[n] - p).sqrMagnitude <= WeldTolerance * WeldTolerance)
                                        return n;
                nodes.Add(p);
                if (!grid.TryGetValue(c, out var cell))
                    grid[c] = cell = new List<int>(1);
                cell.Add(nodes.Count - 1);
                return nodes.Count - 1;
            }

            // Directed edges; an edge and its reverse cancel out (faces inside the solid, e.g. touching bodies).
            var edges = new HashSet<(int, int)>();
            foreach (var (a, b) in segments)
            {
                int na = Node(a), nb = Node(b);
                if (na != nb && !edges.Remove((nb, na)))
                    edges.Add((na, nb));
            }
            var outgoing = new Dictionary<int, List<int>>();
            foreach (var (a, b) in edges)
            {
                if (!outgoing.TryGetValue(a, out var list))
                    outgoing[a] = list = new List<int>(1);
                list.Add(b);
            }

            Vector2 Flat(int n) => new(Vector3.Dot(nodes[n], u), Vector3.Dot(nodes[n], v));
            int Take(int from, int previous)
            {
                if (!outgoing.TryGetValue(from, out var list) || list.Count == 0)
                    return -1;
                var pick = 0;
                if (list.Count > 1 && previous >= 0)
                {
                    // Several ways on (outlines touching at a point): take the sharpest left turn.
                    var incoming = Flat(from) - Flat(previous);
                    var best = float.MinValue;
                    for (var i = 0; i < list.Count; i++)
                    {
                        var turn = Vector2.SignedAngle(incoming, Flat(list[i]) - Flat(from));
                        if (turn > best)
                        {
                            best = turn;
                            pick = i;
                        }
                    }
                }
                var next = list[pick];
                list.RemoveAt(pick);
                return next;
            }

            var closed = new List<List<int>>();
            var open = new List<List<int>>();
            foreach (var start in new List<int>(outgoing.Keys))
            {
                while (outgoing[start].Count > 0)
                {
                    var path = new List<int> { start };
                    var where = new Dictionary<int, int> { [start] = 0 };
                    var previous = -1;
                    var current = start;
                    while (true)
                    {
                        var next = Take(current, previous);
                        if (next < 0)
                        {
                            open.Add(path);
                            break;
                        }
                        if (where.TryGetValue(next, out var at))
                        {
                            // Came back round: split off the closed part and carry on from here.
                            var loop = path.GetRange(at, path.Count - at);
                            closed.Add(loop);
                            foreach (var n in loop)
                                where.Remove(n);
                            path.RemoveRange(at, path.Count - at);
                            if (path.Count == 0 && (!outgoing.TryGetValue(next, out var rest) || rest.Count == 0))
                                break;
                        }
                        where[next] = path.Count;
                        path.Add(next);
                        previous = current;
                        current = next;
                    }
                }
            }

            // Bridge small gaps: join each open chain's end to the nearest chain start.
            for (var changed = true; changed && open.Count > 0;)
            {
                changed = false;
                for (var i = 0; i < open.Count && !changed; i++)
                {
                    var end = nodes[open[i][open[i].Count - 1]];
                    var best = -1;
                    var bestDistance = HealTolerance * HealTolerance;
                    for (var j = 0; j < open.Count; j++)
                    {
                        var d = (nodes[open[j][0]] - end).sqrMagnitude;
                        if (d <= bestDistance)
                        {
                            bestDistance = d;
                            best = j;
                        }
                    }
                    if (best == i)
                    {
                        closed.Add(open[i]);
                        open.RemoveAt(i);
                        changed = true;
                    }
                    else if (best >= 0)
                    {
                        open[i].AddRange(open[best]);
                        open.RemoveAt(best);
                        changed = true;
                    }
                }
            }

            var loops = new List<Loop>();
            foreach (var path in closed)
            {
                var loop = new Loop();
                foreach (var n in path)
                    loop.points3D.Add(nodes[n]);
                RemoveCollinear(loop.points3D);
                if (loop.points3D.Count < 3)
                    continue;
                foreach (var p in loop.points3D)
                    loop.points2D.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
                loop.signedArea = SignedArea(loop.points2D);
                if (Mathf.Abs(loop.signedArea) > 1e-11f)
                    loops.Add(loop);
            }
            return loops;
        }

        static float SignedArea(List<Vector2> points)
        {
            double area = 0;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                area += (double)points[j].x * points[i].y - (double)points[i].x * points[j].y;
            return (float)(area / 2);
        }

        /// <summary>Drops points that sit on the straight line between their neighbours.</summary>
        static void RemoveCollinear(List<Vector3> points)
        {
            for (var i = 0; i < points.Count && points.Count > 3;)
            {
                var prev = points[(i + points.Count - 1) % points.Count];
                var next = points[(i + 1) % points.Count];
                var line = next - prev;
                var offset = points[i] - prev;
                var distance = line.sqrMagnitude > 0f ? Vector3.Cross(line, offset).magnitude / line.magnitude : offset.magnitude;
                if (distance < CollinearTolerance)
                    points.RemoveAt(i);
                else
                    i++;
            }
        }

        /// <summary>
        /// Simplifies a loop's outline (Douglas–Peucker, points3D kept in step): rounded corners become a few
        /// straight edges. For collision outlines, where a tenth of a millimetre doesn't matter.
        /// </summary>
        public static void Simplify(Loop loop, float tolerance)
        {
            var count = loop.points2D.Count;
            if (count <= 4)
                return;
            // Split the loop at the two points furthest apart.
            var far = 0;
            var farDistance = 0f;
            for (var i = 1; i < count; i++)
            {
                var d = (loop.points2D[i] - loop.points2D[0]).sqrMagnitude;
                if (d > farDistance)
                {
                    farDistance = d;
                    far = i;
                }
            }
            var keep = new bool[count];
            keep[0] = keep[far] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, far));
            stack.Push((far, count));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                var pa = loop.points2D[a];
                var line = loop.points2D[b % count] - pa;
                var worst = -1;
                var worstDistance = tolerance;
                for (var i = a + 1; i < b; i++)
                {
                    var offset = loop.points2D[i] - pa;
                    var d = line.sqrMagnitude > 0f ? Mathf.Abs(line.x * offset.y - line.y * offset.x) / line.magnitude : offset.magnitude;
                    if (d > worstDistance)
                    {
                        worstDistance = d;
                        worst = i;
                    }
                }
                if (worst < 0)
                    continue;
                keep[worst] = true;
                stack.Push((a, worst));
                stack.Push((worst, b));
            }
            var flat = new List<Vector2>();
            var points = new List<Vector3>();
            for (var i = 0; i < count; i++)
            {
                if (!keep[i])
                    continue;
                flat.Add(loop.points2D[i]);
                points.Add(loop.points3D[i]);
            }
            if (flat.Count < 3)
                return;
            loop.points2D.Clear();
            loop.points2D.AddRange(flat);
            loop.points3D.Clear();
            loop.points3D.AddRange(points);
            loop.signedArea = SignedArea(loop.points2D);
        }

        /// <summary>Groups loops into outlines with their holes (by nesting depth).</summary>
        public static List<(Loop outer, List<Loop> holes)> Nest(List<Loop> loops)
        {
            var parent = new int[loops.Count];
            var depth = new int[loops.Count];
            for (var i = 0; i < loops.Count; i++)
            {
                parent[i] = -1;
                var best = float.MaxValue;
                for (var j = 0; j < loops.Count; j++)
                {
                    if (i == j)
                        continue;
                    var area = Mathf.Abs(loops[j].signedArea);
                    if (area > Mathf.Abs(loops[i].signedArea) && area < best && Contains(loops[j].points2D, loops[i].points2D[0]))
                    {
                        best = area;
                        parent[i] = j;
                    }
                }
            }
            for (var i = 0; i < loops.Count; i++)
                for (var p = parent[i]; p >= 0; p = parent[p])
                    depth[i]++;

            var result = new List<(Loop, List<Loop>)>();
            var index = new Dictionary<int, int>();
            for (var i = 0; i < loops.Count; i++)
            {
                if (depth[i] % 2 != 0)
                    continue;
                index[i] = result.Count;
                result.Add((loops[i], new List<Loop>()));
            }
            for (var i = 0; i < loops.Count; i++)
                if (depth[i] % 2 == 1 && index.TryGetValue(parent[i], out var outer))
                    result[outer].Item2.Add(loops[i]);
            return result;
        }

        /// <summary>Triangulates each outline (with holes) into 2D triangles: returns points and indices.</summary>
        public static (List<Vector2> points, List<Vector3> points3D, List<int> triangles) Triangulate(List<Loop> loops)
        {
            var points = new List<Vector2>();
            var points3D = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var (outer, holes) in Nest(loops))
            {
                var xy = new List<double>();
                var holeStarts = new List<int>();
                var baseIndex = points.Count;
                void Add(Loop loop)
                {
                    for (var i = 0; i < loop.points2D.Count; i++)
                    {
                        xy.Add(loop.points2D[i].x);
                        xy.Add(loop.points2D[i].y);
                        points.Add(loop.points2D[i]);
                        points3D.Add(loop.points3D[i]);
                    }
                }
                Add(outer);
                foreach (var hole in holes)
                {
                    holeStarts.Add(xy.Count / 2);
                    Add(hole);
                }
                foreach (var index in Earcut.Triangulate(xy, holeStarts))
                    triangles.Add(baseIndex + index);
            }
            return (points, points3D, triangles);
        }

        static void AddCap(List<Vector3> points3D, List<int> triangles, Vector3 outward, List<Vector3> verts, List<Vector3> normals, List<int> output)
        {
            var start = verts.Count;
            foreach (var p in points3D)
            {
                verts.Add(p);
                normals.Add(outward);
            }
            for (var t = 0; t < triangles.Count; t += 3)
            {
                int a = start + triangles[t], b = start + triangles[t + 1], c = start + triangles[t + 2];
                var faceNormal = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (Vector3.Dot(faceNormal, outward) < 0f)
                    (b, c) = (c, b);
                output.Add(a);
                output.Add(b);
                output.Add(c);
            }
        }

        public static bool Contains(List<Vector2> polygon, Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Enclosed volume of a closed mesh (m³) via signed tetrahedra.</summary>
        public static float Volume(Mesh mesh)
        {
            var v = mesh.vertices;
            double volume = 0;
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (var i = 0; i < t.Length; i += 3)
                    volume += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6.0;
            }
            return Mathf.Abs((float)volume);
        }
    }
}
