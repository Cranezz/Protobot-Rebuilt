using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Protobot.Devices;
using Protobot.Motion;
using Protobot.Scripting;
using UnityEngine;
using static Protobot.RobotCode.VexModule;

namespace Protobot.RobotCode {
    // V2 (Phase 9, robot code): VEXcode V5 Python's device classes over the app's simulated devices.

    // ---------------------------------------------------------------------- brain

    public sealed class ThreeWirePort : VexObject {
        public readonly int index;   // 1-8 = A-H
        public readonly int expander;   // V2: the 3-Wire Expander's smart port (0: the brain's own ports)
        public ThreeWirePort(int index, int expander = 0) {
            this.index = index;
            this.expander = expander;
        }
        public override string TypeName => "ThreeWirePort";
    }

    /// <summary>V2: a V5 3-Wire Expander's ports (VEXcode's Triport(Ports.PORT5) / triport(PORT5): .a-.h). Devices
    /// past the brain's A-H go on the build's expanders, in port order (Edit > Devices… shows where).</summary>
    internal sealed class VexTriport : VexObject {
        public override string TypeName => "Triport";
        public VexTriport(Args a) {
            var (port, three) = VexModule.Port(a.Need(0, "port"), "Triport");
            if (three) throw new PyError("TypeError", "Triport needs the expander's smart port (like Ports.PORT5)");
            for (var i = 0; i < 8; i++) Field(((char)('a' + i)).ToString(), new ThreeWirePort(i + 1, port));
        }
    }

    internal sealed class VexBrain : VexObject {
        public override string TypeName => "Brain";
        public readonly BrainCanvas screen;   // V2: the 480 x 240 touch screen (BrainCanvas)

        public VexBrain(RobotProgram p) {
            screen = new BrainCanvas(p);
            Field("screen", screen);
            Field("timer", new VexTimer(p));
            var battery = new Battery();
            Field("battery", battery);
            var three = new ThreeWires();
            Field("three_wire_port", three);
            Method("program_stop", _ => {
                p.vm.StopAll();
                return PyNone.Value;
            });
        }

        private sealed class Battery : VexObject {
            public override string TypeName => "Battery";
            public Battery() {
                Method("capacity", _ => (long)Mathf.RoundToInt(V5Brain.Battery * 100f));
                Method("voltage", a => {
                    var v = 12.0 + 0.8 * V5Brain.Battery;
                    return UnitName(a.Get(0, "units"), "MV") == "VOLT" ? v : v * 1000;
                });
                Method("current", _ => (double)V5Brain.Devices.OfType<MotorDevice>().Sum(m => m.Current) + 0.3);
                Method("temperature", _ => 25.0);
            }
        }

        private sealed class ThreeWires : VexObject {
            public override string TypeName => "Triport";
            public ThreeWires() {
                for (var i = 0; i < 8; i++) Field(((char)('a' + i)).ToString(), new ThreeWirePort(i + 1));
            }
        }
    }

    /// <summary>The controller's text screen (3 rows of 19 characters), as VEXcode prints on it. (The Brain's own
    /// screen draws too: BrainCanvas.)</summary>
    public sealed class BrainScreen : VexObject {
        private readonly RobotProgram program;
        public readonly int rows, cols;
        private readonly char[][] grid;
        private int row, col;
        private readonly string name;
        public static event Action Changed;

        public override string TypeName => name == "Brain" ? "BrainScreen" : "ControllerScreen";

        internal BrainScreen(RobotProgram p, int rows, int cols, string name) {
            program = p;
            this.rows = rows;
            this.cols = cols;
            this.name = name;
            grid = Enumerable.Range(0, rows).Select(_ => Enumerable.Repeat(' ', cols).ToArray()).ToArray();
            Method("print", a => {
                Write(Text(a));
                return PyNone.Value;
            });
            Method("print_at", a => {
                var x = a.Number(-1, "x", 0);
                var y = a.Number(-1, "y", 20);
                row = Mathf.Clamp((int)(y / 20.0) - 1 + (y % 20 > 0 ? 1 : 0), 0, rows - 1);
                col = Mathf.Clamp((int)(x / 10.0), 0, cols - 1);
                Write(Text(a));
                return PyNone.Value;
            });
            Method("new_line", _ => NewLine());
            Method("next_row", _ => NewLine());
            Method("set_cursor", a => {
                row = Mathf.Clamp((int)a.Number(0, "row", 1) - 1, 0, rows - 1);
                col = Mathf.Clamp((int)a.Number(1, "col", 1) - 1, 0, cols - 1);
                return PyNone.Value;
            });
            Method("row", _ => (long)row + 1);
            Method("column", _ => (long)col + 1);
            Method("clear_screen", _ => {
                foreach (var r in grid) Blank(r);
                row = col = 0;
                Changed?.Invoke();
                return PyNone.Value;
            });
            Method("clear_line", a => ClearRow(a));
            Method("clear_row", a => ClearRow(a));
            foreach (var ignored in new[] { "set_font", "set_pen_width", "set_pen_color", "set_fill_color", "set_origin", "draw_pixel", "draw_line", "draw_rectangle", "draw_circle", "draw_image_from_file", "render", "set_clip_region" })
                Method(ignored, _ => PyNone.Value);
            Method("pressing", _ => false);
            Method("x_position", _ => 0L);
            Method("y_position", _ => 0L);
            Method("pressed", _ => PyNone.Value);
            Method("released", _ => PyNone.Value);
        }

        private object ClearRow(Args a) {
            var r = a.Count > 0 ? Mathf.Clamp((int)a.Number(0, "row", 1) - 1, 0, rows - 1) : row;
            Blank(grid[r]);
            Changed?.Invoke();
            return PyNone.Value;
        }

        private object NewLine() {
            row = Math.Min(rows - 1, row + 1);
            col = 0;
            if (row == rows - 1 && grid[row].Any(c => c != ' ')) Scroll();
            return PyNone.Value;
        }

        private void Scroll() {
            for (var r = 0; r < rows - 1; r++) Array.Copy(grid[r + 1], grid[r], cols);
            Blank(grid[rows - 1]);
        }

        private static void Blank(char[] row) {
            for (var i = 0; i < row.Length; i++) row[i] = ' ';
        }

        // VEXcode prints floats to 2 places unless told (precision=).
        private string Text(Args a) {
            if (program.Cpp && a.Count > 0 && a.pos[0] is string f && (a.Count > 1 || f.Contains("%"))) return CppRuntime.CFormat(a.vm, f, a.pos.Skip(1).ToArray());   // V2 (C++)
            var sep = a.kw != null && a.kw.TryGetValue("sep", out var s) ? Ops.Str(s) : "";
            var precision = a.kw != null && a.kw.TryGetValue("precision", out var p) ? (int)Ops.ToLong(p) : 2;
            return string.Join(sep, a.pos.Select(o => o is double d ? d.ToString("F" + Math.Max(0, precision), Ops.Inv) : a.vm.StrOf(o)));
        }

        private void Write(string text) {
            foreach (var ch in text) {
                if (ch == '\n') {
                    NewLine();
                    continue;
                }
                if (col >= cols) continue;
                grid[row][col++] = ch;
            }
            Changed?.Invoke();
        }

        public string Text() => string.Join("\n", grid.Select(r => new string(r).TrimEnd()));
    }

    internal sealed class VexTimer : VexObject {
        public override string TypeName => "Timer";
        private double start;

        public VexTimer(RobotProgram p) {
            start = p.Time;
            Method("time", a => {
                var t = p.Time - start;
                return UnitName(a.Get(0, "units"), "MSEC") == "SECONDS" ? t : Math.Round(t * 1000);
            });
            Method("value", _ => p.Time - start);
            Method("clear", _ => {
                start = p.Time;
                return PyNone.Value;
            });
            Method("reset", _ => {
                start = p.Time;
                return PyNone.Value;
            });
            Method("event", a => {
                var cb = a.Need(0, "callback");
                var at = p.Time + a.Number(1, "delay", 0) / 1000.0;
                Action poll = null;
                poll = () => {
                    if (p.Time + 1e-9 < at) return;
                    p.pollers.Remove(poll);
                    p.vm.Start(cb, null, "timer event");
                };
                p.pollers.Add(poll);
                return PyNone.Value;
            });
            Method("system", _ => Math.Round(p.Time * 1000));
        }
    }

    internal sealed class VexThread : VexObject {
        public override string TypeName => "Thread";
        private readonly Fiber fiber;

        public VexThread(RobotProgram p, Args a) {
            var fn = a.Need(0, "callback");
            var args = a.Get(1, "args");
            var list = args == null || args is PyNone ? null : Ops.Iterate(args).ToArray();
            fiber = p.vm.Start(fn, list, "thread");
            Method("stop", _ => {
                fiber.Stop();
                return PyNone.Value;
            });
            Method("is_alive", _ => !fiber.Done);
            Method("interrupt", _ => {   // V2: VEXcode C++'s names
                fiber.Stop();
                return PyNone.Value;
            });
            Method("join", _ => new Suspend(() => fiber.Done));
        }
    }

