using UnityEngine;

namespace Protobot.Motion {
    // V2: the hex pocket of a nut retainer (VEX's 1-post and 4-post hex nut retainers, the bearing flat hex retainer).
    // The hole scanner saw it as a 0.364" round hole; it isn't one: a nut sits in it, held from turning, and the screw
    // comes through the retainer's small hole at its bottom into the nut. Set by VexHoleFixes when the part is made,
    // in the part's own space; AxlePlaceDisplacement seats a nut in it.
    public class NutPocket : MonoBehaviour {
        public Vector3 floor;     // the middle of the pocket's bottom
        public Vector3 outward;   // from the bottom out through the pocket's mouth
        public float depth;       // bottom to mouth
        public float across;      // across the flats

        /// <summary>The pocket's corner angle about its axis (degrees, 0-60) measured from <paramref name="reference"/>,
        /// from the mesh's points farthest out within the pocket's depth; NaN when it can't tell.</summary>
        public float CornerAngle(Vector3 reference) {
            var filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return float.NaN;
            return HexCorner(filter.sharedMesh.vertices, floor, outward, reference, across * 0.5f, across * 0.5f / Mathf.Cos(30f * Mathf.Deg2Rad) + 0.02f, 0.02f, depth - 0.02f);
        }

        /// <summary>The angle (0-60) of a hex's corners about an axis: the points between rMin and rMax from the axis,
        /// between alongMin and alongMax along it; the farthest out is a corner.</summary>
        public static float HexCorner(Vector3[] points, Vector3 center, Vector3 axis, Vector3 reference, float rMin, float rMax, float alongMin, float alongMax) {
            axis.Normalize();
            var u = Vector3.ProjectOnPlane(reference, axis).normalized;
            if (u.sqrMagnitude < 0.5f) return float.NaN;
            var v = Vector3.Cross(axis, u);
            var best = -1f;
            var angle = float.NaN;
            foreach (var p in points) {
                var d = p - center;
                var along = Vector3.Dot(d, axis);
                if (along < alongMin || along > alongMax) continue;
                var flat = d - axis * along;
                var r = flat.magnitude;
                if (r < rMin || r > rMax || r <= best) continue;
                best = r;
                angle = Mathf.Atan2(Vector3.Dot(flat, v), Vector3.Dot(flat, u)) * Mathf.Rad2Deg;
            }
            return float.IsNaN(angle) ? angle : Mathf.Repeat(angle, 60f);
        }
    }
}
