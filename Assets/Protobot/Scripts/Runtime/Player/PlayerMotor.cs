using UnityEngine;

namespace Protobot.Player
{
    /// <summary>
    /// First-person body: walks, sprints, crouches and jumps with a CharacterController, and turns the head.
    /// It knows nothing about devices; an input component (desktop now, VR later) feeds it.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [SerializeField] Transform head;

        [Header("Speed (m/s)")]
        [SerializeField] float walkSpeed = 1.8f;
        [SerializeField] float sprintSpeed = 3.4f;
        [SerializeField] float crouchSpeed = 0.9f;
        [SerializeField] float acceleration = 14f;

        [Header("Body (m)")]
        [SerializeField] float standingHeight = 1.75f;
        [SerializeField] float crouchingHeight = 1.1f;
        [SerializeField] float eyeBelowTop = 0.1f;
        [SerializeField] float crouchTransitionSpeed = 8f;

        [Header("Jump")]
        [SerializeField] float jumpHeight = 0.45f;
        [SerializeField] float gravity = -18f;

        CharacterController body;
        Vector2 moveInput;
        bool sprintInput;
        bool crouchInput;
        bool jumpQueued;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float yaw;
        float pitch;
        float currentHeight;

        public Transform Head => head;
        public bool IsCrouching => crouchInput || !CanStandUp();

        void Awake()
        {
            body = GetComponent<CharacterController>();
            currentHeight = standingHeight;
            yaw = transform.eulerAngles.y;
            pitch = head != null ? NormalizeAngle(head.localEulerAngles.x) : 0f;
        }

        /// <summary>Movement relative to where the player faces: x = strafe, y = forward.</summary>
        public void SetMoveInput(Vector2 move, bool sprint, bool crouch)
        {
            moveInput = Vector2.ClampMagnitude(move, 1f);
            sprintInput = sprint;
            crouchInput = crouch;
        }

        /// <summary>Turns the body (x, degrees) and tilts the head (y, degrees; positive looks up).</summary>
        public void Look(Vector2 degrees)
        {
            yaw += degrees.x;
            pitch = Mathf.Clamp(pitch - degrees.y, -85f, 85f);
        }

        public void Jump() => jumpQueued = true;

        void Update()
        {
            UpdateHeight();

            var speed = IsCrouching ? crouchSpeed : sprintInput ? sprintSpeed : walkSpeed;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var target = (transform.right * moveInput.x + transform.forward * moveInput.y) * speed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, acceleration * Time.deltaTime);

            if (body.isGrounded)
            {
                verticalVelocity = -1f;  // keeps the controller pressed onto the floor
                if (jumpQueued && !IsCrouching)
                    verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            }
            jumpQueued = false;
            verticalVelocity += gravity * Time.deltaTime;

            body.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (head != null)
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void UpdateHeight()
        {
            var targetHeight = IsCrouching ? crouchingHeight : standingHeight;
            currentHeight = Mathf.MoveTowards(currentHeight, targetHeight, crouchTransitionSpeed * Time.deltaTime);
            body.height = currentHeight;
            body.center = new Vector3(0f, currentHeight / 2f, 0f);
            if (head != null)
                head.localPosition = new Vector3(0f, currentHeight - eyeBelowTop, 0f);
        }

        /// <summary>False when something is right above a crouching player (e.g. under the build table).</summary>
        bool CanStandUp()
        {
            if (currentHeight >= standingHeight - 0.01f)
                return true;
            var radius = body.radius * 0.95f;
            var bottom = transform.position + Vector3.up * (currentHeight - radius);
            var top = transform.position + Vector3.up * (standingHeight - radius);
            return !Physics.CheckCapsule(bottom, top, radius, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore);
        }

        static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
    }
}