    internal sealed class VexEvent : VexObject {
        public override string TypeName => "Event";
        private readonly List<object> callbacks = new();

        public VexEvent(RobotProgram p, Args a) {
            if (a.Count > 0) callbacks.Add(a.pos[0]);
            Method("set", e => {
                callbacks.Add(e.Need(0, "callback"));
                return PyNone.Value;
            });
            Method("broadcast", _ => {
                foreach (var cb in callbacks) p.vm.Start(cb, null, "event");
                return PyNone.Value;
            });
            Method("broadcast_and_wait", _ => {
                var started = callbacks.Select(cb => p.vm.Start(cb, null, "event")).ToList();
                return new Suspend(() => started.All(f => f.Done));
            });
        }
    }

    internal sealed class VexCompetition : VexObject {
        public override string TypeName => "Competition";
        public VexCompetition(RobotProgram p) {
            Method("is_enabled", _ => p.Enabled);
            Method("is_driver_control", _ => p.IsDriver);
            Method("is_autonomous", _ => p.IsAutonomous);
            Method("is_competition_switch", _ => false);
            Method("is_field_control", _ => false);
        }
    }

    // ---------------------------------------------------------------------- controller

    internal sealed class VexController : VexObject {
        public override string TypeName => "Controller";
        private readonly RobotProgram program;
        private readonly bool isPartner;

        public VexController(RobotProgram p, bool isPartner) {
            program = p;
            this.isPartner = isPartner;
            foreach (V5Axis axis in Enum.GetValues(typeof(V5Axis))) Field("axis" + (int)axis, new Axis(this, axis));
            foreach (V5Button button in Enum.GetValues(typeof(V5Button))) Field("button" + button, new Button(this, button));
            screen = new BrainScreen(p, 3, 19, "Controller");
            Field("screen", screen);
            Method("rumble", a => {
                // V2: shown on the code window's controller screen (it shakes) and played on a gamepad if one is in.
                if (!isPartner) {
                    RumblePattern = a.vm.StrOf(a.Need(0, "pattern"));
                    RumbleStart = Time.unscaledTime;
                }
                return PyNone.Value;
            });
            Method("installed", _ => !isPartner);
        }

        public readonly BrainScreen screen;
        public static string RumblePattern;
        public static float RumbleStart;

        /// <summary>Whether the rumble pattern buzzes now: "." short, "-" long, " " a pause.</summary>
        public static bool Buzzing() {
            if (string.IsNullOrEmpty(RumblePattern)) return false;
            var t = Time.unscaledTime - RumbleStart;
            foreach (var ch in RumblePattern) {
                var (on, gap) = ch switch { '.' => (0.15f, 0.1f), '-' => (0.4f, 0.1f), _ => (0f, 0.25f) };
                if (t < on) return true;
                t -= on + gap;
                if (t < 0f) return false;
            }
            RumblePattern = null;
            return false;
        }

        private bool Live => !isPartner && Sandbox.DriveEnabled && !InputEvents.TextInput.IsTyping && !InputEvents.TextInput.ShortcutsPaused;

        private float AxisValue(V5Axis axis) => !Live ? 0f : Motion.Sandbox.TestSticks != null ? TestAxis(axis) : Mathf.Clamp(V5Controller.Axis(axis), -1f, 1f);

        // Scripts' test sticks (Sandbox.TestSticks), as the controller's axes.
        private static float TestAxis(V5Axis axis) {
            var (l, r) = Motion.Sandbox.TestSticks();
            return Mathf.Clamp(axis switch { V5Axis.Axis1 => r.x, V5Axis.Axis2 => r.y, V5Axis.Axis4 => l.x, _ => l.y }, -1f, 1f);
        }
        private bool Held(V5Button button) => Live && V5Controller.Held(button);

        private sealed class Axis : VexObject {
            public override string TypeName => "Axis";
            public Axis(VexController c, V5Axis axis) {
                Method("position", _ => (long)Mathf.RoundToInt(c.AxisValue(axis) * 100f));
                Method("value", _ => (long)Mathf.RoundToInt(c.AxisValue(axis) * 127f));
                Method("changed", a => {
                    var cb = a.Need(0, "callback");
                    var last = c.AxisValue(axis);
                    c.program.pollers.Add(() => {
                        var now = c.AxisValue(axis);
                        if (Mathf.Abs(now - last) < 0.004f) return;
                        last = now;
                        c.program.vm.Start(cb, null, "axis changed");
                    });
                    return PyNone.Value;
                });
            }
        }

        private sealed class Button : VexObject {
            public override string TypeName => "Button";
            public Button(VexController c, V5Button button) {
                Method("pressing", _ => c.Held(button));
                Method("pressed", a => Watch(c, button, a.Need(0, "callback"), true));
                Method("released", a => Watch(c, button, a.Need(0, "callback"), false));
            }

            private static object Watch(VexController c, V5Button button, object cb, bool onPress) {
                var last = c.Held(button);
                c.program.pollers.Add(() => {
                    var now = c.Held(button);
                    if (now == last) return;
                    last = now;
                    if (now == onPress) c.program.vm.Start(cb, null, "button " + button);
                });
                return PyNone.Value;
            }
        }
    }

    // ---------------------------------------------------------------------- motors

    internal sealed class VexMotor : VexObject {
        public override string TypeName => "Motor";
        private readonly RobotProgram program;
        public readonly int port;
        public bool reversed;
        public double velocity = 0.5;      // share of free speed (VEXcode: 50%)
        public string stopping = "COAST";
        public double timeout;             // seconds, 0: none
        private MotorControl control;
        private int spinning;              // V2: spin(dir) under way: set_velocity changes its speed (VEXcode)

