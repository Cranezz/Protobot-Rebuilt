using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Motion {
    // V2: lines one model up with another of the same part made elsewhere (a VEX-CAD model onto the original's):
    // the square turn (of 24) and shift where the first's points land closest to the second's surface points.
    public static class MeshAlign {
        public static (Quaternion turn, Vector3 shift, float error) Find(IList<Vector3> from, IList<Vector3> to) {
            var a = Sample(from, 300);
            var b = Sample(to, 300);
            // V2: the boxes from every point (a sample of 300 could miss a thin part, like a goal's pole).
            var boxA = Box(new List<Vector3>(from));
            var boxB = Box(new List<Vector3>(to));
            // Every point of the target, by cell, for exact nearest points.
            var grid = new Dictionary<Vector3Int, List<Vector3>>();
            foreach (var p in to) {
                var key = Vector3Int.FloorToInt(p / Cell);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Vector3>();
                list.Add(p);
            }
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var best = (Quaternion.identity, Vector3.zero, float.MaxValue);
            foreach (var forward in axes)
                foreach (var up in axes) {
                    if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.1f) continue;
                    var turn = Quaternion.LookRotation(forward, up);
                    var turned = new List<Vector3>(a.Count);
                    foreach (var p in a) turned.Add(turn * p);
                    var box = new Bounds(turn * boxA.center, Abs(turn * boxA.size));   // exact for square turns
                    if ((box.size - boxB.size).magnitude > boxB.size.magnitude * 0.25f) continue;  // other proportions
                    var shift = boxB.center - box.center;
                    var error = 0f;
                    for (var pass = 0; pass < 4; pass++) {
                        var offset = Vector3.zero;
                        error = 0f;
                        foreach (var p in turned) {
                            var q = Nearest(grid, b, p + shift);
                            offset += q - (p + shift);
                            error += (q - (p + shift)).magnitude;
                        }
                        error /= turned.Count;
                        if (pass < 3) shift += offset / turned.Count;
                    }
                    if (error < best.Item3) best = (turn, shift, error);
                }
            return best;
        }

        private const float Cell = 0.1f;

        private static Vector3 Nearest(Dictionary<Vector3Int, List<Vector3>> grid, List<Vector3> sample, Vector3 p) {
            var key = Vector3Int.FloorToInt(p / Cell);
            var best = Vector3.zero;
            var distance = float.MaxValue;
            for (var x = -1; x <= 1; x++)
                for (var y = -1; y <= 1; y++)
                    for (var z = -1; z <= 1; z++)
                        if (grid.TryGetValue(key + new Vector3Int(x, y, z), out var list))
                            foreach (var q in list) {
                                var d = (q - p).sqrMagnitude;
                                if (d < distance) { distance = d; best = q; }
                            }
            return distance < float.MaxValue ? best : Nearest(sample, p);
        }

        private static Vector3 Nearest(List<Vector3> points, Vector3 p) {
            var best = points[0];
            var distance = float.MaxValue;
            foreach (var q in points) {
                var d = (q - p).sqrMagnitude;
                if (d < distance) { distance = d; best = q; }
            }
            return best;
        }

        private static List<Vector3> Sample(IList<Vector3> points, int count) {
            var list = new List<Vector3>(count);
            var step = Mathf.Max(1, points.Count / count);
            for (var i = 0; i < points.Count; i += step) list.Add(points[i]);
            return list;
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static Bounds Box(List<Vector3> points) {
            var box = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) box.Encapsulate(p);
            return box;
        }

        /// <summary>A copy of a mesh turned and moved (all its sub-meshes kept).</summary>
        public static Mesh Moved(Mesh mesh, Quaternion turn, Vector3 shift) {
            var copy = Object.Instantiate(mesh);
            var vertices = copy.vertices;
            for (var i = 0; i < vertices.Length; i++) vertices[i] = turn * vertices[i] + shift;
            copy.vertices = vertices;
            var normals = copy.normals;
            if (normals.Length == vertices.Length) {
                for (var i = 0; i < normals.Length; i++) normals[i] = turn * normals[i];
                copy.normals = normals;
            }
            copy.RecalculateBounds();
            copy.name = mesh.name;
            return copy;
        }
    }
}
