using UnityEngine;
using DG.Tweening;
using Protobot.StateSystems;

namespace Protobot.Tools {
    public class PositionAxis : CastPositionTool {
        public VectorLink normal;
        [HideInInspector] public Vector3 initObjPos;
    
        public Vector3 finalPosition = Vector3.zero;
        public override Vector3 FinalPosition => finalPosition;
        protected override Vector3? SnapAxis => normal.Vector; // V2

        // V2: the part moves by how far the mouse has moved along the axis since the arrow was grabbed (it used to
        // jump so the gizmo's middle sat under the mouse, as far as the grab was from the middle).
        private Vector3 startGizmo;
        private float grabAlong;
        private bool grabbed;

        public override void Move() {
            if (planeCast.hasHit) {
                var axis = normal.Vector.normalized;
                var along = Vector3.Dot(planeCast.point - startGizmo, axis);
                if (!grabbed) {
                    grabbed = true;
                    grabAlong = along;
                }
                Vector3 point = startGizmo + axis * (along - grabAlong);
                finalPosition = MoveToPos(point);
            }
        }

        public override void Initialize() {
            planeCast.GetComponent<LookAt>().vectorLink = normal;
            initObjPos = GizmoPosition; // V2: the gizmo, which may be a group's middle
            startGizmo = initObjPos;
            grabbed = false;
        }
    }
}
