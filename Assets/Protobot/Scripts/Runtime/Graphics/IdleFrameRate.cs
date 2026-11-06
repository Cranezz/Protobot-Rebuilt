using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.GraphicsQuality
{
    /// <summary>
    /// "Lower frame rate when idle" (<see cref="GraphicsSettingsData.idleFrameRate"/>): a CAD view only changes
    /// when the user does something, so after a few seconds without input the app draws 30 frames a second, and
    /// 10 while its window is in the background. Any input brings the full frame rate back on the next frame.
    /// Saves battery and heat: a laptop that stays cool doesn't slow its processor down.
    /// Things that move on their own (the simulation, a running motor, the benchmark) register in <see cref="Busy"/>.
    /// </summary>
    public class IdleFrameRate : MonoBehaviour
    {
        public const int IdleFps = 30;
        public const int BackgroundFps = 10;
        public const float IdleAfter = 3f;

        /// <summary>Checks that keep the full frame rate while they return true.</summary>
        public static readonly List<Func<bool>> Busy = new();

        static IdleFrameRate instance;
        float lastInput;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            Busy.Clear();
        }

        internal static void Ensure()
        {
            if (instance != null)
                return;
            var go = new GameObject("Idle Frame Rate") { hideFlags = HideFlags.HideInHierarchy };  // not DontSave: those outlive Play mode in the editor
            DontDestroyOnLoad(go);
            instance = go.AddComponent<IdleFrameRate>();
        }

        /// <summary>Wakes the full frame rate as if the user had moved the mouse (call after a change made in code).</summary>
        public static void Wake()
        {
            if (instance != null)
                instance.lastInput = Time.unscaledTime;
            Set(0);
        }

        void Update()
        {
            var settings = GraphicsSettingsManager.Current;
            if (settings == null || !settings.idleFrameRate)
            {
                Set(0);
                return;
            }

            var busy = IsBusy();
            if (HadInput() || busy)
                lastInput = Time.unscaledTime;

            // Something running (the simulation, a game) keeps its full frame rate even behind another window.
            var limit = 0;
            if (!Application.isFocused && !busy)
                limit = BackgroundFps;
            else if (Time.unscaledTime - lastInput > IdleAfter)
                limit = IdleFps;
            Set(limit);
        }

        static void Set(int limit)
        {
            if (GraphicsSettingsManager.idleLimit == limit)
                return;
            GraphicsSettingsManager.idleLimit = limit;
            GraphicsSettingsManager.ApplyFrameRate();
        }

        static bool IsBusy()
        {
            foreach (var check in Busy)
            {
                try
                {
                    if (check()) return true;
                }
                catch (Exception)
                {
                    // A check whose objects went away counts as not busy.
                }
            }
            return false;
        }

        static bool HadInput()
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0f || mouse.scroll.ReadValue().sqrMagnitude > 0f
                || mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed))
                return true;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.isPressed)
                return true;
            var gamepad = Gamepad.current;
            if (gamepad != null && (gamepad.leftStick.ReadValue().sqrMagnitude > 0.01f || gamepad.rightStick.ReadValue().sqrMagnitude > 0.01f
                || gamepad.leftTrigger.isPressed || gamepad.rightTrigger.isPressed))
                return true;
            return false;
        }
    }
}