        public VexMotor(RobotProgram p, Args a) {
            program = p;
            (port, _) = Port(a.Need(0, "port"), "Motor");
            // Motor(port, gears, reversed) or Motor(port, reversed)
            var second = a.Get(1, "gears");
            if (second is bool b) reversed = b;
            else reversed = a.Bool(2, "reverse", false);
            if (second is PyEnum gear && Control is MotorControl mc && mc.motor.HasCartridge && gear.value != mc.motor.Rpm)
                p.Warn("gears" + port, "Motor on port " + port + " is set to " + gear.name + " in code, but the build's motor has the " + MotorDrive.CartridgeName(mc.motor.Rpm) + " " + mc.motor.Rpm + " rpm cartridge (the build's cartridge is what turns it)");

            Method("spin", a2 => {
                var dir = Dir(a2.Need(0, "direction"));
                var unit = UnitName(a2.Get(2, "units"), "RPM");
                var c = Control;
                spinning = 0;
                if (c == null) return PyNone.Value;
                if (unit == "VOLT" || unit == "MV") {
                    var v = a2.Number(1, "velocity", 12);
                    c.Volts((float)(dir * Sign * (unit == "MV" ? v / 1000 : v) / 12.0));
                    return PyNone.Value;
                }
                var share = a2.Has(1, "velocity") && !(a2.Get(1, "velocity") is PyNone) ? Share(a2.Number(1, "velocity", 0), unit) : velocity;
                c.Spin((float)(dir * Sign * share));
                spinning = dir < 0 ? -1 : 1;
                return PyNone.Value;
            });
            Method("spin_for", a2 => {
                spinning = 0;
                a2 = WaitFlag(a2, 5);   // V2 (C++): spinFor(fwd, 90, deg, false)
                var dir = Dir(a2.Need(0, "direction"));
                var amount = a2.Number(1, "rotation", 0);
                var unit = UnitName(a2.Get(2, "units"), "DEGREES");
                var speed = a2.Has(3, "velocity") && !(a2.Get(3, "velocity") is PyNone) ? Share(a2.Number(3, "velocity", 0), UnitName(a2.Get(4, "units_v"), "RPM")) : velocity;
                var deg = unit == "MSEC" || unit == "SECONDS" ? double.NaN : ToDegrees(amount, unit);
                if (double.IsNaN(deg)) {   // spin_for(FORWARD, 2, SECONDS): spin for a time
                    var c2 = Control;
                    c2?.Spin((float)(dir * Sign * speed));
                    var until = program.Time + Seconds(amount, unit);
                    return Waiting(a2, 5, () => program.Time + 1e-9 >= until, () => c2?.StopMotor(stopping));
                }
                return MoveTo(Position() + dir * deg, speed, a2, 5);
            });
            Method("spin_to_position", a2 => {
                spinning = 0;
                a2 = WaitFlag(a2, 4);
                var target = ToDegrees(a2.Number(0, "rotation", 0), UnitName(a2.Get(1, "units"), "DEGREES"));
                var speed = a2.Has(2, "velocity") && !(a2.Get(2, "velocity") is PyNone) ? Share(a2.Number(2, "velocity", 0), UnitName(a2.Get(3, "units_v"), "RPM")) : velocity;
                return MoveTo(target, speed, a2, 4);
            });
            Method("stop", a2 => {
                var how = a2.Count > 0 ? UnitName(a2.pos[0], stopping) : stopping;
                spinning = 0;
                Control?.StopMotor(how);
                return PyNone.Value;
            });
            Method("set_velocity", a2 => {
                velocity = Share(a2.Number(0, "velocity", 50), UnitName(a2.Get(1, "units"), "RPM"));
                // V2: on a motor already spinning it changes the speed at once (VEXcode; VEX's forum answers).
                var c = Control;
                if (spinning != 0 && c != null && c.mode == MotorControl.Mode.Velocity) c.Spin((float)(spinning * Sign * velocity));
                return PyNone.Value;
            });
            Method("set_stopping", a2 => {
                stopping = UnitName(a2.Need(0, "mode"), "COAST");
                if (Control != null) Control.stopping = stopping;
                return PyNone.Value;
            });
            Method("set_reversed", a2 => {
                reversed = a2.Bool(0, "value", true);
                return PyNone.Value;
            });
            Method("set_max_torque", a2 => {
                var v = a2.Number(0, "value", 100);
                var unit = UnitName(a2.Get(1, "units"), "PERCENT");
                var share = unit == "PERCENT" ? v / 100 : unit == "NM" ? v / StallNm : v * 0.113 / StallNm;
                if (Control != null) Control.maxTorque = (float)Math.Max(0, Math.Min(1, share));
                return PyNone.Value;
            });
            Method("set_max_velocity", _ => PyNone.Value);
            Method("set_timeout", a2 => {
                timeout = Seconds(a2.Number(0, "value", 0), UnitName(a2.Get(1, "units"), "MSEC"));
                return PyNone.Value;
            });
            Method("get_timeout", _ => (long)(timeout * 1000));
            Method("set_position", a2 => {
                SetPosition(ToDegrees(a2.Number(0, "value", 0), UnitName(a2.Get(1, "units"), "DEGREES")));
                return PyNone.Value;
            });
            Method("reset_position", _ => {
                SetPosition(0);
                return PyNone.Value;
            });
            Method("position", a2 => FromDegrees(Position(), UnitName(a2.Get(0, "units"), "DEGREES")));
            Method("velocity", a2 => Velocity(UnitName(a2.Get(0, "units"), "RPM")));
            Method("current", _ => (double)(Device?.Current ?? 0f));
            Method("power", _ => (double)(Device?.Power ?? 0f));
            Method("voltage", a2 => {   // V2: the volts it's driven with (12 V at full)
                var v = (double)(Control?.motor.Command ?? 0f) * 12 * Sign;
                return UnitName(a2.Get(0, "units"), "VOLT") == "MV" ? v * 1000 : v;
            });
            Method("torque", a2 => {
                var t = (double)(Device?.Torque ?? 0f) * Sign;
                return UnitName(a2.Get(0, "units"), "NM") == "INLB" ? t / 0.113 : t;
            });
            Method("efficiency", _ => {
                var d = Device;
                return d == null || d.Current < 0.01f ? 0.0 : (double)Mathf.Clamp(d.Power / (d.Current * 12f) * 100f, 0f, 100f);
            });
            Method("temperature", a2 => UnitName(a2.Get(0, "units"), "CELSIUS") == "FAHRENHEIT" ? 77.0 : 25.0);
            Method("is_spinning", _ => Control != null && Mathf.Abs(Control.Level) > 0.01f);
            Method("is_done", _ => Control == null || Control.done);
            Method("is_spinning_mode", _ => Control != null && Control.mode != MotorControl.Mode.None);
            Method("direction", _ => Control != null && Control.Level * Sign < 0 ? Units["REVERSE"] : Units["FORWARD"]);
            Method("command", a2 => Velocity(UnitName(a2.Get(0, "units"), "RPM"), command: true));
            Method("installed", _ => Control != null);
            Method("timestamp", _ => (long)(program.Time * 1000));
            Method("count", _ => 1L);
        }

        internal double Sign => reversed ? -1 : 1;

        internal MotorControl Control {
            get {
                if (control != null) return control;
                var device = program.Device<MotorDevice>(port, false, "Motor");
                return device?.motor != null ? control = program.Motor(device.motor) : null;
            }
        }

        private MotorDevice Device => V5Brain.Get<MotorDevice>(port);
        private double StallNm => Control != null ? Control.motor.StallTorque : 1.05;

        private static double Dir(object o) => UnitName(o, "FORWARD") == "REVERSE" ? -1 : 1;

        internal double Share(double v, string unit) {
            var free = Control?.FreeRpm ?? 200f;
            return Math.Max(-1, Math.Min(1, unit switch {
                "PERCENT" => v / 100.0,
                "DPS" => v / (free * 6.0),
                _ => v / free,
            }));
        }

        internal double Position() {
            var c = Control;
            return c == null ? 0 : (c.degrees - offset) * Sign;
        }

        private double offset;

        internal void SetPosition(double deg) {
            var c = Control;
            if (c == null) return;
            offset = c.degrees - deg * Sign;
            if (c.mode == MotorControl.Mode.Position && c.done) c.targetDeg = c.degrees;
        }

        internal double Velocity(string unit, bool command = false) {
            var c = Control;
            if (c == null) return 0.0;
            var share = (command ? c.motor.Command : c.Level) * Sign;
            return unit switch {
                "PERCENT" => share * 100.0,
                "DPS" => share * c.FreeRpm * 6.0,
                _ => share * c.FreeRpm,
            };
        }

        /// <summary>Starts a move to a position (degrees in the program's frame); waits unless wait=False.</summary>
        internal object MoveTo(double deg, double speed, Args a, int waitIndex) {
            var c = Control;
            if (c == null) return PyNone.Value;
            c.stopping = stopping;
            c.MoveTo(offset + deg * Sign, (float)Math.Abs(speed));
            return Waiting(a, waitIndex, () => c.done, null);
        }

        internal object Waiting(Args a, int waitIndex, Func<bool> done, Action after) {
            if (!a.Bool(waitIndex, "wait", true)) {
                if (after != null) {
                    Action poll = null;
                    poll = () => {
                        if (!done()) return;
                        program.pollers.Remove(poll);
                        after();
                    };
                    program.pollers.Add(poll);
                }
                return PyNone.Value;
            }
            var start = program.Time;
            var limit = timeout;
            return new Suspend(() => {
                var over = limit > 0 && program.Time - start >= limit;
                if (!done() && !over) return false;
                after?.Invoke();
                if (over) Control?.StopMotor(stopping);
                return true;
            }, PyNone.Value);
        }
    }

    internal sealed class VexMotorGroup : VexObject {
        public override string TypeName => "MotorGroup";
        internal readonly VexMotor[] motors;

        public VexMotorGroup(RobotProgram p, VexMotor[] motors) {
            this.motors = motors;
            if (motors.Length == 0) throw new PyError("TypeError", "MotorGroup needs at least one Motor");
            void Each(string name) => Method(name, a => {
                foreach (var m in motors) p.vm.CallSync(m.TryGetAttr(name, out var f) ? f : null, a.pos, a.kw);
                return PyNone.Value;
            });
            foreach (var n in new[] { "spin", "stop", "set_velocity", "set_stopping", "set_max_torque", "set_timeout", "set_position", "reset_position", "set_reversed" }) Each(n);
            Method("spin_for", a => All(p, WaitFlag(a, 5), "spin_for", 5));
            Method("spin_to_position", a => All(p, WaitFlag(a, 4), "spin_to_position", 4));
            Method("position", a => Ops.ToDouble(p.vm.CallSync(Get(motors[0], "position"), a.pos, a.kw)));
            Method("velocity", a => motors.Average(m => Ops.ToDouble(p.vm.CallSync(Get(m, "velocity"), a.pos, a.kw))));
            Method("current", a => motors.Sum(m => Ops.ToDouble(p.vm.CallSync(Get(m, "current"), a.pos, a.kw))));
            Method("power", a => motors.Sum(m => Ops.ToDouble(p.vm.CallSync(Get(m, "power"), a.pos, a.kw))));
            Method("torque", a => motors.Sum(m => Ops.ToDouble(p.vm.CallSync(Get(m, "torque"), a.pos, a.kw))));
            Method("is_done", _ => motors.All(m => m.Control == null || m.Control.done));
            Method("is_spinning", _ => motors.Any(m => m.Control != null && Mathf.Abs(m.Control.Level) > 0.01f));
            Method("count", _ => (long)motors.Length);
            // V2: the rest of VEXcode's MotorGroup (the VS Code stub check found them missing).
            Method("temperature", a => motors.Average(m => Ops.ToDouble(p.vm.CallSync(Get(m, "temperature"), a.pos, a.kw))));
            Method("efficiency", a => motors.Average(m => Ops.ToDouble(p.vm.CallSync(Get(m, "efficiency"), a.pos, a.kw))));
            Method("direction", a => p.vm.CallSync(Get(motors[0], "direction"), a.pos, a.kw));
            Method("installed", a => motors.All(m => Ops.Truthy(p.vm.CallSync(Get(m, "installed"), a.pos, a.kw))));
        }

