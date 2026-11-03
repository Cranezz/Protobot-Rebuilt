using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Custom {
    // V2: mesh clean-up for imported parts: welding the corners files repeat per triangle, lowering the
    // triangle count (quality presets) and splitting vertices at sharp edges so flat faces shade flat.
    public static class MeshTools {
        /// <summary>Merges vertices closer than <paramref name="tolerance"/> and drops collapsed triangles.</summary>
        public static void Weld(List<Vector3> vertices, List<int> triangles, float tolerance) {
            var cell = 1f / Mathf.Max(tolerance, 1e-7f);
            var ids = new Dictionary<Vector3Int, int>();
            var welded = new List<Vector3>();
            var map = new int[vertices.Count];
            for (var i = 0; i < vertices.Count; i++) {
                var key = Vector3Int.RoundToInt(vertices[i] * cell);
                if (!ids.TryGetValue(key, out var id)) {
                    id = welded.Count;
                    ids[key] = id;
                    welded.Add(vertices[i]);
                }
                map[i] = id;
            }
            var kept = new List<int>(triangles.Count);
            for (var t = 0; t + 2 < triangles.Count; t += 3) {
                int a = map[triangles[t]], b = map[triangles[t + 1]], c = map[triangles[t + 2]];
                if (a == b || b == c || a == c) continue;
                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }
            vertices.Clear();
            vertices.AddRange(welded);
            triangles.Clear();
            triangles.AddRange(kept);
        }

        /// <summary>
        /// Gives each vertex one copy per smooth group of the triangles around it: faces meeting at more than
        /// <paramref name="angle"/> degrees get their own vertices, so RecalculateNormals keeps the edge sharp.
        /// </summary>
        public static void SplitHardEdges(List<Vector3> vertices, List<int> triangles, float angle) {
            var cos = Mathf.Cos(angle * Mathf.Deg2Rad);
            var triangleCount = triangles.Count / 3;
            var normals = new Vector3[triangleCount];
            for (var t = 0; t < triangleCount; t++)
                normals[t] = Vector3.Cross(vertices[triangles[t * 3 + 1]] - vertices[triangles[t * 3]],
                                           vertices[triangles[t * 3 + 2]] - vertices[triangles[t * 3]]).normalized;
            var around = new List<int>[vertices.Count];
            for (var i = 0; i < triangles.Count; i++)
                (around[triangles[i]] ??= new List<int>(6)).Add(i);
            var newVertices = new List<Vector3>(vertices.Count * 2);
            var newTriangles = new int[triangles.Count];
            var groupNormals = new List<Vector3>();
            var groupIds = new List<int>();
            for (var v = 0; v < vertices.Count; v++) {
                if (around[v] == null) continue;
                groupNormals.Clear();
                groupIds.Clear();
                foreach (var corner in around[v]) {
                    var n = normals[corner / 3];
                    var group = -1;
                    for (var g = 0; g < groupNormals.Count && group < 0; g++)
                        if (Vector3.Dot(groupNormals[g], n) >= cos) group = g;
                    if (group < 0) {
                        group = groupNormals.Count;
                        groupNormals.Add(n);
                        groupIds.Add(newVertices.Count);
                        newVertices.Add(vertices[v]);
                    }
                    newTriangles[corner] = groupIds[group];
                }
            }
            vertices.Clear();
            vertices.AddRange(newVertices);
            triangles.Clear();
            triangles.AddRange(newTriangles);
        }

        // ------------------------------------------------------------------ simplifying

        // Quadric error edge collapse (Garland & Heckbert; the iterative threshold scheme of
        // Sp4cerat's Fast Quadric Mesh Simplification, MIT licence).
        private struct Quadric {
            public double m0, m1, m2, m3, m4, m5, m6, m7, m8, m9;

            public Quadric(double a, double b, double c, double d) {
                m0 = a * a; m1 = a * b; m2 = a * c; m3 = a * d;
                m4 = b * b; m5 = b * c; m6 = b * d;
                m7 = c * c; m8 = c * d;
                m9 = d * d;
            }

            public static Quadric operator +(Quadric x, Quadric y) => new() {
                m0 = x.m0 + y.m0, m1 = x.m1 + y.m1, m2 = x.m2 + y.m2, m3 = x.m3 + y.m3, m4 = x.m4 + y.m4,
                m5 = x.m5 + y.m5, m6 = x.m6 + y.m6, m7 = x.m7 + y.m7, m8 = x.m8 + y.m8, m9 = x.m9 + y.m9,
            };

            private double M(int i) => i switch {
                0 => m0, 1 => m1, 2 => m2, 3 => m3, 4 => m4, 5 => m5, 6 => m6, 7 => m7, 8 => m8, _ => m9,
            };

            public double Det(int a11, int a12, int a13, int a21, int a22, int a23, int a31, int a32, int a33) =>
                M(a11) * M(a22) * M(a33) + M(a13) * M(a21) * M(a32) + M(a12) * M(a23) * M(a31)
                - M(a13) * M(a22) * M(a31) - M(a11) * M(a23) * M(a32) - M(a12) * M(a21) * M(a33);

            public double Error(Vector3d p) =>
                m0 * p.x * p.x + 2 * m1 * p.x * p.y + 2 * m2 * p.x * p.z + 2 * m3 * p.x + m4 * p.y * p.y
                + 2 * m5 * p.y * p.z + 2 * m6 * p.y + m7 * p.z * p.z + 2 * m8 * p.z + m9;
        }

        private struct Vector3d {
            public double x, y, z;
            public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
            public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
            public static Vector3d operator *(Vector3d a, double s) => new(a.x * s, a.y * s, a.z * s);
            public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
            public static Vector3d Cross(Vector3d a, Vector3d b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
            public Vector3d Normalized {
                get {
                    var length = Math.Sqrt(x * x + y * y + z * z);
                    return length > 1e-30 ? new Vector3d(x / length, y / length, z / length) : this;
                }
            }
        }

        private class Tri {
            public readonly int[] v = new int[3];
            public readonly double[] err = new double[4];
            public bool deleted, dirty;
            public Vector3d n;
        }

        private class Vert {
            public Vector3d p;
            public int tstart, tcount;
            public Quadric q;
            public bool border;
        }

        private struct Ref {
            public int tid, tvertex;
        }

        /// <summary>Lowers a welded mesh to about <paramref name="target"/> triangles, keeping its shape.</summary>
        public static void Simplify(List<Vector3> vertices, List<int> triangles, int target, double aggressiveness = 7) {
            var verts = new List<Vert>(vertices.Count);
            foreach (var v in vertices) verts.Add(new Vert { p = new Vector3d(v.x, v.y, v.z) });
            var tris = new List<Tri>(triangles.Count / 3);
            for (var i = 0; i + 2 < triangles.Count; i += 3) {
                var t = new Tri();
                t.v[0] = triangles[i];
                t.v[1] = triangles[i + 1];
                t.v[2] = triangles[i + 2];
                tris.Add(t);
            }
            var refs = new List<Ref>();
            var deletedTriangles = 0;
            var startCount = tris.Count;
            var deleted0 = new List<bool>();
            var deleted1 = new List<bool>();

            for (var iteration = 0; iteration < 100; iteration++) {
                if (startCount - deletedTriangles <= target) break;
                if (iteration % 5 == 0) UpdateMesh(iteration, verts, tris, refs);
                foreach (var t in tris) t.dirty = false;
                var threshold = 1e-9 * Math.Pow(iteration + 3, aggressiveness);
                foreach (var t in tris) {
                    if (t.err[3] > threshold || t.deleted || t.dirty) continue;
                    for (var j = 0; j < 3; j++) {
                        if (t.err[j] >= threshold) continue;
                        var i0 = t.v[j];
                        var i1 = t.v[(j + 1) % 3];
                        var v0 = verts[i0];
                        var v1 = verts[i1];
                        if (v0.border != v1.border) continue;
                        CalculateError(verts, i0, i1, out var p);
                        Resize(deleted0, v0.tcount);
                        Resize(deleted1, v1.tcount);
                        if (Flipped(p, i1, v0, deleted0, verts, tris, refs)) continue;
                        if (Flipped(p, i0, v1, deleted1, verts, tris, refs)) continue;
                        v0.p = p;
                        v0.q = v1.q + v0.q;
                        var tstart = refs.Count;
                        UpdateTriangles(i0, v0, deleted0, ref deletedTriangles, verts, tris, refs);
                        UpdateTriangles(i0, v1, deleted1, ref deletedTriangles, verts, tris, refs);
                        var tcount = refs.Count - tstart;
                        if (tcount <= v0.tcount) {
                            for (var k = 0; k < tcount; k++) refs[v0.tstart + k] = refs[tstart + k];
                        }
                        else v0.tstart = tstart;
                        v0.tcount = tcount;
                        break;
                    }
                    if (startCount - deletedTriangles <= target) break;
                }
            }

            // Compact into the lists.
            foreach (var v in verts) v.tcount = 0;
            var live = new List<Tri>();
            foreach (var t in tris) {
                if (t.deleted) continue;
                live.Add(t);
                for (var j = 0; j < 3; j++) verts[t.v[j]].tcount = 1;
            }
            vertices.Clear();
            for (var i = 0; i < verts.Count; i++) {
                if (verts[i].tcount == 0) continue;
                verts[i].tstart = vertices.Count;
                vertices.Add(new Vector3((float)verts[i].p.x, (float)verts[i].p.y, (float)verts[i].p.z));
            }
            triangles.Clear();
            foreach (var t in live)
                for (var j = 0; j < 3; j++) triangles.Add(verts[t.v[j]].tstart);
        }

        private static void Resize(List<bool> list, int count) {
            while (list.Count < count) list.Add(false);
            for (var i = 0; i < count; i++) list[i] = false;
        }

        private static double CalculateError(List<Vert> verts, int a, int b, out Vector3d result) {
            var q = verts[a].q + verts[b].q;
            var border = verts[a].border && verts[b].border;
            var det = q.Det(0, 1, 2, 1, 4, 5, 2, 5, 7);
            if (Math.Abs(det) > 1e-15 && !border) {
                result = new Vector3d(
                    -1 / det * q.Det(1, 2, 3, 4, 5, 6, 5, 7, 8),
                    1 / det * q.Det(0, 2, 3, 1, 5, 6, 2, 7, 8),
                    -1 / det * q.Det(0, 1, 3, 1, 4, 6, 2, 5, 8));
                return q.Error(result);
            }
            var p1 = verts[a].p;
            var p2 = verts[b].p;
            var p3 = (p1 + p2) * 0.5;
            double e1 = q.Error(p1), e2 = q.Error(p2), e3 = q.Error(p3);
            var error = Math.Min(e1, Math.Min(e2, e3));
            result = error == e1 ? p1 : error == e2 ? p2 : p3;
            return error;
        }

        private static bool Flipped(Vector3d p, int other, Vert v0, List<bool> deleted, List<Vert> verts, List<Tri> tris, List<Ref> refs) {
            for (var k = 0; k < v0.tcount; k++) {
                var r = refs[v0.tstart + k];
                var t = tris[r.tid];
                if (t.deleted) continue;
                var s = r.tvertex;
                var id1 = t.v[(s + 1) % 3];
                var id2 = t.v[(s + 2) % 3];
                if (id1 == other || id2 == other) {
                    deleted[k] = true;
                    continue;
                }
                var d1 = (verts[id1].p - p).Normalized;
                var d2 = (verts[id2].p - p).Normalized;
                if (Math.Abs(Vector3d.Dot(d1, d2)) > 0.999) return true;
                var n = Vector3d.Cross(d1, d2).Normalized;
                deleted[k] = false;
                if (Vector3d.Dot(n, t.n) < 0.2) return true;
            }
            return false;
        }

        private static void UpdateTriangles(int i0, Vert v, List<bool> deleted, ref int deletedTriangles, List<Vert> verts, List<Tri> tris, List<Ref> refs) {
            for (var k = 0; k < v.tcount; k++) {
                var r = refs[v.tstart + k];
                var t = tris[r.tid];
                if (t.deleted) continue;
                if (deleted[k]) {
                    t.deleted = true;
                    deletedTriangles++;
                    continue;
                }
                t.v[r.tvertex] = i0;
                t.dirty = true;
                t.err[0] = CalculateError(verts, t.v[0], t.v[1], out _);
                t.err[1] = CalculateError(verts, t.v[1], t.v[2], out _);
                t.err[2] = CalculateError(verts, t.v[2], t.v[0], out _);
                t.err[3] = Math.Min(t.err[0], Math.Min(t.err[1], t.err[2]));
                refs.Add(r);
            }
        }

        private static void UpdateMesh(int iteration, List<Vert> verts, List<Tri> tris, List<Ref> refs) {
            if (iteration > 0) tris.RemoveAll(t => t.deleted);
            foreach (var v in verts) {
                v.tstart = 0;
                v.tcount = 0;
            }
            foreach (var t in tris)
                for (var j = 0; j < 3; j++) verts[t.v[j]].tcount++;
            var start = 0;
            foreach (var v in verts) {
                v.tstart = start;
                start += v.tcount;
                v.tcount = 0;
            }
            refs.Clear();
            for (var i = 0; i < tris.Count * 3; i++) refs.Add(default);
            for (var i = 0; i < tris.Count; i++) {
                var t = tris[i];
                for (var j = 0; j < 3; j++) {
                    var v = verts[t.v[j]];
                    refs[v.tstart + v.tcount] = new Ref { tid = i, tvertex = j };
                    v.tcount++;
                }
            }
            if (iteration != 0) return;

            // Borders: edges with one triangle.
            var vcount = new List<int>();
            var vids = new List<int>();
            foreach (var v in verts) v.border = false;
            for (var i = 0; i < verts.Count; i++) {
                var v = verts[i];
                vcount.Clear();
                vids.Clear();
                for (var j = 0; j < v.tcount; j++) {
                    var t = tris[refs[v.tstart + j].tid];
                    for (var k = 0; k < 3; k++) {
                        var id = t.v[k];
                        var found = vids.IndexOf(id);
                        if (found < 0) {
                            vids.Add(id);
                            vcount.Add(1);
                        }
                        else vcount[found]++;
                    }
                }
                for (var j = 0; j < vids.Count; j++)
                    if (vcount[j] == 1) verts[vids[j]].border = true;
            }
            // Quadrics from the planes of the triangles around each vertex.
            foreach (var v in verts) v.q = default;
            foreach (var t in tris) {
                var p0 = verts[t.v[0]].p;
                var n = Vector3d.Cross(verts[t.v[1]].p - p0, verts[t.v[2]].p - p0).Normalized;
                t.n = n;
                var q = new Quadric(n.x, n.y, n.z, -Vector3d.Dot(n, p0));
                for (var j = 0; j < 3; j++) verts[t.v[j]].q = verts[t.v[j]].q + q;
            }
            foreach (var t in tris) {
                t.err[0] = CalculateError(verts, t.v[0], t.v[1], out _);
                t.err[1] = CalculateError(verts, t.v[1], t.v[2], out _);
                t.err[2] = CalculateError(verts, t.v[2], t.v[0], out _);
                t.err[3] = Math.Min(t.err[0], Math.Min(t.err[1], t.err[2]));
            }
        }
    }
}
