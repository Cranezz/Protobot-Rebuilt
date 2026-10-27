using UnityEngine;

namespace Protobot.Player
{
    /// <summary>
    /// First-person body: walks, sprints, crouches and jumps with a CharacterController, and turns the head.
    /// It knows nothing about devices; an input component (desktop now, VR later) feeds it.
    ///
    /// Build view: the body stays put and the head floats free (slowly, for precise work), so you can get
    /// your eyes right up to small parts on the bench, look under an assembly, or over the top of it. It
    /// can't pass through things, and it can't wander more than a few metres from the body.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        // Things the floating head bumps into: not the player, the tool in hand, a held part, or the UI.
        const int HeadBlockers = ~((1 << 2) | (1 << 5) | (1 << Parts.PartLayers.ViewModel) | (1 << Parts.PartLayers.Held) | (1 << Parts.PartLayers.Fastened));

        [SerializeField] Transform head;

        [Header("Build view")]
        [SerializeField] float flySpeed = 0.3f;
        [SerializeField] float flyFastSpeed = 1.1f;
        [SerializeField] float flyAcceleration = 3f;
        [SerializeField] float maxHeadReach = 3f;
        [SerializeField] float headRadius = 0.012f;

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
        bool buildView;
        Vector3 headOffset;   // world space, from where the eyes would be
        Vector3 flyInput;
        bool flyFast;
        Vector3 flyVelocity;

        public Transform Head => head;
        public bool IsCrouching => !buildView && (crouchInput || !CanStandUp());
        public bool BuildView => buildView;

        /// <summary>Build view on or off (off brings the head back to the body).</summary>
        public void SetBuildView(bool on)
        {
            buildView = on;
            flyVelocity = Vector3.zero;
            flyInput = Vector3.zero;
        }

        /// <summary>Turns the view to look at a point.</summary>
        public void Face(Vector3 point)
        {
            var from = head != null ? head.position : transform.position;
            var dir = (point - from).normalized;
            yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (head != null)
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Build view: puts the floating head at a point (within reach of the body).</summary>
        public void PlaceHead(Vector3 point)
        {
            buildView = true;
            flyVelocity = Vector3.zero;
            headOffset = Vector3.ClampMagnitude(point - EyePosition, maxHeadReach);
            if (head != null)
                head.position = EyePosition + headOffset;
        }

        /// <summary>Build view movement: x = right, y = up, z = forward (where the player faces, kept level).</summary>
        public void SetFlyInput(Vector3 move, bool fast)
        {
            flyInput = Vector3.ClampMagnitude(move, 1f);
            flyFast = fast;
        }

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

        /// <summary>Puts the player somewhere, facing a direction (degrees; positive pitch looks down).</summary>
        public void Teleport(Vector3 position, float newYaw, float newPitch)
        {
            body.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, newYaw, 0f));
            body.enabled = true;
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, -85f, 85f);
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            headOffset = Vector3.zero;
            flyVelocity = Vector3.zero;
        }

        void Update()
        {
            UpdateHeight();

            var speed = IsCrouching ? crouchSpeed : sprintInput ? sprintSpeed : walkSpeed;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var target = buildView ? Vector3.zero : (transform.right * moveInput.x + transform.forward * moveInput.y) * speed;
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
            {
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                UpdateHeadOffset();
            }
        }

        void UpdateHeight()
        {
            var targetHeight = IsCrouching ? crouchingHeight : standingHeight;
            currentHeight = Mathf.MoveTowards(currentHeight, targetHeight, crouchTransitionSpeed * Time.deltaTime);
            body.height = currentHeight;
            body.center = new Vector3(0f, currentHeight / 2f, 0f);
        }

        Vector3 EyePosition => transform.position + Vector3.up * (currentHeight - eyeBelowTop);

        /// <summary>Build view flying (or drifting back to the body when it's off).</summary>
        void UpdateHeadOffset()
        {
            var eye = EyePosition;
            if (buildView)
            {
                var level = Quaternion.Euler(0f, yaw, 0f);
                var wanted = (level * new Vector3(flyInput.x, 0f, flyInput.z) + Vector3.up * flyInput.y) * (flyFast ? flyFastSpeed : flySpeed);
                flyVelocity = Vector3.MoveTowards(flyVelocity, wanted, flyAcceleration * (flyFast ? 3f : 1f) * Time.deltaTime);
                var step = flyVelocity * Time.deltaTime;
                var from = eye + headOffset;
                if (step.sqrMagnitude > 1e-10f)
                {
                    if (Physics.SphereCast(from, headRadius, step.normalized, out var hit, step.magnitude, HeadBlockers, QueryTriggerInteraction.Ignore))
                    {
                        // Slide along what's in the way instead of stopping dead.
                        step = Vector3.ProjectOnPlane(step, hit.normal);
                        if (Physics.SphereCast(from, headRadius, step.normalized, out _, step.magnitude, HeadBlockers, QueryTriggerInteraction.Ignore))
                            step = Vector3.zero;
                        flyVelocity = Vector3.ProjectOnPlane(flyVelocity, hit.normal);
                    }
                    headOffset = Vector3.ClampMagnitude(headOffset + step, maxHeadReach);
                }
            }
            else
            {
                headOffset = Vector3.MoveTowards(headOffset, Vector3.zero, Mathf.Max(3f, headOffset.magnitude * 6f) * Time.deltaTime);
            }
            head.position = eye + headOffset;
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
