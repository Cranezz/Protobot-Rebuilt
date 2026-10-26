using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.CameraControl
{
    /// <summary>
    /// Temporary developer camera for looking around the workshop until the real camera system exists.
    /// Hold the right mouse button to look; WASD to move, Q/E down/up, Shift to move faster.
    /// </summary>
    public class DevFlyCamera : MonoBehaviour
    {
        [SerializeField] float moveSpeed = 1.5f;
        [SerializeField] float fastMultiplier = 3f;
        [SerializeField] float lookSensitivity = 0.12f;

        float yaw;
        float pitch;

        void OnEnable()
        {
            var euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null)
                return;

            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue() * lookSensitivity;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            var move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += Vector3.forward;
            if (keyboard.sKey.isPressed) move += Vector3.back;
            if (keyboard.aKey.isPressed) move += Vector3.left;
            if (keyboard.dKey.isPressed) move += Vector3.right;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            if (keyboard.qKey.isPressed) move += Vector3.down;

            var speed = moveSpeed * (keyboard.leftShiftKey.isPressed ? fastMultiplier : 1f);
            transform.Translate(move * (speed * Time.unscaledDeltaTime), Space.Self);
        }
    }
}