        private static object Get(VexMotor m, string name) => m.TryGetAttr(name, out var f) ? f : null;

        // Each motor starts its move without waiting; then the group waits for all of them.
        private object All(RobotProgram p, Args a, string name, int waitIndex) {
            var wait = a.Bool(waitIndex, "wait", true);
            var kw = a.kw != null ? new Dictionary<string, object>(a.kw) : new Dictionary<string, object>();
            var pos = a.pos.Take(waitIndex).ToArray();
            kw["wait"] = false;
            foreach (var m in motors) p.vm.CallSync(Get(m, name), pos, kw);
            if (!wait) return PyNone.Value;
            return new Suspend(() => motors.All(m => m.Control == null || m.Control.done));
        }
    }

    // ---------------------------------------------------------------------- drivetrains

    /// <summary>DriveTrain(left, right, wheel_travel, track_width, wheel_base, units, external_gear_ratio) and
    /// SmartDrive(left, right, gyro, ...): drives and turns by wheel turns, or (SmartDrive) turns by the gyro.</summary>
    internal sealed class VexDrive : VexObject {
        public override string TypeName => smart ? "SmartDrive" : "DriveTrain";
        private readonly RobotProgram program;
        private readonly bool smart;
        private readonly VexMotor[] left, right;
        private readonly VexInertial gyro;
        private readonly double travelMm, trackMm, ratio;
        private double driveVelocity = 0.5, turnVelocity = 0.5;
        private string stopping = "COAST";
        private double timeout;
        private double turnThreshold = 1.0;
        private double turnConstant = 1.0;
        private bool turning;   // a gyro turn under way
        private int driving, spinningTurn;   // V2: drive(dir) / turn(dir) under way: the set velocities change their speed

        public VexDrive(RobotProgram p, Args a, bool smart) {
            program = p;
            this.smart = smart;
            left = Side(a.Need(0, "lm"), "left");
            right = Side(a.Need(1, "rm"), "right");
            var i = 2;
            if (smart) {
                gyro = a.Need(2, "g") as VexInertial ?? throw new PyError("TypeError", "SmartDrive's third argument is the Inertial sensor");
                i = 3;
            }
            var units = UnitName(a.Get(i + 3, "units"), "MM");
            travelMm = Mm(a.Number(i, "wheel_travel", 319.19), units);
            trackMm = Mm(a.Number(i + 1, "track_width", 295), units);
            ratio = a.Number(i + 4, "external_gear_ratio", 1.0);
            if (travelMm <= 0) throw new PyError("ValueError", "wheel_travel must be more than 0 (the wheel's circumference)");

            Method("drive", a2 => {
                var dir = Dir(a2.Need(0, "direction"));
                var v = a2.Has(1, "velocity") && !(a2.Get(1, "velocity") is PyNone) ? Share(a2.Number(1, "velocity", 0), UnitName(a2.Get(2, "units"), "RPM")) : driveVelocity;
                turning = false;
                foreach (var m in left.Concat(right)) m.Control?.Spin((float)(dir * m.Sign * v));
                driving = dir < 0 ? -1 : 1;
                spinningTurn = 0;
                return PyNone.Value;
            });
            Method("drive_for", a2 => {
                driving = spinningTurn = 0;
                a2 = WaitFlag(a2, 5);
                var dir = Dir(a2.Need(0, "direction"));
                var mm = Mm(a2.Number(1, "distance", 0), UnitName(a2.Get(2, "units"), "MM"));
                var v = a2.Has(3, "velocity") && !(a2.Get(3, "velocity") is PyNone) ? Share(a2.Number(3, "velocity", 0), UnitName(a2.Get(4, "units_v"), "RPM")) : driveVelocity;
                var deg = dir * mm / travelMm * 360.0 * ratio;
                turning = false;
                return Move(deg, deg, v, a2.Bool(5, "wait", true));
            });
            Method("turn", a2 => {
                var dir = Turn(a2.Need(0, "direction"));
                var v = a2.Has(1, "velocity") && !(a2.Get(1, "velocity") is PyNone) ? Share(a2.Number(1, "velocity", 0), UnitName(a2.Get(2, "units"), "RPM")) : turnVelocity;
                turning = false;
                foreach (var m in left) m.Control?.Spin((float)(dir * m.Sign * v));
                foreach (var m in right) m.Control?.Spin((float)(-dir * m.Sign * v));
                spinningTurn = dir < 0 ? -1 : 1;
                driving = 0;
                return PyNone.Value;
            });
            Method("turn_for", a2 => {
                driving = spinningTurn = 0;
                a2 = WaitFlag(a2, 5);
                var dir = Turn(a2.Need(0, "direction"));
                var angle = a2.Number(1, "angle", 0);
                if (UnitName(a2.Get(2, "units"), "DEGREES") == "TURNS") angle *= 360;
                var v = a2.Has(3, "velocity") && !(a2.Get(3, "velocity") is PyNone) ? Share(a2.Number(3, "velocity", 0), UnitName(a2.Get(4, "units_v"), "RPM")) : turnVelocity;
                var wait = a2.Bool(5, "wait", true);
                if (smart) return GyroTurn(gyro.Rotation() + dir * angle, v, wait);
                var arc = Math.PI * trackMm * angle / 360.0;
                var deg = dir * arc / travelMm * 360.0 * ratio;
                return Move(deg, -deg, v, wait);
            });
            Method("turn_to_heading", a2 => {
                driving = spinningTurn = 0;
                a2 = WaitFlag(a2, 4);
                RequireSmart("turn_to_heading");
                var target = a2.Number(0, "angle", 0);
                var now = gyro.Heading();
                var delta = Mathf.DeltaAngle((float)now, (float)target);
                var v = a2.Has(2, "velocity") && !(a2.Get(2, "velocity") is PyNone) ? Share(a2.Number(2, "velocity", 0), UnitName(a2.Get(3, "units_v"), "RPM")) : turnVelocity;
                return GyroTurn(gyro.Rotation() + delta, v, a2.Bool(4, "wait", true));
            });
            Method("turn_to_rotation", a2 => {
                driving = spinningTurn = 0;
                a2 = WaitFlag(a2, 4);
                RequireSmart("turn_to_rotation");
                var v = a2.Has(2, "velocity") && !(a2.Get(2, "velocity") is PyNone) ? Share(a2.Number(2, "velocity", 0), UnitName(a2.Get(3, "units_v"), "RPM")) : turnVelocity;
                return GyroTurn(a2.Number(0, "angle", 0), v, a2.Bool(4, "wait", true));
            });
            Method("stop", a2 => {
                driving = spinningTurn = 0;
                var how = a2.Count > 0 ? UnitName(a2.pos[0], stopping) : stopping;
                turning = false;
                foreach (var m in left.Concat(right)) m.Control?.StopMotor(how);
                return PyNone.Value;
            });
            Method("arcade", a2 => {
                driving = spinningTurn = 0;   // (PROS-style) arcade(forward, turn) in percent
                var f = a2.Number(0, "drive_power", 0) / 100.0;
                var t = a2.Number(1, "turn_power", 0) / 100.0;
                turning = false;
                foreach (var m in left) m.Control?.Spin((float)(m.Sign * Math.Max(-1, Math.Min(1, f + t))));
                foreach (var m in right) m.Control?.Spin((float)(m.Sign * Math.Max(-1, Math.Min(1, f - t))));
                return PyNone.Value;
            });
            Method("set_drive_velocity", a2 => {
                driveVelocity = Share(a2.Number(0, "velocity", 50), UnitName(a2.Get(1, "units"), "RPM"));
                if (driving != 0)   // V2: driving already: the new speed at once (VEXcode)
                    foreach (var m in left.Concat(right)) if (m.Control is MotorControl c && c.mode == MotorControl.Mode.Velocity) c.Spin((float)(driving * m.Sign * driveVelocity));
                return PyNone.Value;
            });
            Method("set_turn_velocity", a2 => {
                turnVelocity = Share(a2.Number(0, "velocity", 50), UnitName(a2.Get(1, "units"), "RPM"));
                if (spinningTurn != 0) {
                    foreach (var m in left) if (m.Control is MotorControl c && c.mode == MotorControl.Mode.Velocity) c.Spin((float)(spinningTurn * m.Sign * turnVelocity));
                    foreach (var m in right) if (m.Control is MotorControl c && c.mode == MotorControl.Mode.Velocity) c.Spin((float)(-spinningTurn * m.Sign * turnVelocity));
                }
                return PyNone.Value;
            });
            Method("set_stopping", a2 => {
                stopping = UnitName(a2.Need(0, "mode"), "COAST");
                foreach (var m in left.Concat(right)) {
                    m.stopping = stopping;
                    if (m.Control != null) m.Control.stopping = stopping;
                }
                return PyNone.Value;
            });
            Method("set_timeout", a2 => {
                timeout = Seconds(a2.Number(0, "value", 0), UnitName(a2.Get(1, "units"), "MSEC"));
                return PyNone.Value;
            });
            Method("set_gear_ratio", _ => PyNone.Value);
            Method("set_turn_threshold", a2 => {
                turnThreshold = Math.Max(0.1, a2.Number(0, "value", 1));
                return PyNone.Value;
            });
            Method("set_turn_constant", a2 => {
                turnConstant = Math.Max(0.1, Math.Min(4, a2.Number(0, "value", 1)));
                return PyNone.Value;
            });
            Method("is_done", _ => !turning && left.Concat(right).All(m => m.Control == null || m.Control.done));
            Method("is_moving", _ => left.Concat(right).Any(m => m.Control != null && Mathf.Abs(m.Control.Level) > 0.01f));
            Method("velocity", a2 => left.Concat(right).Average(m => m.Velocity(UnitName(a2.Get(0, "units"), "RPM"))));
            Method("current", _ => (double)left.Concat(right).Sum(m => V5Brain.Get<MotorDevice>(m.port)?.Current ?? 0f));
            Method("power", _ => (double)left.Concat(right).Sum(m => V5Brain.Get<MotorDevice>(m.port)?.Power ?? 0f));
            Method("heading", a2 => {
                RequireSmart("heading");
                return gyro.Heading();
            });
            Method("rotation", a2 => {
                RequireSmart("rotation");
                return gyro.Rotation();
            });
            Method("set_heading", a2 => {
                RequireSmart("set_heading");
                gyro.SetHeading(a2.Number(0, "value", 0));
                return PyNone.Value;
            });
            Method("set_rotation", a2 => {
                RequireSmart("set_rotation");
                gyro.SetRotation(a2.Number(0, "value", 0));
                return PyNone.Value;
            });
        }

