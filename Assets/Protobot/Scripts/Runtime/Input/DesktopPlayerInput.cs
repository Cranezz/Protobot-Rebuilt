using Protobot.Core;
using Protobot.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.InputHandling
{
    /// <summary>
    /// Keyboard + mouse controls for the player: the mouse looks around (the cursor is locked to the
    /// centre), WASD to walk, Shift to sprint, C (hold) to crouch, Space to jump.
    /// Nothing moves while a menu is open.
    /// </summary>
    public class DesktopPlayerInput : MonoBehaviour
    {
        const float BaseSensitivity = 0.1f;

        [SerializeField] PlayerMotor motor;
        [SerializeField] Camera viewCamera;
        [SerializeField] InputActionAsset actions;

        InputAction move;
        InputAction sprint;
        InputAction crouch;
        InputAction jump;

        void OnEnable()
        {
            var map = actions.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            sprint = map.FindAction("Sprint", true);
            crouch = map.FindAction("Crouch", true);
            jump = map.FindAction("Jump", true);
            map.Enable();
            ControlSettings.Changed += ApplyCameraSettings;
            ApplyCameraSettings(ControlSettings.Current);
        }

        void OnDisable()
        {
            actions.FindActionMap("Player")?.Disable();
            ControlSettings.Changed -= ApplyCameraSettings;
        }

        void Update()
        {
            if (MenuState.AnyOpen || !Application.isFocused)
            {
                motor.SetMoveInput(Vector2.zero, false, crouch.IsPressed());
                return;
            }

            motor.SetMoveInput(move.ReadValue<Vector2>(), sprint.IsPressed(), crouch.IsPressed());
            if (jump.WasPressedThisFrame())
                motor.Jump();

            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var settings = ControlSettings.Current;
                var delta = mouse.delta.ReadValue() * (BaseSensitivity * settings.mouseSensitivity);
                if (settings.invertY)
                    delta.y = -delta.y;
                motor.Look(delta);
            }
        }

        void ApplyCameraSettings(ControlSettingsData settings)
        {
            if (viewCamera != null)
                viewCamera.fieldOfView = settings.fieldOfView;
        }
    }
}
