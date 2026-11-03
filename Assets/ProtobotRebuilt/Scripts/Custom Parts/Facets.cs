using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Custom {
    // V2: the flat faces of a mesh and the outlines inside them, for "Pick a shape as a hole" in the import
    // window: triangles that touch and lie in one plane make a face; the face's border edges, chained, make
    // loops; the biggest loop is the face's outside and every other one is a shape cut into it (a hole's rim).
    public class Facets {
        public class Loop {
            public List<Vector3> points;
            public Vector3 normal;   // the face's, out of the material
            public Vector3 center;
        }

        public readonly List<Loop> innerLoops = new();

        public Facets(IList<Vector3> vertices, IList<int> triangles) {
            // Corners that share a position are one point (hard edges were split into separate vertices).
            var ids = new Dictionary<Vector3Int, int>();
            var positions = new List<Vector3>();
            var pid = new int[vertices.Count];
            for (var i = 0; i < vertices.Count; i++) {
                var v = vertices[i];
                var key = new Vector3Int(Mathf.RoundToInt(v.x * 1e4f), Mathf.RoundToInt(v.y * 1e4f), Mathf.RoundToInt(v.z * 1e4f));
                if (!ids.TryGetValue(key, out var id)) {
                    id = positions.Count;
                    ids[key] = id;
                    positions.Add(v);
                }
                pid[i] = id;
            }

            var count = triangles.Count / 3;
            var corner = new int[count * 3];
            var normals = new Vector3[count];
            var planes = new float[count];
            var valid = new bool[count];
            for (var t = 0; t < count; t++) {
                for (var k = 0; k < 3; k++) corner[t * 3 + k] = pid[triangles[t * 3 + k]];
                Vector3 a = positions[corner[t * 3]], b = positions[corner[t * 3 + 1]], c = positions[corner[t * 3 + 2]];
                var cross = Vector3.Cross(b - a, c - a);
                var length = cross.magnitude;
                valid[t] = length > 1e-9f && corner[t * 3] != corner[t * 3 + 1] && corner[t * 3 + 1] != corner[t * 3 + 2] && corner[t * 3] != corner[t * 3 + 2];
                if (!valid[t]) continue;
                normals[t] = cross / length;
                planes[t] = Vector3.Dot(normals[t], a);
            }

            // The triangle across each edge (one, when two triangles share it; edges of three or more are borders).
            var firstOnEdge = new Dictionary<long, int>();
            var across = new int[count * 3];
            for (var i = 0; i < across.Length; i++) across[i] = -1;
            long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            for (var t = 0; t < count; t++) {
                if (!valid[t]) continue;
                for (var k = 0; k < 3; k++) {
                    var key = EdgeKey(corner[t * 3 + k], corner[t * 3 + (k + 1) % 3]);
                    if (firstOnEdge.TryGetValue(key, out var other)) {
                        if (other < 0) continue;  // already shared by two
                        var ot = other / 3;
                        across[t * 3 + k] = ot;
                        across[other] = t;
                        firstOnEdge[key] = -1;
                    }
                    else firstOnEdge[key] = t * 3 + k;
                }
            }

            // Faces: neighbours in the same plane.
            var facet = new int[count];
            for (var i = 0; i < count; i++) facet[i] = -1;
            var queue = new Queue<int>();
            var members = new List<int>();
            var next = new Dictionary<int, int>();
            var facetCount = 0;
            for (var seed = 0; seed < count; seed++) {
                if (!valid[seed] || facet[seed] >= 0) continue;
                var f = facetCount++;
                var n = normals[seed];
                var d = planes[seed];
                members.Clear();
                facet[seed] = f;
                queue.Enqueue(seed);
                while (queue.Count > 0) {
                    var t = queue.Dequeue();
                    members.Add(t);
                    for (var k = 0; k < 3; k++) {
                        var o = across[t * 3 + k];
                        if (o < 0 || facet[o] >= 0 || !valid[o]) continue;
                        if (Vector3.Dot(normals[o], n) < 0.9995f || Mathf.Abs(planes[o] - d) > 1e-3f) continue;
                        facet[o] = f;
                        queue.Enqueue(o);
                    }
                }
                if (members.Count < 2) continue;  // one triangle can't have a shape inside it

                // Its border, chained into loops (each edge runs the way its triangle winds).
                next.Clear();
                foreach (var t in members)
                    for (var k = 0; k < 3; k++) {
                        var o = across[t * 3 + k];
                        if (o >= 0 && facet[o] == f) continue;
                        next[corner[t * 3 + k]] = corner[t * 3 + (k + 1) % 3];
                    }
                var loops = new List<(List<Vector3> points, float area)>();
                var u = Vector3.ProjectOnPlane(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right, n).normalized;
                var w = Vector3.Cross(n, u);
                while (next.Count > 0) {
                    var start = -1;
                    foreach (var key in next.Keys) { start = key; break; }
                    var points = new List<Vector3>();
                    var at = start;
                    var guard = 0;
                    while (next.TryGetValue(at, out var to) && guard++ < 100000) {
                        next.Remove(at);
                        points.Add(positions[at]);
                        at = to;
                        if (at == start) break;
                    }
                    if (points.Count < 3) continue;
                    var area = 0f;
                    for (var i = 0; i < points.Count; i++) {
                        Vector3 p = points[i], q = points[(i + 1) % points.Count];
                        area += Vector3.Dot(p, u) * Vector3.Dot(q, w) - Vector3.Dot(q, u) * Vector3.Dot(p, w);
                    }
                    loops.Add((points, Mathf.Abs(area) / 2f));
                }
                if (loops.Count < 2) continue;
                var outside = 0;
                for (var i = 1; i < loops.Count; i++)
                    if (loops[i].area > loops[outside].area) outside = i;
                for (var i = 0; i < loops.Count; i++) {
                    if (i == outside || loops[i].area < 1e-4f) continue;
                    var center = Vector3.zero;
                    foreach (var p in loops[i].points) center += p;
                    innerLoops.Add(new Loop { points = loops[i].points, normal = n, center = center / loops[i].points.Count });
                }
            }
        }
    }
}
