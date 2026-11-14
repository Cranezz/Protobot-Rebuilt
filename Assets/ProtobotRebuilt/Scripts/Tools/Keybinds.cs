using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools {
    // V2: the keys of the V2 features (quick placing, the sketch editor's tools, the sandbox's drive keys...), each
    // changeable in Edit > Keyboard shortcuts (KeybindsWindow, which also lists the original's InputEvents). A key is
    // a combination like "C", "Shift+C" or "Ctrl+Z" (Key enum names), saved in PlayerPrefs "V2.Key.<id>"; "" = none.
    // `loose` keys also fire with extra modifiers held (Shift/Ctrl change the step of a nudge).
    public static class Keybinds {
        public class Bind {
            public string id, group, label, defaultKey, help;
            public bool loose;
            public string Key => PlayerPrefs.GetString(Prefix + id, defaultKey ?? "");
            public bool Changed => PlayerPrefs.HasKey(Prefix + id) && Key != (defaultKey ?? "");
        }

        private const string Prefix = "V2.Key.";
        private static readonly List<Bind> binds = new();
        private static readonly Dictionary<string, Bind> byId = new();
        private static readonly Dictionary<string, (Key key, bool ctrl, bool shift, bool alt, bool ok)> parsed = new();

        /// <summary>Every V2 key, in the window's order.</summary>
        public static IReadOnlyList<Bind> All => binds;
        public static System.Action Changed;

        static Keybinds() {
            const string quick = "Quick placing";
            Add("QuickScrew", quick, "Screw + nut through the hole and the one touching it", "C");
            Add("QuickScrewAll", quick, "Screw + nut through every hole in line", "Shift+C");
            Add("HoldScrew", quick, "Hold: screw over a hole / nut over a screw (wheel: size)", "F");
            Add("HoldSpacer", quick, "Hold: nylon spacer over a screw or hole (wheel: length)", "E");
            Add("PlaceTubing", quick, "Over a fitting's port: run tubing; over a brain, motor or sensor port: run a cable", "E");   // V2
            const string place = "Placing and editing";
            Add("ResetTurn", place, "Placing: set the part's turn back", "T");
            Add("Mirror", place, "Mirror the selected part (arrows on its sides)", "M");
            Add("SawTilt", place, "Saw: tilt rings", "R");
            const string sim = "Simulation (drive)";
            Add("DriveForward", sim, "Left stick forward (W)", "W", true);
            Add("DriveBack", sim, "Left stick back", "S", true);
            Add("DriveLeft", sim, "Left stick left", "A", true);
            Add("DriveRight", sim, "Left stick right", "D", true);
            Add("Drive2Forward", sim, "Right stick forward", "UpArrow", true);
            Add("Drive2Back", sim, "Right stick back", "DownArrow", true);
            Add("Drive2Left", sim, "Right stick left", "LeftArrow", true);
            Add("Drive2Right", sim, "Right stick right", "RightArrow", true);
            Add("StepFrame", sim, "Paused: one physics step", "Period");
            const string game = "Play game: start position";
            Add("NudgeLeft", game, "Nudge left 1/4\" (Shift: 1/16\")", "LeftArrow", true);
            Add("NudgeRight", game, "Nudge right", "RightArrow", true);
            Add("NudgeBack", game, "Nudge toward the audience", "DownArrow", true);
            Add("NudgeAway", game, "Nudge away from the audience", "UpArrow", true);
            Add("TurnLeft", game, "Turn 5° counterclockwise (Shift 0.5°)", "Q", true);
            Add("TurnRight", game, "Turn 5° clockwise", "E", true);
            const string sketch = "Sketch editor";
            Add("SketchUndo", sketch, "Undo", "Ctrl+Z");
            Add("SketchRedo", sketch, "Redo", "Ctrl+Y");
            Add("SketchSelectAll", sketch, "Select all", "Ctrl+A");
            Add("SketchCopy", sketch, "Copy", "Ctrl+C");
            Add("SketchPaste", sketch, "Paste", "Ctrl+V");
            Add("SketchFit", sketch, "Fit the view", "F");
            Add("SketchConstruction", sketch, "Construction geometry on/off", "Q");
            Add("SheetsFit", "Plastic sheets", "Fit the view", "F");
        }

        public static Bind Add(string id, string group, string label, string defaultKey, bool loose = false) {
            if (byId.TryGetValue(id, out var existing)) return existing;
            var bind = new Bind { id = id, group = group, label = label, defaultKey = defaultKey, loose = loose };
            binds.Add(bind);
            byId[id] = bind;
            return bind;
        }

        public static Bind Find(string id) => byId.TryGetValue(id, out var b) ? b : null;

        public static void Set(string id, string combo) {
            var bind = Find(id);
            if (bind == null) return;
            if (combo == (bind.defaultKey ?? "")) PlayerPrefs.DeleteKey(Prefix + id);
            else PlayerPrefs.SetString(Prefix + id, combo ?? "");
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void Reset(string id) {
            PlayerPrefs.DeleteKey(Prefix + id);
            Changed?.Invoke();
        }

        public static void ResetAll() {
            foreach (var bind in binds) PlayerPrefs.DeleteKey(Prefix + bind.id);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static (Key key, bool ctrl, bool shift, bool alt, bool ok) Parse(string combo) {
            if (string.IsNullOrEmpty(combo)) return (Key.None, false, false, false, false);
            if (parsed.TryGetValue(combo, out var known)) return known;
            bool ctrl = false, shift = false, alt = false;
            var key = Key.None;
            foreach (var part in combo.Split('+')) {
                if (part == "Ctrl") ctrl = true;
                else if (part == "Shift") shift = true;
                else if (part == "Alt") alt = true;
                else if (!System.Enum.TryParse(part, out key)) key = Key.None;
            }
            var result = (key, ctrl, shift, alt, key != Key.None);
            parsed[combo] = result;
            return result;
        }

        private static bool Modifiers(Keyboard k, (Key key, bool ctrl, bool shift, bool alt, bool ok) p, bool loose) {
            var ctrl = k.ctrlKey.isPressed || k.leftCommandKey.isPressed;
            if (loose) return (!p.ctrl || ctrl) && (!p.shift || k.shiftKey.isPressed) && (!p.alt || k.altKey.isPressed);
            return ctrl == p.ctrl && k.shiftKey.isPressed == p.shift && k.altKey.isPressed == p.alt;
        }

        /// <summary>The key went down this frame with its modifiers held (exactly, unless the bind is loose).</summary>
        public static bool Pressed(string id) {
            var bind = Find(id);
            var k = Keyboard.current;
            if (bind == null || k == null) return false;
            var p = Parse(bind.Key);
            return p.ok && k[p.key].wasPressedThisFrame && Modifiers(k, p, bind.loose);
        }

        /// <summary>The key is down (modifiers are checked only when it went down: see Pressed).</summary>
        public static bool Held(string id) {
            var bind = Find(id);
            var k = Keyboard.current;
            if (bind == null || k == null) return false;
            var p = Parse(bind.Key);
            return p.ok && k[p.key].isPressed && (bind.loose || Modifiers(k, p, true));
        }

        public static bool Released(string id) {
            var bind = Find(id);
            var k = Keyboard.current;
            if (bind == null || k == null) return false;
            var p = Parse(bind.Key);
            return p.ok && k[p.key].wasReleasedThisFrame;
        }

        /// <summary>The main key of a bind (for code that also checks it itself), Key.None when unbound.</summary>
        public static Key MainKey(string id) {
            var bind = Find(id);
            return bind == null ? Key.None : Parse(bind.Key).key;
        }

        /// <summary>"Shift+C", "Ctrl+Z", "Up"…; "" when unbound.</summary>
        public static string Label(string id) {
            var bind = Find(id);
            return bind == null ? "" : Display(bind.Key);
        }

        public static string Display(string combo) {
            if (string.IsNullOrEmpty(combo)) return "";
            return string.Join("+", combo.Split('+').Select(KeyName));
        }

        public static string KeyName(string key) => key switch {
            "UpArrow" => "Up", "DownArrow" => "Down", "LeftArrow" => "Left", "RightArrow" => "Right",
            "Period" => ".", "Comma" => ",", "Slash" => "/", "Backslash" => "\\", "Semicolon" => ";", "Quote" => "'",
            "LeftBracket" => "[", "RightBracket" => "]", "Minus" => "-", "Equals" => "=", "Backquote" => "`",
            _ when key.StartsWith("Digit") => key.Substring(5),
            _ when key.StartsWith("Numpad") => "Num " + key.Substring(6),
            _ => key,
        };

        /// <summary>The combination being pressed right now (a key that just went down plus the modifiers held), or
        /// null. Modifier keys alone don't count.</summary>
        public static string Capture(out bool cancel) {
            cancel = false;
            var k = Keyboard.current;
            if (k == null) return null;
            if (k.escapeKey.wasPressedThisFrame) { cancel = true; return null; }
            foreach (var control in k.allKeys) {
                if (control == null || !control.wasPressedThisFrame) continue;
                var key = control.keyCode;
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
                    or Key.LeftMeta or Key.RightMeta or Key.ContextMenu or Key.None) continue;
                var combo = key.ToString();
                if (k.altKey.isPressed) combo = "Alt+" + combo;
                if (k.shiftKey.isPressed) combo = "Shift+" + combo;
                if (k.ctrlKey.isPressed || k.leftCommandKey.isPressed) combo = "Ctrl+" + combo;
                return combo;
            }
            return null;
        }
    }
}
