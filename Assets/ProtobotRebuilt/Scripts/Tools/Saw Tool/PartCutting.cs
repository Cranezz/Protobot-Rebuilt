using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Parts_List;
using Protobot.Geometry;
using UnityEngine;

namespace Protobot.Tools {
    // V2: cuts any part's mesh along a plane (MeshSlicer, capped), for parts that aren't made from a hole count or
    // a length. The holes on the cut-off side go with it. A cut is kept in the part's SavedObject.state
    // ("cut:x,y,z,nx,ny,nz;", part space, keeping the side the normal points to) and made again on load, in
    // order with the drilled holes (PartDrilling.ApplySaved).
    public static class PartCutting {
        private const string Prefix = "cut:";
        private const float MetresPerUnit = 0.0254f; // the slicer's tolerances are in metres; parts are in inches

        public static bool IsCuttable(GameObject part) {
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id))
                return false;
            var filter = part.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable;
        }

        /// <summary>
        /// Keeps the side of the part the normal points to (part space). False, and nothing changed, if nothing
        /// of the mesh would be left on that side.
        /// </summary>
        public static bool Cut(GameObject part, Vector3 point, Vector3 normal) {
            var filter = part.GetComponent<MeshFilter>();
            var source = filter.sharedMesh;
            var metres = Scaled(source, MetresPerUnit);
            var kept = MeshSlicer.Slice(metres, new Plane(normal.normalized, point * MetresPerUnit));
            Object.Destroy(metres);
            if (kept == null || kept.vertexCount == 0)
                return false;
            var mesh = Scaled(kept, 1f / MetresPerUnit);
            Object.Destroy(kept);
            mesh.name = source.name;

            var before = MeshSlicer.Volume(source);
            filter.sharedMesh = mesh;
            if (part.TryGetComponent(out MeshCollider collider))
                collider.sharedMesh = mesh;

            // Holes on the cut-off side go with it.
            var plane = new Plane(normal.normalized, point);
            foreach (var hole in PartDrilling.Holes(part))
                if (!plane.GetSide(hole.transform.localPosition)) {
                    hole.transform.SetParent(null);
                    Object.Destroy(hole.gameObject);
                }

            if (part.TryGetComponent(out PartName partName) && before > 0f)
                partName.weightInGrams = Mathf.Round(partName.weightInGrams * MeshSlicer.Volume(mesh) / before * 100f) / 100f;
            return true;
        }

        public static void Record(SavedObject saved, Vector3 point, Vector3 normal) {
            saved.state += Prefix + string.Join(",", new[] { point.x, point.y, point.z, normal.x, normal.y, normal.z }
                .Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + ";";
        }

        /// <summary>Reads a "cut:" entry of a part's state.</summary>
        public static bool TryParse(string entry, out Vector3 point, out Vector3 normal) {
            point = normal = Vector3.zero;
            if (!entry.StartsWith(Prefix))
                return false;
            var values = entry.Substring(Prefix.Length).Split(',');
            if (values.Length != 6)
                return false;
            var f = new float[6];
            for (var i = 0; i < 6; i++)
                if (!float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]))
                    return false;
            point = new Vector3(f[0], f[1], f[2]);
            normal = new Vector3(f[3], f[4], f[5]);
            return true;
        }

        private static Mesh Scaled(Mesh source, float factor) {
            var mesh = Object.Instantiate(source);
            var vertices = source.vertices;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] *= factor;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Where along <paramref name="axis"/> (part space) a cut near <paramref name="coordinate"/> should go: the
        /// middle of the nearest gap between holes along that axis, or a 1/16" step when there are no holes to
        /// line up with (or <paramref name="free"/>).
        /// </summary>
        public static float Snap(GameObject part, Vector3 axis, float coordinate, bool free) {
            const float step = 1f / 16f;
            if (!free) {
                // Rows of holes across the axis; only the main ones (a channel's square holes, not the odd
                // small hole between them).
                var counts = PartDrilling.Holes(part)
                    .Select(h => Mathf.Round(Vector3.Dot(h.transform.localPosition, axis) * 64f) / 64f)
                    .GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
                var most = counts.Count > 0 ? counts.Values.Max() : 0;
                var rows = counts.Where(kv => kv.Value * 2 >= most).Select(kv => kv.Key).OrderBy(v => v).ToList();
                if (rows.Count >= 2) {
                    var best = float.NaN;
                    var bestDistance = float.MaxValue;
                    for (var i = 0; i + 1 < rows.Count; i++) {
                        var gap = (rows[i] + rows[i + 1]) / 2f;
                        var distance = Mathf.Abs(gap - coordinate);
                        if (distance < bestDistance) {
                            bestDistance = distance;
                            best = gap;
                        }
                    }
                    return best;
                }
            }
            return Mathf.Round(coordinate / step) * step;
        }

        /// <summary>The part's longest side in its own space (the usual cut is across it).</summary>
        public static int LongestAxis(GameObject part) {
            var size = part.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            return size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        }
    }
}
