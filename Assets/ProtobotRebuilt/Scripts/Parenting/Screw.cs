using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

namespace Protobot {
    public class Screw : ConnectingPart {
        // V2: sharedMesh (reading .mesh made a copy of the mesh for every screw, never freed).
        // V2: threaded studs (couplers, the rubber link) have no head: all their length is thread.
        public float ScrewLength {
            get {
                var mesh = GetComponent<MeshFilter>().sharedMesh;
                return Lengths.TryGetValue(mesh, out var length) ? length : mesh.bounds.size.z - (Studs.Contains(mesh) ? 0f : 0.087f);
            }
        }

        /// <summary>V2: models whose thread is a set length from the origin (a pneumatic fitting's stud).</summary>
        public static readonly Dictionary<Mesh, float> Lengths = new();
        /// <summary>V2: models of headless threaded studs (couplers, the rubber link).</summary>
        public static readonly HashSet<Mesh> Studs = new();
        /// <summary>V2: shoulder screws' models and their shoulder length (a threaded part stops at its end).</summary>
        public static readonly Dictionary<Mesh, float> Shoulders = new();
        public float Shoulder => Shoulders.TryGetValue(GetComponent<MeshFilter>().sharedMesh, out var s) ? s : 0f;

        public override bool CanFormConnection => holeDetector.TargetHoleFound;

        public override HoleDetector CreateHoleDetector() {
            // V2: a pneumatic fitting's thread is shorter than its port is deep: a trigger wholly inside a hole's
            // (hollow) mesh collider is never reported, so its line reaches 0.35" past the tip, out of the port.
            var mesh = GetComponent<MeshFilter>().sharedMesh;
            var length = ScrewLength + (ChildPartGenerator.fittingDiameters.ContainsKey(mesh) ? 0.35f : 0f);
            var detector = HoleDetector.Create(transform, length, HoleCollider.HoleType.Threaded);
            detector.transform.position -= (transform.forward * length/2);
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
        public Vector3 FreeSpot(Vector3 near, float length, bool threaded = false) {
            // V2: the rubber link: on the stud nearer the mouse, against the rubber (never into it).
            if (TryGetComponent(out Motion.RubberLinkPart link)) {
                var (center, outward, _) = link.Seat(near, length);
                return center + transform.forward * (length / 2f);
            }
            var head = transform.position;
            var down = -transform.forward;
            var used = AxleLine.Used(gameObject, head, down, ScrewLength);
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            // V2: a threaded part (standoff, nut) on a shoulder screw goes no higher than the shoulder's end.
            var start = AxleLine.Free(used, Vector3.Dot(near - head, down), length, threaded ? Shoulder : 0f, ScrewLength, true, keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed));
            return start.HasValue ? head + down * start.Value : GetLowestPoint();
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