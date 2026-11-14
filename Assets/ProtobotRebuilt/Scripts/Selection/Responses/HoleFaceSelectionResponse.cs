using System;
using UnityEngine;
using Protobot.InputEvents;

namespace Protobot.SelectionSystem {
    public class HoleFaceSelectionResponse : SelectionResponse {
        public override bool RespondOnlyToSelectors => false;

        [SerializeField] private HoleFace holeFace = null;
        [SerializeField] private MouseCast mouseCast = null;
        [SerializeField] private InputEvent flipInput;

        private bool flipFaceDir => flipInput.IsPressed;

        public override void OnSet(ISelection holeColliderSel) {
            if (holeColliderSel.gameObject.TryGetComponent(out HoleCollider holeCollider)) {
                HoleData selHole = holeCollider.holeData;

                holeFace.gameObject.SetActive(true);

                Vector3 faceDir = mouseCast.hit.normal;
                // V2: the face is one end of the hole, along its axis. A round hole's collider is a cylinder: a click
                // near its rim hit the side and gave a sideways "face", so a part picked up by it turned the wrong
                // way. The end the hit normal leans to, or (side hits) the end facing the camera.
                var axis = selHole.forward.normalized;
                var along = Vector3.Dot(faceDir, axis);
                if (Mathf.Abs(along) < 0.5f) along = Vector3.Dot(-mouseCast.ray.direction, axis);
                if (axis.sqrMagnitude > 0.5f) faceDir = axis * (along >= 0f ? 1f : -1f);

                if (flipFaceDir)
                    faceDir = -faceDir;

                holeFace.Set(selHole, faceDir);
            }
        }

        public override void OnClear(ClearInfo info) {
            holeFace.gameObject.SetActive(false);
        }
    }
}