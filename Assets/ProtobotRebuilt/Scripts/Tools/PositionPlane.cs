using UnityEngine;
using DG.Tweening;

namespace Protobot.Tools {
    public class PositionPlane : CastPositionTool {
        [SerializeField] private VectorLink normal;

        public Vector3 finalPosition = Vector3.zero;
        public override Vector3 FinalPosition => finalPosition;

        // V2: moved by how far the mouse went on the plane since the grab (not jumped so the middle is under it).
        private Vector3 startGizmo, grabOffset;
        private bool grabbed;

        public override void Move() {
            if (planeCast.hasHit) {
                if (!grabbed) {
                    grabbed = true;
                    grabOffset = planeCast.point - startGizmo;
                }
                finalPosition = MoveToPos(planeCast.point - grabOffset);
            }
        }

        public override void Initialize() {
            planeCast.transform.forward = normal.Vector;
            startGizmo = GizmoPosition;
            grabbed = false;
        }
    }
}