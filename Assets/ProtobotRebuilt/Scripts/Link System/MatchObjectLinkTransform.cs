using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Protobot.Tools;

namespace Protobot {
    public class MatchObjectLinkTransform : MonoBehaviour {
        [SerializeField] private ObjectLink objectLink;

        public bool matchPosition;
        public bool matchRotation;
        public bool matchScale;

        // V2: the Transform tool's gizmo goes to the middle of a group of parts (TransformGizmo).
        public bool centerOnGroup;

        void Update() {
            if (objectLink.active) {
                if (matchPosition) transform.position = centerOnGroup ? TransformGizmo.PositionFor(objectLink.obj) : objectLink.tform.position;
                if (matchRotation) transform.rotation = objectLink.tform.rotation;
                if (matchScale) transform.localScale = objectLink.tform.localScale;
                if (centerOnGroup) TransformGizmo.Frame = matchRotation ? objectLink.tform.rotation : Quaternion.identity;
            }
        }
    }
}
