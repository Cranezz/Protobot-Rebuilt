using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Protobot.Devices {
    public enum V5Button { L1, L2, R1, R2, Up, Down, Left, Right, X, B, Y, A }

    /// <summary>The V5 controller's joystick axes, numbered as VEX does: 1 right stick across, 2 right stick up/down,
    /// 3 left stick up/down, 4 left stick across.</summary>
    public enum V5Axis { Axis1 = 1, Axis2 = 2, Axis3 = 3, Axis4 = 4 }

    /// <summary>
    /// V2: the V5 controller the robot is driven with (Phase 7). Each of its controls (the four stick axes, each way, and
    /// its 12 buttons) is bound to a key and a gamepad control, like a game's controller settings; the drive reads its
    /// sticks (Sandbox.Sticks) and a motor or cylinder set to a key also runs from the gamepad control bound to the
    /// controller button that key is on (Controls.Held/Pressed). Robot code (Phase 9) reads it as VEX's API does
    /// (Axis, Held). The bindings are saved in the build (BuildData.controller: each robot can have its own); a build
    /// without its own uses the player's default (PlayerPrefs), else these defaults: the sticks on W/A/S/D and the
    /// arrows (Edit > Keyboard shortcuts) and a gamepad's sticks; L1/L2 U/J, R1/R2 I/K, X/B E/Q (the motor keys), A Space
    /// (the cylinder key); the gamepad's shoulders, triggers, d-pad and face buttons where the V5 controller has them.
    /// </summary>
    public static class V5Controller {
        /// <summary>A control's id: "L1".."A" for buttons, "Axis3+" / "Axis3-" for a stick axis each way.</summary>
        public static readonly string[] Buttons = Enum.GetNames(typeof(V5Button));
        public static readonly string[] AxisSlots = { "Axis3+", "Axis3-", "Axis4-", "Axis4+", "Axis2+", "Axis2-", "Axis1-", "Axis1+" };
        public static IEnumerable<string> Slots => AxisSlots.Concat(Buttons);

        [Serializable] public class Binding { public string id, key, pad; }
        [Serializable] private class Layout { public List<Binding> bindings = new(); }

        private const string DefaultKey = "V2.Controller.Default";
        private const float Deadzone = 0.08f;

        /// <summary>The open build's own bindings (null: it uses the default).</summary>
        private static Dictionary<string, Binding> build;
        private static Dictionary<string, Binding> fallback;
        public static event Action Changed;

        // ------------------------------------------------------------------ defaults

        private static string DefaultPad(string id) => id switch {
            "Axis3+" => "<Gamepad>/leftStick/up", "Axis3-" => "<Gamepad>/leftStick/down",
            "Axis4+" => "<Gamepad>/leftStick/right", "Axis4-" => "<Gamepad>/leftStick/left",
            "Axis2+" => "<Gamepad>/rightStick/up", "Axis2-" => "<Gamepad>/rightStick/down",
            "Axis1+" => "<Gamepad>/rightStick/right", "Axis1-" => "<Gamepad>/rightStick/left",
            "L1" => "<Gamepad>/leftShoulder", "L2" => "<Gamepad>/leftTrigger", "R1" => "<Gamepad>/rightShoulder", "R2" => "<Gamepad>/rightTrigger",
            "Up" => "<Gamepad>/dpad/up", "Down" => "<Gamepad>/dpad/down", "Left" => "<Gamepad>/dpad/left", "Right" => "<Gamepad>/dpad/right",
            // The V5 controller's X is on top, B at the bottom, Y on the left and A on the right.
            "X" => "<Gamepad>/buttonNorth", "B" => "<Gamepad>/buttonSouth", "Y" => "<Gamepad>/buttonWest", "A" => "<Gamepad>/buttonEast",
            _ => "",
        };

        private static string DefaultKeyOf(string id) {
            string Shortcut(string bind) {
                var key = Tools.Keybinds.MainKey(bind);
                return key == Key.None ? "" : key.ToString();
            }
            return id switch {
                "Axis3+" => Shortcut("DriveForward"), "Axis3-" => Shortcut("DriveBack"),
                "Axis4+" => Shortcut("DriveRight"), "Axis4-" => Shortcut("DriveLeft"),
                "Axis2+" => Shortcut("Drive2Forward"), "Axis2-" => Shortcut("Drive2Back"),
                "Axis1+" => Shortcut("Drive2Right"), "Axis1-" => Shortcut("Drive2Left"),
                "L1" => "U", "L2" => "J", "R1" => "I", "R2" => "K",
                "X" => Controls.DefaultForward.ToString(), "B" => Controls.DefaultReverse.ToString(), "A" => Controls.DefaultCylinder.ToString(),
                _ => "",
            };
        }

        public static Binding Default(string id) => new() { id = id, key = DefaultKeyOf(id), pad = DefaultPad(id) };

        // ------------------------------------------------------------------ the bindings in use

        private static Dictionary<string, Binding> Fallback() {
            if (fallback != null) return fallback;
            fallback = Parse(PlayerPrefs.GetString(DefaultKey, ""));
            return fallback;
        }

        public static Binding Get(string id) {
            if (build != null && build.TryGetValue(id, out var own)) return own;
            if (Fallback().TryGetValue(id, out var player)) return player;
            return Default(id);
        }

        public static Key KeyOf(string id) => Enum.TryParse(Get(id).key ?? "", out Key key) ? key : Key.None;
        public static string PadOf(string id) => Get(id).pad ?? "";
        public static bool HasOwn => build != null;

        /// <summary>Binds a control (this robot's bindings: saved with the build).</summary>
        public static void Set(string id, Key? key, string pad) {
            build ??= Slots.ToDictionary(s => s, s => Copy(Get(s)));
            var b = build[id];
            if (key.HasValue) b.key = key.Value == Key.None ? "" : key.Value.ToString();
            if (pad != null) b.pad = pad;
            // One key, one control (a key on two buttons would run both).
            if (key.HasValue && key.Value != Key.None)
                foreach (var other in build.Values)
                    if (other != b && other.key == b.key) other.key = "";
            Changed?.Invoke();
        }

        /// <summary>Back to the defaults (the player's, else Protobot's) for this robot.</summary>
        public static void ResetToDefault() {
            build = null;
            Changed?.Invoke();
        }

        /// <summary>This robot's bindings become the default for robots without their own.</summary>
        public static void MakeDefault() {
            var all = Slots.Select(s => Copy(Get(s))).ToList();
            PlayerPrefs.SetString(DefaultKey, JsonUtility.ToJson(new Layout { bindings = all }));
            fallback = null;
            Changed?.Invoke();
        }

        private static Binding Copy(Binding b) => new() { id = b.id, key = b.key, pad = b.pad };

        private static Dictionary<string, Binding> Parse(string json) {
            var result = new Dictionary<string, Binding>();
            if (string.IsNullOrEmpty(json)) return result;
            try {
                var layout = JsonUtility.FromJson<Layout>(json);
                foreach (var b in layout.bindings)
                    if (!string.IsNullOrEmpty(b.id)) result[b.id] = b;
            }
            catch (Exception e) { Debug.LogWarning("V2 controller: " + e.Message); }
            return result;
        }

        /// <summary>As saved in a build file ("" when the robot uses the default).</summary>
        public static string Token => build == null ? "" : JsonUtility.ToJson(new Layout { bindings = build.Values.ToList() });

        public static void Load(string token) {
            build = string.IsNullOrEmpty(token) ? null : Parse(token);
            if (build != null && build.Count == 0) build = null;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ reading it

        private static readonly Dictionary<string, InputControl> padControls = new();
        private static Gamepad padFor;

        private static InputControl Pad(string id) {
            var pad = Gamepad.current;
            if (pad == null) return null;
            if (pad != padFor) {
                padControls.Clear();
                padFor = pad;
            }
            var path = PadOf(id);
            if (string.IsNullOrEmpty(path)) return null;
            var cacheKey = id + "|" + path;
            if (!padControls.TryGetValue(cacheKey, out var control)) {
                control = InputSystem.FindControl(path.Replace("<Gamepad>", pad.path));
                if (control == null) {
                    var relative = path.Replace("<Gamepad>/", "");
                    control = pad.TryGetChildControl(relative);
                }
                padControls[cacheKey] = control;
            }
            return control;
        }

        private static float PadValue(string id) {
            var control = Pad(id);
            if (control == null) return 0f;
            try {
                return control switch {
                    ButtonControl b => b.ReadValue(),
                    AxisControl a => Mathf.Max(0f, a.ReadValue()),
                    _ => Convert.ToSingle(control.ReadValueAsObject()),
                };
            }
            catch { return 0f; }
        }

        private static bool Typing => InputEvents.TextInput.IsTyping;

        private static bool KeyHeld(string id) {
            var key = KeyOf(id);
            if (key == Key.None || Keyboard.current == null || Typing) return false;
            var control = Keyboard.current[key];
            return control != null && control.isPressed;
        }

        /// <summary>A stick axis, -1..1 (keys give full deflection; a gamepad stick beyond its dead zone).</summary>
        public static float Axis(V5Axis axis) {
            var plus = "Axis" + (int)axis + "+";
            var minus = "Axis" + (int)axis + "-";
            var keys = (KeyHeld(plus) ? 1f : 0f) - (KeyHeld(minus) ? 1f : 0f);
            var pad = PadValue(plus) - PadValue(minus);
            if (Mathf.Abs(pad) < Deadzone) pad = 0f;
            return Mathf.Clamp(Mathf.Abs(pad) > Mathf.Abs(keys) ? pad : keys, -1f, 1f);
        }

        /// <summary>The VEX value of an axis, -127..127.</summary>
        public static int AxisValue(V5Axis axis) => Mathf.RoundToInt(Axis(axis) * 127f);

        public static bool Held(V5Button button) => KeyHeld(button.ToString()) || PadValue(button.ToString()) > 0.5f;

        public static bool Held(string id) => id.StartsWith("Axis") ? (KeyHeld(id) || PadValue(id) > 0.5f) : KeyHeld(id) || PadValue(id) > 0.5f;

        private static readonly Dictionary<string, bool> padWas = new();
        private static int padFrame = -1;
        private static readonly Dictionary<string, bool> padNow = new();

        /// <summary>Whether the gamepad control of the button a key is on is held (a motor set to that key runs).</summary>
        public static bool PadHeldForKey(Key key) {
            if (key == Key.None || Gamepad.current == null) return false;
            foreach (var id in Buttons)
                if (KeyOf(id) == key && PadValue(id) > 0.5f) return true;
            return false;
        }

        /// <summary>Whether that gamepad control went down this frame.</summary>
        public static bool PadPressedForKey(Key key) {
            if (key == Key.None || Gamepad.current == null) return false;
            if (padFrame != Time.frameCount) {
                padFrame = Time.frameCount;
                padWas.Clear();
                foreach (var kv in padNow) padWas[kv.Key] = kv.Value;
                foreach (var id in Buttons) padNow[id] = PadValue(id) > 0.5f;
            }
            foreach (var id in Buttons)
                if (KeyOf(id) == key && padNow.TryGetValue(id, out var now) && now && !(padWas.TryGetValue(id, out var was) && was)) return true;
            return false;
        }

        // ------------------------------------------------------------------ names

        public static string KeyLabel(string id) {
            var key = KeyOf(id);
            return key == Key.None ? "—" : Controls.KeyName(key);
        }

        public static string PadLabel(string id) {
            var path = PadOf(id);
            if (string.IsNullOrEmpty(path)) return "—";
            var name = path.Replace("<Gamepad>/", "");
            return name switch {
                "leftShoulder" => "LB", "rightShoulder" => "RB", "leftTrigger" => "LT", "rightTrigger" => "RT",
                "buttonNorth" => "Y / △", "buttonSouth" => "A / ✕", "buttonWest" => "X / □", "buttonEast" => "B / ○",
                "start" => "Start", "select" => "Select", "leftStickPress" => "L3", "rightStickPress" => "R3",
                _ => name.Replace("leftStick/", "L stick ").Replace("rightStick/", "R stick ").Replace("dpad/", "D-pad "),
            };
        }

        public static string SlotName(string id) => id switch {
            "Axis3+" => "Left stick up", "Axis3-" => "Left stick down", "Axis4+" => "Left stick right", "Axis4-" => "Left stick left",
            "Axis2+" => "Right stick up", "Axis2-" => "Right stick down", "Axis1+" => "Right stick right", "Axis1-" => "Right stick left",
            "Up" => "Up arrow", "Down" => "Down arrow", "Left" => "Left arrow", "Right" => "Right arrow",
            _ => id,
        };

        /// <summary>The gamepad control pressed this frame (to bind it), as a path, or null.</summary>
        public static string PressedPadControl() {
            var pad = Gamepad.current;
            if (pad == null) return null;
            foreach (var c in pad.allControls) {
                if (c is ButtonControl b && !c.synthetic && b.wasPressedThisFrame && !(c.parent is StickControl) && c.parent != null) {
                    var relative = c.path.Substring(pad.path.Length + 1);
                    return "<Gamepad>/" + relative;
                }
            }
            // A stick pushed past half: that direction.
            foreach (var (stick, name) in new[] { (pad.leftStick, "leftStick"), (pad.rightStick, "rightStick") }) {
                var v = stick.ReadValue();
                if (v.magnitude < 0.6f) continue;
                var dir = Mathf.Abs(v.x) > Mathf.Abs(v.y) ? (v.x > 0 ? "right" : "left") : (v.y > 0 ? "up" : "down");
                return "<Gamepad>/" + name + "/" + dir;
            }
            return null;
        }
    }
}
