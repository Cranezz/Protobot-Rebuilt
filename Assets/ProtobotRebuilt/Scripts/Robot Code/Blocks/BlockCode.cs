using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Protobot.RobotCode.Blocks {
    /// <summary>
    /// V2 (Blocks): the Python a blocks program makes, laid out as VEXcode's Blocks-to-Python does: the robot
    /// configuration, the variables and messages, a function per hat block (when started, when autonomous, when a
    /// button is pressed...), the event hookups, a Competition when there are autonomous / driver control hats, then
    /// every "when started" starts at once (the others in threads). Loops wait 5 ms each time round, as VEXcode's do.
    /// Blocks not under a hat don't run. Each line remembers the block it came from (errors and running lines show on
    /// the blocks).
    /// </summary>
    public sealed class BlockCode {
        public const int HeaderLines = 2;   // the blocks line and the notice, before the Python

        public string python;
        /// <summary>The block each line of <see cref="python"/> came from (index = line - 1), or null.</summary>
        public readonly List<Block> lineBlocks = new();
        public Dictionary<string, List<string>> devices;

        private readonly StringBuilder sb = new();
        private BlockProgram program;
        private bool usesMath, usesRandom;
        private string drivetrain;

        private static readonly HashSet<string> Keywords = new() {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except",
            "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try",
            "while", "with", "yield", "print", "wait", "brain", "controller", "competition", "math", "random", "str", "int", "float", "len", "abs", "round",
        };

        /// <summary>The robot's devices in a configuration: kind (BlockDefs.DeviceClasses) to names, in order.</summary>
        public static Dictionary<string, List<string>> Devices(string config) {
            var result = BlockDefs.DeviceClasses.Keys.ToDictionary(k => k, _ => new List<string>());
            foreach (Match m in Regex.Matches(config ?? "", @"^(\w+)\s*=\s*(\w+)\(", RegexOptions.Multiline)) {
                var name = m.Groups[1].Value;
                var cls = m.Groups[2].Value;
                foreach (var kv in BlockDefs.DeviceClasses)
                    if (kv.Value.Contains(cls)) result[kv.Key].Add(name);
            }
            return result;
        }

        public const string DefaultConfig = "from vex import *\n\nbrain = Brain()\ncontroller = Controller()";

        public static BlockCode Generate(BlockProgram p) {
            var code = new BlockCode { program = p };
            code.Build();
            return code;
        }

        /// <summary>A name made fit for Python (letters, digits, _; not a keyword).</summary>
        public static string Ident(string name) {
            var s = Regex.Replace(name ?? "", @"[^A-Za-z0-9_]", "_");
            if (s.Length == 0) s = "_";
            if (char.IsDigit(s[0]) || Keywords.Contains(s)) s = "v_" + s;
            return s;
        }

        // ------------------------------------------------------------------ writing

        private void Line(int indent, string text, Block from) {
            foreach (var part in text.Split('\n')) {
                sb.Append(' ', indent * 4).Append(part).Append('\n');
                lineBlocks.Add(from);
            }
        }

        private void Build() {
            var config = string.IsNullOrWhiteSpace(program.config) ? DefaultConfig : program.config.TrimEnd();
            devices = Devices(config);
            drivetrain = devices["drivetrain"].FirstOrDefault();

            // The functions first (they tell which modules are used), then everything in order.
            var body = new BlockCode { program = program, devices = devices, drivetrain = drivetrain };
            var hats = program.stacks.Where(s => s.Def != null && s.Def.shape == BlockShape.Hat).ToList();
            var names = new Dictionary<Block, string>();
            var counts = new Dictionary<string, int>();
            string Name(string kind) {
                counts.TryGetValue(kind, out var n);
                counts[kind] = n + 1;
                return kind + "_" + n;
            }
            foreach (var hat in hats) {
                var f = hat.type switch {
                    "when_started" => "when_started" + (counts.TryGetValue("ws", out var n) ? n + 1 : 1),
                    "when_autonomous" => Name("onauton_autonomous"),
                    "when_driver" => Name("ondriver_drivercontrol"),
                    "when_button" => Name("oncontroller_" + Field(hat, 0) + "_" + Field(hat, 1)),
                    "when_axis" => Name("oncontroller_" + Field(hat, 0) + "_changed"),
                    "when_receive" => Name("onevent_" + VarName(hat, 0, "event")),
                    "when_timer" => Name("ontimer"),
                    "when_screen" => Name("onscreen_" + Field(hat, 0)),
                    "when_bumper" => Name("onbumper_" + Field(hat, 1)),
                    _ => Name("on_" + hat.type),
                };
                if (hat.type == "when_started") counts["ws"] = counts.TryGetValue("ws", out var w) ? w + 1 : 1;
                names[hat] = f;
                body.Line(0, "def " + f + "():", hat);
                var globals = program.numbers.Concat(program.booleans).Select(Ident).Distinct().ToList();
                if (globals.Count > 0) body.Line(1, "global " + string.Join(", ", globals), hat);
                var before = body.lineBlocks.Count;
                body.Stack(hat.next, 1);
                if (body.lineBlocks.Count == before) body.Line(1, "pass", hat);
                body.Line(0, "", null);
            }
            usesMath = body.usesMath;
            usesRandom = body.usesRandom;

            foreach (var line in config.Split('\n')) Line(0, line.TrimEnd('\r'), null);
            if (usesMath || usesRandom) {
                Line(0, "", null);
                if (usesMath) Line(0, "import math", null);
                if (usesRandom) Line(0, "import random", null);
            }
            var vars = program.numbers.Select(Ident).Distinct().ToList();
            var bools = program.booleans.Select(Ident).Distinct().ToList();
            var events = program.events.Select(Ident).Distinct().ToList();
            if (vars.Count + bools.Count + events.Count > 0) {
                Line(0, "", null);
                Line(0, "# Variables and messages", null);
                foreach (var v in vars) Line(0, v + " = 0", null);
                foreach (var v in bools) Line(0, v + " = False", null);
                foreach (var v in events) Line(0, v + " = Event()", null);
            }
            Line(0, "", null);
            // The functions (their lines keep their blocks).
            var text = body.sb.ToString();
            sb.Append(text);
            lineBlocks.AddRange(body.lineBlocks);

            // Hooking up the events.
            var hooked = false;
            foreach (var hat in hats) {
                var f = names[hat];
                string hook = hat.type switch {
                    "when_button" => "controller." + Field(hat, 0) + "." + Field(hat, 1) + "(" + f + ")",
                    "when_axis" => "controller." + Field(hat, 0) + ".changed(" + f + ")",
                    "when_receive" => VarName(hat, 0, "event") + ".set(" + f + ")",
                    "when_timer" => "brain.timer.event(" + f + ", int(" + Number(hat.fields[0]) + " * 1000))",
                    "when_screen" => "brain.screen." + Field(hat, 0) + "(" + f + ")",
                    "when_bumper" => DeviceName(hat, 0, out var bumper) ? bumper + "." + Field(hat, 1) + "(" + f + ")" : null,
                    _ => null,
                };
                if (hook == null) continue;
                if (!hooked) Line(0, "# Events", null);
                hooked = true;
                Line(0, hook, hat);
            }
            if (hooked) Line(0, "", null);

            // Competition: the autonomous and driver control hats.
            var autons = hats.Where(h => h.type == "when_autonomous").ToList();
            var drivers = hats.Where(h => h.type == "when_driver").ToList();
            if (autons.Count + drivers.Count > 0) {
                Line(0, "# Competition: autonomous, then driver control", null);
                void Period(string name, List<Block> list) {
                    Line(0, "def " + name + "():", null);
                    if (list.Count == 0) Line(1, "pass", null);
                    for (var i = 1; i < list.Count; i++) Line(1, "Thread(" + names[list[i]] + ")", list[i]);
                    if (list.Count > 0) Line(1, names[list[0]] + "()", list[0]);
                }
                Period("vexcode_auton_function", autons);
                Period("vexcode_driver_function", drivers);
                Line(0, "competition = Competition(vexcode_driver_function, vexcode_auton_function)", null);
                Line(0, "", null);
            }

            // Start: every "when started" at once.
            var starts = hats.Where(h => h.type == "when_started").ToList();
            if (starts.Count > 0) {
                Line(0, "# Start", null);
                for (var i = 1; i < starts.Count; i++) Line(0, "ws" + (i + 1) + " = Thread(" + names[starts[i]] + ")", starts[i]);
                Line(0, names[starts[0]] + "()", starts[0]);
            }
            python = sb.ToString();
        }

        // ------------------------------------------------------------------ statements

        private void Stack(Block first, int indent) {
            for (var b = first; b != null; b = b.next) Statement(b, indent);
        }

        private void Body(Block first, int indent, Block owner, bool loop) {
            var before = lineBlocks.Count;
            Stack(first, indent);
            if (loop) Line(indent, "wait(5, MSEC)", owner);
            else if (lineBlocks.Count == before) Line(indent, "pass", owner);
        }

        private void Statement(Block b, int indent) {
            var def = b.Def;
            if (def == null || def.IsReporter || def.shape == BlockShape.Hat) return;
            switch (b.type) {
                case "repeat":
                    Line(indent, "for repeat_count in range(int(" + Expr(b, 0) + ")):", b);
                    Body(b.mouths[0], indent + 1, b, true);
                    return;
                case "forever":
                    Line(indent, "while True:", b);
                    Body(b.mouths[0], indent + 1, b, true);
                    return;
                case "repeat_until":
                    Line(indent, "while not " + Expr(b, 0) + ":", b);
                    Body(b.mouths[0], indent + 1, b, true);
                    return;
                case "while":
                    Line(indent, "while " + Expr(b, 0) + ":", b);
                    Body(b.mouths[0], indent + 1, b, true);
                    return;
                case "if":
                    Line(indent, "if " + Expr(b, 0) + ":", b);
                    Body(b.mouths[0], indent + 1, b, false);
                    return;
                case "if_else":
                    Line(indent, "if " + Expr(b, 0) + ":", b);
                    Body(b.mouths[0], indent + 1, b, false);
                    Line(indent, "else:", b);
                    Body(b.mouths[1], indent + 1, b, false);
                    return;
            }
            var text = Fill(b, out var missing);
            if (missing != null) {
                Line(indent, "pass  # \"" + Spec(b) + "\" needs a " + missing + ": the robot has none (Edit > Devices, then Devices in the code window)", b);
                return;
            }
            Line(indent, text, b);
        }

        // ------------------------------------------------------------------ expressions

        /// <summary>A block's template with its slots filled in; <paramref name="missing"/> names a device kind the
        /// robot doesn't have.</summary>
        private string Fill(Block b, out string missing) {
            missing = null;
            var def = b.Def;
            var py = def.py;
            if (py.Contains("%dt")) {
                if (drivetrain == null) {
                    missing = "drivetrain";
                    return null;
                }
                py = py.Replace("%dt", drivetrain);
            }
            // "#$a($b)": slot a is a dropdown whose value wraps slot b ("math.sqrt(#)").
            var wrap = Regex.Match(py, @"^#\$(\d)\(\$(\d)\)$");
            if (wrap.Success) {
                var a = int.Parse(wrap.Groups[1].Value);
                var c = int.Parse(wrap.Groups[2].Value);
                var template = Field(b, a);
                if (template.Contains("math.")) usesMath = true;
                return template.Replace("#", Expr(b, c));
            }
            if (py.Contains("math.")) usesMath = true;
            if (py.Contains("random.")) usesRandom = true;
            var result = new StringBuilder();
            for (var i = 0; i < py.Length; i++) {
                if (py[i] == '$' && i + 1 < py.Length) {
                    if (py[i + 1] == 'T') {
                        result.Append(b.toggle ? def.toggleOn : def.toggleOff);
                        i++;
                        continue;
                    }
                    if (char.IsDigit(py[i + 1])) {
                        var slot = py[i + 1] - '0';
                        if (slot < def.slots.Count && def.slots[slot].kind == SlotKind.Device) {
                            if (!DeviceName(b, slot, out var device)) {
                                missing = BlockDefs.KindName(def.slots[slot].key);
                                return null;
                            }
                            result.Append(device);
                        }
                        else result.Append(Expr(b, slot));
                        i++;
                        continue;
                    }
                }
                result.Append(py[i]);
            }
            return result.ToString();
        }

        /// <summary>What's in a slot: the block dropped in, else the typed value.</summary>
        private string Expr(Block b, int slot) {
            var def = b.Def;
            if (slot >= def.slots.Count) return "None";
            var s = def.slots[slot];
            var input = slot < b.inputs.Count ? b.inputs[slot] : null;
            if (input != null && input.Def != null) {
                var text = Fill(input, out var missing);
                if (missing == null) return text;
                return input.Def.shape == BlockShape.Boolean ? "False" : "0";
            }
            var field = slot < b.fields.Count ? b.fields[slot] : "";
            switch (s.kind) {
                case SlotKind.Number: return Number(field);
                case SlotKind.Text: return Text(field);
                case SlotKind.Boolean: return "False";
                case SlotKind.Dropdown: return Field(b, slot);
                case SlotKind.Variable: return VarName(b, slot, s.key);
                case SlotKind.Name: return Ident(field);
                case SlotKind.Device: return DeviceName(b, slot, out var d) ? d : "None";
            }
            return "None";
        }

        private static string Field(Block b, int slot) {
            var def = b.Def;
            var value = slot < b.fields.Count ? b.fields[slot] : "";
            var key = def.slots[slot].key;
            if (BlockDefs.Options.TryGetValue(key ?? "", out var options) && options.All(o => o.value != value)) value = options[0].value;
            return value;
        }

        private string VarName(Block b, int slot, string kind) {
            var list = kind == "bool" ? program.booleans : kind == "event" ? program.events : program.numbers;
            var value = slot < b.fields.Count ? b.fields[slot] : "";
            if (!list.Contains(value) && list.Count > 0) value = list[0];
            return Ident(value);
        }

        private bool DeviceName(Block b, int slot, out string name) {
            var kind = b.Def.slots[slot].key;
            var list = devices.TryGetValue(kind, out var l) ? l : new List<string>();
            var value = slot < b.fields.Count ? b.fields[slot] : "";
            name = list.Contains(value) ? value : list.FirstOrDefault();
            return name != null;
        }

        public static string Number(string text) {
            if (!double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || double.IsNaN(v) || double.IsInfinity(v)) return "0";
            if (v == System.Math.Floor(v) && System.Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string Text(string text) => "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

        /// <summary>The block's words, as shown ("drive forward for 200 mm").</summary>
        public static string Spec(Block b) {
            var def = b.Def;
            if (def == null) return b.type;
            var words = new List<string>();
            var slot = 0;
            foreach (var part in def.parts) {
                if (part.label != null) { words.Add(part.label); continue; }
                var s = part.slot;
                var value = slot < b.fields.Count ? b.fields[slot] : "";
                words.Add(s.kind == SlotKind.Dropdown ? BlockDefs.OptionLabel(s.key, value) : s.kind == SlotKind.Boolean ? "<>" : value.Length > 0 ? value : s.key);
                slot++;
            }
            return string.Join(" ", words);
        }
    }
}
