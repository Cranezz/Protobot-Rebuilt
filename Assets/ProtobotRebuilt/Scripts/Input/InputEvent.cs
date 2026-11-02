using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System;

namespace Protobot.InputEvents {
    public class InputEvent : MonoBehaviour {
        public InputAction defaultAction;
        public RebindAction rebindAction;

        public Action performed;
        public Action canceled;
        public bool IsPressed = false;

        private bool prevPressStatus = false;

        public void Awake() {
            //performed += () => Debug.Log("Performed " + name + " input event!");

            rebindAction = new RebindAction(name);

            rebindAction.OnCompleteRebind += () => defaultAction.Disable();
            rebindAction.OnSaveRebinds += () => defaultAction.Disable();

            rebindAction.OnResetRebinds += () => defaultAction.Enable();

            rebindAction.OnLoadRebinds += hasRebinds => {
                if (hasRebinds) {
                    defaultAction.Disable();
                }
            };

            if (rebindAction.IsEmpty) {
                defaultAction.Enable();
            }

            defaultAction.TrackTaps(); // V2
            rebindAction.action.TrackTaps();
            rebindAction.OnCompleteRebind += () => rebindAction.action.TrackTaps();
        }

        public void Update() {
            if (RebindAction.Rebinding) return;

            // V2: keyboard shortcuts (1-5 for the tools, Delete, R...) do nothing while a text box is being typed in.
            if (TextInput.IsTyping && IsKeyboardShortcut) {
                prevPressStatus = IsPressed = false;
                return;
            }

            IsPressed = defaultAction.AllControlsPressed() || rebindAction.action.AllControlsPressed();

            if (IsPressed != prevPressStatus) {
                if (IsPressed)
                    performed?.Invoke();
                else
                    canceled?.Invoke();
            }
            // V2: the original only polled, so a click pressed and let go between two frames did nothing.
            else if (!IsPressed && (defaultAction.AllControlsTappedThisFrame() || rebindAction.action.AllControlsTappedThisFrame())) {
                IsPressed = true;
                performed?.Invoke();
                IsPressed = false;
                canceled?.Invoke();
            }

            prevPressStatus = IsPressed;
        }

        // Bound only to keys (not the mouse), and not Escape, which still closes things while typing.
        private bool IsKeyboardShortcut {
            get {
                var action = rebindAction.IsEmpty ? defaultAction : rebindAction.action;
                var any = false;
                foreach (var control in action.controls) {
                    if (!(control.device is Keyboard) || control.name == "escape")
                        return false;
                    any = true;
                }
                return any;
            }
        }

        public void OnDisable() {
            defaultAction.Disable();
        }
        
        public string GetCurrentKeybind()
        {
            return defaultAction.GetBindingDisplayString();
        }
        
        public bool IsKeyPressed(string keyName)
        {
            var key = Keyboard.current.FindKeyOnCurrentKeyboardLayout(keyName);
            if (key == null) print("Key not found: " + keyName);
            return key != null && key.isPressed;
        }
        //i have no idea what im doing
        public void Rebind(string newBinding)
        {
            if (defaultAction != null)
            {
                defaultAction.ApplyBindingOverride(newBinding);
                Debug.Log($"Rebound to {newBinding}");
            }
            else
            {
                Debug.LogError("inputAction is null! Cannot rebind.");
            }
        }
    }
}