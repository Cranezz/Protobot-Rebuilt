using Protobot.Audio;
using Protobot.Core;
using Protobot.Interaction;
using Protobot.Parts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// Empty hands. Click a part to pick it up, click again to drop it. A held part is pulled towards a point
    /// in front of the camera with physics, so it still bumps into things; let go and it falls (or flies, if you
    /// swung it). While holding: the mouse wheel moves it closer or further, holding the wheel down turns it
    /// freely with the mouse, right click gives it a quarter turn, and F freezes it in mid air (F on a frozen
    /// part, or picking it up, unfreezes it). R opens rotate mode for the held part or the one you aim at.
    /// </summary>
    public class HandsTool : PlayerTool
    {
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
        [Tooltip("Degrees the held part turns per pixel of mouse movement while the wheel is held down.")]
        [SerializeField] float freeTurnSpeed = 0.35f;

        Rigidbody held;
        HoverTarget heldTarget;
        Vector3 localGrabPoint;
        Quaternion rotationFromView;
        float holdDistance;
        bool heldUsedGravity;
        float heldDrag;
        float heldAngularDrag;
        bool freeTurning;
        PartRotator rotator;

        public bool IsHolding => held != null;

        public override string ControlsHint => IsHolding
            ? "Left click: drop   ·   Wheel: closer / further   ·   Hold wheel: turn   ·   R: rotate mode   ·   F: freeze   ·   Right click: 90°"
            : "Left click: pick up   ·   R: rotate a part   ·   F: unfreeze a frozen part";

        public override void PrimaryDown()
        {
            if (held != null)
            {
                Release();
                Sounds.Play(Sounds.Release, 0.4f);
                return;
            }
            var target = Cursor.Current;
            if (target == null || !Cursor.CurrentInReach)
                return;
            if (target.Cursor != CursorKind.Grab || target.GetComponentInParent<Rigidbody>() is not { } body)
                return;
            if (body.isKinematic)
                Unfreeze(body, false);  // picking up a frozen part unfreezes it
            Grab(body, target, Cursor.CurrentHit.point, Cursor.CurrentHit.distance);
            Sounds.Play(Sounds.Pickup, 0.45f);
        }

        public override void OnUnequip() => Release();
        public override void OnSuspend() => Release();

        public override void Scroll(int notches)
        {
            if (held != null)
                holdDistance = Mathf.Clamp(holdDistance + notches * scrollStep, minHoldDistance, maxHoldDistance);
        }

        public override void SecondaryTap()
        {
            if (held == null)
                return;
            rotationFromView = Quaternion.Euler(0f, 90f, 0f) * rotationFromView;
            Sounds.Play(Sounds.Tick, 0.3f);
        }

        public override void Tick()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            // Hold the wheel down to turn the held part with the mouse (the view stays still meanwhile).
            var turning = held != null && mouse.middleButton.isPressed;
            if (turning != freeTurning)
            {
                freeTurning = turning;
                InputLocks.LookSuppressed = turning;
            }
            if (turning)
            {
                var delta = mouse.delta.ReadValue() * freeTurnSpeed;
                rotationFromView = Quaternion.AngleAxis(-delta.x, Vector3.up) * Quaternion.AngleAxis(delta.y, Vector3.right) * rotationFromView;
            }

            if (keyboard.fKey.wasPressedThisFrame)
                ToggleFreeze();
            if (keyboard.rKey.wasPressedThisFrame)
                StartRotateMode();
        }

        public override bool DescribeTarget(HoverTarget target, bool inReach, ref CursorView view)
        {
            if (target == null || !inReach || held != null)
                return false;
            if (target.GetComponentInParent<PartBody>() == null || target.GetComponentInParent<Rigidbody>() is not { isKinematic: true })
                return false;
            view.hint = (string.IsNullOrEmpty(view.hint) ? "" : view.hint + "   ·   ") + "Frozen in place (F unfreezes)";
            return true;
        }

        // ---------------------------------------------------------------- freezing

        void ToggleFreeze()
        {
            if (held != null)
            {
                var body = held;
                Release();
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                Sounds.Play(Sounds.Freeze, 0.45f);
                Hud.ShowToast("Frozen in mid air: F on it (or picking it up) unfreezes it", 2.5f);
                return;
            }
            var target = Cursor.Current;
            if (target != null && Cursor.CurrentInReach && target.GetComponentInParent<PartBody>() != null
                && target.GetComponentInParent<Rigidbody>() is { isKinematic: true } frozen)
            {
                Unfreeze(frozen, true);
                return;
            }
            Hud.ShowError("Hold a part and press F to freeze it in mid air");
        }

        void Unfreeze(Rigidbody body, bool announce)
        {
            body.isKinematic = false;
            body.WakeUp();
            if (!announce)
                return;
            Sounds.Play(Sounds.Release, 0.4f);
            Hud.ShowToast("Unfrozen", 1.2f);
        }

        // ---------------------------------------------------------------- rotate mode

        void StartRotateMode()
        {
            var wasHeld = held != null;
            Rigidbody body = held;
            HoverTarget target = heldTarget;
            if (body == null)
            {
                target = Cursor.Current;
                if (target != null && Cursor.CurrentInReach && target.GetComponentInParent<PartBody>() != null)
                    body = target.GetComponentInParent<Rigidbody>();
            }
            if (body == null)
            {
                Hud.ShowError("Hold a part or aim at one, then press R to rotate it");
                return;
            }
            var distance = holdDistance;
            var localGrab = localGrabPoint;
            Release();
            if (rotator == null && !TryGetComponent(out rotator))
                rotator = gameObject.AddComponent<PartRotator>();
            rotator.Begin(body, View, Hud, part =>
            {
                // Back into the hand if it was being carried.
                if (wasHeld && part != null && isActiveAndEnabled && Belt.Current == this)
                    Grab(part, target, part.transform.TransformPoint(localGrab), distance);
            });
        }

        // ---------------------------------------------------------------- carrying

        void Grab(Rigidbody body, HoverTarget target, Vector3 grabPoint, float distance)
        {
            held = body;
            heldTarget = target;
            localGrabPoint = body.transform.InverseTransformPoint(grabPoint);
            holdDistance = Mathf.Clamp(distance, minHoldDistance, maxHoldDistance);
            rotationFromView = Quaternion.Inverse(ViewYaw()) * body.rotation;

            heldUsedGravity = body.useGravity;
            heldDrag = body.linearDamping;
            heldAngularDrag = body.angularDamping;
            body.useGravity = false;
            body.linearDamping = 2f;
            body.angularDamping = 6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.WakeUp();
            Cursor.Holding = target;
        }

        public void Release()
        {
            if (freeTurning)
            {
                freeTurning = false;
                InputLocks.LookSuppressed = false;
            }
            if (held == null)
                return;
            if (held)
            {
                held.useGravity = heldUsedGravity;
                held.linearDamping = heldDrag;
                held.angularDamping = heldAngularDrag;
                // Don't let a fast swing launch parts across the room.
                held.linearVelocity = Vector3.ClampMagnitude(held.linearVelocity, 2.5f);
            }
            held = null;
            heldTarget = null;
            Cursor.Holding = null;
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
                Hud.ShowError("Dropped it: something was in the way");
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
