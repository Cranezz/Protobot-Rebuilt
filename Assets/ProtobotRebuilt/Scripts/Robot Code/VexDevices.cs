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
        public ThreeWirePort(int index) => this.index = index;
        public override string TypeName => "ThreeWirePort";
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

        private float AxisValue(V5Axis axis) => Live ? Mathf.Clamp(V5Controller.Axis(axis), -1f, 1f) : 0f;
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
                if (c == null) return PyNone.Value;
                if (unit == "VOLT" || unit == "MV") {
                    var v = a2.Number(1, "velocity", 12);
                    c.Volts((float)(dir * Sign * (unit == "MV" ? v / 1000 : v) / 12.0));
                    return PyNone.Value;
                }
                var share = a2.Has(1, "velocity") && !(a2.Get(1, "velocity") is PyNone) ? Share(a2.Number(1, "velocity", 0), unit) : velocity;
                c.Spin((float)(dir * Sign * share));
                return PyNone.Value;
            });
            Method("spin_for", a2 => {
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
                var target = ToDegrees(a2.Number(0, "rotation", 0), UnitName(a2.Get(1, "units"), "DEGREES"));
                var speed = a2.Has(2, "velocity") && !(a2.Get(2, "velocity") is PyNone) ? Share(a2.Number(2, "velocity", 0), UnitName(a2.Get(3, "units_v"), "RPM")) : velocity;
                return MoveTo(target, speed, a2, 4);
            });
            Method("stop", a2 => {
                var how = a2.Count > 0 ? UnitName(a2.pos[0], stopping) : stopping;
                Control?.StopMotor(how);
                return PyNone.Value;
            });
            Method("set_velocity", a2 => {
                velocity = Share(a2.Number(0, "velocity", 50), UnitName(a2.Get(1, "units"), "RPM"));
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
            Method("spin_for", a => All(p, a, "spin_for", 5));
            Method("spin_to_position", a => All(p, a, "spin_to_position", 4));
            Method("position", a => Ops.ToDouble(p.vm.CallSync(Get(motors[0], "position"), a.pos, a.kw)));
            Method("velocity", a => motors.Average(m => Ops.ToDouble(p.vm.CallSync(Get(m, "velocity"), a.pos, a.kw))));
            Method("current", a => motors.Sum(m => Ops.ToDouble(p.vm.CallSync(Get(m, "current"), a.pos, a.kw))));
            Method("power", a => motors.Sum(m => Ops.ToDouble(p.vm.CallSync(Get(m, "power"), a.pos, a.kw))));
            Method("torque", a => motors.Sum(m => Ops.ToDouble(p.vm.CallSync(Get(m, "torque"), a.pos, a.kw))));
            Method("is_done", _ => motors.All(m => m.Control == null || m.Control.done));
            Method("is_spinning", _ => motors.Any(m => m.Control != null && Mathf.Abs(m.Control.Level) > 0.01f));
            Method("count", _ => (long)motors.Length);
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
                return PyNone.Value;
            });
            Method("drive_for", a2 => {
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
                return PyNone.Value;
            });
            Method("turn_for", a2 => {
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
                RequireSmart("turn_to_heading");
                var target = a2.Number(0, "angle", 0);
                var now = gyro.Heading();
                var delta = Mathf.DeltaAngle((float)now, (float)target);
                var v = a2.Has(2, "velocity") && !(a2.Get(2, "velocity") is PyNone) ? Share(a2.Number(2, "velocity", 0), UnitName(a2.Get(3, "units_v"), "RPM")) : turnVelocity;
                return GyroTurn(gyro.Rotation() + delta, v, a2.Bool(4, "wait", true));
            });
            Method("turn_to_rotation", a2 => {
                RequireSmart("turn_to_rotation");
                var v = a2.Has(2, "velocity") && !(a2.Get(2, "velocity") is PyNone) ? Share(a2.Number(2, "velocity", 0), UnitName(a2.Get(3, "units_v"), "RPM")) : turnVelocity;
                return GyroTurn(a2.Number(0, "angle", 0), v, a2.Bool(4, "wait", true));
            });
            Method("stop", a2 => {
                var how = a2.Count > 0 ? UnitName(a2.pos[0], stopping) : stopping;
                turning = false;
                foreach (var m in left.Concat(right)) m.Control?.StopMotor(how);
                return PyNone.Value;
            });
            Method("arcade", a2 => {   // (PROS-style) arcade(forward, turn) in percent
                var f = a2.Number(0, "drive_power", 0) / 100.0;
                var t = a2.Number(1, "turn_power", 0) / 100.0;
                turning = false;
                foreach (var m in left) m.Control?.Spin((float)(m.Sign * Math.Max(-1, Math.Min(1, f + t))));
                foreach (var m in right) m.Control?.Spin((float)(m.Sign * Math.Max(-1, Math.Min(1, f - t))));
                return PyNone.Value;
            });
            Method("set_drive_velocity", a2 => {
                driveVelocity = Share(a2.Number(0, "velocity", 50), UnitName(a2.Get(1, "units"), "RPM"));
                return PyNone.Value;
            });
            Method("set_turn_velocity", a2 => {
                turnVelocity = Share(a2.Number(0, "velocity", 50), UnitName(a2.Get(1, "units"), "RPM"));
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
        private bool Calibrating => program.Time < calibratingUntil;

        public double Rotation() => Calibrating || Device == null ? rotationOffset : Device.Rotation + rotationOffset;
        public double Heading() => Calibrating || Device == null ? Repeat(headingOffset) : Repeat(Device.Rotation + headingOffset);
        private static double Repeat(double v) => ((v % 360) + 360) % 360;

        public void SetHeading(double v) => headingOffset = v - (Device?.Rotation ?? 0);
        public void SetRotation(double v) => rotationOffset = v - (Device?.Rotation ?? 0);
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
                    break;
            }
        }
    }
}
