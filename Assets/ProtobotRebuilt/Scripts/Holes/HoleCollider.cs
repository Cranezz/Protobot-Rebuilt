using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Protobot {
    public class HoleCollider : MonoBehaviour {
        public static int HOLE_COLLISIONS_LAYER => 11;
        public static int HOLE_COLLISIONS_MASK => LayerMask.GetMask("HoleCollisions");

        public HoleData holeData;

        public Action<HoleDetector> OnSetDetector;

        public enum HoleType { 
            Normal,
            Threaded,
            Clamp
        }
        
        public HoleType holeType;

        public List<HoleDetector> detectors; // the list of hole detectors attached to this collider

        public bool twoSided = false;
        private int MaxDetectorLimit => twoSided ? 2 : 1;
        
        public bool IsOccupied => detectors.Count == MaxDetectorLimit;
        public bool IsEmpty => detectors.Count == 0;

        private void Awake() {
            detectors = new List<HoleDetector>();
            MeshCollider meshCollider = GetComponent<MeshCollider>();

            holeData = new HoleData {
                source = transform,  // V2: position, rotation and forward are read from it when asked

                shape = meshCollider.sharedMesh,
                depth = transform.localScale.z,
                size = new Vector2(transform.localScale.x, transform.localScale.y),
                part = transform.parent.gameObject
            };
        }

        /// <summary>V2: takes the collider's size and shape again after it was changed (custom parts set the size
        /// after copying a template, which had already read the template's: the hole showed the wrong size, off
        /// the surface).</summary>
        public void Refresh(Vector2[] outline = null) {
            holeData.shape = GetComponent<MeshCollider>().sharedMesh;
            holeData.depth = transform.localScale.z;
            holeData.size = new Vector2(transform.localScale.x, transform.localScale.y);
            holeData.outline = outline;
            holeData.part = transform.parent.gameObject;
        }

        // V2: there was an Update on every hole copying its transform into holeData and looking for deleted
        // screws: with a few thousand holes in a robot that was 8 ms of every frame, most of the app's CPU time.
        // holeData now reads the transform when asked, and only holes holding a screw/shaft are checked, by one
        // ticker (Occupied).
        private static readonly HashSet<HoleCollider> Occupied = new();
        private static readonly List<HoleCollider> Checking = new();
        private static HoleTicker ticker;

        private void Track() {
            if (detectors != null && detectors.Count > 0) {
                Occupied.Add(this);
                if (ticker == null) {
                    // Not DontSave: that would outlive Play mode in the editor.
                    ticker = new GameObject("Hole Ticker") { hideFlags = HideFlags.HideInHierarchy }.AddComponent<HoleTicker>();
                    DontDestroyOnLoad(ticker.gameObject);
                }
            }
            else Occupied.Remove(this);
        }

        private class HoleTicker : MonoBehaviour {
            private void Update() {
                Checking.Clear();
                Checking.AddRange(Occupied);
                foreach (var hole in Checking) {
                    if (hole == null) { Occupied.Remove(hole); continue; }
                    hole.RemoveDeletedDetectors();
                }
            }
        }

        private void OnDestroy() => Occupied.Remove(this);

        public void RemoveDeletedDetectors() {
            detectors.RemoveAll(detector => detector == null); // V2: destroyed ones (not only deleted) free the hole
            for (var i = detectors.Count - 1; i >= 0; i--)
                if (i < detectors.Count && detectors[i].gameObject.IsDeleted())
                    detectors[i].RemoveHole(this);
            Track();
        }

        public bool IsOccupiedBy(HoleDetector otherDetector) {
            return detectors.Contains(otherDetector);
        }
        
        public void AddDetector(HoleDetector newDetector) {
            if (IsOccupiedBy(newDetector) || IsOccupied) return;
            
            detectors.Add(newDetector);
            Track();  // V2
            OnSetDetector?.Invoke(newDetector);
        }

        public void RemoveDetector(HoleDetector detector) {
            detectors.Remove(detector);
            Track();  // V2
            OnSetDetector?.Invoke(null);  // V2: the insert goes when the screw or shaft comes out
        }

        private void OnDisable() {
            if (!IsEmpty && gameObject.IsDeleted()) {
                foreach (var detector in detectors)
                    detector.RemoveHole(this);
            }
        }
    }

    [Serializable]
    public class HoleData {

        public Mesh shape = null;
        public float depth;
        public Vector2 size;
        public GameObject part;

        // V2: read from the hole collider's transform when asked (they were copied into fields every frame).
        [NonSerialized] public Transform source;
        private Vector3 storedPosition;
        private Quaternion storedRotation = Quaternion.identity;
        private Vector3 storedForward = Vector3.forward;

        public Vector3 position {
            get => source != null ? source.position : storedPosition;
            set { source = null; storedPosition = value; }
        }

        public Quaternion rotation {
            get => source != null ? source.rotation : storedRotation;
            set { source = null; storedRotation = value; }
        }

        public Vector3 forward {
            get => source != null ? source.forward : storedForward;
            set { source = null; storedForward = value; }
        }

        // V2: the exact shape of a custom hole (a filleted slot, any outline), in inches in the hole's own x/y around
        // its middle; null for plain round/square/rectangular holes.
        public Vector2[] outline;

        public bool IsHighStrength() => size == new Vector2(0.25f, 0.25f) && shape == HoleShapes.instance.GetShapeMesh("square");
    }
}