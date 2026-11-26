using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Devices;
using Protobot.Scripting;
using UnityEngine;
using static Protobot.RobotCode.VexModule;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9, C++): EZ-Template (ez::), the other library competitive PROS teams use besides LemLib, as its
    /// template project is written (v3 names, and the common v2 ones):
    ///   ez::Drive chassis({1, 2, 3}, {-4, -5, -6}, 7, 3.25, 360);      // left/right ports (negative: reversed), IMU, wheel, wheel rpm
    ///   chassis.pid_drive_constants_set(20, 0, 100); chassis.pid_turn_constants_set(3, 0.05, 20, 15); ...
    ///   chassis.pid_drive_set(24_in, 110, true); chassis.pid_wait();
    ///   chassis.pid_turn_set(90_deg, 90); chassis.pid_wait_quick_chain();
    ///   chassis.pid_swing_set(ez::LEFT_SWING, 45_deg, 110, 45); chassis.pid_odom_set({{0_in, 24_in}, fwd, 110}, true);
    ///   ez::as::auton_selector.autons_add({{"Drive", drive_example}, ...}); ez::as::auton_selector.selected_auton_call();
    ///   chassis.opcontrol_tank(); chassis.opcontrol_arcade_standard(ez::SPLIT);
    /// Like EZ-Template: its PID runs every 10 ms (ez::util::DELAY_TIME), so its constants mean the same; a motion
    /// starts at once (it replaces the one under way); pid_wait waits for the exit conditions (small / big error
    /// for their times, or not moving); drive, turn and swing motions keep holding their target after that until the
    /// next motion or the sticks. Odometry (from the drive motors or tracking wheels and the IMU) runs every physics
    /// step: x right, y forward, theta clockwise (as EZ-Template). The auton selector shows on the Brain screen; touch
    /// its left or right side to page (kept between runs).
    /// </summary>
    public static class EzTemplate {
        private const string SelectedKey = "V2.Code.EzAuton";

        internal static PyModule Build(RobotProgram program) {
            var m = new PyModule("ez");
            PyEnum E(string type, string name, int value) {
                var e = new PyEnum(type, name, value);
                m.globals[name] = e;
                return e;
            }
            E("e_type", "SINGLE", 0);
            E("e_type", "SPLIT", 1);
            E("e_swing", "LEFT_SWING", 0);
            E("e_swing", "RIGHT_SWING", 1);
            foreach (var (n, v) in new[] { ("raw", 0), ("left_turn", 1), ("right_turn", 2), ("longest", 3), ("shortest", 4) }) E("e_angle_behavior", n, v);
            m.globals["ccw"] = m.globals["left_turn"];
            m.globals["cw"] = m.globals["right_turn"];
            foreach (var (n, v) in new[] { ("fwd", 0), ("FWD", 0), ("rev", 1), ("REV", 1) }) E("drive_directions", n, v);
            m.globals["Drive"] = new PyClass("Drive", null) { construct = (_, a) => new EzDrive(program, a) };
            m.globals["tracking_wheel"] = new PyClass("tracking_wheel", null) { construct = (_, a) => new EzTracker(program, a) };
            m.globals["Auton"] = new PyClass("Auton", null) { construct = (_, a) => new PyTuple(new[] { a.Need(0, "name"), a.Need(1, "callback") }) };
            var print = new PyBuiltin("ez_template_print", _ => {
                program.Print("EZ-Template (simulated)\n");
                return PyNone.Value;
            });
            m.globals["ez_template_print"] = print;
            m.globals["print_ez_template"] = print;   // (v2)

            var util = new PyModule("util");
            util.globals["DELAY_TIME"] = 10L;
            util.globals["sgn"] = new PyBuiltin("sgn", a => (long)Math.Sign(a.Number(0, "input", 0)));
            util.globals["clamp"] = new PyBuiltin("clamp", a => {
                var v = a.Number(0, "input", 0);
                var max = a.Number(1, "max", 0);
                var min = a.Has(2, "min") ? a.Number(2, "min", -max) : -max;
                return Math.Max(min, Math.Min(max, v));
            });
            util.globals["to_deg"] = new PyBuiltin("to_deg", a => a.Number(0, "input", 0) * 180 / Math.PI);
            util.globals["to_rad"] = new PyBuiltin("to_rad", a => a.Number(0, "input", 0) * Math.PI / 180);
            m.globals["util"] = util;

            // The auton selector (ez::as).
            var selector = new Selector(program);
            var asm = new PyModule("as");
            asm.globals["auton_selector"] = selector;
            object Init(Args _) {
                selector.Show();
                return PyNone.Value;
            }
            asm.globals["initialize"] = new PyBuiltin("initialize", Init);
            asm.globals["init_auton_selector"] = new PyBuiltin("init_auton_selector", Init);   // (v2)
            asm.globals["shutdown"] = new PyBuiltin("shutdown", _ => PyNone.Value);
            asm.globals["page_up"] = new PyBuiltin("page_up", _ => { selector.Page(1); return PyNone.Value; });
            asm.globals["page_down"] = new PyBuiltin("page_down", _ => { selector.Page(-1); return PyNone.Value; });
            m.globals["as"] = asm;
            return m;
        }

        /// <summary>Names EZ-Template's own files make global: master, Drive (v2), fwd/rev.</summary>
        internal static void Globals(RobotProgram program, PyModule ez, PyModule pros) {
            var g = program.vm.main.globals;
            if (pros.globals["Controller"] is PyClass c && pros.globals["E_CONTROLLER_MASTER"] is PyEnum master)
                g["master"] = c.construct(c, new Args(program.vm, "Controller", new object[] { master }, null));
            foreach (var n in new[] { "Drive", "Auton", "fwd", "rev", "FWD", "REV" }) g[n] = ez.globals[n];
        }

        // ------------------------------------------------------------------ ez::as::auton_selector

        private sealed class Selector : VexObject {
            public override string TypeName => "AutonSelector";
            private readonly RobotProgram program;
            private readonly List<(string name, object fn)> autons = new();
            private int page;
            private bool shown, wasPressing;

            public Selector(RobotProgram p) {
                program = p;
                page = PlayerPrefs.GetInt(SelectedKey, 0);
                object Add(Args a) {
                    foreach (var item in Ops.Iterate(a.Need(0, "autons"))) {
                        var parts = Ops.Iterate(item).ToList();
                        if (parts.Count < 2) throw new PyError("TypeError", "each auton is {\"name\", function}");
                        autons.Add((a.vm.StrOf(parts[0]), parts[1]));
                    }
                    if (page >= autons.Count) page = 0;
                    if (shown) Draw();
                    return PyNone.Value;
                }
                Method("autons_add", Add);
                Method("add_autons", Add);   // (v2)
                object CallSelected(Args a) {
                    if (autons.Count == 0) {
                        program.Warn("ez-noautons", "ez::as::auton_selector has no autons: add them with autons_add({...}) in initialize()");
                        return PyNone.Value;
                    }
                    var (name, fn) = autons[Mathf.Clamp(page, 0, autons.Count - 1)];
                    program.Print("[auton selector] running \"" + name.Split('\n')[0] + "\"\n");
                    // In its own thread, waited for: the auton's pid_wait()s pause it like any call.
                    var fiber = a.vm.Start(fn, null, "auton");
                    return new Suspend(() => fiber.Done);
                }
                Method("selected_auton_call", CallSelected);
                Method("call_selected_auton", CallSelected);   // (v2)
                Method("selected_auton_print", _ => { Draw(); return PyNone.Value; });
                Method("auton_count", _ => (long)autons.Count);
                Field("auton_page_current", (long)page);
            }

            public void Show() {
                if (shown) return;
                shown = true;
                Draw();
                program.pollers.Add(Watch);
            }

            public void Page(int by) {
                if (autons.Count == 0) return;
                page = ((page + by) % autons.Count + autons.Count) % autons.Count;
                PlayerPrefs.SetInt(SelectedKey, page);
                Field("auton_page_current", (long)page);
                Draw();
            }

            // LLEMU's left / right buttons: the left and right thirds of the Brain's screen.
            private void Watch() {
                var screen = program.Brain.screen;
                var press = screen.Pressing && !wasPressing;
                wasPressing = screen.Pressing;
                if (!press) return;
                if (screen.TouchX < BrainCanvas.Width / 3) Page(-1);
                else if (screen.TouchX > BrainCanvas.Width * 2 / 3) Page(1);
            }

            private void Draw() {
                var screen = program.Brain.screen;
                void Row(long row, string text) {
                    CppApi.Call(program, screen, "clear_row", row);
                    CppApi.Call(program, screen, "set_cursor", row, 1L);
                    CppApi.Call(program, screen, "print", text);
                }
                if (autons.Count == 0) {
                    Row(1, "Auton selector: no autons yet");
                    return;
                }
                var lines = autons[Mathf.Clamp(page, 0, autons.Count - 1)].name.Split('\n');
                Row(1, "Page " + (page + 1) + " of " + autons.Count);
                for (var i = 0; i < 5; i++) Row(i + 2, i < lines.Length ? lines[i] : "");
                Row(8, "<  touch left / right to change  >");
            }
        }
    }

    /// <summary>ez::tracking_wheel(port, wheel diameter, distance to the center, ratio): a Rotation sensor (port, negative
    /// reversed) or an ADI encoder ('A' or {'A', 'B'}).</summary>
    internal sealed class EzTracker : VexObject {
        public override string TypeName => "tracking_wheel";
        private readonly RobotProgram program;
        private readonly VexObject sensor;
        private readonly double diameter, ratio, sign;
        public double offset;

        public EzTracker(RobotProgram p, Args a) {
            program = p;
            var port = a.Need(0, "port");
            diameter = a.Number(1, "wheel_diameter", 2.75);
            offset = Math.Abs(a.Number(2, "distance_to_center", 0));
            ratio = MotionModule.Arg(a, 3, "ratio", 1);
            sign = 1;
            if (port is long l) sensor = new VexRotation(p, new Args(p.vm, "Rotation", new object[] { (long)Math.Abs(l), l < 0 }, null));
            else {
                if (port is string s && s.StartsWith("-")) { sign = -1; port = s.Substring(1); }
                sensor = new VexShaft(p, new Args(p.vm, "Encoder", new object[] { CppApi.Adi(port) }, null), "Encoder");
            }
            Method("get", _ => Distance());
            Method("reset", _ => { Reset(); return PyNone.Value; });
            Method("distance_to_center_set", a2 => { offset = Math.Abs(a2.Number(0, "input", 0)); return PyNone.Value; });
            Method("distance_to_center_get", _ => offset);
        }

        public double Distance() => Ops.ToDouble(CppApi.Call(program, sensor, "position", Units["DEGREES"])) * sign / 360.0 * Math.PI * diameter / Math.Max(1e-6, ratio);
        public void Reset() => CppApi.Call(program, sensor, "reset_position");
    }

    // ---------------------------------------------------------------------- ez::Drive

    internal sealed class EzDrive : VexObject {
        public override string TypeName => "Drive";
        private readonly RobotProgram program;
        private readonly VexMotor[] left, right;
        private readonly VexInertial imu;
        private readonly double wheelDiameter, wheelRpm;
        private readonly double trackWidth;   // only used without an IMU
        private readonly double ticksPerInch = 1;   // EZ-Template 2: drive errors in encoder ticks
        private EzTracker trackLeft, trackRight, trackFront, trackBack;

        // Constants (EZ-Template's defaults: its template's default_constants()).
        internal sealed class Gains { public double p, i, d, startI; }
        internal sealed class ExitSet { public double smallTime = 90, smallError, bigTime = 250, bigError, velocityTime = 500; }
        private Gains driveFwd = new() { p = 20, d = 100 }, driveBack = new() { p = 20, d = 100 }, heading = new() { p = 11, d = 20 },
                      turn = new() { p = 3, i = 0.05, d = 20, startI = 15 }, swingFwd = new() { p = 6, d = 65 }, swingBack = new() { p = 6, d = 65 },
                      odomAngular = new() { p = 6.5, d = 52.5 }, boomerang = new() { p = 5.8, d = 32.5 };
        private ExitSet turnExit = new() { smallError = 3, bigError = 7 }, swingExit = new() { smallError = 3, bigError = 7 },
                        driveExit = new() { smallError = 1, bigError = 3 }, odomTurnExit = new() { smallError = 3, bigError = 7, velocityTime = 750 },
                        odomDriveExit = new() { smallError = 1, bigError = 3, velocityTime = 750 };
        private double turnChain = 3, swingChain = 5, driveChainFwd = 3, driveChainBack = 3;
        private (double distance, double min) slewDrive = (3, 70), slewTurn = (3, 70), slewSwing = (3, 80);
        private double turnBias = 0.9, lookAhead = 7, boomerangDistance = 16, dlead = 0.625;
        private int turnBehavior, swingBehavior;   // e_angle_behavior: 0 raw ... 4 shortest
        private string brakeMode = "COAST";

        // Odometry.
        private double x, y, theta;
        private double lastL, lastR, lastTL, lastTR, lastTF, lastTB, lastImu;
        private bool odomStarted;
        private float nextGraph;

        // The motion under way (it keeps running after it's done, holding, as EZ-Template's PID task does).
        private EzMotion current;
        private double headingTarget;
        private double clock, nextTick;
        private const double Tick = 0.01;

        // Driver control.
        private double curveLeft, curveRight, activeBrakeKp, joystickThreshold = 5;
        private double brakeTargetL, brakeTargetR;
        private bool braking;

        public EzDrive(RobotProgram p, Args a) {
            program = p;
            left = Motors(a.Need(0, "left_motor_ports"));
            right = Motors(a.Need(1, "right_motor_ports"));
            var imuPort = (long)a.Number(2, "imu_port", 0);
            imu = imuPort > 0 ? new VexInertial(p, new Args(p.vm, "Inertial", new object[] { imuPort }, null)) : null;
            wheelDiameter = a.Number(3, "wheel_diameter", 4.125);
            // v3: (left, right, imu, wheel diameter, wheel rpm); v2: (..., cartridge rpm, external ratio driven/driving),
            // whose drive PID and exit conditions are in encoder ticks (50 x 3600 / cartridge x ratio a wheel turn).
            wheelRpm = a.Number(4, "wheel_rpm", 343);
            if (a.Count == 6 && (a.pos[5] is double || a.pos[5] is long)) {
                var cartridge = wheelRpm;
                var ratio = Math.Max(0.01, Ops.ToDouble(a.pos[5]));
                wheelRpm = cartridge / ratio;
                ticksPerInch = 50.0 * 3600.0 / Math.Max(1, cartridge) * ratio / (Math.PI * wheelDiameter);
                // EZ-Template 2's defaults.
                driveFwd = new Gains { p = 0.45, d = 5 };
                driveBack = new Gains { p = 0.45, d = 5 };
                turn = new Gains { p = 5, i = 0.003, d = 35, startI = 15 };
                swingFwd = swingBack = new Gains { p = 7, d = 45 };
                turnExit = new ExitSet { smallTime = 100, smallError = 3, bigTime = 500, bigError = 7 };
                swingExit = new ExitSet { smallTime = 100, smallError = 3, bigTime = 500, bigError = 7 };
                driveExit = new ExitSet { smallTime = 80, smallError = 50, bigTime = 300, bigError = 150 };
                slewDrive = (7, 80);
            }
            if (imu != null && !imu.Installed)
                p.Warn("ez-imu", "there's no Inertial sensor on port " + imuPort + " in the build: EZ-Template's turns read the drive's wheels instead (add an IMU for real turns)");
            trackWidth = DeviceConfig.TrackWidth(left, right);
            p.pollers.Add(Step);
            Members();
        }

        private VexMotor[] Motors(object ports) {
            var list = new List<VexMotor>();
            foreach (var item in Ops.Iterate(ports)) {
                var port = (int)Ops.ToLong(item);
                list.Add(new VexMotor(program, new Args(program.vm, "Motor", new object[] { (long)Math.Abs(port), port < 0 }, null)));
            }
            if (list.Count == 0) throw new PyError("ValueError", "ez::Drive needs at least one motor on each side, like {1, 2, 3}");
            return list.ToArray();
        }

        // ------------------------------------------------------------------ the API

        private void Members() {
            object None(Action act) {
                act();
                return PyNone.Value;
            }
            Gains G(Args a, int at = 0) => new() { p = a.Number(at, "p", 0), i = MotionModule.Arg(a, at + 1, "i", 0), d = MotionModule.Arg(a, at + 2, "d", 0), startI = MotionModule.Arg(a, at + 3, "p_start_i", 0) };
            ExitSet X(Args a, int at = 0) => new() {
                smallTime = a.Number(at, "p_small_exit_time", 90), smallError = a.Number(at + 1, "p_small_error", 1), bigTime = a.Number(at + 2, "p_big_exit_time", 250),
                bigError = a.Number(at + 3, "p_big_error", 3), velocityTime = MotionModule.Arg(a, at + 4, "p_velocity_exit_time", 500),
            };
            // Constants.
            Method("pid_drive_constants_set", a => None(() => driveFwd = driveBack = G(a)));
            Method("pid_drive_constants_forward_set", a => None(() => driveFwd = G(a)));
            Method("pid_drive_constants_backward_set", a => None(() => driveBack = G(a)));
            Method("pid_heading_constants_set", a => None(() => heading = G(a)));
            Method("pid_turn_constants_set", a => None(() => turn = G(a)));
            Method("pid_swing_constants_set", a => None(() => swingFwd = swingBack = G(a)));
            Method("pid_swing_constants_forward_set", a => None(() => swingFwd = G(a)));
            Method("pid_swing_constants_backward_set", a => None(() => swingBack = G(a)));
            Method("pid_odom_angular_constants_set", a => None(() => odomAngular = G(a)));
            Method("pid_odom_boomerang_constants_set", a => None(() => boomerang = G(a)));
            Method("pid_turn_exit_condition_set", a => None(() => turnExit = X(a)));
            Method("pid_swing_exit_condition_set", a => None(() => swingExit = X(a)));
            Method("pid_drive_exit_condition_set", a => None(() => driveExit = X(a)));
            Method("pid_odom_turn_exit_condition_set", a => None(() => odomTurnExit = X(a)));
            Method("pid_odom_drive_exit_condition_set", a => None(() => odomDriveExit = X(a)));
            Method("pid_turn_chain_constant_set", a => None(() => turnChain = a.Number(0, "input", 3)));
            Method("pid_swing_chain_constant_set", a => None(() => swingChain = a.Number(0, "input", 5)));
            Method("pid_swing_chain_forward_constant_set", a => None(() => swingChain = a.Number(0, "input", 5)));
            Method("pid_swing_chain_backward_constant_set", a => None(() => swingChain = a.Number(0, "input", 5)));
            Method("pid_drive_chain_constant_set", a => None(() => driveChainFwd = driveChainBack = a.Number(0, "input", 3)));
            Method("pid_drive_chain_forward_constant_set", a => None(() => driveChainFwd = a.Number(0, "input", 3)));
            Method("pid_drive_chain_backward_constant_set", a => None(() => driveChainBack = a.Number(0, "input", 3)));
            Method("slew_drive_constants_set", a => None(() => slewDrive = (a.Number(0, "distance", 3), a.Number(1, "min_speed", 70))));
            Method("slew_drive_constants_forward_set", a => None(() => slewDrive = (a.Number(0, "distance", 3), a.Number(1, "min_speed", 70))));
            Method("slew_drive_constants_backward_set", a => None(() => slewDrive = (a.Number(0, "distance", 3), a.Number(1, "min_speed", 70))));
            Method("slew_turn_constants_set", a => None(() => slewTurn = (a.Number(0, "distance", 3), a.Number(1, "min_speed", 70))));
            Method("slew_swing_constants_set", a => None(() => slewSwing = (a.Number(0, "distance", 3), a.Number(1, "min_speed", 80))));
            Method("slew_swing_constants_forward_set", a => None(() => slewSwing = (a.Number(0, "distance", 3), a.Number(1, "min_speed", 80))));
            Method("slew_swing_constants_backward_set", a => None(() => slewSwing = (a.Number(0, "distance", 3), a.Number(1, "min_speed", 80))));
            Method("odom_turn_bias_set", a => None(() => turnBias = Mathf.Clamp01((float)a.Number(0, "bias", 0.9))));
            Method("odom_look_ahead_set", a => None(() => lookAhead = Math.Max(1, a.Number(0, "distance", 7))));
            Method("odom_boomerang_distance_set", a => None(() => boomerangDistance = a.Number(0, "distance", 16)));
            Method("odom_boomerang_dlead_set", a => None(() => dlead = a.Number(0, "input", 0.625)));
            Method("pid_angle_behavior_set", a => None(() => turnBehavior = swingBehavior = Behavior(a.Need(0, "behavior"))));
            Method("pid_turn_behavior_set", a => None(() => turnBehavior = Behavior(a.Need(0, "behavior"))));
            Method("pid_swing_behavior_set", a => None(() => swingBehavior = Behavior(a.Need(0, "behavior"))));
            Method("odom_tracker_left_set", a => None(() => trackLeft = a.Get(0, "input") as EzTracker));
            Method("odom_tracker_right_set", a => None(() => trackRight = a.Get(0, "input") as EzTracker));
            Method("odom_tracker_front_set", a => None(() => trackFront = a.Get(0, "input") as EzTracker));
            Method("odom_tracker_back_set", a => None(() => trackBack = a.Get(0, "input") as EzTracker));

            // Set up.
            Method("initialize", _ => Calibrate());
            Method("drive_imu_calibrate", _ => Calibrate());
            Method("drive_imu_calibrated", _ => true);
            Method("drive_imu_reset", a => None(() => SetImu(MotionModule.Arg(a, 0, "new_heading", 0))));
            Method("drive_imu_get", _ => Rotation());
            Method("drive_imu_scaler_set", _ => PyNone.Value);
            Method("drive_sensor_reset", _ => None(ResetSensors));
            Method("drive_sensor_left", _ => left.Average(m => m.Position()));
            Method("drive_sensor_right", _ => right.Average(m => m.Position()));
            Method("drive_brake_set", a => None(() => {
                var n = a.Need(0, "brake_type") is PyEnum e ? e.name.ToUpperInvariant() : "COAST";
                brakeMode = n.Contains("HOLD") ? "HOLD" : n.Contains("BRAKE") && !n.EndsWith("COAST") ? "BRAKE" : "COAST";
                foreach (var m in left.Concat(right)) m.stopping = brakeMode;
            }));
            Method("drive_brake_get", _ => new PyEnum("MotorBrake", "E_MOTOR_BRAKE_" + brakeMode, brakeMode == "HOLD" ? 2 : brakeMode == "BRAKE" ? 1 : 0));
            Method("drive_set", a => None(() => {
                current = null;
                Output(a.Number(0, "left", 0), a.Number(1, "right", 0));
            }));
            Method("pid_targets_reset", _ => None(() => {
                current = null;
                headingTarget = 0;
            }));

            // Odometry.
            Method("odom_xyt_set", a => None(() => {
                if (a.Get(0, "x") is PyList || a.Get(0, "x") is PyTuple) {
                    var v = Ops.Iterate(a.Need(0, "pose")).Select(q => Ops.ToDouble(q)).ToArray();
                    SetPose(v.ElementAtOrDefault(0), v.ElementAtOrDefault(1), v.Length > 2 ? v[2] : theta);
                }
                else SetPose(a.Number(0, "x", 0), a.Number(1, "y", 0), MotionModule.Arg(a, 2, "t", theta));
            }));
            Method("odom_xy_set", a => None(() => SetPose(a.Number(0, "x", 0), a.Number(1, "y", 0), theta)));
            Method("odom_x_set", a => None(() => SetPose(a.Number(0, "x", 0), y, theta)));
            Method("odom_y_set", a => None(() => SetPose(x, a.Number(0, "y", 0), theta)));
            Method("odom_theta_set", a => None(() => SetPose(x, y, a.Number(0, "a", 0))));
            Method("odom_reset", _ => None(() => SetPose(0, 0, 0)));
            Method("odom_x_get", _ => { StartOdom(); return x; });
            Method("odom_y_get", _ => { StartOdom(); return y; });
            Method("odom_theta_get", _ => { StartOdom(); return theta; });
            Method("odom_pose_get", _ => { StartOdom(); return new MotionPose(x, y, theta); });
            Method("odom_enable", _ => PyNone.Value);
            Method("odom_enabled", _ => true);

            // Motions.
            Method("pid_drive_set", a => Drive(a.Number(0, "target", 0), a.Number(1, "speed", 110), a.Bool(2, "slew_on", false), a.Bool(3, "toggle_heading", true)));
            Method("pid_turn_set", a => {
                var (behavior, slew) = TurnOptions(a, 2, turnBehavior);
                return Turn(a.Number(0, "target", 0), a.Number(1, "speed", 90), behavior, slew, false);
            });
            Method("pid_turn_relative_set", a => {
                var (behavior, slew) = TurnOptions(a, 2, 0);
                return Turn(headingTarget + a.Number(0, "target", 0), a.Number(1, "speed", 90), behavior, slew, false);
            });
            Method("pid_swing_set", a => Swing(a));
            Method("pid_swing_relative_set", a => Swing(a, true));
            Method("pid_odom_set", a => Odom(a));
            Method("pid_odom_ptp_set", a => Odom(a));
            Method("pid_odom_boomerang_set", a => Odom(a));
            Method("pid_odom_pp_set", a => Odom(a));
            Method("pid_odom_injected_pp_set", a => Odom(a));
            Method("pid_odom_smooth_pp_set", a => Odom(a));
            Method("pid_speed_max_set", a => None(() => { if (current != null) current.speed = Math.Abs(a.Number(0, "speed", 127)); }));
            Method("pid_wait", _ => Wait(() => current == null || current.exited));
            Method("pid_wait_until", a => WaitUntil(a.Number(0, "target", 0)));
            Method("pid_wait_until_index", a => {
                var index = (int)a.Number(0, "index", 0);
                return Wait(() => current == null || current.exited || current.index >= index);
            });
            Method("pid_wait_until_point", a => {
                var v = Ops.Iterate(a.Need(0, "target")).Select(q => Ops.ToDouble(q)).ToArray();
                var (px, py) = (v.ElementAtOrDefault(0), v.ElementAtOrDefault(1));
                return Wait(() => current == null || current.exited || Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) < 2);
            });
            Method("pid_wait_quick", _ => current == null ? PyNone.Value : WaitUntilTarget(current, 0));
            Method("pid_wait_quick_chain", _ => {
                if (current == null) return PyNone.Value;
                var chain = current is TurnMotion ? turnChain : current is SwingMotion ? swingChain : current.Direction >= 0 ? driveChainFwd : driveChainBack;
                return WaitUntilTarget(current, chain);
            });
            Field("interfered", false);   // V2: a bool in EZ-Template (as a method, if (chassis.interfered) was always true)
            Method("drive_mode_get", _ => current == null ? 0L : 1L);

            // Driver control.
            Method("opcontrol_tank", _ => None(() => Sticks(Axis(3), Axis(2), true)));
            Method("opcontrol_arcade_standard", a => None(() => {
                var split = !(a.Get(0, "stick_type") is PyEnum e) || e.value == 1;
                Arcade(Axis(3), split ? Axis(1) : Axis(4));
            }));
            Method("opcontrol_arcade_flipped", a => None(() => {
                var split = !(a.Get(0, "stick_type") is PyEnum e) || e.value == 1;
                Arcade(Axis(2), split ? Axis(4) : Axis(1));
            }));
            Method("opcontrol_curve_default_set", a => None(() => {
                curveLeft = a.Number(0, "left", 0);
                curveRight = MotionModule.Arg(a, 1, "right", curveLeft);
            }));
            Method("opcontrol_drive_activebrake_set", a => None(() => activeBrakeKp = a.Number(0, "kp", 0)));
            Method("opcontrol_drive_activebrake_get", _ => activeBrakeKp);
            Method("opcontrol_joystick_threshold_set", a => None(() => joystickThreshold = a.Number(0, "threshold", 5)));
            Method("opcontrol_curve_left", a => Curve(a.Number(0, "x", 0), curveLeft));
            Method("opcontrol_curve_right", a => Curve(a.Number(0, "x", 0), curveRight));

            // EZ-Template v2's names.
            foreach (var (v2, v3) in new[] {
                ("set_drive_pid", "pid_drive_set"), ("set_turn_pid", "pid_turn_set"), ("set_swing_pid", "pid_swing_set"), ("wait_drive", "pid_wait"),
                ("wait_until", "pid_wait_until"), ("set_max_speed", "pid_speed_max_set"), ("reset_pid_targets", "pid_targets_reset"),
                ("reset_gyro", "drive_imu_reset"), ("reset_drive_sensor", "drive_sensor_reset"), ("set_drive_brake", "drive_brake_set"),
                ("set_tank", "drive_set"), ("tank", "opcontrol_tank"), ("arcade_standard", "opcontrol_arcade_standard"),
                ("arcade_flipped", "opcontrol_arcade_flipped"), ("set_active_brake", "opcontrol_drive_activebrake_set"),
                ("set_curve_default", "opcontrol_curve_default_set"), ("set_joystick_threshold", "opcontrol_joystick_threshold_set"),
                ("get_gyro", "drive_imu_get"), ("imu_calibrate", "drive_imu_calibrate"),
            })
                if (TryGetAttr(v3, out var f)) Field(v2, f);
            // v2: set_pid_constants(&chassis.headingPID, p, i, d, start_i); set_exit_condition(chassis.turn_exit, ...)
            foreach (var n in new[] { "headingPID", "turnPID", "swingPID", "forward_drivePID", "backward_drivePID", "leftPID", "rightPID", "turn_exit", "swing_exit", "drive_exit" })
                Field(n, n);
            Method("set_pid_constants", a => None(() => {
                var g = G(a, 1);
                switch (a.vm.StrOf(a.Need(0, "pid"))) {
                    case "headingPID": heading = g; break;
                    case "turnPID": turn = g; break;
                    case "swingPID": swingFwd = swingBack = g; break;
                    case "backward_drivePID": driveBack = g; break;
                    default: driveFwd = g; if (a.vm.StrOf(a.pos[0]) != "forward_drivePID") driveBack = g; break;
                }
            }));
            Method("set_exit_condition", a => None(() => {
                var e = X(a, 1);
                switch (a.vm.StrOf(a.Need(0, "type"))) {
                    case "turn_exit": turnExit = e; break;
                    case "swing_exit": swingExit = e; break;
                    default: driveExit = e; break;
                }
            }));
            Method("set_slew_min_power", a => None(() => slewDrive = (slewDrive.distance, a.Number(0, "fwd", 80))));
            Method("set_slew_distance", a => None(() => slewDrive = (a.Number(0, "fwd", 7), slewDrive.min)));
        }

        // EZ-Template's tuner, curve buttons, printing and the like: nothing to simulate. Unknown chassis members do
        // nothing too, with one note, so a template project runs as it is.
        private static readonly string[] Quiet = { "pid_tuner", "pid_print", "opcontrol_curve_buttons", "opcontrol_curve_sd", "opcontrol_drive_activebrake_targets_set",
                                                   "opcontrol_speed_max_set", "opcontrol_drive_reverse", "odom_tracker", "drive_width", "pid_odom_drive_exit", "slew_", "toggle_", "set_" };

        public override bool TryGetAttr(string name, out object value) {
            if (base.TryGetAttr(name, out value)) return true;
            if (name.StartsWith("__")) return false;
            var quiet = Quiet.Any(name.StartsWith);
            if (!quiet) program.Warn("ez-" + name, "chassis." + name + "() isn't simulated here: it does nothing");
            value = new PyBuiltin(name, _ => name.EndsWith("_enabled") || name.EndsWith("_get") ? (object)false : PyNone.Value);
            return true;
        }

        private static int Behavior(object o) => o is PyEnum e && e.type == "e_angle_behavior" ? e.value : 0;

        // (target, speed, [behavior], [slew]) in either order.
        private static (int behavior, bool slew) TurnOptions(Args a, int from, int behavior) {
            var slew = false;
            for (var i = from; i < a.Count; i++) {
                if (a.pos[i] is bool b) slew = b;
                else if (a.pos[i] is PyEnum e && e.type == "e_angle_behavior") behavior = e.value;
            }
            return (behavior, slew);
        }

        private object Calibrate() {
            StartOdom();
            if (imu == null || !imu.Installed) return true;
            CppApi.Call(program, imu, "calibrate");
            return new Suspend(() => {
                var busy = Ops.Truthy(CppApi.Call(program, imu, "is_calibrating"));
                if (!busy) ResetOdomBase();
                return !busy;
            }, true);
        }

        private object Wait(Func<bool> done) {
            StartOdom();
            return done() ? PyNone.Value : new Suspend(done);
        }

        // pid_wait_until(target): a drive's distance from where it began, a turn or swing's heading.
        private object WaitUntil(double target) {
            var m = current;
            if (m == null) return PyNone.Value;
            return Wait(() => current != m || m.exited || m.stuck || m.Reached(target));
        }

        // pid_wait_quick (chain 0) / pid_wait_quick_chain: the target pushed on by the chain constant, so the robot
        // doesn't slow down, and the code goes on once it passes the target asked for.
        private object WaitUntilTarget(EzMotion m, double chain) {
            var target = m.Target;
            if (chain > 0) m.Extend(chain);
            return Wait(() => current != m || m.exited || m.stuck || m.Reached(target));
        }

        // ------------------------------------------------------------------ sensors and odometry

        private double SideIn(VexMotor[] ms) {
            var deg = ms.Average(m => m.Position());
            var free = ms[0].Control?.FreeRpm ?? 200f;
            return deg * wheelRpm / free / 360.0 * Math.PI * wheelDiameter;
        }

        internal double LeftIn => SideIn(left);
        internal double RightIn => SideIn(right);
        private bool HasImu => imu != null && imu.Installed;

        /// <summary>Degrees clockwise, continuous (the IMU's rotation, else the odometry's heading).</summary>
        internal double Rotation() {
            StartOdom();
            return HasImu ? imu.Rotation() : theta;
        }

        private void SetImu(double value) {
            StartOdom();
            if (HasImu) {
                imu.SetRotation(value);
                lastImu = imu.Rotation();
            }
            else theta = value;
        }

        private void ResetSensors() {
            foreach (var m in left.Concat(right)) m.SetPosition(0);
            foreach (var t in new[] { trackLeft, trackRight, trackFront, trackBack }) t?.Reset();
            ResetOdomBase();
        }

        private void StartOdom() {
            if (odomStarted) return;
            odomStarted = true;
            clock = program.Time;
            ResetOdomBase();
        }

        private void ResetOdomBase() {
            lastL = LeftIn;
            lastR = RightIn;
            lastTL = trackLeft?.Distance() ?? 0;
            lastTR = trackRight?.Distance() ?? 0;
            lastTF = trackFront?.Distance() ?? 0;
            lastTB = trackBack?.Distance() ?? 0;
            lastImu = HasImu ? imu.Rotation() : 0;
        }

        private void SetPose(double px, double py, double pt) {
            StartOdom();
            x = px;
            y = py;
            theta = pt;
            ResetOdomBase();
        }

        private void Odometry() {
            // Forward travel from the vertical tracking wheels (or the drive sides), sideways from the horizontal ones.
            double dl, dr, ol, or;
            if (trackLeft != null || trackRight != null) {
                var l = trackLeft?.Distance() ?? 0;
                var r = trackRight?.Distance() ?? 0;
                dl = l - lastTL;
                dr = r - lastTR;
                lastTL = l;
                lastTR = r;
                ol = trackLeft?.offset ?? 0;
                or = trackRight?.offset ?? 0;
                if (trackLeft == null) { dl = dr; ol = -or; }
                if (trackRight == null) { dr = dl; or = -ol; }
            }
            else {
                var l = LeftIn;
                var r = RightIn;
                dl = l - lastL;
                dr = r - lastR;
                lastL = l;
                lastR = r;
                ol = or = trackWidth / 2;
            }
            double dTheta;   // radians, clockwise
            if (HasImu) {
                var rot = imu.Rotation();
                dTheta = (rot - lastImu) * Math.PI / 180;
                lastImu = rot;
            }
            else dTheta = ol + or > 0.1 ? (dl - dr) / (ol + or) : 0;
            var forward = (dl - ol * dTheta + dr + or * dTheta) / 2;
            if (trackLeft != null && trackRight == null) forward = dl - ol * dTheta;
            if (trackRight != null && trackLeft == null) forward = dr + or * dTheta;
            double side = 0;
            if (trackFront != null || trackBack != null) {
                var count = 0;
                if (trackFront != null) {
                    var f = trackFront.Distance();
                    side += f - lastTF - trackFront.offset * dTheta;
                    lastTF = f;
                    count++;
                }
                if (trackBack != null) {
                    var b = trackBack.Distance();
                    side += b - lastTB + trackBack.offset * dTheta;
                    lastTB = b;
                    count++;
                }
                side /= count;
            }
            var mid = theta * Math.PI / 180 + dTheta / 2;
            x += forward * Math.Sin(mid) + side * Math.Cos(mid);
            y += forward * Math.Cos(mid) - side * Math.Sin(mid);
            theta += dTheta * 180 / Math.PI;
        }

        // ------------------------------------------------------------------ each physics step

        private void Step() {
            var dt = program.Time - clock;
            clock = program.Time;
            if (odomStarted && dt > 0) Odometry();
            if (odomStarted && program.Time >= nextGraph) {
                nextGraph = (float)program.Time + 0.02f;
                TuningData.Get("odom x", "in", false).Add((float)program.Time, (float)x);
                TuningData.Get("odom y", "in", false).Add((float)program.Time, (float)y); PathTrail.Odom2 = new UnityEngine.Vector2((float)x, (float)y);   // V2: the path planner draws it
                TuningData.Get("odom theta", "deg", false).Add((float)program.Time, (float)theta);
                TuningData.Version++;
            }
            if (current == null || clock < nextTick) return;
            nextTick += Tick;
            if (nextTick < clock) nextTick = clock + Tick;
            current.Update();
        }

        /// <summary>Drive outputs, -127..127 (a share of 12 V); 0 on a side stops it with the drive's brake mode.</summary>
        internal void Output(double l, double r) {
            void Side(VexMotor[] ms, double v) {
                foreach (var m in ms) {
                    if (Math.Abs(v) < 0.5) m.Control?.StopMotor(brakeMode);
                    else m.Control?.Volts((float)(Math.Max(-127, Math.Min(127, v)) / 127.0 * m.Sign));
                }
            }
            Side(left, l);
            Side(right, r);
        }

        private void Stop() => Output(0, 0);

        private object Start(EzMotion m) {
            StartOdom();
            braking = false;
            current = m;
            nextTick = clock;
            return PyNone.Value;
        }

        // ------------------------------------------------------------------ driver control

        private double Axis(int n) {
            var c = program.Controller(false);
            return c.TryGetAttr("axis" + n, out var axis) && axis is VexObject o ? Ops.ToDouble(CppApi.Call(program, o, "value")) : 0;
        }

        // EZ-Template's joystick curve (its "red curve"), -127..127.
        private static double Curve(double x, double scale) {
            if (scale == 0) return x;
            return (Math.Exp(-scale / 10) + Math.Exp((Math.Abs(x) - 127) / 10) * (1 - Math.Exp(-scale / 10))) * x;
        }

        private void Sticks(double l, double r, bool tank) {
            current = null;   // the sticks take over
            StartOdom();
            if (Math.Abs(l) < joystickThreshold) l = 0;
            if (Math.Abs(r) < joystickThreshold) r = 0;
            if (tank) {
                l = Curve(l, curveLeft);
                r = Curve(r, curveLeft);
            }
            if (l == 0 && r == 0 && activeBrakeKp > 0) {
                // Active brake: hold where the sticks let go.
                if (!braking) {
                    braking = true;
                    brakeTargetL = LeftIn;
                    brakeTargetR = RightIn;
                }
                Output(activeBrakeKp * (brakeTargetL - LeftIn), activeBrakeKp * (brakeTargetR - RightIn));
                return;
            }
            braking = false;
            Output(l, r);
        }

        private void Arcade(double fwd, double turnIn) {
            if (Math.Abs(fwd) < joystickThreshold) fwd = 0;
            if (Math.Abs(turnIn) < joystickThreshold) turnIn = 0;
            fwd = Curve(fwd, curveLeft);
            turnIn = Curve(turnIn, curveRight);
            Sticks(fwd + turnIn, fwd - turnIn, false);
        }

        // ------------------------------------------------------------------ the motions

        private object Drive(double target, double speed, bool slew, bool headingOn) => Start(new DriveMotion(this, target, Math.Abs(speed), slew, headingOn));

        private object Turn(double target, double speed, int behavior, bool slew, bool _) {
            var now = Rotation();
            var aim = Aim(target, now, behavior);
            headingTarget = aim;   // drives after it keep this heading
            return Start(new TurnMotion(this, aim, Math.Abs(speed), slew));
        }

        private object Swing(Args a, bool relative = false) {
            var type = a.Need(0, "type") is PyEnum e && e.type == "e_swing" ? e.value : throw new PyError("TypeError", "pid_swing_set's first argument is ez::LEFT_SWING or ez::RIGHT_SWING");
            var target = a.Number(1, "target", 0) + (relative ? headingTarget : 0);
            var speed = Math.Abs(a.Number(2, "speed", 110));
            double opposite = 0;
            var behavior = swingBehavior;
            var slew = false;
            for (var i = 3; i < a.Count; i++) {
                if (a.pos[i] is bool b) slew = b;
                else if (a.pos[i] is PyEnum pe && pe.type == "e_angle_behavior") behavior = pe.value;
                else if (a.pos[i] is long || a.pos[i] is double) opposite = Ops.ToDouble(a.pos[i]);
            }
            var aim = Aim(target, Rotation(), behavior);
            headingTarget = aim;
            return Start(new SwingMotion(this, type == 0, aim, speed, opposite, slew));
        }

        // A heading target in the IMU's continuous frame, the way asked (raw: as given).
        private static double Aim(double target, double now, int behavior) {
            var e = MotionModule.Wrap(target - now);
            switch (behavior) {
                case 4: return now + e;   // shortest
                case 3: return now + (e > 0 ? e - 360 : e + 360);   // longest
                case 2: return now + (e < 0 ? e + 360 : e);   // right turn (clockwise)
                case 1: return now + (e > 0 ? e - 360 : e);   // left turn
                default: return target;
            }
        }

        // pid_odom_set(distance, speed, slew) / ({{x, y}, fwd, speed}, slew) / ({{x, y, theta}, fwd, speed}, slew) /
        // ({{{x, y}, fwd, speed}, {{x, y}, fwd, speed}, ...}, slew)
        private object Odom(Args a) {
            StartOdom();
            var first = a.Need(0, "target");
            if (first is long || first is double) {
                var d = Ops.ToDouble(first);
                var h = headingTarget * Math.PI / 180;
                var px = x + d * Math.Sin(h);
                var py = y + d * Math.Cos(h);
                var speed = Math.Abs(a.Number(1, "speed", 110));
                return Start(new PointMotion(this, new List<Waypoint> { new() { x = px, y = py, forwards = d >= 0, speed = speed } }, a.Bool(2, "slew_on", false)));
            }
            var items = Ops.Iterate(first).ToList();
            var slewOn = a.Bool(1, "slew_on", false);
            var points = new List<Waypoint>();
            if (items.Count > 0 && IsList(items[0]) && Ops.Iterate(items[0]).Any() && IsList(Ops.Iterate(items[0]).First())) {
                foreach (var it in items) points.Add(Point(it));   // a path
            }
            else points.Add(Point(first));
            if (points.Count == 0) throw new PyError("ValueError", "pid_odom_set needs a point like {{0_in, 24_in}, fwd, 110}");
            if (points.Count == 1 && points[0].theta.HasValue) return Start(new BoomerangMotion(this, points[0], slewOn));
            return Start(new PointMotion(this, points, slewOn));
        }

        private static bool IsList(object o) => o is PyList || o is PyTuple;

        private static Waypoint Point(object o) {
            var parts = Ops.Iterate(o).ToList();
            if (parts.Count == 0 || !IsList(parts[0])) throw new PyError("TypeError", "an odom point is {{x, y}, fwd, speed} or {{x, y, theta}, fwd, speed}");
            var xy = Ops.Iterate(parts[0]).Select(q => Ops.ToDouble(q)).ToArray();
            var w = new Waypoint { x = xy.ElementAtOrDefault(0), y = xy.ElementAtOrDefault(1), theta = xy.Length > 2 ? xy[2] : (double?)null, forwards = true, speed = 110 };
            foreach (var extra in parts.Skip(1)) {
                if (extra is PyEnum e && e.type == "drive_directions") w.forwards = e.value == 0;
                else if (extra is long || extra is double) w.speed = Math.Abs(Ops.ToDouble(extra));
            }
            return w;
        }

        internal sealed class Waypoint {
            public double x, y, speed;
            public double? theta;
            public bool forwards;
        }

        /// <summary>EZ-Template's PID (run every 10 ms): the integral only within start_i and reset when the error
        /// crosses zero.</summary>
        internal sealed class Pid {
            private readonly Gains g;
            private double integral, prev;
            private bool first = true;
            public double derivative;
            public Pid(Gains g) => this.g = g;

            public double Update(double error) {
                if (first) { prev = error; first = false; }
                derivative = error - prev;
                if (g.i != 0) {
                    if (Math.Abs(error) < g.startI) integral += error;
                    if (Math.Sign(error) != Math.Sign(prev)) integral = 0;
                }
                prev = error;
                return g.p * error + g.i * integral + g.d * derivative;
            }
        }

        /// <summary>EZ-Template's exit conditions: within the small error for its time, the big error for its, or not
        /// moving for the velocity time.</summary>
        internal sealed class Exit {
            private readonly ExitSet s;
            private readonly double still;
            private double smallSince = -1, bigSince = -1, stillSince = -1;
            public Exit(ExitSet s, double still) {
                this.s = s;
                this.still = still;
            }

            public bool Done(double error, double change, double now, ref bool stuck) {
                bool Timer(bool inside, ref double since, double ms) {
                    if (!inside) { since = -1; return false; }
                    if (since < 0) since = now;
                    return ms > 0 && now - since >= ms / 1000.0;
                }
                var small = Timer(Math.Abs(error) < s.smallError, ref smallSince, s.smallTime);
                var big = Timer(Math.Abs(error) < s.bigError, ref bigSince, s.bigTime);
                if (Timer(Math.Abs(change) < still, ref stillSince, s.velocityTime)) stuck = true;
                return small || big || stuck;
            }
        }

        internal abstract class EzMotion {
            public bool exited, stuck;
            public double speed;
            public int index;
            protected readonly EzDrive c;
            protected readonly double startTime;
            protected EzMotion(EzDrive c, double speed) {
                this.c = c;
                this.speed = speed;
                startTime = c.clock;
            }
            public abstract void Update();
            /// <summary>The target (distance from the start or heading) and whether a wait-until value is passed.</summary>
            public virtual double Target => 0;
            public virtual int Direction => 1;
            public virtual void Extend(double by) { }
            public abstract bool Reached(double target);
            protected double Now => c.clock;

            // Slew: from its minimum speed up to the full speed over its first distance.
            protected double Slewed(bool on, (double distance, double min) slew, double travelled) =>
                !on || slew.distance <= 0 || travelled >= slew.distance ? speed : Math.Min(speed, slew.min + (speed - slew.min) * travelled / slew.distance);
        }

        private sealed class DriveMotion : EzMotion {
            private readonly Pid leftPid, rightPid, headingPid;
            private readonly Exit leftExit, rightExit;
            private readonly double lStart, rStart, headingAim;
            private double target;
            private readonly bool slew, headingOn;
            private bool leftDone, rightDone;

            public DriveMotion(EzDrive c, double target, double speed, bool slew, bool headingOn) : base(c, speed) {
                this.target = target;
                this.slew = slew;
                this.headingOn = headingOn;
                lStart = c.LeftIn;
                rStart = c.RightIn;
                headingAim = c.headingTarget;
                var g = target >= 0 ? c.driveFwd : c.driveBack;
                leftPid = new Pid(g);
                rightPid = new Pid(g);
                headingPid = new Pid(c.heading);
                leftExit = new Exit(c.driveExit, 0.002 * c.ticksPerInch);
                rightExit = new Exit(c.driveExit, 0.002 * c.ticksPerInch);
            }

            public override double Target => target;
            public override int Direction => target >= 0 ? 1 : -1;
            public override void Extend(double by) => target += Math.Sign(target) * by;
            private double Travelled => ((c.LeftIn - lStart) + (c.RightIn - rStart)) / 2;
            public override bool Reached(double t) => t >= 0 ? Travelled >= t : Travelled <= t;

            public override void Update() {
                var le = (lStart + target - c.LeftIn) * c.ticksPerInch;
                var re = (rStart + target - c.RightIn) * c.ticksPerInch;
                var max = Slewed(slew, c.slewDrive, Math.Abs(Travelled));
                var l = Math.Max(-max, Math.Min(max, leftPid.Update(le)));
                var r = Math.Max(-max, Math.Min(max, rightPid.Update(re)));
                var gyro = headingOn ? headingPid.Update(headingAim - c.Rotation()) : 0;
                c.Output(l + gyro, r - gyro);
                if (!exited) {
                    leftDone |= leftExit.Done(le, leftPid.derivative, Now, ref stuck);
                    rightDone |= rightExit.Done(re, rightPid.derivative, Now, ref stuck);
                    exited = leftDone && rightDone;
                }
            }
        }

        private sealed class TurnMotion : EzMotion {
            private readonly Pid pid;
            private readonly Exit exit;
            private readonly double start;
            private double target;
            private readonly bool slew;

            public TurnMotion(EzDrive c, double target, double speed, bool slew) : base(c, speed) {
                this.target = target;
                this.slew = slew;
                start = c.Rotation();
                pid = new Pid(c.turn);
                exit = new Exit(c.turnExit, 0.02);
            }

            public override double Target => target;
            public override int Direction => target >= start ? 1 : -1;
            public override void Extend(double by) => target += Direction * by;
            public override bool Reached(double t) => Direction > 0 ? c.Rotation() >= Near(t) : c.Rotation() <= Near(t);
            // A heading asked for in a wait: the turn of it nearest this motion's target.
            private double Near(double t) => target + MotionModule.Wrap(t - target);

            public override void Update() {
                var error = target - c.Rotation();
                var max = Slewed(slew, c.slewTurn, Math.Abs(c.Rotation() - start));
                var output = Math.Max(-max, Math.Min(max, pid.Update(error)));
                c.Output(output, -output);
                if (!exited) exited = exit.Done(error, pid.derivative, Now, ref stuck);
            }
        }

        private sealed class SwingMotion : EzMotion {
            private readonly Pid pid;
            private readonly Exit exit;
            private readonly bool leftSide, slew;
            private readonly double start, opposite;
            private double target;

            public SwingMotion(EzDrive c, bool leftSide, double target, double speed, double opposite, bool slew) : base(c, speed) {
                this.leftSide = leftSide;
                this.target = target;
                this.opposite = opposite;
                this.slew = slew;
                start = c.Rotation();
                // A left swing turning clockwise drives the left side forward (forward constants), and so on.
                var forward = leftSide == target >= start;
                pid = new Pid(forward ? c.swingFwd : c.swingBack);
                exit = new Exit(c.swingExit, 0.02);
            }

            public override double Target => target;
            public override int Direction => target >= start ? 1 : -1;
            public override void Extend(double by) => target += Direction * by;
            public override bool Reached(double t) {
                var near = target + MotionModule.Wrap(t - target);
                return Direction > 0 ? c.Rotation() >= near : c.Rotation() <= near;
            }

            public override void Update() {
                var error = target - c.Rotation();
                var max = Slewed(slew, c.slewSwing, Math.Abs(c.Rotation() - start) * Math.PI / 180 * 6);
                var output = Math.Max(-max, Math.Min(max, pid.Update(error)));
                // The other side at its speed in proportion (0: it brakes, the robot pivots on it).
                var other = speed > 0 ? opposite * output / speed : 0;
                if (leftSide) c.Output(output, other);
                else c.Output(-other, -output);
                if (!exited) exited = exit.Done(error, pid.derivative, Now, ref stuck);
            }
        }

        // Point to point and pure pursuit: drive toward the point (or the path's point a look-ahead away), turning
        // to face it with the odom angular constants; turning gets its share first (odom_turn_bias).
        private sealed class PointMotion : EzMotion {
            private readonly List<Waypoint> path;
            private readonly Pid drivePid, turnPid;
            private readonly Exit exit;
            private readonly bool slew;
            private readonly double sx, sy;
            private double extend;

            public PointMotion(EzDrive c, List<Waypoint> path, bool slew) : base(c, path[0].speed) {
                this.path = path;
                this.slew = slew;
                sx = c.x;
                sy = c.y;
                drivePid = new Pid(path[path.Count - 1].forwards ? c.driveFwd : c.driveBack);
                turnPid = new Pid(c.odomAngular);
                exit = new Exit(c.odomDriveExit, 0.002);
            }

            private Waypoint End => path[path.Count - 1];
            private double ToEnd => Math.Sqrt((End.x - c.x) * (End.x - c.x) + (End.y - c.y) * (End.y - c.y));
            public override int Direction => End.forwards ? 1 : -1;
            public override double Target => 0;
            public override void Extend(double by) => extend = by;
            public override bool Reached(double t) {
                // pid_wait_until(distance): travelled that far; quick waits: within the chain distance of the end.
                if (t > 0) return Math.Sqrt((c.x - sx) * (c.x - sx) + (c.y - sy) * (c.y - sy)) >= t;
                return ToEnd <= Math.Max(1, extend);
            }

            public override void Update() {
                // The point to steer for: the next path point a look-ahead away, or the end.
                var (tx, ty) = (End.x, End.y);
                var wp = End;
                for (var i = index; i < path.Count - 1; i++) {
                    var d = Math.Sqrt((path[i].x - c.x) * (path[i].x - c.x) + (path[i].y - c.y) * (path[i].y - c.y));
                    if (d > c.lookAhead) {
                        (tx, ty) = (path[i].x, path[i].y);
                        wp = path[i];
                        break;
                    }
                    index = i + 1;
                }
                speed = wp.speed;
                var dx = tx - c.x;
                var dy = ty - c.y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var heading = c.theta + (wp.forwards ? 0 : 180);
                var angErr = MotionModule.Wrap(Math.Atan2(dx, dy) * 180 / Math.PI - heading);
                var final = wp == End;
                // Distance along the facing (negative once past), then pushed on for a chain.
                var along = (final ? ToEnd : dist) * Math.Cos(angErr * Math.PI / 180) + (final ? extend : 0);
                var max = Slewed(slew, c.slewDrive, Math.Sqrt((c.x - sx) * (c.x - sx) + (c.y - sy) * (c.y - sy)));
                var drive = final ? drivePid.Update(along) : max;
                drive = Math.Max(-max, Math.Min(max, drive));
                var turnOut = final && ToEnd < 3 ? 0 : turnPid.Update(angErr);
                turnOut = Math.Max(-speed * c.turnBias, Math.Min(speed * c.turnBias, turnOut));
                if (Math.Abs(drive) + Math.Abs(turnOut) > speed) drive = Math.Sign(drive) * Math.Max(0, speed - Math.Abs(turnOut));
                if (!wp.forwards) drive = -drive;
                c.Output(drive + turnOut, drive - turnOut);
                if (!exited && final && exit.Done(along, drivePid.derivative, Now, ref stuck)) {
                    exited = true;
                }
                if (exited) {
                    c.current = null;
                    c.Stop();
                }
            }
        }

        // Boomerang: steer for a carrot point behind the target along its heading (dlead x distance, at most the
        // boomerang distance), then the target itself once close, turning to its heading.
        private sealed class BoomerangMotion : EzMotion {
            private readonly Waypoint w;
            private readonly Pid drivePid, turnPid;
            private readonly Exit exit;
            private readonly bool slew;
            private readonly double sx, sy;
            private bool close;

            public BoomerangMotion(EzDrive c, Waypoint w, bool slew) : base(c, w.speed) {
                this.w = w;
                this.slew = slew;
                sx = c.x;
                sy = c.y;
                drivePid = new Pid(w.forwards ? c.driveFwd : c.driveBack);
                turnPid = new Pid(c.boomerang);
                exit = new Exit(c.odomDriveExit, 0.002);
            }

            private double ToEnd => Math.Sqrt((w.x - c.x) * (w.x - c.x) + (w.y - c.y) * (w.y - c.y));
            public override int Direction => w.forwards ? 1 : -1;
            public override bool Reached(double t) => t > 0 ? Math.Sqrt((c.x - sx) * (c.x - sx) + (c.y - sy) * (c.y - sy)) >= t : ToEnd < 1.5;

            public override void Update() {
                var dist = ToEnd;
                if (dist < 6) close = true;
                var face = w.theta.Value + (w.forwards ? 0 : 180);
                var rad = face * Math.PI / 180;
                var lead = Math.Min(c.dlead * dist, c.boomerangDistance);
                var cx = close ? w.x : w.x - Math.Sin(rad) * lead;
                var cy = close ? w.y : w.y - Math.Cos(rad) * lead;
                var heading = c.theta + (w.forwards ? 0 : 180);
                var toCarrot = Math.Atan2(cx - c.x, cy - c.y) * 180 / Math.PI;
                var angErr = close ? MotionModule.Wrap(face - heading) : MotionModule.Wrap(toCarrot - heading);
                var along = dist * Math.Cos(MotionModule.Wrap(Math.Atan2(w.x - c.x, w.y - c.y) * 180 / Math.PI - heading) * Math.PI / 180);
                var max = Slewed(slew, c.slewDrive, Math.Sqrt((c.x - sx) * (c.x - sx) + (c.y - sy) * (c.y - sy)));
                var drive = Math.Max(-max, Math.Min(max, drivePid.Update(close ? along : Math.Max(along, lead + along))));
                var turnOut = turnPid.Update(angErr);
                turnOut = Math.Max(-speed * c.turnBias, Math.Min(speed * c.turnBias, turnOut));
                if (Math.Abs(drive) + Math.Abs(turnOut) > speed) drive = Math.Sign(drive) * Math.Max(0, speed - Math.Abs(turnOut));
                if (!w.forwards) drive = -drive;
                c.Output(drive + turnOut, drive - turnOut);
                if (close && !exited && exit.Done(along, drivePid.derivative, Now, ref stuck) && Math.Abs(MotionModule.Wrap(face - heading)) < 5) exited = true;
                if (exited || stuck) {
                    exited = true;
                    c.current = null;
                    c.Stop();
                }
            }
        }
    }
}
