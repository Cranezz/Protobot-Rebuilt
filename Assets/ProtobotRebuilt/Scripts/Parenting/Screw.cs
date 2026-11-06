using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

namespace Protobot {
    public class Screw : ConnectingPart {
        // V2: sharedMesh (reading .mesh made a copy of the mesh for every screw, never freed).
        public float ScrewLength => GetComponent<MeshFilter>().sharedMesh.bounds.size.z - 0.087f;

        public override bool CanFormConnection => holeDetector.TargetHoleFound;

        public override HoleDetector CreateHoleDetector() {
            var detector = HoleDetector.Create(transform, ScrewLength, HoleCollider.HoleType.Threaded);
            detector.transform.position -= (transform.forward * ScrewLength/2);
            return detector;
        }
        public override List<GameObject> GetAttachedParts() {
            if (CanFormConnection) {
                var orderedHoles = holeDetector.GetOrderedHoles();

                bool connect = false;

                List<GameObject> attachedParts = new List<GameObject>();

                //iterate backwards through the ordered holes to find the lowest threaded hole
                for (int i = orderedHoles.Count - 1; i >= 0; i--) {
                    if (holeDetector.IsTargetHole(orderedHoles[i]))
                        connect = true;

                    if (connect)
                        attachedParts.Add(orderedHoles[i].holeData.part);
                }

                return attachedParts;

            }
            
            return null;
        }

        /// <summary>
        /// Returns the list of attached parts given a custom a position and direction
        /// </summary>
        public List<GameObject> GetAttachedParts(Vector3 position, Vector3 direction) {
            List<GameObject> attachedParts = new List<GameObject>();

            var hits = GetRaycastHits(position, direction);

            for (int i = 0; i < hits.Length; i++) {
                var hit = hits[i];

                if (hit.collider.CompareTag("HoleCollider"))
                    attachedParts.Add(hit.transform.parent.gameObject);
            }

            return attachedParts;
        }


        private RaycastHit[] GetRaycastHits(Vector3 position, Vector3 direction) {
            Ray lowRay = new Ray(position + (direction * 0.1f), -direction);
            return Physics.RaycastAll(lowRay, ScrewLength);
        }

        private List<GameObject> GetAttachedPartsOrdered() {
            return GetRaycastHits(transform.position, transform.forward)
                .OrderBy(hit => Vector3.Distance(hit.point, transform.position))
                .Where(hit => hit.collider.CompareTag("HoleCollider"))
                .Select(hit => hit.transform.parent.gameObject)
                .ToList();
        }

        public Vector3 GetLowestPoint() {
            int attPartsCount = GetAttachedParts(transform.position, transform.forward).Count;

            if (attPartsCount > 0)
                return FindLowestPoint(attPartsCount - 1);
            else
                return transform.position;
        }
        
        // V2: where a part slid onto the screw goes: the free stretch of the screw nearest the mouse that's long
        // enough for it (between two channels, say), against the nearer of the parts beside it; past the last part
        // on the screw (the old place) counts as one of the stretches. Returns the point the part starts at (its side
        // toward the head); it runs from there toward the tip, like GetLowestPoint.
        public Vector3 FreeSpot(Vector3 near, float length) {
            var head = transform.position;
            var down = -transform.forward;
            var used = UsedStretches();
            var end = used.Count > 0 ? Mathf.Max(used.Max(u => u.to), 0f) : 0f;
            // The free stretches along the screw (distance from the head's underside toward the tip).
            var free = new List<(float from, float to)>();
            var at = 0f;
            foreach (var (from, to) in used.OrderBy(u => u.from)) {
                if (from > at) free.Add((at, from));
                at = Mathf.Max(at, to);
            }
            free.Add((Mathf.Max(at, end), float.PositiveInfinity));
            var mouse = Vector3.Dot(near - head, down);
            var best = (spot: GetLowestPoint(), distance: float.MaxValue);
            foreach (var (from, to) in free) {
                if (to - from < length - 0.01f) continue;
                // Against the nearer side of the stretch.
                var start = float.IsPositiveInfinity(to) || mouse - from <= to - mouse ? from : to - length;
                var distance = Mathf.Abs(start + length / 2f - mouse);
                if (distance < best.distance) best = (head + down * start, distance);
            }
            return best.spot;
        }

        // Worked out once per frame per screw while a part hovers over it (the part being moved isn't counted).
        private static readonly Dictionary<int, (int frame, List<(float, float)> used)> usedCache = new();

        // Where parts already sit along the screw: each part it goes through, by its mesh near the screw's line
        // (a wheel is wider than its hole) and its holes there.
        private List<(float from, float to)> UsedStretches() {
            if (usedCache.TryGetValue(GetInstanceID(), out var cached) && cached.frame == Time.frameCount)
                return cached.used;
            if (usedCache.Count > 64) usedCache.Clear();
            var head = transform.position;
            var down = -transform.forward;
            var used = new List<(float, float)>();
            var parts = Physics.RaycastAll(new Ray(head + transform.forward * 0.1f, down), ScrewLength + 0.2f)
                .Where(h => h.collider.CompareTag("HoleCollider") && h.transform.parent != null)
                .Select(h => h.transform.parent.gameObject).Distinct().ToList();
            foreach (var part in parts) {
                if (part.layer == Placement.PLACEMENT_LAYER) continue;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var hole in part.GetComponentsInChildren<HoleCollider>(true)) {
                    if (hole.transform.parent != part.transform) continue;
                    var offset = hole.transform.position - head;
                    if (Vector3.ProjectOnPlane(offset, down).magnitude > 0.1f) continue;
                    var along = Vector3.Dot(offset, down);
                    var half = Mathf.Abs(Vector3.Dot(hole.transform.forward, down)) * hole.transform.lossyScale.z / 2f;
                    lo = Mathf.Min(lo, along - half);
                    hi = Mathf.Max(hi, along + half);
                }
                if (part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && filter.sharedMesh.isReadable) {
                    var t = part.transform;
                    var localHead = t.InverseTransformPoint(head);
                    var localDown = t.InverseTransformDirection(down).normalized;
                    var scale = t.lossyScale.x;
                    foreach (var v in filter.sharedMesh.vertices) {
                        var offset = v - localHead;
                        var along = Vector3.Dot(offset, localDown);
                        if ((offset - localDown * along).sqrMagnitude * scale * scale > 0.35f * 0.35f) continue;
                        lo = Mathf.Min(lo, along * scale);
                        hi = Mathf.Max(hi, along * scale);
                    }
                }
                if (lo < hi) used.Add((lo, hi));
            }
            usedCache[GetInstanceID()] = (Time.frameCount, used);
            return used;
        }

        public Vector3 FindLowestPoint(int attachedPartsIndex) {
            GameObject attPart = GetAttachedPartsOrdered()[attachedPartsIndex];
            Ray lowRay = new Ray(transform.position - (transform.forward * ScrewLength), transform.forward);
            Debug.DrawLine(lowRay.origin, transform.position);
            RaycastHit[] hits = Physics.RaycastAll(lowRay, ScrewLength);

            foreach (RaycastHit hit in hits) {
                if (hit.collider.CompareTag("HoleCollider") && hit.transform.parent.gameObject == attPart)
                    return hit.point;
            }
            return transform.position;
        }
    }
}