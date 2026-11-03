using UnityEngine;

namespace Protobot {
    // V2: a piece of a part that isn't a part of its own (a cylinder's rod, its threaded end): clicking or hovering
    // it selects the part. While placing, the piece itself is pointed at (a nut snaps onto the rod's thread).
    public class SelectAs : MonoBehaviour {
        public GameObject target;

        public static GameObject Resolve(GameObject obj) {
            if (obj == null || Placement.IsPlacing || !obj.TryGetComponent(out SelectAs redirect) || redirect.target == null)
                return obj;
            return redirect.target;
        }
    }
}
