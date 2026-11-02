using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Protobot.InputEvents;

namespace Protobot.UI {
    public class ToggleInput : MonoBehaviour {
        [CacheComponent] private Toggle toggle;

        [Tooltip("Toggles the toggle when input is pressed!")]
        [SerializeField] private InputEvent input;

        private bool listening;

        private void Awake() => Listen();

        // V2: toggles in menus that start closed (the Move tool's "holes only") never woke up, so their key did
        // nothing until the menu had been opened once. Every toggle listens from launch.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ListenWhileHidden() {
            foreach (var toggleInput in FindObjectsByType<ToggleInput>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                toggleInput.Listen();
        }

        private void Listen() {
            if (listening || input == null)
                return;
            listening = true;
            input.performed += () => {
                if (!MouseInput.overUI)
                    toggle.isOn = !toggle.isOn;
            };
        }
    }
}
