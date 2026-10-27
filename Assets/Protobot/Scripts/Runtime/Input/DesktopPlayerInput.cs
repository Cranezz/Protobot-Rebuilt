using Protobot.Audio;
using Protobot.Core;
using Protobot.Menus;
using Protobot.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.InputHandling
{
    /// <summary>
    /// Keyboard + mouse controls for the player: the mouse looks around (the cursor is locked to the
    /// centre), WASD to walk, Shift to sprint, C (hold) to crouch, Space to jump. B switches build view,
    /// where WASD floats the view (Space up, C down, Shift faster) to get close to small parts.
    /// Nothing moves while a menu is open, or while Ctrl is held (Ctrl+S, Ctrl+Z).
    /// </summary>
    public class DesktopPlayerInput : MonoBehaviour
    {
        const float BaseSensitivity = 0.1f;

        [SerializeField] PlayerMotor motor;
        [SerializeField] Camera viewCamera;
        [SerializeField] InputActionAsset actions;
        [SerializeField] Hud hud;

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
            var keyboard = Keyboard.current;
            var held = keyboard != null && keyboard.ctrlKey.isPressed;
            if (MenuState.AnyOpen || !Application.isFocused || held)
            {
                motor.SetMoveInput(Vector2.zero, false, !motor.BuildView && crouch.IsPressed());
                motor.SetFlyInput(Vector3.zero, false);
                if (MenuState.AnyOpen || !Application.isFocused)
                    return;
            }
            else if (keyboard != null && keyboard.bKey.wasPressedThisFrame)
            {
                motor.SetBuildView(!motor.BuildView);
                Sounds.Play(Sounds.Tick, 0.4f);
                if (hud != null)
                    hud.ShowToast(motor.BuildView
                        ? "Build view: WASD floats the view   ·   Space up, C down, Shift faster   ·   B goes back"
                        : "Walking again", motor.BuildView ? 3.5f : 1.2f);
            }

            if (!held)
            {
                var input = move.ReadValue<Vector2>();
                if (motor.BuildView)
                {
                    var vertical = (jump.IsPressed() ? 1f : 0f) - (crouch.IsPressed() ? 1f : 0f);
                    motor.SetMoveInput(Vector2.zero, false, false);
                    motor.SetFlyInput(new Vector3(input.x, vertical, input.y), sprint.IsPressed());
                }
                else
                {
                    motor.SetMoveInput(input, sprint.IsPressed(), crouch.IsPressed());
                    if (jump.WasPressedThisFrame())
                        motor.Jump();
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked && !InputLocks.LookSuppressed)
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