        private void RequireSmart(string what) {
            if (!smart) throw new PyError("AttributeError", "a DriveTrain has no gyro: " + what + "() needs a SmartDrive(left, right, inertial, ...)");
        }

        private static VexMotor[] Side(object o, string what) => o switch {
            VexMotor m => new[] { m },
            VexMotorGroup g => g.motors,
            _ => throw new PyError("TypeError", "the drivetrain's " + what + " side must be a Motor or MotorGroup, not " + Ops.Describe(o)),
        };

        private static double Dir(object o) => UnitName(o, "FORWARD") == "REVERSE" ? -1 : 1;
        private static double Turn(object o) => UnitName(o, "RIGHT") == "LEFT" ? -1 : 1;

        private double Share(double v, string unit) => left[0].Share(v, unit);

        private object Move(double leftDeg, double rightDeg, double speed, bool wait) {
            var maxDeg = Math.Max(Math.Abs(leftDeg), Math.Abs(rightDeg));
            foreach (var m in left) Start(m, leftDeg, maxDeg, speed);
            foreach (var m in right) Start(m, rightDeg, maxDeg, speed);
            if (!wait) return PyNone.Value;
            var start = program.Time;
            return new Suspend(() => {
                var over = timeout > 0 && program.Time - start >= timeout;
                if (over)
                    foreach (var m in left.Concat(right))
                        m.Control?.StopMotor(stopping);
                return over || left.Concat(right).All(m => m.Control == null || m.Control.done);
            });
        }

        private void Start(VexMotor m, double deg, double maxDeg, double speed) {
            var c = m.Control;
            if (c == null) return;
            c.stopping = stopping;
            // Both sides arrive together: the shorter one goes slower.
            var share = maxDeg > 0 ? speed * Math.Abs(deg) / maxDeg : speed;
            c.MoveTo(c.degrees + deg * m.Sign, (float)Math.Abs(share));
        }

        // SmartDrive turns: spin both sides opposite ways, slowing as the gyro nears the target.
        private object GyroTurn(double targetRotation, double speed, bool wait) {
            turning = true;
            var start = program.Time;
            var settled = 0;
            Action poll = null;
            poll = () => {
                if (!turning) {
                    program.pollers.Remove(poll);
                    return;
                }
                var err = targetRotation - gyro.Rotation();
                var over = timeout > 0 && program.Time - start >= timeout;
                settled = Math.Abs(err) < turnThreshold ? settled + 1 : 0;
                if (settled > 12 || over) {
                    turning = false;
                    foreach (var m in left.Concat(right)) m.Control?.StopMotor(stopping);
                    program.pollers.Remove(poll);
                    return;
                }
                var v = Math.Sign(err) * Math.Min(Math.Abs(speed), Math.Max(0.04, Math.Abs(err) * 0.012 * turnConstant));
                foreach (var m in left) m.Control?.Spin((float)(v * m.Sign));
                foreach (var m in right) m.Control?.Spin((float)(-v * m.Sign));
            };
            program.pollers.Add(poll);
            if (!wait) return PyNone.Value;
            return new Suspend(() => !turning);
        }
    }

    // ---------------------------------------------------------------------- sensors

    internal sealed class VexInertial : VexObject {
        public override string TypeName => "Inertial";
        private readonly RobotProgram program;
        private readonly int port;
        private double headingOffset, rotationOffset, calibratingUntil;

        public VexInertial(RobotProgram p, Args a) {
            program = p;
            (port, _) = Port(a.Need(0, "port"), "Inertial");
            Method("calibrate", _ => {
                Device?.Calibrate();
                headingOffset = rotationOffset = 0;
                calibratingUntil = p.Time + 2.0;   // a V5 Inertial Sensor takes about 2 s
                return PyNone.Value;
            });
            Method("is_calibrating", _ => p.Time < calibratingUntil);
            Field("start_calibration", Member("calibrate"));   // V2: VEXcode C++'s startCalibration()
            Method("heading", _ => Heading());
            Method("rotation", _ => Rotation());
            Method("set_heading", a2 => {
                SetHeading(a2.Number(0, "value", 0));
                return PyNone.Value;
            });
            Method("set_rotation", a2 => {
                SetRotation(a2.Number(0, "value", 0));
                return PyNone.Value;
            });
            Method("reset_heading", _ => {
                SetHeading(0);
                return PyNone.Value;
            });
            Method("reset_rotation", _ => {
                SetRotation(0);
                return PyNone.Value;
            });
            Method("orientation", a2 => UnitName(a2.Need(0, "axis"), "YAW") switch {
                "ROLL" => (double)(Device?.Roll ?? 0f),
                "PITCH" => (double)(Device?.Pitch ?? 0f),
                _ => (double)Mathf.DeltaAngle(0f, (float)Heading()),
            });
            Method("pitch", _ => (double)(Device?.Pitch ?? 0f));
            Method("roll", _ => (double)(Device?.Roll ?? 0f));
            Method("yaw", _ => (double)Mathf.DeltaAngle(0f, (float)Heading()));
            Method("gyro_rate", a2 => UnitName(a2.Get(0, "axis"), "ZAXIS") == "ZAXIS" ? (double)(Device?.Rate ?? 0f) : 0.0);
            Method("acceleration", a2 => {
                var g = Device?.Accel ?? Vector3.zero;
                return UnitName(a2.Get(0, "axis"), "XAXIS") switch {
                    "YAXIS" => (double)g.y,
                    "ZAXIS" => (double)g.z,
                    _ => (double)g.x,
                };
            });
            Method("installed", _ => Device != null);
            Method("changed", _ => PyNone.Value);
        }

        private InertialSensor Device => program.Device<InertialSensor>(port, false, "Inertial");
        internal bool Installed => V5Brain.Get<InertialSensor>(port) != null;   // (no warning)
        private bool Calibrating => program.Time < calibratingUntil;

        public double Rotation() => Calibrating || Device == null ? rotationOffset : Device.Rotation + rotationOffset;
        public double Heading() => Calibrating || Device == null ? Repeat(headingOffset) : Repeat(Device.Rotation + headingOffset);
        private static double Repeat(double v) => ((v % 360) + 360) % 360;

        public void SetHeading(double v) => headingOffset = v - (Device?.Rotation ?? 0);
        public void SetRotation(double v) => rotationOffset = v - (Device?.Rotation ?? 0);
    }

