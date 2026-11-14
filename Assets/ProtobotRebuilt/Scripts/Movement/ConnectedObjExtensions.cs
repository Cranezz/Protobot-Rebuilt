using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Protobot {
    public static class ConnectedObjExtensions {
        public static List<GameObject> GetConnectedObjects(this GameObject obj, bool includeObj = false, bool onlyParts = false) {
            if (obj == null) return null;

            var connectedObjs = new List<GameObject>();
            
            if (obj.TryGetComponent(out Pivot pivot)) {
                connectedObjs.AddRange(pivot.objects);
            }

            if (includeObj && !obj.CompareTag("Group"))
                connectedObjs.Add(obj);

            if (obj.TryGetComponent(out HoleFace holeFace)) {
                if (onlyParts && includeObj)
                    connectedObjs.Remove(obj);

                connectedObjs.Add(holeFace.hole.part.gameObject);

                var holeObjs = holeFace.hole.part.GetConnectedObjects(true, onlyParts);
                connectedObjs.AddRange(holeObjs);
            }

            // V2: a wheel, gear or sprocket spinning on a screw (Linkage.SpinsOn) goes on its own (with what's bolted
            // to it, like a gear on a wheel): picked up, it slides along its screw or comes off it, instead of taking
            // the whole robot with it. Moving the frame still carries it (from the frame, the group is the same).
            if (obj.GetComponent<SavedObject>() != null && SpinsOnAScrew(obj)) {
                var own = Motion.Dynamics.Fastened(new[] { obj }, new HashSet<GameObject>(), null, out _);
                connectedObjs.AddRange(own);
                if (!includeObj) connectedObjs.Remove(obj);
                return Owners(connectedObjs, obj, includeObj);
            }

            // V2: a pneumatic fitting screwed into a port goes on its own (with a plug or fitting pushed into it): it
            // turns in its thread about the port's axis (the gizmo sits on the thread) instead of turning the valve
            // or cylinder with it.
            if (obj.GetComponent<SavedObject>() != null && ChildPartGenerator.IsFittingPart(obj) && GroupManager.Neighbours(obj).Any()) {
                var own = new List<GameObject> { obj };
                foreach (var next in GroupManager.Neighbours(obj))
                    if (next != null && ChildPartGenerator.IsFittingPart(next) && InsertedInto(next, obj)) own.Add(next);
                connectedObjs.AddRange(own);
                if (!includeObj) connectedObjs.Remove(obj);
                return Owners(connectedObjs, obj, includeObj);
            }

            if (obj.TryGetGroup(out List<GameObject> groupObjs)) {
                connectedObjs.AddRange(groupObjs);
                if (!includeObj) {
                    connectedObjs.Remove(obj);
                }
            }

            return Owners(connectedObjs, obj, includeObj);
        }

        // V2: a connecting piece that lives inside a part (a pneumatic cylinder's rod thread, a child of its rod) is
        // that part: moving it on its own pulled it out of the part (moved to the moving pivot, then let go at the top
        // level), so the rod's screw stayed behind and survived deleting the cylinder.
        private static List<GameObject> Owners(List<GameObject> objects, GameObject obj, bool includeObj) {
            var result = new List<GameObject>();
            foreach (var o in objects) {
                if (o == null) continue;
                var owner = o;
                var parent = o.transform.parent;
                if (o.GetComponent<SavedObject>() == null && parent != null) {
                    var saved = parent.GetComponentInParent<SavedObject>(true);
                    if (saved != null) owner = saved.gameObject;
                }
                if (!includeObj && owner == obj) continue;
                if (!result.Contains(owner)) result.Add(owner);
            }
            return result;
        }

        // A fitting or plug whose thread is in one of the part's holes.
        private static bool InsertedInto(GameObject fitting, GameObject part) =>
            fitting.TryGetComponent(out Screw screw) && screw.holeDetector != null && screw.holeDetector.GetOrderedHoles().Any(h => h != null && h.holeData.part == part);

        private static bool SpinsOnAScrew(GameObject part) {
            foreach (var next in GroupManager.Neighbours(part))
                if (next != null && Motion.Linkage.SpinsOn(next, part)) return true;
            return false;
        }
    }
}