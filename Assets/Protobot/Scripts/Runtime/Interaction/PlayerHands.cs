using Protobot.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Interaction
{
    /// <summary>
    /// What the player does with the left mouse button: click things that react (bins), and hold the button
    /// on a part to pick it up. A held part is pulled towards a point in front of the camera with physics,
    /// so it still bumps into things; let go and it falls (or flies, if you were moving it).
    /// The mouse wheel moves a held part closer or further away.
    /// </summary>
    public class PlayerHands : MonoBehaviour
    {
        [SerializeField] CursorController cursor;
        [SerializeField] Transform head;

        [Header("Holding")]
        [SerializeField] float minHoldDistance = 0.25f;
        [SerializeField] float maxHoldDistance = 1.6f;
        [SerializeField] float scrollStep = 0.04f;
        [Tooltip("How quickly the part follows the cursor (higher = snappier).")]
        [SerializeField] float followStrength = 0.5f;
        [SerializeField] float maxCarrySpeed = 5f;
        [SerializeField] float maxTurnSpeed = 15f;
        [Tooltip("If something blocks the part this far from where it should be, it is dropped.")]
        [SerializeField] float breakDistance = 0.8f;

        Rigidbody held;
        HoverTarget heldTarget;
        Vector3 localGrabPoint;
        Quaternion rotationFromView;
        float holdDistance;
        bool heldUsedGravity;
        float heldDrag;
        float heldAngularDrag;

        public bool IsHolding => held != null;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            if (MenuState.AnyOpen)
            {
                Release();
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
                Press();
            else if (mouse.leftButton.wasReleasedThisFrame)
                Release();

            if (held != null)
            {
                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    holdDistance = Mathf.Clamp(holdDistance + Mathf.Sign(scroll) * scrollStep, minHoldDistance, maxHoldDistance);
            }
        }

        void Press()
        {
            var target = cursor.Current;
            if (target == null || !cursor.CurrentInReach)
                return;

            if (target.GetComponentInParent<IInteractable>() is { } interactable)
            {
                interactable.Interact();
                return;
            }

            if (target.Cursor == CursorKind.Grab && target.GetComponentInParent<Rigidbody>() is { isKinematic: false } body)
                Grab(body, target, cursor.CurrentHit);
        }

        void Grab(Rigidbody body, HoverTarget target, RaycastHit hit)
        {
            held = body;
            heldTarget = target;
            localGrabPoint = body.transform.InverseTransformPoint(hit.point);
            holdDistance = Mathf.Clamp(hit.distance, minHoldDistance, maxHoldDistance);
            rotationFromView = Quaternion.Inverse(ViewYaw()) * body.rotation;

            heldUsedGravity = body.useGravity;
            heldDrag = body.linearDamping;
            heldAngularDrag = body.angularDamping;
            body.useGravity = false;
            body.linearDamping = 2f;
            body.angularDamping = 6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.WakeUp();
            cursor.Holding = target;
        }

        public void Release()
        {
            if (held == null)
                return;
            held.useGravity = heldUsedGravity;
            held.linearDamping = heldDrag;
            held.angularDamping = heldAngularDrag;
            // Don't let a fast swing launch parts across the room.
            held.linearVelocity = Vector3.ClampMagnitude(held.linearVelocity, 2.5f);
            held = null;
            heldTarget = null;
            cursor.Holding = null;
        }

        void FixedUpdate()
        {
            if (held == null)
                return;
            if (!held || !held.gameObject.activeInHierarchy)
            {
                Release();
                return;
            }

            // Move the grabbed point towards the spot in front of the camera.
            var target = head.position + head.forward * holdDistance;
            var grabPoint = held.transform.TransformPoint(localGrabPoint);
            var offset = target - grabPoint;
            if (offset.magnitude > breakDistance)
            {
                Release();
                return;
            }
            var velocity = offset / Time.fixedDeltaTime * followStrength;
            held.linearVelocity = Vector3.ClampMagnitude(velocity, maxCarrySpeed);

            // Keep the part's orientation relative to where the player faces (it turns with you).
            var wanted = ViewYaw() * rotationFromView;
            var delta = wanted * Quaternion.Inverse(held.rotation);
            delta.ToAngleAxis(out var angle, out var axis);
            if (angle > 180f)
                angle -= 360f;
            if (Mathf.Abs(angle) > 0.01f && !float.IsNaN(axis.x))
                held.angularVelocity = Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime * followStrength), maxTurnSpeed);
            else
                held.angularVelocity = Vector3.zero;
        }

        Quaternion ViewYaw() => Quaternion.Euler(0f, head.eulerAngles.y, 0f);

        void OnDisable() => Release();
    }
}