    /// <summary>V2: VEXcode's GPS Sensor (Python Gps, C++ gps): the reference point's field position and heading.
    /// Gps(port, heading_offset) or Gps(port, origin_x, origin_y, units, heading_offset, turn_direction); the origin is
    /// where the sensor sits from the reference point (x right, y forward of the robot), the heading offset the sensor's
    /// turn from the robot's forward (180 = mounted on the back, facing backward, as VEX advises).</summary>
    internal sealed class VexGps : VexObject {
        public override string TypeName => "Gps";
        private readonly RobotProgram p;
        private readonly int port;
        private double ox, oy, headingOffset;   // inches, degrees
        private bool left;
        private double dx, dy, dLocation, dHeading, rotationBase, lastHeading = double.NaN, rotation, calibratingUntil;

        public VexGps(RobotProgram p, Args a) {
            this.p = p;
            (port, _) = Port(a.Need(0, "port"), "GPS");
            if (a.Count == 2 || (a.Count == 3 && !(a.pos[2] is double || a.pos[2] is long))) {
                headingOffset = Ops.ToDouble(a.pos[1]);
                if (a.Count == 3) left = UnitName(a.pos[2], "RIGHT") == "LEFT";
            }
            else {
                var units = UnitName(a.Get(3, "units"), "MM");
                ox = Mm(a.Number(1, "origin_x", 0), units) / 25.4;
                oy = Mm(a.Number(2, "origin_y", 0), units) / 25.4;
                headingOffset = a.Number(4, "heading_offset", 0);
                left = UnitName(a.Get(5, "turn_direction"), "RIGHT") == "LEFT";
            }
            Method("x_position", a2 => FromMm(Reference().x * 25.4, UnitName(a2.Get(0, "units"), "MM")));
            Method("y_position", a2 => FromMm(Reference().y * 25.4, UnitName(a2.Get(0, "units"), "MM")));
            Method("heading", a2 => Angle(Heading(), a2.Get(0, "units")));
            Method("rotation", a2 => Angle(Rotation(), a2.Get(0, "units")));
            Method("quality", _ => (long)Mathf.RoundToInt(Device?.Quality ?? 0f));
            Method("set_origin", a2 => {
                if (a2.Count == 0) ox = oy = 0;
                else {
                    var units = UnitName(a2.Get(2, "units"), "MM");
                    ox = Mm(a2.Number(0, "x", 0), units) / 25.4;
                    oy = Mm(a2.Number(1, "y", 0), units) / 25.4;
                }
                return PyNone.Value;
            });
            Method("set_location", a2 => {
                // (x, y, units, heading, heading units) or C++'s (x, y, heading)
                double x = a2.Number(0, "x", 0), y = a2.Number(1, "y", 0), heading;
                var units = "MM";
                if (a2.Count == 3 && (a2.pos[2] is double || a2.pos[2] is long)) heading = Ops.ToDouble(a2.pos[2]);
                else {
                    units = UnitName(a2.Get(2, "units"), "MM");
                    heading = ToDegrees(a2.Number(3, "heading", 0), UnitName(a2.Get(4, "heading_units"), "DEGREES"));
                }
                // (a real GPS reading the code strip reports where it is: a location set by code holds only while
                // it can't see the strip, as with the start position given to PROS's Gps)
                dx = dy = dLocation = 0;
                var now = Reference();
                dx = Mm(x, units) / 25.4 - now.x;
                dy = Mm(y, units) / 25.4 - now.y;
                dLocation = heading - Heading();
                return PyNone.Value;
            });
            Method("calibrate", _ => {
                calibratingUntil = p.Time + 2.0;
                return PyNone.Value;
            });
            Field("start_calibration", Member("calibrate"));
            Method("is_calibrating", _ => p.Time < calibratingUntil);
            Method("set_heading", a2 => {
                dHeading += ToDegrees(a2.Number(0, "value", 0), UnitName(a2.Get(1, "units"), "DEGREES")) - Heading();
                return PyNone.Value;
            });
            Method("reset_heading", _ => {
                dHeading -= Heading();
                return PyNone.Value;
            });
            Method("set_rotation", a2 => {
                rotationBase += ToDegrees(a2.Number(0, "value", 0), UnitName(a2.Get(1, "units"), "DEGREES")) - Rotation();
                return PyNone.Value;
            });
            Method("reset_rotation", _ => {
                rotationBase -= Rotation();
                return PyNone.Value;
            });
            Method("orientation", a2 => UnitName(a2.Need(0, "axis"), "YAW") switch {
                "ROLL" => (double)(Device?.Roll ?? 0f),
                "PITCH" => (double)(Device?.Pitch ?? 0f),
                _ => (double)Mathf.DeltaAngle(0f, (float)Heading()),
            });
            Method("gyro_rate", a2 => {
                var rate = UnitName(a2.Get(0, "axis"), "ZAXIS") == "ZAXIS" ? (double)(Device?.Rate ?? 0f) : 0.0;
                return UnitName(a2.Get(1, "units"), "DPS") == "RPM" ? rate / 6.0 : rate;
            });
            Method("acceleration", a2 => {
                var g = Device?.Accel ?? Vector3.zero;
                return UnitName(a2.Get(0, "axis"), "XAXIS") switch {
                    "YAXIS" => (double)g.y,
                    "ZAXIS" => (double)g.z,
                    _ => (double)g.x,
                };
            });
            Method("installed", _ => V5Brain.Get<GpsSensor>(port) != null);
            Method("timestamp", _ => Math.Round(p.Time * 1000));
            Method("changed", _ => PyNone.Value);
            Method("set_sensor_rotation", a2 => {
                headingOffset = ToDegrees(a2.Number(0, "value", 0), UnitName(a2.Get(1, "units"), "DEGREES"));
                return PyNone.Value;
            });
        }

        internal int PortNumber => port;
        private GpsSensor Device => p.Device<GpsSensor>(port, false, "GPS Sensor");
        private static double Angle(double deg, object units) => UnitName(units, "DEGREES") == "TURNS" ? deg / 360.0 : deg;
        private static double Repeat(double v) => ((v % 360) + 360) % 360;

        /// <summary>The robot's heading: the sensor's less its turn on the robot.</summary>
        private double RobotHeading() {
            var d = Device;
            if (d == null) return 0;
            var h = d.Heading - headingOffset;
            return left ? -h : h;
        }

        public double Heading() => p.Time < calibratingUntil ? 0 : Repeat(RobotHeading() + dHeading + Located().h);

        /// <summary>The location set by code, while the strip is out of view (dropped once it's seen).</summary>
        private (double x, double y, double h) Located() {
            if (Device is GpsSensor d && d.Seeing) dx = dy = dLocation = 0;
            return (dx, dy, dLocation);
        }

        public double Rotation() {
            var h = Heading();
            if (double.IsNaN(lastHeading)) lastHeading = h;
            rotation += Mathf.DeltaAngle((float)lastHeading, (float)h);
            lastHeading = h;
            return rotation + rotationBase;
        }

