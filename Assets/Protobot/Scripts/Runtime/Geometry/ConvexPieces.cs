using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Geometry
{
    /// <summary>
    /// Splits a triangulated 2D shape into a few convex pieces (Hertel–Mehlhorn: merge neighbouring
    /// triangles while the result stays convex). Physics colliders must be convex, so a C-channel's
    /// cross-section becomes a handful of convex pieces instead of one solid box.
    /// </summary>
    public static class ConvexPieces
    {
        public static List<List<Vector2>> FromTriangles(IList<Vector2> points, IList<int> triangles)
        {
            // Each polygon is a counter-clockwise list of point indices.
            var polygons = new List<List<int>>();
            for (var t = 0; t < triangles.Count; t += 3)
            {
                var tri = new List<int> { triangles[t], triangles[t + 1], triangles[t + 2] };
                if (Cross(points[tri[0]], points[tri[1]], points[tri[2]]) < 0f)
                    (tri[1], tri[2]) = (tri[2], tri[1]);
                if (Mathf.Abs(Cross(points[tri[0]], points[tri[1]], points[tri[2]])) > 1e-14f)
                    polygons.Add(tri);
            }

            var merged = true;
            while (merged)
            {
                merged = false;
                for (var i = 0; i < polygons.Count && !merged; i++)
                {
                    for (var j = i + 1; j < polygons.Count && !merged; j++)
                    {
                        var combined = TryMerge(polygons[i], polygons[j], points);
                        if (combined == null)
                            continue;
                        polygons[i] = combined;
                        polygons.RemoveAt(j);
                        merged = true;
                    }
                }
            }

            var result = new List<List<Vector2>>(polygons.Count);
            foreach (var polygon in polygons)
            {
                var pts = new List<Vector2>(polygon.Count);
                foreach (var index in polygon)
                    pts.Add(points[index]);
                result.Add(pts);
            }
            return result;
        }

        /// <summary>Joins two polygons sharing an edge, if the result is convex.</summary>
        static List<int> TryMerge(List<int> p, List<int> q, IList<Vector2> points)
        {
            for (var i = 0; i < p.Count; i++)
            {
                int a = p[i], b = p[(i + 1) % p.Count];
                // In q the shared edge runs the other way (b -> a).
                var j = q.IndexOf(b);
                if (j < 0 || q[(j + 1) % q.Count] != a)
                    continue;

                var combined = new List<int>(p.Count + q.Count - 2);
                for (var k = 0; k < p.Count; k++)
                    combined.Add(p[(i + 1 + k) % p.Count]);           // b ... a around p
                for (var k = 2; k < q.Count; k++)
                    combined.Add(q[(j + k) % q.Count]);               // q's vertices between a and b
                return IsConvex(combined, points) ? combined : null;
            }
            return null;
        }

        static bool IsConvex(List<int> polygon, IList<Vector2> points)
        {
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = points[polygon[i]];
                var b = points[polygon[(i + 1) % polygon.Count]];
                var c = points[polygon[(i + 2) % polygon.Count]];
                if (Cross(a, b, c) < -1e-12f)
                    return false;
            }
            return true;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        /// <summary>A convex prism mesh: the 2D polygon (in the u/v plane) swept from start to end along the axis.</summary>
        public static Mesh Extrude(List<Vector2> polygon, Vector3 origin, Vector3 u, Vector3 v, Vector3 axis, float start, float end)
        {
            var n = polygon.Count;
            var verts = new Vector3[n * 2];
            for (var i = 0; i < n; i++)
            {
                var p = origin + u * polygon[i].x + v * polygon[i].y;
                verts[i] = p + axis * start;
                verts[n + i] = p + axis * end;
            }
            var tris = new List<int>();
            for (var i = 1; i < n - 1; i++)
            {
                tris.Add(0); tris.Add(i + 1); tris.Add(i);
                tris.Add(n); tris.Add(n + i); tris.Add(n + i + 1);
            }
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                tris.Add(i); tris.Add(j); tris.Add(n + j);
                tris.Add(i); tris.Add(n + j); tris.Add(n + i);
            }
            var mesh = new Mesh { name = "Stock collider" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
