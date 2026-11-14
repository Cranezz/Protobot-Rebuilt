using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot {
    // V2: free room along a screw or shaft. What already sits on the line (each part it goes through, by its holes
    // there, or its mesh close to the line when it has none) is measured as stretches from the line's start; a part slid on goes into the free stretch nearest the mouse that's long enough, against the
    // nearer neighbour.
    public static class AxleLine {
        private static readonly Dictionary<int, (int frame, List<(float from, float to)> used)> cache = new();

        /// <summary>Stretches taken along the line (distances from <paramref name="origin"/> along <paramref name="direction"/>),
        /// by every part the line goes through except <paramref name="self"/> and anything being moved.</summary>
        public static List<(float from, float to)> Used(GameObject self, Vector3 origin, Vector3 direction, float length) {
            var key = (self != null ? self.GetInstanceID() : 0) ^ origin.GetHashCode() ^ (direction.GetHashCode() * 31);   // per line, per mover
            if (cache.TryGetValue(key, out var cached) && cached.frame == Time.frameCount) return cached.used;
            if (cache.Count > 64) cache.Clear();
            var used = new List<(float, float)>();
            // (from well behind the line's start: a ray that starts inside a hole collider never hits it, so a coupler
            // or stud whose start is inside a standoff didn't see that standoff, and a part went on top of it)
            var parts = Physics.RaycastAll(new Ray(origin - direction * 3f, direction), length + 3.1f, ~0, QueryTriggerInteraction.Collide)
                .Where(h => h.collider.CompareTag("HoleCollider") && h.transform.parent != null)
                .Select(h => h.transform.parent.gameObject).Distinct().ToList();
            foreach (var part in parts) {
                if (part == self || part.layer == Placement.PLACEMENT_LAYER) continue;
                // Where the part's material is along the line (its holes there, its mesh near the line), as separate
                // pieces where there's a real gap between them: a C-channel crossed at both flanges is two pieces with
                // its inside free, not one solid block.
                var pieces = new List<(float a, float b)>();
                foreach (var hole in part.GetComponentsInChildren<HoleCollider>(true)) {
                    if (hole.transform.parent != part.transform) continue;
                    var offset = hole.transform.position - origin;
                    if (Vector3.ProjectOnPlane(offset, direction).magnitude > 0.1f) continue;
                    var along = Vector3.Dot(offset, direction);
                    var half = Mathf.Abs(Vector3.Dot(hole.transform.forward, direction)) * hole.transform.lossyScale.z / 2f;
                    pieces.Add((along - half, along + half));
                }
                // A part with a hole on the line is where its holes are (exact: a flange is its thickness, not the web
                // beside the hole too); one without (a wheel's rim over the line...) by its mesh close to the line.
                if (pieces.Count == 0 && part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && filter.sharedMesh.isReadable) {
                    var t = part.transform;
                    var localOrigin = t.InverseTransformPoint(origin);
                    var localDirection = t.InverseTransformDirection(direction).normalized;
                    var scale = t.lossyScale.x;
                    foreach (var v in filter.sharedMesh.vertices) {
                        var offset = v - localOrigin;
                        var along = Vector3.Dot(offset, localDirection);
                        if ((offset - localDirection * along).sqrMagnitude * scale * scale > 0.2f * 0.2f) continue;
                        pieces.Add((along * scale, along * scale));
                    }
                }
                foreach (var piece in Merge(pieces, 0.12f))
                    if (piece.from < length - 0.005f && piece.to > piece.from) used.Add(piece);   // not past the line's end
            }
            cache[key] = (Time.frameCount, used);
            return used;
        }

        // Spans along a line joined where they overlap or are closer than `gap` (no part fits between them).
        private static List<(float from, float to)> Merge(List<(float a, float b)> spans, float gap) {
            var merged = new List<(float from, float to)>();
            foreach (var (a, b) in spans.OrderBy(p => p.a)) {
                if (merged.Count > 0 && a - merged[merged.Count - 1].to <= gap)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].from, Mathf.Max(merged[merged.Count - 1].to, b));
                else merged.Add((a, b));
            }
            return merged;
        }

        public const float EdgeSnap = 0.15f;   // this close to a neighbour, the part goes against it

        /// <summary>Where a part <paramref name="length"/> long starts (distance along the line) with its middle as near
        /// <paramref name="near"/> (the mouse) as it can be: it slides along a free stretch with the mouse and goes
        /// against the part beside it when within <see cref="EdgeSnap"/> (not with <paramref name="free"/>: Ctrl);
        /// over parts, or in a stretch too short for it, it jumps to the nearest stretch it fits in. The stretches are
        /// between <paramref name="min"/> and <paramref name="max"/>, plus past the last part (as far as the part still
        /// sits on the line) when <paramref name="pastEnd"/>. Null if it fits nowhere.</summary>
        public static float? Free(List<(float from, float to)> used, float near, float length, float min, float max, bool pastEnd, bool free = false) =>
            Free(used, near, length, min, max, pastEnd, free, out _);

        /// <summary>As Free, and which neighbour the part ends up against: -1 the one before it (toward the line's
        /// start), +1 the one after it, 0 none (sliding free, or only the screw's tip beyond it).</summary>
        public static float? Free(List<(float from, float to)> used, float near, float length, float min, float max, bool pastEnd, bool free, out int edge) {
            edge = 0;
            var stretches = new List<(float from, float to)>();
            var at = min;
            foreach (var (from, to) in used.OrderBy(u => u.from)) {
                if (to <= min) continue;
                if (from > at) stretches.Add((at, Mathf.Min(from, max)));
                at = Mathf.Max(at, to);
            }
            var tip = float.NaN;
            if (pastEnd) {
                tip = Mathf.Max(max, at + length);
                stretches.Add((at, tip));
            }
            else if (at < max) stretches.Add((at, max));
            float? best = null;
            var bestDistance = float.MaxValue;
            foreach (var (from, to) in stretches) {
                if (to - from < length - 0.01f) continue;
                var start = Mathf.Clamp(near - length / 2f, from, Mathf.Max(from, to - length));
                if (!free) {
                    var high = Mathf.Max(from, to - length);
                    var nearLow = start - from < EdgeSnap;
                    var nearHigh = high - start < EdgeSnap;
                    // V2: a stretch barely longer than the part has both neighbours within reach: go against the
                    // one on the mouse's side (it went to the first one every time).
                    if (nearLow && nearHigh) start = near - length / 2f <= (from + high) / 2f ? from : high;
                    else if (nearLow) start = from;
                    else if (nearHigh) start = high;
                }
                var distance = Mathf.Abs(start + length / 2f - near);
                if (distance < bestDistance - 1e-4f) {
                    best = start;
                    bestDistance = distance;
                    edge = start - from < 0.002f ? -1 : (to - length - start < 0.002f && to != tip) ? 1 : 0;
                }
            }
            return best;
        }
    }
}
