using Protobot.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.InputHandling
{
    /// <summary>
    /// Keyboard + mouse controls for the player: WASD to walk, Shift to sprint, C (hold) to crouch,
    /// Space to jump, and hold the right mouse button to look around. While the right button is up the
    /// mouse is a normal cursor for pointing at things in the workshop.
    /// </summary>
    public class DesktopPlayerInput : MonoBehaviour
    {
        [SerializeField] PlayerMotor motor;
        [SerializeField] InputActionAsset actions;
        [SerializeField] float mouseSensitivity = 0.12f;

        InputAction move;
        InputAction sprint;
        InputAction crouch;
        InputAction jump;

        /// <summary>True while the right mouse button is held and the mouse turns the view.</summary>
        public bool IsLooking { get; private set; }

        void OnEnable()
        {
            var map = actions.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            sprint = map.FindAction("Sprint", true);
            crouch = map.FindAction("Crouch", true);
            jump = map.FindAction("Jump", true);
            map.Enable();
        }

        void OnDisable()
        {
            actions.FindActionMap("Player")?.Disable();
            SetLooking(false);
        }

        void Update()
        {
            var mouse = Mouse.current;
            SetLooking(mouse != null && mouse.rightButton.isPressed && Application.isFocused);

            motor.SetMoveInput(move.ReadValue<Vector2>(), sprint.IsPressed(), crouch.IsPressed());
            if (jump.WasPressedThisFrame())
                motor.Jump();

            if (IsLooking)
                motor.Look(mouse.delta.ReadValue() * mouseSensitivity);
        }

        void SetLooking(bool looking)
        {
            if (looking == IsLooking)
                return;
            IsLooking = looking;
            Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
        }
    }
}
