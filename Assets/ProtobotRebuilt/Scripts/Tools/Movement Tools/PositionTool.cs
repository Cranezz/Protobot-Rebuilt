using UnityEngine;
using UnityEngine.Events;
using System;
using UnityEngine.EventSystems;
using Protobot.StateSystems;

namespace Protobot.Tools {
    public abstract class PositionTool : ClickDragTool {
        public static bool snapping = false;
        public abstract Vector3 FinalPosition {get;}

        /// <summary> Executes right when mouse is down before dragging </summary>
        abstract public void Initialize();
        abstract public void Move();

        // V2: where the gizmo sits relative to the part (the middle of a group, TransformGizmo) and where the
        // part was when the drag started; moves are snapped from there.
        protected Vector3 gizmoOffset;
        private Vector3 startPosition;

        /// <summary>The drag's axis, for snapping along it only (null: snap in the gizmo's frame).</summary>
        protected virtual Vector3? SnapAxis => null;

        protected Vector3 GizmoPosition => refObj.transform.position + gizmoOffset;

        //Input events
        public override void OnPointerDown() {
            var canvas = GetComponentInParent<MatchObjectLinkTransform>();
            gizmoOffset = canvas != null && canvas.centerOnGroup ? TransformGizmo.Offset : Vector3.zero;
            startPosition = refObj.transform.position;
            Initialize();
        }

        public override void OnPointerUp() {
        }

        /// <summary>Moves the part so the gizmo ends up at <paramref name="pos"/> (snapped when snapping is on).</summary>
        public Vector3 MoveToPos(Vector3 pos)
        {
            pos -= gizmoOffset;
            // V2: the original rounded the move on the world axes, so a part dragged along its own (local) axis
            // also jumped sideways; now the move is rounded along the dragged axis, or in the gizmo's frame.
            if (snapping && movementManager.MovingObj != null)
                pos = startPosition + TransformGizmo.SnapDelta(pos - startPosition, SnapAxis);
            movementManager.MoveTo(pos);
            return pos;
        }
    }
}