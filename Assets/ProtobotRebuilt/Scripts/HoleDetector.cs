using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections;
using UnityEngine;
using Protobot.StateSystems;

namespace Protobot {
    public class HoleDetector : MonoBehaviour {
        public HashSet<HoleCollider> holes = new HashSet<HoleCollider>();
        [SerializeField] HoleCollider.HoleType targetHoleType;

        private void Update() {
            var allHoles = new HashSet<HoleCollider>(holes);
            foreach (var holeCollider in allHoles) {
                // V2: a hole destroyed outright (opening another build) is let go of too.
                if (holeCollider == null) {
                    holes.Remove(holeCollider);
                    TargetHoleCount = holes.Count(h => h != null && IsTargetHole(h));
                    OnRemoveHole?.Invoke();
                    continue;
                }
                if (holeCollider.gameObject.IsDeleted())
                    RemoveHole(holeCollider);
            }
        }

        public bool TargetHoleFound => TargetHoleCount > 0;
        public int TargetHoleCount { get; private set; } = 0;

        public Action OnAddHole; // Runs whenever any hole gets added to this hole detector
        public Action OnAddTargetHole; // Runs when a target hole is added to this hole detector
        public Action OnRemoveHole; // Runs whenever any hole gets removed from this hole detector
        public Action OnRemoveTargetHole; // Runs when a target hole is added to this hole detector
        
        private void Start() {
            TargetHoleCount = 0;
            holes.Clear();
        }
        
        /// <summary>
        /// Makes sure to wait until all holes have been added before deciding to add a target hole
        /// Due to the randomness of OnTriggerStay
        /// </summary>
        /// <returns></returns>
        IEnumerator WaitAddTargetHole() {
            yield return new WaitForEndOfFrame();
            OnAddTargetHole?.Invoke();
        }

        public List<GameObject> GetObjects() {
            holes.RemoveWhere(h => h == null);   // V2: (see GetOrderedHoles)
            return holes.Select(col => col.holeData.part).ToList();
        }

        /// <summary>
        /// Returns the list of holes detected in order from distance to the connecting part
        /// </summary>
        /// <returns></returns>
        public List<HoleCollider> GetOrderedHoles() {
            holes.RemoveWhere(h => h == null);   // V2: a hole destroyed with its part (a build cleared) isn't one
            return holes.OrderBy(x => Vector3.Distance(transform.parent.position, x.transform.position))
                        .ToList();
        }

        public void RemoveAll() {
            var allHoles = new List<HoleCollider>(holes);
            
            foreach (HoleCollider hole in allHoles) {
                RemoveHole(hole);
            }
        }

        //Returns true if a hole is intersecting any other hole current detected by holeDetector
        public bool IsHoleIntersecting(HoleCollider otherHole) {
            if (!holes.Contains(otherHole)) {
                // V2: the original compared world-aligned bounds, which grow on turned parts, so stacked holes
                // (a plate's and then a nut's) counted as overlapping and a copied screw never connected.
                // Holes overlap when one's centre is inside the other.
                foreach (HoleCollider hole in holes) {
                    if (hole == null) continue;  // V2: destroyed with its part (a build opened over this one)
                    if (Contains(hole.transform, otherHole.transform.position) || Contains(otherHole.transform, hole.transform.position))
                        return true;
                }
            }

            return false;
        }

        // Whether a point is inside a hole collider's box (its mesh spans -0.5..0.5, scaled by the transform).
        private static bool Contains(Transform hole, Vector3 point) {
            var local = hole.InverseTransformPoint(point);
            const float inside = 0.49f;
            return Mathf.Abs(local.x) < inside && Mathf.Abs(local.y) < inside && Mathf.Abs(local.z) < inside;
        }

        private void OnTriggerStay(Collider other) {
            if (other.TryGetComponent(out HoleCollider hole))
                if (!hole.IsOccupied && !IsHoleIntersecting(hole) && Along(hole))
                    AddHole(hole);
        }

        // V2: a hole the detector only crosses isn't one it goes through (a screw through a hole drilled across a
        // standoff passes its threaded bore at right angles): the hole's axis must lie along one of the detector's
        // lines (a bent rubber link has a second one along its far stud).
        private CapsuleCollider[] lines;
        private int linesFor = -1;

        private bool Along(HoleCollider hole) {
            if (lines == null || linesFor != transform.childCount) {
                lines = GetComponentsInChildren<CapsuleCollider>(true);
                linesFor = transform.childCount;
            }
            var axis = hole.transform.forward;
            foreach (var line in lines)
                if (line != null && line.enabled && Mathf.Abs(Vector3.Dot(axis, line.transform.up)) > 0.82f) return true;
            return lines.Length == 0;
        }

        private void OnTriggerExit(Collider other) {
            if (other.TryGetComponent(out HoleCollider hole))
                if (hole.IsOccupiedBy(this))
                    RemoveHole(hole);
        }

        public void RemoveHole(HoleCollider hole) {
            holes.Remove(hole);
            hole.RemoveDetector(this);

            if (IsTargetHole(hole)) {
                TargetHoleCount--;
                OnRemoveTargetHole?.Invoke();
            }
            
            OnRemoveHole?.Invoke();
        }

        public bool IsTargetHole(HoleCollider hole) => hole.holeType == targetHoleType;

        public void AddHole(HoleCollider hole) {
            holes.Add(hole);
            hole.AddDetector(this);

            if (IsTargetHole(hole)) {
                TargetHoleCount++;
                StartCoroutine(WaitAddTargetHole());
            }
            
            OnAddHole?.Invoke();
        }

        public static HoleDetector Create(Transform parent, float length, HoleCollider.HoleType targetHoleType) {
            var newGameObject = new GameObject("Hole Detector");
            newGameObject.layer = HoleCollider.HOLE_COLLISIONS_LAYER;

            Rigidbody rb = newGameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            CapsuleCollider collider = newGameObject.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.radius = 0.01f;
            collider.height = length;

            newGameObject.transform.position = parent.position;
            newGameObject.transform.up = parent.forward;

            newGameObject.transform.SetParent(parent);

            HoleDetector holeDetector = newGameObject.AddComponent<HoleDetector>();
            holeDetector.targetHoleType = targetHoleType;

            return holeDetector;
        }

        public void ClearEvents() {
            OnAddHole = null;
            OnAddTargetHole = null;
            
            OnRemoveHole = null;
            OnRemoveTargetHole = null;
        }
    }
}