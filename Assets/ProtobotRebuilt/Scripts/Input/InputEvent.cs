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

        /// <summary>V2: a key taken by something else right now (C and F over a hole or screw are quick fastening and
        /// placing, not the color picker or focus): the event doesn't fire.</summary>
        public static Func<InputEvent, bool> Claimed;

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
            // V2: and in the simulation sandbox (the keys drive the robot there).
            if ((TextInput.IsTyping || TextInput.ShortcutsPaused || Motion.Sandbox.Running || (Claimed != null && Claimed(this))) && IsKeyboardShortcut) {
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

        /// <summary>V2: the key this shortcut is on now (not a modifier), Key.None for mouse buttons.</summary>
        public Key ShortcutKey {
            get {
                var action = rebindAction == null || rebindAction.IsEmpty ? defaultAction : rebindAction.action;
                var key = Key.None;
                if (action == null) return key;
                foreach (var control in action.controls)
                    if (control is UnityEngine.InputSystem.Controls.KeyControl k
                        && k.keyCode is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt))
                        key = k.keyCode;
                return key;
            }
        }

        /// <summary>V2: the shortcut as the interface writes it ("Ctrl+Z"), after any change.</summary>
        public string CurrentKeysText {
            get {
                var action = rebindAction == null || rebindAction.IsEmpty ? defaultAction : rebindAction.action;
                if (action == null) return "";
                var parts = new List<string>();
                foreach (var binding in action.bindings) {
                    var path = string.IsNullOrEmpty(binding.overridePath) ? binding.path : binding.overridePath;
                    if (string.IsNullOrEmpty(path) || binding.isComposite || binding.isPartOfComposite) continue;
                    var text = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
                    if (text == "Control" || text == "Left Control" || text == "Right Control") text = "Ctrl";
                    if (text == "Left Shift" || text == "Right Shift") text = "Shift";
                    if (text == "Left Alt" || text == "Right Alt") text = "Alt";
                    parts.Add(text);
                }
                return string.Join("+", parts);
            }
        }

        /// <summary>V2: bound to keys only (shown in Edit > Keyboard shortcuts).</summary>
        public bool IsKeyboardOnly => IsKeyboardShortcut || ShortcutKey == Key.Escape;

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