        /// <summary>The reference point (inches, field frame): the sensor less its origin offset, turned with the robot.</summary>
        public Vector2 Reference() {
            var d = Device;
            if (d == null) return Vector2.zero;
            var h = (float)RobotHeading() * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(h), Mathf.Cos(h));
            var right = new Vector2(Mathf.Cos(h), -Mathf.Sin(h));
            var offset = right * (float)ox + forward * (float)oy;
            var at = Located();
            return new Vector2(d.X, d.Y) - offset + new Vector2((float)at.x, (float)at.y);
        }
    }

    internal sealed class VexDistance : VexObject {
        public override string TypeName => "Distance";
        public VexDistance(RobotProgram p, Args a) {
            var (port, _) = Port(a.Need(0, "port"), "Distance");
            DistanceSensor D() => p.Device<DistanceSensor>(port, false, "Distance");
            Method("object_distance", a2 => {
                var d = D();
                return d == null || !d.Found ? FromMm(9999, UnitName(a2.Get(0, "units"), "MM")) : FromMm(d.Mm, UnitName(a2.Get(0, "units"), "MM"));
            });
            Method("is_object_detected", _ => D()?.Found ?? false);
            Method("object_velocity", _ => (double)(D()?.Velocity ?? 0f));
            Method("object_size", _ => {
                var s = (D()?.Size ?? "None").ToUpperInvariant();
                return new PyEnum("ObjectSizeType", s, s == "SMALL" ? 1 : s == "MEDIUM" ? 2 : s == "LARGE" ? 3 : 0);
            });
            Method("object_raw_size", _ => {   // V2: (C++ objectRawSize)
                var s = D()?.Size ?? "None";
                return s == "Small" ? 50L : s == "Medium" ? 150L : s == "Large" ? 350L : 0L;
            });
            Method("object_rawsize", _ => {
                var s = D()?.Size ?? "None";
                return s == "Small" ? 50L : s == "Medium" ? 150L : s == "Large" ? 350L : 0L;
            });
            Method("installed", _ => D() != null);
            Method("changed", _ => PyNone.Value);
        }
    }

    internal sealed class VexOptical : VexObject {
        private static readonly string[] ColorNames = { "RED", "GREEN", "BLUE", "YELLOW", "ORANGE", "PURPLE", "CYAN", "WHITE", "BLACK" };
        public override string TypeName => "Optical";
        public VexOptical(RobotProgram p, Args a) {
            var (port, _) = Port(a.Need(0, "port"), "Optical");
            OpticalSensor D() => p.Device<OpticalSensor>(port, false, "Optical");
            Method("hue", _ => (double)(D()?.Hue ?? 0f));
            Method("brightness", _ => (double)(D()?.Brightness ?? 0f));
            Method("is_near_object", _ => D()?.Near ?? false);
            Method("color", _ => {
                var name = (D()?.ColorName ?? "None").ToUpperInvariant();
                return p.vm.GetAttr(p.vm.modules["vex"].globals["Color"], ColorNames.Contains(name) ? name : "BLACK");
            });
            Method("set_light", a2 => {
                var d = D();
                if (d == null) return PyNone.Value;
                var v = a2.Get(0, "value");
                d.Led = v is PyEnum e ? (e.name == "ON" ? 100 : 0) : (int)Ops.ToDouble(v ?? 0L);
                return PyNone.Value;
            });
            Method("set_light_power", a2 => {
                var d = D();
                if (d != null) d.Led = (int)a2.Number(0, "value", 0);
                return PyNone.Value;
            });
            Method("proximity", _ => (double)(D()?.Proximity ?? 0f));
            Method("installed", _ => D() != null);
            foreach (var n in new[] { "object_detected", "object_lost", "gesture_enable", "gesture_disable", "integration_time" }) Method(n, _ => PyNone.Value);
        }
    }

    internal sealed class VexRotation : VexObject {
        public override string TypeName => "Rotation";
        public VexRotation(RobotProgram p, Args a) {
            var (port, _) = Port(a.Need(0, "port"), "Rotation");
            var offset = 0.0;
            ShaftSensor D() => p.Device<ShaftSensor>(port, false, "Rotation");
            if (a.Bool(1, "reverse", false) && D() is ShaftSensor s0 && !s0.IsReversed) s0.ToggleReversed();
            Method("angle", a2 => FromDegrees(D()?.Angle ?? 0f, UnitName(a2.Get(0, "units"), "DEGREES")));
            Method("position", a2 => FromDegrees((D()?.Position ?? 0) - offset, UnitName(a2.Get(0, "units"), "DEGREES")));
            Method("velocity", a2 => {
                var r = (double)(D()?.Rpm ?? 0f);
                return UnitName(a2.Get(0, "units"), "RPM") == "DPS" ? r * 6 : r;
            });
            Method("reset_position", _ => {
                offset = D()?.Position ?? 0;
                return PyNone.Value;
            });
            Method("set_position", a2 => {
                offset = (D()?.Position ?? 0) - ToDegrees(a2.Number(0, "value", 0), UnitName(a2.Get(1, "units"), "DEGREES"));
                return PyNone.Value;
            });
            Method("set_reversed", a2 => {
                var d = D();
                if (d != null && d.IsReversed != a2.Bool(0, "value", true)) d.ToggleReversed();
                return PyNone.Value;
            });
            Method("installed", _ => D() != null);
            Method("changed", _ => PyNone.Value);
        }
    }

    internal sealed class VexSwitch : VexObject {
        private readonly string name;
        public override string TypeName => name;
        public VexSwitch(RobotProgram p, Args a, string name) {
            this.name = name;
            var (port, three) = Port(a.Need(0, "port"), name);
            SwitchSensor D() => p.Device<SwitchSensor>(port, three, name);
            bool Pressed() => D()?.Pressed ?? false;
            Method("pressing", _ => Pressed());
            Method("value", _ => name == "DigitalIn" ? (Pressed() ? 0L : 1L) : Pressed() ? 1L : 0L);
            object Watch(object cb, bool onPress) {
                var last = Pressed();
                p.pollers.Add(() => {
                    var now = Pressed();
                    if (now == last) return;
                    last = now;
                    if (now == onPress) p.vm.Start(cb, null, name);
                });
                return PyNone.Value;
            }
            Method("pressed", a2 => Watch(a2.Need(0, "callback"), true));
            Method("released", a2 => Watch(a2.Need(0, "callback"), false));
            Method("high", a2 => Watch(a2.Need(0, "callback"), false));
            Method("low", a2 => Watch(a2.Need(0, "callback"), true));
        }
    }

    internal sealed class VexSolenoid : VexObject {
        private readonly string name;
        public override string TypeName => name;
        public VexSolenoid(RobotProgram p, Args a, string name) {
            this.name = name;
            var (port, three) = Port(a.Need(0, "port"), name);
            var state = false;
            SolenoidDevice D() => p.Device<SolenoidDevice>(port, three, "Solenoid");
            void Set(bool high) {
                state = high;
                D()?.Set(high);
            }
            Method("set", a2 => {
                Set(a2.Bool(0, "value", true));
                return PyNone.Value;
            });
            Method("value", _ => state ? 1L : 0L);
            Method("open", _ => {
                Set(true);
                return PyNone.Value;
            });
            Method("close", _ => {
                Set(false);
                return PyNone.Value;
            });
        }
    }

    internal sealed class VexShaft : VexObject {
        private readonly string name;
        public override string TypeName => name;
        public VexShaft(RobotProgram p, Args a, string name) {
            this.name = name;
            var (port, three) = Port(a.Need(0, "port"), name);
            var offset = 0.0;
            ShaftSensor D() => p.Device<ShaftSensor>(port, three, name == "Encoder" ? "Shaft Encoder" : "Potentiometer");
            Method("position", a2 => FromDegrees((D()?.Position ?? 0) - offset, UnitName(a2.Get(0, "units"), "DEGREES")));
            Method("value", _ => name == "Encoder" ? (long)Math.Round((D()?.Position ?? 0) - offset) : (long)(D()?.PotValue ?? 0));
            Method("velocity", a2 => {
                var r = (double)(D()?.Rpm ?? 0f);
                return UnitName(a2.Get(0, "units"), "RPM") == "DPS" ? r * 6 : r;
            });
            Method("reset_position", _ => {
                offset = D()?.Position ?? 0;
                return PyNone.Value;
            });
            Method("set_position", a2 => {
                offset = (D()?.Position ?? 0) - ToDegrees(a2.Number(0, "value", 0), UnitName(a2.Get(1, "units"), "DEGREES"));
                return PyNone.Value;
            });
            // V2: VEXcode C++'s older encoder names (rotation, resetRotation, setRotation).
            Field("rotation", Member("position"));
            Field("reset_rotation", Member("reset_position"));
            Field("set_rotation", Member("set_position"));
            Method("angle", a2 => {
                var deg = (double)(D()?.Angle ?? 0f);
                return UnitName(a2.Get(0, "units"), "DEGREES") == "PERCENT" ? deg / ShaftSensor.PotRange * 100 : FromDegrees(deg, UnitName(a2.Get(0, "units"), "DEGREES"));
            });
        }
    }

    /// <summary>The analog 3-wire sensors: line tracker, light sensor, range finder (Sonar), yaw gyro.</summary>
    internal sealed class VexAnalog : VexObject {
        private readonly string name;
        public override string TypeName => name;
        public VexAnalog(RobotProgram p, Args a, string name) {
            this.name = name;
            var (port, three) = Port(a.Need(0, "port"), name);
            switch (name) {
                case "Line":
                    Method("reflectivity", _ => (double)(p.Device<LineTracker>(port, three, "Line Tracker")?.Reflectivity ?? 0f));
                    Method("value", _ => (long)(p.Device<LineTracker>(port, three, "Line Tracker")?.Value ?? 4095));
                    break;
                case "Light":
                    Method("brightness", _ => (double)(p.Device<LightSensor>(port, three, "Light Sensor")?.Percent ?? 0f));
                    Method("value", _ => (long)(p.Device<LightSensor>(port, three, "Light Sensor")?.Value ?? 4095));
                    break;
                case "Sonar":
                    Method("distance", a2 => {
                        var d = p.Device<UltrasonicSensor>(port, three, "Range Finder");
                        if (d == null || d.Inches < 0) return 0.0;
                        return FromMm(d.Inches * 25.4, UnitName(a2.Get(0, "units"), "MM"));
                    });
                    Method("found_object", _ => (p.Device<UltrasonicSensor>(port, three, "Range Finder")?.Inches ?? -1f) >= 0f);
                    break;
                case "Gyro":
                    var offset = 0.0;
                    Method("heading", _ => (double)(p.Device<YawGyro>(port, three, "Gyro")?.Heading ?? 0f) - offset);
                    Method("rotation", _ => (double)(p.Device<YawGyro>(port, three, "Gyro")?.Heading ?? 0f) - offset);
                    Method("rate", _ => (double)(p.Device<YawGyro>(port, three, "Gyro")?.Rate ?? 0f));
                    Method("calibrate", _ => {
                        p.Device<YawGyro>(port, three, "Gyro")?.Calibrate();
                        return PyNone.Value;
                    });
                    Method("set_heading", a2 => {
                        offset = (p.Device<YawGyro>(port, three, "Gyro")?.Heading ?? 0f) - a2.Number(0, "value", 0);
                        return PyNone.Value;
                    });
                    Field("set_rotation", Member("set_heading"));   // V2 (this gyro's rotation is its heading)
                    Method("is_calibrating", _ => false);
                    break;
                case "Accelerometer":
                    // V2: VEXcode's Accelerometer(port, high_sensitivity): one per axis (the sensor's X, Y, Z leads on
                    // three ports in a row); acceleration() in g, 2 g range (6 g with the jumper: high_sensitivity False).
                    Accelerometer A() => p.Device<Accelerometer>(port, three, "Accelerometer");
                    int Axis() {
                        var d = A();
                        return d == null ? 0 : Mathf.Clamp(port % 100 - d.port, 0, 2);
                    }
                    var range = a.Bool(1, "high_sensitivity", false) ? 2.0 : 6.0;
                    var zero = 0.0;
                    Method("acceleration", _ => (double)(A()?.G[Axis()] ?? 0f) - zero);
                    Method("value", _ => (long)(A()?.Value(Axis()) ?? 2048));
                    Method("changed", _ => PyNone.Value);
                    Method("installed", _ => A() != null);
                    Method("calibrate", _ => {
                        zero = A()?.G[Axis()] ?? 0f;
                        return PyNone.Value;
                    });
                    _ = range;
                    break;
            }
        }
    }
    /// <summary>V2: the V5 Vision Sensor's API (VEXcode's Vision / vision: Vision(port, brightness, signatures and codes),
    /// take_snapshot / takeSnapshot, objects, largest_object, object_count, set_signature, set_brightness) on the
    /// simulated sensor (`VisionSensor`: it draws what's in front of it and finds the signatures' colors). Python's
    /// take_snapshot returns the objects (largest first), C++'s takeSnapshot their count.</summary>
    internal sealed class VexVision : VexObject {
        public override string TypeName => "Vision";
        private readonly RobotProgram p;
        private readonly int port;
        private readonly PyList objects = new();

        public VexVision(RobotProgram p, Args a) {
            this.p = p;
            (port, _) = Port(a.Need(0, "port"), "Vision");
            for (var i = 0; i < 16; i++) objects.items.Add(new VexVisionObject(null));
            var none = new VexVisionObject(null);
            Field("objects", objects);
            Field("object_count", 0L);
            if (p.Cpp) Field("largest_object", none);
            else Method("largest_object", _ => objects.items[0]);
            var brightness = a.Count > 1 && !(a.pos[1] is VexVisionSignature) && !(a.pos[1] is VexVisionCode) ? (int)Ops.ToLong(a.pos[1]) : -1;
            var sensor = Sensor(false);
            if (sensor != null) {
                if (brightness > 0) sensor.brightness = Mathf.Clamp(brightness, 1, 100);
                foreach (var s in a.pos.Skip(1)) AddSig(sensor, s);
            }
            Method("take_snapshot", a2 => {
                var what = a2.Need(0, "signature");
                var count = (int)a2.Number(1, "count", 8);
                var id = what switch {
                    VexVisionSignature s => s.sig.id,
                    VexVisionCode c => VisionSensor.CodeId(c.ids),
                    _ => (int)Ops.ToLong(what),
                };
                var found = Snap(id, count);
                if (p.Cpp) return (long)found.Count;
                return new PyTuple(found.Select(b => (object)new VexVisionObject(b)).ToArray());
            });
            Method("set_signature", a2 => {
                var s = Sensor(true);
                if (s != null) AddSig(s, a2.Need(0, "signature"));
                return PyNone.Value;
            });
            Method("set_brightness", a2 => {
                var s = Sensor(true);
                if (s != null) s.brightness = Mathf.Clamp((int)a2.Number(0, "value", 50), 1, 100);
                return PyNone.Value;
            });
            Method("installed", _ => Sensor(false) != null);
            Method("timestamp", _ => (long)(p.Time * 1000));
            foreach (var n in new[] { "set_led", "set_led_mode", "set_led_brightness", "set_led_color", "set_white_balance", "set_white_balance_mode", "set_wifi_mode" })
                Method(n, _ => PyNone.Value);
        }

        private VisionSensor Sensor(bool warn) => warn ? p.Device<VisionSensor>(port, false, "Vision Sensor") : V5Brain.Get<VisionSensor>(port, false);

        private static void AddSig(VisionSensor sensor, object s) {
            switch (s) {
                case VexVisionSignature sig: sensor.SetSignature(sig.sig); break;
                case VexVisionCode code:
                    foreach (var part in code.sigs) sensor.SetSignature(part.sig);
                    if (!sensor.codes.Any(c => c.SequenceEqual(code.ids))) sensor.codes.Add(code.ids);
                    break;
            }
        }

        /// <summary>The objects of a signature (or code) in the sensor's latest picture; objects / object_count /
        /// largest_object follow it (VEXcode keeps the last snapshot's).</summary>
        private List<VisionBlob> Snap(int id, int count) {
            var sensor = Sensor(true);
            var found = sensor != null ? sensor.Snapshot(id, count) : new List<VisionBlob>();
            for (var i = 0; i < 16; i++) objects.items[i] = new VexVisionObject(i < found.Count ? found[i] : (VisionBlob?)null);
            Add("object_count", (long)found.Count);
            if (p.Cpp) Add("largest_object", objects.items[0]);
            return found;
        }
    }

    /// <summary>V2: one object a Vision snapshot found (none: exists is false, everything 0). VEXcode's names:
    /// centerX/center_x, originX, width, height, angle, exists, id.</summary>
    internal sealed class VexVisionObject : VexObject {
        public override string TypeName => "VisionObject";
        public VexVisionObject(VisionBlob? found) {
            var b = found ?? default;
            Field("id", (long)b.id);
            Field("center_x", (long)b.CenterX);
            Field("center_y", (long)b.CenterY);
            Field("origin_x", (long)b.x);
            Field("origin_y", (long)b.y);
            Field("width", (long)b.width);
            Field("height", (long)b.height);
            Field("angle", (double)b.angle);
            Field("exists", found.HasValue);
            Field("centerX", (long)b.CenterX);
            Field("centerY", (long)b.CenterY);
            Field("originX", (long)b.x);
            Field("originY", (long)b.y);
        }
    }

    /// <summary>V2: a Vision signature (VEXcode's Signature(index, uMin, uMax, uMean, vMin, vMax, vMean, range, type),
    /// C++ vision::signature), as the Vision Utility writes it.</summary>
    internal sealed class VexVisionSignature : VexObject {
        public override string TypeName => "Signature";
        public readonly VisionSignature sig;

        public VexVisionSignature(Args a) {
            int I(int i) => a.Count > i ? (int)Math.Round(Ops.ToDouble(a.pos[i])) : 0;
            sig = new VisionSignature { id = I(0), uMin = I(1), uMax = I(2), uMean = I(3), vMin = I(4), vMax = I(5), vMean = I(6),
                range = a.Count > 7 ? (float)Ops.ToDouble(a.pos[7]) : 2.5f, type = I(8), name = "SIG_" + I(0) };
            if (!sig.Valid) throw new PyError("ValueError", "a Signature's index is 1 to 7, then uMin, uMax, uMean, vMin, vMax, vMean (min below max), range, type: copy them from the Vision Utility");
            Field("id", (long)sig.id);
            Field("values", new PyList(a.pos));
        }

        public VexVisionSignature(VisionSignature s) {
            sig = s;
            Field("id", (long)s.id);
        }
    }

    /// <summary>V2: a color code: 2 to 5 signatures side by side, seen as one object with an angle (VEXcode's Code(sig1,
    /// sig2, ...), C++ vision::code). Its id is its signatures as octal digits.</summary>
    internal sealed class VexVisionCode : VexObject {
        public override string TypeName => "Code";
        public readonly List<VexVisionSignature> sigs = new();
        public readonly int[] ids;

        public VexVisionCode(Args a) {
            foreach (var s in a.pos) {
                if (s is VexVisionSignature sig) sigs.Add(sig);
                else if (s is long n && n >= 1 && n <= 7) sigs.Add(new VexVisionSignature(new VisionSignature { id = (int)n, uMin = 0, uMax = 0, vMin = 0, vMax = 0 }));
            }
            if (sigs.Count < 2 || sigs.Count > 5) throw new PyError("ValueError", "a Code takes 2 to 5 signatures");
            ids = sigs.Select(s => s.sig.id).ToArray();
            Field("id", (long)VisionSensor.CodeId(ids));
        }
    }
}
