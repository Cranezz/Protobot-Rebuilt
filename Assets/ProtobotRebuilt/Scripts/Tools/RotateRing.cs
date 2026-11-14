using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Protobot.StateSystems;

namespace Protobot.Tools {
    public class RotateRing : RotationTool {
        public static bool snapping = false;
        
        [SerializeField] private Camera refCamera = null; //used for dot product angle comparison

        [SerializeField] private VectorLink rotVectorLink;
        
        private Vector3 initRotVector; //the initial Vector to rotate about

        private Vector3 initMouseVector; //the initial Vector between the mouse position and the selection position
        // V2: measured from where the ring was when the drag began (a part turning about a joint carries the ring
        // round with it, which made the angle jump).
        private Vector3 MouseVector => (MouseInput.Position - (dragging ? initScreen : refCamera.WorldToScreenPoint(transform.position))).normalized;
        private Vector3 initScreen;
        private bool dragging;
        private Quaternion initRot; //the intiial rotation of the selection
        private Vector3 initPos, initOffset; // V2: where the part and the gizmo were when the drag started

        private Quaternion finalRotation = Quaternion.identity;
        private Vector2 initMouseScreen;   // V2: where the drag began, for the dial
        private float lastAngle;
        public override Quaternion FinalRotation => finalRotation;

        private static float Increment => TransformGizmo.TurnStep; // V2: set in the tool options (was 5°)

        //Main functions
        public override void Rotate() {
            Vector3 curMouseVector = MouseVector;
            float angle = Vector2.SignedAngle(curMouseVector, initMouseVector);
            
            // V2: Ctrl turns in big steps (45°, Ctrl+Shift 15°) whether snapping is on or not.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var ctrl = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed);
            var step = ctrl ? (keyboard.shiftKey.isPressed ? 15f : 45f) : Increment;
            lastStep = step;
            if (snapping || ctrl)
                angle = Mathf.Round(angle / step) * step; //force values to round at specific increment
            lastAngle = angle;  // V2: shown on the dial

            float camDot = Vector3.Dot(refCamera.transform.forward, initRotVector); //finds the dot product between the camera direction vs. the look direction of this rotate ring
            Quaternion newRot = Quaternion.AngleAxis(angle, -initRotVector * Mathf.Sign(camDot)) * initRot;
            
            // V2: turn about the gizmo (a group's middle), not only about the clicked part's own middle.
            var center = initPos + initOffset;
            var newPos = center + newRot * Quaternion.Inverse(initRot) * (initPos - center);
            movementManager.NextKind = Motion.Linkage.MoveKind.Rotate;  // V2: a part on a joint to a grounded one only turns about it
            movementManager.DisplaceTo(new Protobot.Transformations.Displacement(newPos, newRot));

            finalRotation = newRot;
        }

        public override void Initialize() {
            dragging = false;
            initScreen = refCamera.WorldToScreenPoint(transform.position);
            initMouseScreen = MouseInput.Position;  // V2
            lastAngle = 0f;
            initMouseVector = MouseVector;
            dragging = true;

            initRot = refObj.transform.rotation;
            initPos = refObj.transform.position;
            var canvas = GetComponentInParent<MatchObjectLinkTransform>();
            initOffset = canvas != null && canvas.centerOnGroup ? TransformGizmo.Offset : Vector3.zero;
            initRotVector = rotVectorLink.Vector;
            initCenter = initPos + initOffset;  // V2: the dial lies round the turn's axis
            dialAxis = -initRotVector * Mathf.Sign(Vector3.Dot(refCamera.transform.forward, initRotVector));
        }

        private Vector3 initCenter, dialAxis;
        private float lastStep = 5f;

        //Input Events
        public override void OnDrag() {
            if (MouseInput.LeftButton.isPressed)
                Rotate();
        }

        public override void OnEndDrag() {
            dragging = false;  // V2
            Interface.RingDial.Clear();
        }

        // V2: while dragging, a dial of tick marks (one per snap step) and the angle turned so far.
        private void Update() {
            if (dragging && MouseInput.LeftButton.isPressed)
                Interface.RingDial.Draw(initCenter, dialAxis, initMouseScreen, MouseInput.Position, lastAngle, snapping || lastStep > Increment ? lastStep : 5f);
            else if (dragging) {
                dragging = false;
                Interface.RingDial.Clear();
            }
        }
    }
}