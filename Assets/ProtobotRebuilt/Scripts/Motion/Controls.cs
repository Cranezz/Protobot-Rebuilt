using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Protobot.Motion {
    // V2: how motors and cylinders are driven, like a PROS program: each motor has a forward and a reverse key
    // (several motors on one key run together), a speed (share of its cartridge's rpm), reversed (PROS's
    // "reversed" flag: forward turns the other way) and optionally a side of the drivetrain; a cylinder has one
    // key that extends and retracts it. Kept in each part's saved state ("fkey:E;rkey:Q;rev:1;speed:0.75;
    // name:Arm;side:L"), so they go with the build. The drivetrain's control (tank, arcade, split arcade) is saved
    // in the build too (`BuildData.drive`); a build that never chose one uses the last one picked (preferences).
    public static class Controls {
        public const Key DefaultForward = Key.E, DefaultReverse = Key.Q, DefaultCylinder = Key.Space;

        public enum DriveMode { Tank, Arcade, SplitArcade }

        public static readonly string[] DriveModeNames = { "Tank", "Arcade", "Split arcade" };

        /// <summary>The open build's drive control, or null if it never chose one.</summary>
        public static DriveMode? BuildDrive;

        public static DriveMode Drive {
            get => BuildDrive ?? (DriveMode)Mathf.Clamp(PlayerPrefs.GetInt("V2.DriveMode", (int)DriveMode.SplitArcade), 0, 2);
            set {
                BuildDrive = value;
                PlayerPrefs.SetInt("V2.DriveMode", (int)value);
            }
        }

        /// <summary>The drive control as saved in a build file ("" when it never chose one).</summary>
        public static string DriveToken => BuildDrive.HasValue ? BuildDrive.Value.ToString() : "";

        public static void LoadDrive(string token) =>
            BuildDrive = !string.IsNullOrEmpty(token) && System.Enum.TryParse(token, out DriveMode mode) ? mode : null;

        /// <summary>The drivetrain's keys as (keys, what they do) lines, for the on-screen card.</summary>
        public static (string keys, string action)[] DriveLayout(DriveMode mode) => mode switch {
            DriveMode.Tank => new[] { ("W / S", "left side forward / back"), ("Up / Down", "right side forward / back") },
            DriveMode.Arcade => new[] { ("W / S", "drive forward / back"), ("A / D", "turn left / right") },
            _ => new[] { ("W / S", "drive forward / back"), ("Left / Right", "turn left / right") },
        };

        public static string DriveHelp(DriveMode mode) => mode switch {
            DriveMode.Tank => "Left stick (W/S) drives the left side, right stick (Up/Down) the right side",
            DriveMode.Arcade => "One stick (W/A/S/D): up/down drives, left/right turns",
            _ => "Left stick (W/S) drives, right stick (Left/Right) turns",
        };

        // ------------------------------------------------------------------ keys

        public static Key ForwardKey(GameObject part) => KeyToken(part, "fkey", DefaultForward);
        public static Key ReverseKey(GameObject part) => KeyToken(part, "rkey", DefaultReverse);
        public static Key CylinderKey(GameObject part) => KeyToken(part, "key", DefaultCylinder);

        public static void SetForwardKey(GameObject part, Key key) => Dynamics.SetToken(part, "fkey", key == DefaultForward ? null : key.ToString());
        public static void SetReverseKey(GameObject part, Key key) => Dynamics.SetToken(part, "rkey", key == DefaultReverse ? null : key.ToString());
        public static void SetCylinderKey(GameObject part, Key key) => Dynamics.SetToken(part, "key", key == DefaultCylinder ? null : key.ToString());

        private static Key KeyToken(GameObject part, string token, Key fallback) {
            var value = Dynamics.Token(part, token);
            return value != null && System.Enum.TryParse(value, out Key key) ? key : fallback;
        }

        public static bool Held(Key key) {
            if (key == Key.None || Keyboard.current == null) return false;
            var control = Keyboard.current[key];
            return control != null && control.isPressed;
        }

        public static bool Pressed(Key key) {
            if (key == Key.None || Keyboard.current == null) return false;
            var control = Keyboard.current[key];
            return control != null && control.wasPressedThisFrame;
        }

        /// <summary>The key pressed this frame (for setting a key), or None.</summary>
        public static Key PressedKey() {
            if (Keyboard.current == null) return Key.None;
            foreach (KeyControl control in Keyboard.current.allKeys)
                if (control != null && control.wasPressedThisFrame) return control.keyCode;
            return Key.None;
        }

        public static string KeyName(Key key) => key switch {
            Key.None => "none",
            Key.UpArrow => "Up",
            Key.DownArrow => "Down",
            Key.LeftArrow => "Left",
            Key.RightArrow => "Right",
            Key.Space => "Space",
            Key.LeftShift => "Shift",
            Key.RightShift => "Right Shift",
            Key.Enter => "Enter",
            _ => key.ToString().Replace("Digit", "").Replace("Numpad", "Num "),
        };

        /// <summary>The build mode shortcut on a key (it can't run motors there), or null.</summary>
        public static string ShortcutOn(Key key) {
            var name = KeyName(key).ToUpperInvariant();
            foreach (var e in Object.FindObjectsByType<InputEvents.InputEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                string bind;
                try { bind = e.GetCurrentKeybind(); }
                catch { continue; }
                if (string.IsNullOrEmpty(bind) || bind.Contains("|")) continue;  // with Ctrl: not the bare key
                if (bind.Trim().ToUpperInvariant() == name) return e.name;
            }
            return key == Key.T ? "Reset turn (while placing)" : null;
        }

        /// <summary>Keys the sandbox's drive uses.</summary>
        public static bool DriveKey(Key key) => key is Key.W or Key.A or Key.S or Key.D or Key.UpArrow or Key.DownArrow or Key.LeftArrow or Key.RightArrow;

        // ------------------------------------------------------------------ other settings

        public static bool Reversed(GameObject part) => Dynamics.Token(part, "rev") == "1";
        public static void SetReversed(GameObject part, bool value) => Dynamics.SetToken(part, "rev", value ? "1" : null);

        /// <summary>V2: a motor that holds its position when it isn't driven (the V5 motor's "hold" brake mode: arms and
        /// lifts stay up), instead of only braking. Token "hold:1".</summary>
        public static bool Hold(GameObject part) => Dynamics.Token(part, "hold") == "1";
        public static void SetHold(GameObject part, bool value) => Dynamics.SetToken(part, "hold", value ? "1" : null);

        /// <summary>Share of full speed, 0.05 to 1.</summary>
        public static float Speed(GameObject part) =>
            float.TryParse(Dynamics.Token(part, "speed"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Mathf.Clamp(v, 0.05f, 1f) : 1f;

        public static void SetSpeed(GameObject part, float value) {
            value = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, 0.05f, 1f);
            Dynamics.SetToken(part, "speed", value >= 0.999f ? null : value.ToString("0.##", CultureInfo.InvariantCulture));
        }

        /// <summary>"L", "R" or "" (not on the drivetrain).</summary>
        public static string Side(GameObject part) => Dynamics.Token(part, "side") ?? "";
        public static void SetSide(GameObject part, string side) => Dynamics.SetToken(part, "side", string.IsNullOrEmpty(side) ? null : side);

        /// <summary>The name given to a motor or cylinder, or null.</summary>
        public static string GivenName(GameObject part) {
            var value = Dynamics.Token(part, "name");
            return string.IsNullOrEmpty(value) ? null : value.Replace("%3B", ";").Replace("%3A", ":");
        }

        public static void SetName(GameObject part, string name) {
            name = (name ?? "").Trim();
            Dynamics.SetToken(part, "name", name.Length == 0 ? null : name.Replace(";", "%3B").Replace(":", "%3A"));
        }

        // ------------------------------------------------------------------ what can be driven

        public static List<PneumaticCylinder> Cylinders() =>
            PneumaticCylinder.All.Where(c => c != null && c.enabled && !c.gameObject.IsDeleted()).ToList();
    }
}
