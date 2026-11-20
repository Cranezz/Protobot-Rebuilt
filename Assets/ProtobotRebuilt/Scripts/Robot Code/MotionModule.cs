using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Scripting;
using UnityEngine;
using static Protobot.RobotCode.VexModule;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9): the "motion" module: odometry and autonomous movement the way competitive teams write them with
    /// LemLib / EZ-Template (C++), in Python, with LemLib's names (snake_case, and camelCase too):
    ///   from motion import *
    ///   drivetrain = Drivetrain(left_drive, right_drive, 11.5, 3.25, 450, 2)      # track width, wheel, wheel rpm, drift
    ///   lateral = ControllerSettings(10, 0, 3, 3, 1, 100, 3, 500, 20)              # kP kI kD windup small/timeout large/timeout slew
    ///   angular = ControllerSettings(2, 0, 10, 3, 1, 100, 3, 500, 0)
    ///   sensors = OdomSensors(vertical, None, horizontal, None, imu)            # or OdomSensors(None, None, None, None, imu)
    ///   chassis = Chassis(drivetrain, lateral, angular, sensors)
    ///   chassis.calibrate(); chassis.set_pose(0, 0, 0)
    ///   chassis.move_to_point(0, 24, 2000); chassis.turn_to_heading(90, 1000); chassis.move_to_pose(24, 24, 90, 3000)
    /// Coordinates are inches; heading in degrees, clockwise from +Y (forward), as LemLib. Motions run in the
    /// background (as LemLib's): a call starts it and returns; the next motion waits for the one before; wait=True
    /// (or wait_until_done) waits. The controllers run every 10 ms like LemLib's, so its PID constants, exit
    /// conditions (small/large error range and timeout) and slew mean the same here. Odometry runs every physics
    /// step from tracking wheels (Rotation sensors or encoders), or the drive motors, and the Inertial sensor.
    /// The pose is on the tuning graph (odom x, y, theta).
    /// </summary>
    public static class MotionModule {
        public static PyModule Build(RobotProgram program) {
            var m = new PyModule("motion");
            void Class(string name, Func<Args, object> make) => m.globals[name] = new PyClass(name, null) { construct = (_, a) => make(a) };
            Class("Pose", a => new MotionPose(a.Number(0, "x", 0), a.Number(1, "y", 0), a.Number(2, "theta", 0)));
            Class("Drivetrain", a => new MotionDrivetrain(a));
            Class("TrackingWheel", a => new TrackingWheel(program, a));
            Class("OdomSensors", a => new OdomSensors(a));
            Class("ControllerSettings", a => new ControllerSettings(a));
            // V2: LemLib 0.4's names (Drivetrain_t {left, right, track, wheel, rpm}; ChassisController_t {kP, kD,
            // smallError, smallErrorTimeout, largeError, largeErrorTimeout, slew}; OdomSensors_t).
            Class("Drivetrain_t", a => new MotionDrivetrain(a) { v04 = true });
            Class("ChassisController_t", a => new ControllerSettings(a, true));
            Class("OdomSensors_t", a => new OdomSensors(a));
            Class("ExpoDriveCurve", a => new ExpoCurve(a));
            Class("Chassis", a => new MotionChassis(program, a));
            foreach (var (n, v) in new[] { ("AUTO", 0), ("CW_CLOCKWISE", 1), ("CCW_COUNTERCLOCKWISE", 2) }) m.globals[n] = new PyEnum("AngularDirection", n, v);
            var dirs = new PyClass("AngularDirection", null);
            foreach (var n in new[] { "AUTO", "CW_CLOCKWISE", "CCW_COUNTERCLOCKWISE" }) dirs.dict[n] = m.globals[n];
            m.globals["AngularDirection"] = dirs;
            var sides = new PyClass("DriveSide", null);
            sides.dict["LEFT"] = m.globals["DRIVE_LEFT"] = new PyEnum("DriveSide", "LEFT", 0);
            sides.dict["RIGHT"] = m.globals["DRIVE_RIGHT"] = new PyEnum("DriveSide", "RIGHT", 1);
            m.globals["DriveSide"] = sides;
            // VEX wheel sizes (LemLib's Omniwheel constants), inches.
            var omni = new PyClass("Omniwheel", null);
            foreach (var (n, d) in new[] { ("NEW_2", 2.125), ("NEW_275", 2.75), ("OLD_275", 2.75), ("NEW_275_HALF", 2.744), ("OLD_275_HALF", 2.74),
                                           ("NEW_325", 3.25), ("OLD_325", 3.25), ("NEW_325_HALF", 3.246), ("OLD_325_HALF", 3.246), ("NEW_4", 4.0), ("OLD_4", 4.18), ("NEW_4_HALF", 3.995), ("OLD_4_HALF", 4.175) })
                omni.dict[n] = d;
            m.globals["Omniwheel"] = omni;
            return m;
        }

        internal static double Arg(Args a, int i, string name, double fallback) => a.Has(i, name) && !(a.Get(i, name) is PyNone) ? a.Number(i, name, fallback) : fallback;

        /// <summary>Wraps an angle to -180..180.</summary>
        internal static double Wrap(double deg) {
            deg %= 360;
            if (deg > 180) deg -= 360;
            if (deg < -180) deg += 360;
            return deg;
        }
    }

    // ---------------------------------------------------------------------- what other libraries' devices give it

    /// <summary>Motors (a PROS MotorGroup's).</summary>
    internal interface IMotorSet { VexMotor[] Motors { get; } }

    /// <summary>A rotation sensor or encoder's degrees (PROS's Rotation, ADI encoder).</summary>
    internal interface IAngleSource { double Degrees(); }

    /// <summary>An Inertial sensor (PROS's Imu).</summary>
    internal interface IInertialSource { VexInertial Inertial { get; } }

    // ---------------------------------------------------------------------- settings objects

    public sealed class MotionPose : VexObject {
        public readonly double x, y, theta;
        public override string TypeName => "Pose";

        public MotionPose(double x, double y, double theta) {
            this.x = x;
            this.y = y;
            this.theta = theta;
            Field("x", x);
            Field("y", y);
            Field("theta", theta);
            Method("distance", a => {
                var o = a.Need(0, "other") as MotionPose ?? throw new PyError("TypeError", "distance() takes a Pose");
                return Math.Sqrt((o.x - x) * (o.x - x) + (o.y - y) * (o.y - y));
            });
            Method("angle", a => {
                var o = a.Need(0, "other") as MotionPose ?? throw new PyError("TypeError", "angle() takes a Pose");
                return Math.Atan2(o.x - x, o.y - y) * 180 / Math.PI;
            });
            Method("__repr__", _ => $"Pose(x={x.ToString("0.##", Ops.Inv)}, y={y.ToString("0.##", Ops.Inv)}, theta={theta.ToString("0.##", Ops.Inv)})");
            Method("__str__", _ => $"Pose(x={x.ToString("0.##", Ops.Inv)}, y={y.ToString("0.##", Ops.Inv)}, theta={theta.ToString("0.##", Ops.Inv)})");
        }
    }

    internal sealed class MotionDrivetrain : VexObject {
        public override string TypeName => "Drivetrain";
        public readonly VexMotor[] left, right;
        public readonly double trackWidth, wheelDiameter, rpm, horizontalDrift;
        public bool v04;   // V2: LemLib 0.4's Drivetrain_t

        public MotionDrivetrain(Args a) {
            left = Side(a.Need(0, "left_motors"), "left");
            right = Side(a.Need(1, "right_motors"), "right");
            trackWidth = a.Number(2, "track_width", 12);
            wheelDiameter = a.Number(3, "wheel_diameter", 3.25);
            rpm = a.Number(4, "rpm", 450);
            horizontalDrift = MotionModule.Arg(a, 5, "horizontal_drift", 2);
            if (trackWidth <= 0 || wheelDiameter <= 0 || rpm <= 0) throw new PyError("ValueError", "Drivetrain needs a track width, wheel diameter and rpm above 0");
        }

        internal static VexMotor[] Side(object o, string what) => o switch {
            VexMotor m => new[] { m },
            VexMotorGroup g => g.motors,
            IMotorSet s => s.Motors,   // PROS motor groups
            _ => throw new PyError("TypeError", "the drivetrain's " + what + " side is a Motor or MotorGroup, not " + Ops.Describe(o)),
        };
    }

    internal sealed class TrackingWheel : VexObject {
        public override string TypeName => "TrackingWheel";
        private readonly RobotProgram program;
        private readonly object sensor;
        public readonly double diameter, offset, ratio;
        private readonly object positionFn;
        private readonly IAngleSource angle;
        private readonly VexMotor[] motors;
        private readonly double motorRpm;

        public TrackingWheel(RobotProgram p, Args a) {
            program = p;
            sensor = a.Need(0, "sensor");
            diameter = a.Number(1, "wheel_diameter", 2.75);
            offset = a.Number(2, "distance", 0);
            ratio = MotionModule.Arg(a, 3, "gear_ratio", 1);
            // A Rotation sensor or encoder (degrees), or drive motors (their degrees times the wheel's rpm over theirs).
            if (sensor is VexMotorGroup g) motors = g.motors;
            else if (sensor is VexMotor m) motors = new[] { m };
            else if (sensor is IMotorSet ms) motors = ms.Motors;
            else if (sensor is IAngleSource src) angle = src;
            else if (sensor is VexObject o && o.TryGetAttr("position", out var f)) positionFn = f;
            else throw new PyError("TypeError", "a TrackingWheel takes a Rotation sensor, an Encoder or a Motor/MotorGroup");
            motorRpm = MotionModule.Arg(a, 4, "rpm", 0);
        }

        /// <summary>Inches the wheel has rolled.</summary>
        public double Distance() {
            double deg;
            if (motors != null) {
                deg = motors.Average(m => m.Position());
                var free = motors[0].Control?.FreeRpm ?? 200f;
                if (motorRpm > 0) deg *= motorRpm / free;
            }
            else if (angle != null) deg = angle.Degrees();
            else deg = Ops.ToDouble(program.vm.CallSync(positionFn, new object[] { Units["DEGREES"] }));
            return deg / 360.0 * Math.PI * diameter * ratio;
        }
    }

    internal sealed class OdomSensors : VexObject {
        public override string TypeName => "OdomSensors";
        public readonly TrackingWheel vertical1, vertical2, horizontal1, horizontal2;
        public readonly VexInertial imu;

        public OdomSensors(Args a) {
            vertical1 = Wheel(a.Get(0, "vertical1"));
            vertical2 = Wheel(a.Get(1, "vertical2"));
            horizontal1 = Wheel(a.Get(2, "horizontal1"));
            horizontal2 = Wheel(a.Get(3, "horizontal2"));
            var imuArg = a.Get(4, "imu");
            imu = imuArg == null || imuArg is PyNone ? null : imuArg as VexInertial ?? (imuArg as IInertialSource)?.Inertial
                  ?? throw new PyError("TypeError", "OdomSensors' fifth argument is an Inertial sensor (or None)");
        }

        private static TrackingWheel Wheel(object o) => o == null || o is PyNone ? null : o as TrackingWheel ?? throw new PyError("TypeError", "OdomSensors takes TrackingWheels (or None), not " + Ops.Describe(o));
    }

    internal sealed class ControllerSettings : VexObject {
        public override string TypeName => "ControllerSettings";
        public readonly double kP, kI, kD, windup, smallError, smallTimeout, largeError, largeTimeout, slew;
        /// <summary>V2: LemLib before 0.5's final release: 7 numbers (kP, kD, smallError, smallErrorTimeout, largeError,
        /// largeErrorTimeout, slew); 0.4 named it ChassisController_t.</summary>
        public readonly bool sevenNumbers, v04;

        public ControllerSettings(Args a, bool v04 = false) {
            this.v04 = v04;
            if (v04 || a.Count == 7 && (a.kw == null || a.kw.Count == 0)) {
                sevenNumbers = true;
                kP = a.Number(0, "kP", 0);
                kD = MotionModule.Arg(a, 1, "kD", 0);
                smallError = MotionModule.Arg(a, 2, "smallError", 0);
                smallTimeout = MotionModule.Arg(a, 3, "smallErrorTimeout", 0);
                largeError = MotionModule.Arg(a, 4, "largeError", 0);
                largeTimeout = MotionModule.Arg(a, 5, "largeErrorTimeout", 0);
                slew = MotionModule.Arg(a, 6, "slew", 0);
                foreach (var (n, v) in new[] { ("kP", kP), ("kI", kI), ("kD", kD) }) Field(n, v);
                return;
            }
            kP = a.Number(0, "kP", 0);
            kI = MotionModule.Arg(a, 1, "kI", 0);
            kD = MotionModule.Arg(a, 2, "kD", 0);
            windup = MotionModule.Arg(a, 3, "anti_windup", 0);
            smallError = MotionModule.Arg(a, 4, "small_error", 0);
            smallTimeout = MotionModule.Arg(a, 5, "small_error_timeout", 0);
            largeError = MotionModule.Arg(a, 6, "large_error", 0);
            largeTimeout = MotionModule.Arg(a, 7, "large_error_timeout", 0);
            slew = MotionModule.Arg(a, 8, "slew", 0);
            foreach (var (n, v) in new[] { ("kP", kP), ("kI", kI), ("kD", kD) }) Field(n, v);
        }
    }

    internal sealed class ExpoCurve : VexObject {
        public override string TypeName => "ExpoDriveCurve";
        private readonly double deadband, minOutput, curve;

        public ExpoCurve(Args a) {
            deadband = MotionModule.Arg(a, 0, "deadband", 0);
            minOutput = MotionModule.Arg(a, 1, "min_output", 0);
            curve = MotionModule.Arg(a, 2, "curve", 1);
            Method("curve", a2 => Apply(a2.Number(0, "input", 0)));
        }

        /// <summary>LemLib's expo curve (-127..127 in and out).</summary>
        public double Apply(double input) {
            if (Math.Abs(input) <= deadband) return 0;
            if (curve <= 1.0001) return Math.Sign(input) * (minOutput + (127 - minOutput) * (Math.Abs(input) - deadband) / Math.Max(1, 127 - deadband));
            var g = Math.Abs(input) - deadband;
            var g127 = 127 - deadband;
            var i = Math.Pow(curve, g - 127) * g;
            var i127 = Math.Pow(curve, g127 - 127) * g127;
            return Math.Sign(input) * ((127.0 - minOutput) / 127 * i * 127 / i127 + minOutput);
        }
    }

    // ---------------------------------------------------------------------- the chassis

    internal sealed class MotionChassis : VexObject {
        public override string TypeName => "Chassis";
        private readonly RobotProgram program;
        private readonly MotionDrivetrain drive;
        private readonly ControllerSettings lateral, angular;
        private readonly OdomSensors sensors;
        private readonly ExpoCurve throttleCurve, steerCurve;
        private readonly string legacy;   // V2: "0.4", "0.5-rc" (LemLib before 0.5.0) or null

        // Odometry.
        private double x, y, theta;   // inches, degrees clockwise from +Y
        private double lastV1, lastV2, lastH1, lastH2, lastLeft, lastRight, lastImu;
        private bool odomStarted;
        private float nextGraph;

        // The motion under way (null: none), and the ones waiting.
        private Motion current;
        private string brakeMode = "COAST";
        private double clock, nextTick;
        private const double Tick = 0.01;   // LemLib's loop: every 10 ms

        public MotionChassis(RobotProgram p, Args a) {
            program = p;
            drive = a.Need(0, "drivetrain") as MotionDrivetrain ?? throw new PyError("TypeError", "Chassis' first argument is a Drivetrain(...)");
            lateral = a.Need(1, "linear_settings") as ControllerSettings ?? throw new PyError("TypeError", "Chassis' second argument is the lateral ControllerSettings(...)");
            angular = a.Need(2, "angular_settings") as ControllerSettings ?? throw new PyError("TypeError", "Chassis' third argument is the angular ControllerSettings(...)");
            sensors = a.Get(3, "sensors") as OdomSensors ?? new OdomSensors(new Args(a.vm, "OdomSensors", new object[0], null));
            // V2: which LemLib the program was written for (their turnTo/moveTo/follow take different arguments).
            legacy = drive.v04 || lateral.v04 ? "0.4" : lateral.sevenNumbers ? "0.5-rc" : null;
            throttleCurve = a.Get(4, "throttle_curve") as ExpoCurve;
            steerCurve = a.Get(5, "steer_curve") as ExpoCurve;
            p.pollers.Add(Step);

            Method("calibrate", a2 => {
                StartOdom();
                if (sensors.imu == null) return PyNone.Value;
                // The Inertial sensor calibrates (about 2 s, as the real one): wait for it.
                if (sensors.imu.TryGetAttr("calibrate", out var cal)) program.vm.CallSync(cal, new object[0]);
                return new Suspend(() => {
                    var busy = sensors.imu.TryGetAttr("is_calibrating", out var f) && Ops.Truthy(program.vm.CallSync(f, new object[0]));
                    if (!busy) ResetOdomBase();
                    return !busy;
                });
            });
            Method("set_pose", a2 => {
                if (a2.Get(0, "x") is MotionPose pose) SetPose(pose.x, pose.y, pose.theta, a2.Bool(1, "radians", false));
                else SetPose(a2.Number(0, "x", 0), a2.Number(1, "y", 0), a2.Number(2, "theta", 0), a2.Bool(3, "radians", false));
                return PyNone.Value;
            });
            Method("get_pose", a2 => {
                StartOdom();
                var radians = a2.Bool(0, "radians", false);
                return new MotionPose(x, y, radians ? theta * Math.PI / 180 : theta);
            });
            Method("turn_to_heading", a2 => Queue(a2, 2, () => new TurnMotion(this, a2.Number(0, "theta", 0), Ms(a2, 1), Dir(a2), Opt(a2, "max_speed", 127), Opt(a2, "min_speed", 0), Opt(a2, "early_exit_range", 0), null, false)));
            Method("turn_to_point", a2 => Queue(a2, 3, () => new TurnMotion(this, 0, Ms(a2, 2), Dir(a2), Opt(a2, "max_speed", 127), Opt(a2, "min_speed", 0), Opt(a2, "early_exit_range", 0),
                                                                         (a2.Number(0, "x", 0), a2.Number(1, "y", 0)), !Forwards(a2))));
            Method("swing_to_heading", a2 => Queue(a2, 3, () => new SwingMotion(this, a2.Number(0, "theta", 0), Side(a2.Need(1, "locked_side")), Ms(a2, 2), Dir(a2), Opt(a2, "max_speed", 127), Opt(a2, "min_speed", 0), Opt(a2, "early_exit_range", 0), null)));
            Method("swing_to_point", a2 => Queue(a2, 4, () => new SwingMotion(this, 0, Side(a2.Need(2, "locked_side")), Ms(a2, 3), Dir(a2), Opt(a2, "max_speed", 127), Opt(a2, "min_speed", 0), Opt(a2, "early_exit_range", 0),
                                                                          (a2.Number(0, "x", 0), a2.Number(1, "y", 0)))));
            Method("move_to_point", a2 => Queue(a2, 3, () => new PointMotion(this, a2.Number(0, "x", 0), a2.Number(1, "y", 0), Ms(a2, 2), Forwards(a2),
                                                                         Opt(a2, "max_speed", 127), Opt(a2, "min_speed", 0), Opt(a2, "early_exit_range", 0))));
            Method("move_to_pose", a2 => Queue(a2, 4, () => new PoseMotion(this, a2.Number(0, "x", 0), a2.Number(1, "y", 0), a2.Number(2, "theta", 0), Ms(a2, 3), Forwards(a2),
                                                                        Opt(a2, "lead", 0.6), Opt(a2, "horizontal_drift", 0), Opt(a2, "max_speed", 127), Opt(a2, "min_speed", 0), Opt(a2, "early_exit_range", 0))));
            Method("follow", a2 => {
                var path = a2.Need(0, "path");
                // V2: LemLib 0.4: follow("file.txt", timeout, lookahead, reverse, maxSpeed), waits; the file was on the SD card.
                if (path is string file) {
                    var pts = PathFile(file);
                    if (pts == null) return PyNone.Value;
                    return Queue(a2, 99, () => new PursuitMotion(this, pts, a2.Number(2, "lookahead", 15), Ms(a2, 1), !a2.Bool(3, "reverse", false), MotionModule.Arg(a2, 4, "maxSpeed", 127)), true);
                }
                if (path is PathAsset asset) {
                    var pts = asset.Points(program);
                    if (pts == null) return PyNone.Value;
                    return Queue(a2, 4, () => new PursuitMotion(this, pts, a2.Number(1, "lookahead", 15), Ms(a2, 2), a2.Bool(3, "forwards", true), Opt(a2, "max_speed", 127)));
                }
                return Queue(a2, 4, () => new PursuitMotion(this, Path(path), a2.Number(1, "lookahead", 15), Ms(a2, 2), a2.Bool(3, "forwards", true), Opt(a2, "max_speed", 127)));
            });
            // V2: LemLib 0.4 / 0.5-rc: turnTo(x, y, timeout, reversed (0.4) or forwards (0.5-rc), maxSpeed, async);
            // moveTo(x, y, timeout, maxSpeed) in 0.4 (waits), moveTo(x, y, theta, timeout, forwards, chasePower, lead,
            // maxSpeed, async) in 0.5-rc (boomerang); moveToOld = 0.4's moveTo.
            Method("turnTo", a2 => {
                var old = legacy == "0.4";
                var away = old ? a2.Bool(3, "reversed", false) : !a2.Bool(3, "forwards", true);
                return Queue(a2, old ? 99 : 5, () => new TurnMotion(this, 0, Ms(a2, 2), 0, MotionModule.Arg(a2, 4, "maxSpeed", 127), 0, 0, (a2.Number(0, "x", 0), a2.Number(1, "y", 0)), away), old);
            });
            Method("moveTo", a2 => {
                if (legacy == "0.4")
                    return Queue(a2, 99, () => new PointMotion(this, a2.Number(0, "x", 0), a2.Number(1, "y", 0), Ms(a2, 2), true, Math.Min(127, MotionModule.Arg(a2, 3, "maxSpeed", 127)), 0, 0), true);
                return Queue(a2, 8, () => new PoseMotion(this, a2.Number(0, "x", 0), a2.Number(1, "y", 0), a2.Number(2, "theta", 0), Ms(a2, 3), a2.Bool(4, "forwards", true),
                                                         MotionModule.Arg(a2, 6, "lead", 0.6), MotionModule.Arg(a2, 5, "chasePower", 0), Math.Min(127, MotionModule.Arg(a2, 7, "maxSpeed", 127)), 0, 0));
            });
            Method("moveToOld", a2 => {
                // (x, y, timeout, forwards, async = false, maxSpeed)
                var wait = !(a2.Count > 4 && a2.pos[4] is bool b && b);
                return Queue(a2, 99, () => new PointMotion(this, a2.Number(0, "x", 0), a2.Number(1, "y", 0), Ms(a2, 2), a2.Bool(3, "forwards", true), Math.Min(127, MotionModule.Arg(a2, 5, "maxSpeed", 127)), 0, 0), wait);
            });
            Method("wait_until", a2 => {
                var dist = a2.Number(0, "dist", 0);
                var motion = current;
                return new Suspend(() => motion == null || motion.finished || motion.travelled >= dist);
            });
            Method("wait_until_done", _ => new Suspend(() => current == null));
            Method("cancel_motion", _ => {
                if (current != null) Finish(true);
                return PyNone.Value;
            });
            Method("cancel_all_motions", _ => {
                if (current != null) Finish(true);
                return PyNone.Value;
            });
            Method("is_in_motion", _ => current != null);
            Method("set_brake_mode", a2 => {
                brakeMode = UnitName(a2.Need(0, "mode"), "COAST");
                return PyNone.Value;
            });
            Method("tank", a2 => {
                var raw = a2.Bool(2, "disable_drive_curve", false);
                var l = raw || throttleCurve == null ? a2.Number(0, "left", 0) : throttleCurve.Apply(a2.Number(0, "left", 0));
                var r = raw || throttleCurve == null ? a2.Number(1, "right", 0) : throttleCurve.Apply(a2.Number(1, "right", 0));
                Driver(l, r);
                return PyNone.Value;
            });
            Method("arcade", a2 => {
                var raw = a2.Bool(2, "disable_drive_curve", false);
                var t = raw || throttleCurve == null ? a2.Number(0, "throttle", 0) : throttleCurve.Apply(a2.Number(0, "throttle", 0));
                var s = raw || steerCurve == null ? a2.Number(1, "turn", 0) : steerCurve.Apply(a2.Number(1, "turn", 0));
                var bias = MotionModule.Arg(a2, 3, "desaturate_bias", 0.5);
                // LemLib's desaturation: over 127, the throttle and turn give up their shares by the bias.
                var sum = Math.Abs(t) + Math.Abs(s);
                if (sum > 127) {
                    var tShare = Math.Abs(t) / sum;
                    t = Math.Sign(t) * Math.Min(Math.Abs(t), 127 * (bias + (1 - bias) * tShare));
                    s = Math.Sign(s) * (127 - Math.Abs(t));
                }
                Driver(t + s, t - s);
                return PyNone.Value;
            });
            Method("curvature", a2 => {
                var raw = a2.Bool(2, "disable_drive_curve", false);
                var t = raw || throttleCurve == null ? a2.Number(0, "throttle", 0) : throttleCurve.Apply(a2.Number(0, "throttle", 0));
                var c = raw || steerCurve == null ? a2.Number(1, "turn", 0) : steerCurve.Apply(a2.Number(1, "turn", 0));
                if (Math.Abs(t) < 1) {   // turning on the spot when not driving
                    Driver(c, -c);
                    return PyNone.Value;
                }
                var l = t + Math.Abs(t) * c / 127;
                var r = t - Math.Abs(t) * c / 127;
                var most = Math.Max(Math.Abs(l), Math.Abs(r)) / 127;
                if (most > 1) { l /= most; r /= most; }
                Driver(l, r);
                return PyNone.Value;
            });
            // LemLib's own (camelCase) names too, so code moved from C++ reads the same.
            foreach (var (camel, snake) in new[] { ("setPose", "set_pose"), ("getPose", "get_pose"), ("turnToHeading", "turn_to_heading"), ("turnToPoint", "turn_to_point"),
                                                   ("swingToHeading", "swing_to_heading"), ("swingToPoint", "swing_to_point"), ("moveToPoint", "move_to_point"), ("moveToPose", "move_to_pose"),
                                                   ("waitUntil", "wait_until"), ("waitUntilDone", "wait_until_done"), ("cancelMotion", "cancel_motion"), ("cancelAllMotions", "cancel_all_motions"),
                                                   ("isInMotion", "is_in_motion"), ("setBrakeMode", "set_brake_mode") })
                if (TryGetAttr(snake, out var f)) Field(camel, f);
        }

        // ------------------------------------------------------------------ arguments

        private static double Ms(Args a, int i) => Math.Max(0, a.Number(i, "timeout", 0)) / 1000.0;
        // An option by its Python name (max_speed) or LemLib's C++ one (maxSpeed).
        private static object Kw(Args a, string name) {
            if (a.kw == null) return null;
            if (a.kw.TryGetValue(name, out var v)) return v;
            return a.kw.TryGetValue(Camel(name), out v) ? v : null;
        }

        private static string Camel(string snake) {
            var parts = snake.Split('_');
            return parts[0] + string.Concat(parts.Skip(1).Select(x => x.Length > 0 ? char.ToUpperInvariant(x[0]) + x.Substring(1) : ""));
        }

        private static double Opt(Args a, string name, double fallback) => Kw(a, name) is object v && !(v is PyNone) ? Ops.ToDouble(v) : fallback;
        private static bool Forwards(Args a) => !(Kw(a, "forwards") is object v) || Ops.Truthy(v);

        private static int Dir(Args a) => Kw(a, "direction") is PyEnum e ? e.value : 0;

        private static bool Side(object o) => o is PyEnum e && e.type == "DriveSide" ? e.value == 1 : throw new PyError("TypeError", "the locked side is DriveSide.LEFT or DriveSide.RIGHT");

        // V2: a path file of the project (LemLib 0.4 read it from the SD card). Null (warned): not there.
        private List<(double x, double y, double speed)> PathFile(string name) {
            var text = CodeProject.Find(program.files, name);
            if (text == null) {
                program.Warn("path:" + name, "follow(\"" + name + "\"): that path file isn't in the project (LemLib 0.4 reads it from the SD card). Add it in the code window: Project > Add path files. This follow() was skipped");
                return null;
            }
            var pts = CodeProject.ParsePath(text);
            if (pts.Count < 2) {
                program.Warn("path:" + name, "follow(\"" + name + "\"): no points in that file (path.jerryio's \"x, y, speed\" lines); skipped");
                return null;
            }
            return pts;
        }

        private static List<(double x, double y, double speed)> Path(object o) {
            var points = new List<(double, double, double)>();
            foreach (var p in Ops.Iterate(o)) {
                if (p is MotionPose pose) points.Add((pose.x, pose.y, double.NaN));
                else {
                    var xy = Ops.Iterate(p).Take(2).Select(v => Ops.ToDouble(v)).ToArray();
                    if (xy.Length < 2) throw new PyError("TypeError", "a path is a list of points like [(0, 0), (0, 24), (24, 36)]");
                    points.Add((xy[0], xy[1], double.NaN));
                }
            }
            if (points.Count < 2) throw new PyError("ValueError", "a path needs at least 2 points");
            return points;
        }

        // A motion call: waits for the one under way to end, starts, then (wait=True) waits for it too.
        private object Queue(Args a, int asyncIndex, Func<Motion> make, bool alwaysWait = false) {
            StartOdom();
            // wait=True (Python), or LemLib's async argument false (C++: chassis.moveToPoint(0, 24, 2000, {}, false)).
            var wait = alwaysWait || a.kw != null && a.kw.TryGetValue("wait", out var w) && Ops.Truthy(w)
                       || a.Count > asyncIndex && a.pos[asyncIndex] is bool async && !async
                       || a.kw != null && a.kw.TryGetValue("async_", out var ak) && !Ops.Truthy(ak);
            Motion mine = null;
            var started = false;
            bool Try() {
                if (!started) {
                    if (current != null) return false;
                    mine = make();
                    current = mine;
                    started = true;
                    nextTick = clock;   // its first update at once
                }
                return !wait || mine.finished;
            }
            if (Try()) return PyNone.Value;
            return new Suspend(Try);
        }

        // ------------------------------------------------------------------ odometry

        private void StartOdom() {
            if (odomStarted) return;
            odomStarted = true;
            ResetOdomBase();
        }

        private void ResetOdomBase() {
            lastV1 = sensors.vertical1?.Distance() ?? 0;
            lastV2 = sensors.vertical2?.Distance() ?? 0;
            lastH1 = sensors.horizontal1?.Distance() ?? 0;
            lastH2 = sensors.horizontal2?.Distance() ?? 0;
            lastLeft = SideDistance(drive.left);
            lastRight = SideDistance(drive.right);
            lastImu = sensors.imu?.Rotation() ?? 0;
        }

        private void SetPose(double px, double py, double ptheta, bool radians) {
            StartOdom();
            x = px;
            y = py;
            theta = radians ? ptheta * 180 / Math.PI : ptheta;
            ResetOdomBase();
        }

        // Inches a drive side has rolled (its motors' degrees at the wheels' rpm).
        private double SideDistance(VexMotor[] motors) {
            if (motors.Length == 0) return 0;
            var deg = motors.Average(m => m.Position());
            var free = motors[0].Control?.FreeRpm ?? 200f;
            return deg * drive.rpm / free / 360.0 * Math.PI * drive.wheelDiameter;
        }

        private void Odometry() {
            // Forward travel and turn from two wheels a known distance apart (tracking wheels, else the drive sides).
            double d1, d2, o1, o2;
            if (sensors.vertical1 != null) {
                var v1 = sensors.vertical1.Distance();
                d1 = v1 - lastV1;
                lastV1 = v1;
                o1 = sensors.vertical1.offset;
                if (sensors.vertical2 != null) {
                    var v2 = sensors.vertical2.Distance();
                    d2 = v2 - lastV2;
                    lastV2 = v2;
                    o2 = sensors.vertical2.offset;
                }
                else { d2 = d1; o2 = o1 + 1; }
            }
            else {
                var l = SideDistance(drive.left);
                var r = SideDistance(drive.right);
                d1 = l - lastLeft;
                d2 = r - lastRight;
                lastLeft = l;
                lastRight = r;
                o1 = -drive.trackWidth / 2;
                o2 = drive.trackWidth / 2;
            }
            double dTheta;   // radians, clockwise
            if (sensors.imu != null) {
                var rot = sensors.imu.Rotation();
                dTheta = (rot - lastImu) * Math.PI / 180;
                lastImu = rot;
            }
            else if (sensors.vertical2 != null || sensors.vertical1 == null) dTheta = (d1 - d2) / (o2 - o1);
            else dTheta = 0;
            // A wheel at a side offset also rolls when the robot turns: take that out to get the middle's travel.
            var forward = sensors.vertical1 != null && sensors.vertical2 == null ? d1 + o1 * dTheta : (d1 + o1 * dTheta + d2 + o2 * dTheta) / 2;
            double sideways = 0;
            if (sensors.horizontal1 != null) {
                var h1 = sensors.horizontal1.Distance();
                var dh = h1 - lastH1;
                lastH1 = h1;
                sideways = dh - sensors.horizontal1.offset * dTheta;
                if (sensors.horizontal2 != null) {
                    var h2 = sensors.horizontal2.Distance();
                    var dh2 = h2 - lastH2;
                    lastH2 = h2;
                    sideways = (sideways + dh2 - sensors.horizontal2.offset * dTheta) / 2;
                }
            }
            var mid = (theta * Math.PI / 180) + dTheta / 2;
            x += forward * Math.Sin(mid) + sideways * Math.Cos(mid);
            y += forward * Math.Cos(mid) - sideways * Math.Sin(mid);
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
                TuningData.Get("odom y", "in", false).Add((float)program.Time, (float)y);
                TuningData.Get("odom theta", "deg", false).Add((float)program.Time, (float)theta);
                TuningData.Version++;
            }
            if (current == null || clock < nextTick) return;
            nextTick += Tick;
            if (nextTick < clock) nextTick = clock + Tick;
            if (current.timeout > 0 && clock - current.start >= current.timeout) {
                Finish(true);
                return;
            }
            current.Update();
            if (current.finished) Finish(current.stopAtEnd);
        }

        private void Finish(bool stop) {
            current.finished = true;
            current = null;
            if (stop) StopDrive();
        }

        internal Pose Now => new() { x = x, y = y, theta = theta };

        internal struct Pose {
            public double x, y, theta;
        }

        /// <summary>Drive outputs, -127..127 (LemLib's units: a share of the battery's 12 V).</summary>
        internal void Output(double left, double right) {
            foreach (var m in drive.left) m.Control?.Volts((float)(Math.Max(-127, Math.Min(127, left)) / 127.0 * m.Sign));
            foreach (var m in drive.right) m.Control?.Volts((float)(Math.Max(-127, Math.Min(127, right)) / 127.0 * m.Sign));
        }

        private void Driver(double left, double right) {
            if (current != null) Finish(false);   // the sticks take over
            Output(left, right);
        }

        internal void StopDrive() {
            foreach (var m in drive.left.Concat(drive.right)) m.Control?.StopMotor(brakeMode);
        }

        internal ControllerSettings Lateral => lateral;
        internal ControllerSettings Angular => angular;
        internal double TrackWidth => drive.trackWidth;
        internal double HorizontalDrift => drive.horizontalDrift;

        // ------------------------------------------------------------------ the controllers

        /// <summary>LemLib's PID, run every 10 ms: integral and derivative per loop (so its constants carry over),
        /// the integral reset when the error changes sign and kept only within the anti-windup range.</summary>
        internal sealed class Pid {
            private readonly ControllerSettings s;
            private double integral, prev;
            private bool first = true;
            public Pid(ControllerSettings s) => this.s = s;

            public double Update(double error) {
                if (!first && Math.Sign(error) != Math.Sign(prev)) integral = 0;
                if (s.windup <= 0 || Math.Abs(error) < s.windup) integral += error;
                else integral = 0;
                var derivative = first ? 0 : error - prev;
                first = false;
                prev = error;
                return s.kP * error + s.kI * integral + s.kD * derivative;
            }
        }

        /// <summary>LemLib's exit conditions: within the small range for its time, or the large range for its time.</summary>
        internal sealed class Exit {
            private readonly double range, time;
            private double since = -1;
            public Exit(double range, double timeMs) {
                this.range = range;
                time = timeMs / 1000.0;
            }

            public bool Done(double error, double now) {
                if (range <= 0 || time <= 0) return false;
                if (Math.Abs(error) > range) {
                    since = -1;
                    return false;
                }
                if (since < 0) since = now;
                return now - since >= time;
            }
        }

        internal double Slew(double output, double previous, double maxChange) =>
            maxChange <= 0 ? output : previous + Math.Max(-maxChange, Math.Min(maxChange, output - previous));

        internal abstract class Motion {
            public bool finished, stopAtEnd = true;
            public double start, timeout, travelled;
            protected readonly MotionChassis c;
            protected Motion(MotionChassis chassis, double timeout) {
                c = chassis;
                this.timeout = timeout;
                start = chassis.clock;
            }
            public abstract void Update();
            protected double Now => c.clock;
        }

        // Angle error to a heading, by the way asked (AUTO: the short way).
        internal static double AngleError(double target, double current, int direction) {
            var e = MotionModule.Wrap(target - current);
            if (direction == 1 && e < 0) e += 360;   // clockwise
            if (direction == 2 && e > 0) e -= 360;   // counterclockwise
            return e;
        }

        private sealed class TurnMotion : Motion {
            private readonly Pid pid;
            private readonly Exit small, large;
            private readonly double maxSpeed, minSpeed, earlyExit, startTheta;
            private double target, prevOut;
            private int direction;
            private readonly (double x, double y)? point;
            private readonly bool reversed;

            public TurnMotion(MotionChassis c, double theta, double timeout, int direction, double maxSpeed, double minSpeed, double earlyExit, (double x, double y)? point, bool reversed) : base(c, timeout) {
                pid = new Pid(c.Angular);
                small = new Exit(c.Angular.smallError, c.Angular.smallTimeout);
                large = new Exit(c.Angular.largeError, c.Angular.largeTimeout);
                target = theta;
                this.direction = direction;
                this.maxSpeed = maxSpeed;
                this.minSpeed = minSpeed;
                this.earlyExit = earlyExit;
                this.point = point;
                this.reversed = reversed;
                startTheta = c.Now.theta;
                if (minSpeed > 0) stopAtEnd = false;   // chained into the next motion
            }

            public override void Update() {
                var pose = c.Now;
                if (point.HasValue) target = Math.Atan2(point.Value.x - pose.x, point.Value.y - pose.y) * 180 / Math.PI + (reversed ? 180 : 0);
                // The direction asked only decides the first way round (as LemLib), then it's the short way.
                var error = AngleError(target, pose.theta, direction);
                if (Math.Abs(error) < 90) direction = 0;
                travelled = Math.Abs(pose.theta - startTheta);
                if (minSpeed > 0 && Math.Abs(error) < earlyExit) {
                    finished = true;
                    return;
                }
                if (small.Done(error, Now) || large.Done(error, Now)) {
                    finished = true;
                    return;
                }
                var output = pid.Update(error);
                output = c.Slew(output, prevOut, c.Angular.slew);
                output = Math.Max(-maxSpeed, Math.Min(maxSpeed, output));
                if (minSpeed > 0 && Math.Abs(output) < minSpeed) output = Math.Sign(output) * minSpeed;
                prevOut = output;
                c.Output(output, -output);
            }
        }

        private sealed class SwingMotion : Motion {
            private readonly Pid pid;
            private readonly Exit small, large;
            private readonly bool lockRight;
            private readonly double maxSpeed, minSpeed, earlyExit, startTheta;
            private double target, prevOut;
            private int direction;
            private readonly (double x, double y)? point;

            public SwingMotion(MotionChassis c, double theta, bool lockRight, double timeout, int direction, double maxSpeed, double minSpeed, double earlyExit, (double x, double y)? point) : base(c, timeout) {
                pid = new Pid(c.Angular);
                small = new Exit(c.Angular.smallError, c.Angular.smallTimeout);
                large = new Exit(c.Angular.largeError, c.Angular.largeTimeout);
                target = theta;
                this.lockRight = lockRight;
                this.direction = direction;
                this.maxSpeed = maxSpeed;
                this.minSpeed = minSpeed;
                this.earlyExit = earlyExit;
                this.point = point;
                startTheta = c.Now.theta;
                if (minSpeed > 0) stopAtEnd = false;
            }

            public override void Update() {
                var pose = c.Now;
                if (point.HasValue) target = Math.Atan2(point.Value.x - pose.x, point.Value.y - pose.y) * 180 / Math.PI;
                var error = AngleError(target, pose.theta, direction);
                if (Math.Abs(error) < 90) direction = 0;
                travelled = Math.Abs(pose.theta - startTheta);
                if ((minSpeed > 0 && Math.Abs(error) < earlyExit) || small.Done(error, Now) || large.Done(error, Now)) {
                    finished = true;
                    return;
                }
                var output = pid.Update(error);
                output = c.Slew(output, prevOut, c.Angular.slew);
                output = Math.Max(-maxSpeed, Math.Min(maxSpeed, output));
                if (minSpeed > 0 && Math.Abs(output) < minSpeed) output = Math.Sign(output) * minSpeed;
                prevOut = output;
                // The locked side holds; the other swings the robot round it (clockwise: the left side forward).
                if (lockRight) {
                    c.Output(output, 0);
                    foreach (var m in c.drive.right) m.Control?.StopMotor("HOLD");
                }
                else {
                    c.Output(0, -output);
                    foreach (var m in c.drive.left) m.Control?.StopMotor("HOLD");
                }
            }
        }

        // LemLib's moveToPoint: drive at the point, turning to face it, the heading held once close (7.5").
        private sealed class PointMotion : Motion {
            private readonly Pid lat, ang;
            private readonly Exit small, large;
            private readonly double tx, ty, maxSpeed, minSpeed, earlyExit;
            private readonly bool forwards;
            private bool close;
            private double prevLat, startX, startY;
            private int prevSide;

            public PointMotion(MotionChassis c, double x, double y, double timeout, bool forwards, double maxSpeed, double minSpeed, double earlyExit) : base(c, timeout) {
                lat = new Pid(c.Lateral);
                ang = new Pid(c.Angular);
                small = new Exit(c.Lateral.smallError, c.Lateral.smallTimeout);
                large = new Exit(c.Lateral.largeError, c.Lateral.largeTimeout);
                tx = x;
                ty = y;
                this.forwards = forwards;
                this.maxSpeed = maxSpeed;
                this.minSpeed = minSpeed;
                this.earlyExit = earlyExit;
                startX = c.Now.x;
                startY = c.Now.y;
                if (minSpeed > 0) stopAtEnd = false;
            }

            public override void Update() {
                var p = c.Now;
                var dx = tx - p.x;
                var dy = ty - p.y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                travelled = Math.Sqrt((p.x - startX) * (p.x - startX) + (p.y - startY) * (p.y - startY));
                if (dist < 7.5) close = true;
                var heading = p.theta + (forwards ? 0 : 180);
                var toPoint = Math.Atan2(dx, dy) * 180 / Math.PI;
                var angErr = MotionModule.Wrap(toPoint - heading);
                // Along the robot's facing (negative once it's past the point).
                var along = dist * Math.Cos(angErr * Math.PI / 180);
                var side = Math.Sign(along);
                if (minSpeed > 0 && ((prevSide != 0 && side != prevSide) || dist < earlyExit)) {
                    finished = true;
                    return;
                }
                prevSide = side;
                if (small.Done(along, Now) || large.Done(along, Now)) {
                    finished = true;
                    return;
                }
                var latOut = lat.Update(along);
                var angOut = close ? 0 : ang.Update(angErr);
                if (!close) latOut = c.Slew(latOut, prevLat, c.Lateral.slew);
                latOut = Math.Max(-maxSpeed, Math.Min(maxSpeed, latOut));
                if (minSpeed > 0 && Math.Abs(latOut) < minSpeed) latOut = Math.Sign(latOut) * minSpeed;
                angOut = Math.Max(-maxSpeed, Math.Min(maxSpeed, angOut));
                prevLat = latOut;
                if (!forwards) latOut = -latOut;
                // Keep the turn's share when both together would be over the most.
                var over = (Math.Abs(latOut) + Math.Abs(angOut)) / maxSpeed;
                if (over > 1) latOut /= over;
                c.Output(latOut + angOut, latOut - angOut);
            }
        }

        // LemLib's moveToPose (boomerang): aim at a carrot point ahead of the target along its heading.
        private sealed class PoseMotion : Motion {
            private readonly Pid lat, ang;
            private readonly Exit small, large;
            private readonly double tx, ty, tTheta, lead, drift, maxSpeed, minSpeed, earlyExit;
            private readonly bool forwards;
            private bool close;
            private double prevLat, startX, startY;
            private int prevSide;

            public PoseMotion(MotionChassis c, double x, double y, double theta, double timeout, bool forwards, double lead, double drift, double maxSpeed, double minSpeed, double earlyExit) : base(c, timeout) {
                lat = new Pid(c.Lateral);
                ang = new Pid(c.Angular);
                small = new Exit(c.Lateral.smallError, c.Lateral.smallTimeout);
                large = new Exit(c.Lateral.largeError, c.Lateral.largeTimeout);
                tx = x;
                ty = y;
                tTheta = theta + (forwards ? 0 : 180);
                this.forwards = forwards;
                this.lead = lead;
                this.drift = drift > 0 ? drift : c.HorizontalDrift;
                this.maxSpeed = maxSpeed;
                this.minSpeed = minSpeed;
                this.earlyExit = earlyExit;
                startX = c.Now.x;
                startY = c.Now.y;
                if (minSpeed > 0) stopAtEnd = false;
            }

            public override void Update() {
                var p = c.Now;
                var dist = Math.Sqrt((tx - p.x) * (tx - p.x) + (ty - p.y) * (ty - p.y));
                travelled = Math.Sqrt((p.x - startX) * (p.x - startX) + (p.y - startY) * (p.y - startY));
                if (dist < 7.5) close = true;
                var rad = tTheta * Math.PI / 180;
                // The carrot: behind the target along its heading, by lead x distance (none once close).
                var carrotX = close ? tx : tx - Math.Sin(rad) * lead * dist;
                var carrotY = close ? ty : ty - Math.Cos(rad) * lead * dist;
                var heading = p.theta + (forwards ? 0 : 180);
                var toCarrot = Math.Atan2(carrotX - p.x, carrotY - p.y) * 180 / Math.PI;
                var angErr = close ? MotionModule.Wrap(tTheta - heading) : MotionModule.Wrap(toCarrot - heading);
                var carrotDist = Math.Sqrt((carrotX - p.x) * (carrotX - p.x) + (carrotY - p.y) * (carrotY - p.y));
                var along = (close ? dist : carrotDist) * Math.Cos(MotionModule.Wrap(Math.Atan2(tx - p.x, ty - p.y) * 180 / Math.PI - heading) * Math.PI / 180);
                var side = Math.Sign(along);
                if (minSpeed > 0 && ((prevSide != 0 && side != prevSide && close) || dist < earlyExit)) {
                    finished = true;
                    return;
                }
                prevSide = side;
                if (close && (small.Done(dist, Now) || large.Done(dist, Now)) && Math.Abs(MotionModule.Wrap(tTheta - heading)) < Math.Max(2, c.Angular.smallError * 2)) {
                    finished = true;
                    return;
                }
                var latOut = lat.Update(along);
                var angOut = ang.Update(angErr);
                if (!close) latOut = c.Slew(latOut, prevLat, c.Lateral.slew);
                // Not faster than the robot can turn without sliding sideways (horizontal drift: how much it slides).
                var curvature = Math.Abs(Math.Sin(angErr * Math.PI / 180)) * 2 / Math.Max(1, carrotDist);
                if (curvature > 1e-4) {
                    var radius = 1 / curvature;
                    var slip = Math.Sqrt(drift * radius * 9.8);
                    latOut = Math.Max(-slip, Math.Min(slip, latOut));
                }
                latOut = Math.Max(-maxSpeed, Math.Min(maxSpeed, latOut));
                if (minSpeed > 0 && Math.Abs(latOut) < minSpeed) latOut = Math.Sign(latOut) * minSpeed;
                angOut = Math.Max(-maxSpeed, Math.Min(maxSpeed, angOut));
                prevLat = latOut;
                if (!forwards) latOut = -latOut;
                var over = (Math.Abs(latOut) + Math.Abs(angOut)) / maxSpeed;
                if (over > 1) latOut /= over;
                c.Output(latOut + angOut, latOut - angOut);
            }
        }

        // Pure pursuit: steer for the point on the path a lookahead away; done near its end.
        private sealed class PursuitMotion : Motion {
            private readonly List<(double x, double y, double speed)> path;
            private readonly bool speeds;   // V2: the path file's speed at each point (path.jerryio), else our own
            private readonly double lookahead, maxSpeed;
            private readonly bool forwards;
            private int closest;
            private double startX, startY;

            public PursuitMotion(MotionChassis c, List<(double x, double y, double speed)> path, double lookahead, double timeout, bool forwards, double maxSpeed) : base(c, timeout) {
                this.path = path;
                speeds = path.All(q => !double.IsNaN(q.speed));
                this.lookahead = Math.Max(1, lookahead);
                this.forwards = forwards;
                this.maxSpeed = maxSpeed;
                startX = c.Now.x;
                startY = c.Now.y;
            }

            public override void Update() {
                var p = c.Now;
                travelled = Math.Sqrt((p.x - startX) * (p.x - startX) + (p.y - startY) * (p.y - startY));
                // The nearest path point, never going back along the path.
                var best = double.MaxValue;
                for (var i = closest; i < path.Count; i++) {
                    var d = Dist(path[i], p);
                    if (d < best) { best = d; closest = i; }
                }
                var end = path[path.Count - 1];
                // (A path file's last speeds are 0: done when the nearest point is the last, as LemLib.)
                if (closest == path.Count - 1 && (speeds || Dist(end, p) < 2)) {
                    finished = true;
                    return;
                }
                // The lookahead point: where a circle round the robot crosses the path after the nearest point.
                var target = (end.x, end.y);
                for (var i = Math.Max(1, closest); i < path.Count; i++) {
                    if (Intersect(path[i - 1], path[i], p, lookahead, out var hit)) { target = hit; }
                    if (Dist(path[i], p) > lookahead) break;
                }
                var heading = (p.theta + (forwards ? 0 : 180)) * Math.PI / 180;
                // The target in the robot's frame: right (x) and forward (y).
                var dx = target.x - p.x;
                var dy = target.y - p.y;
                var localX = dx * Math.Cos(heading) - dy * Math.Sin(heading);
                var l2 = dx * dx + dy * dy;
                var curvature = l2 > 1e-6 ? 2 * localX / l2 : 0;
                // Slow down for tight curves and near the end (a path file: its own speed at the nearest point).
                var speed = speeds ? Math.Min(maxSpeed, Math.Max(12, path[closest].speed))
                    : maxSpeed * Math.Min(1, 3 / Math.Max(0.5, Math.Abs(curvature) * 40)) * Math.Min(1, Dist(end, p) / 12 + 0.3);
                var left = speed * (2 + curvature * c.TrackWidth) / 2;
                var right = speed * (2 - curvature * c.TrackWidth) / 2;
                var most = Math.Max(Math.Abs(left), Math.Abs(right)) / maxSpeed;
                if (most > 1) { left /= most; right /= most; }
                if (forwards) c.Output(left, right);
                else c.Output(-right, -left);
            }

            private static double Dist((double x, double y, double speed) a, Pose p) => Math.Sqrt((a.x - p.x) * (a.x - p.x) + (a.y - p.y) * (a.y - p.y));

            private static bool Intersect((double x, double y, double speed) a, (double x, double y, double speed) b, Pose p, double r, out (double x, double y) hit) {
                hit = (b.x, b.y);
                var dx = b.x - a.x;
                var dy = b.y - a.y;
                var fx = a.x - p.x;
                var fy = a.y - p.y;
                var qa = dx * dx + dy * dy;
                var qb = 2 * (fx * dx + fy * dy);
                var qc = fx * fx + fy * fy - r * r;
                var disc = qb * qb - 4 * qa * qc;
                if (qa < 1e-9 || disc < 0) return false;
                disc = Math.Sqrt(disc);
                var t2 = (-qb + disc) / (2 * qa);
                var t1 = (-qb - disc) / (2 * qa);
                var t = t2 >= 0 && t2 <= 1 ? t2 : t1 >= 0 && t1 <= 1 ? t1 : -1;
                if (t < 0) return false;
                hit = (a.x + t * dx, a.y + t * dy);
                return true;
            }
        }
    }

    /// <summary>V2: LemLib's ASSET(name_txt): the project's path file (static/name.txt), read when a motion follows it.</summary>
    internal sealed class PathAsset : VexObject {
        public override string TypeName => "asset";
        public readonly string name;

        public PathAsset(string name) {
            this.name = name;
            Field("name", name);
        }

        public List<(double x, double y, double speed)> Points(RobotProgram p) {
            var text = CodeProject.Find(p.files, "static/" + name) ?? CodeProject.Find(p.files, name);
            if (text == null) {
                p.Warn("asset:" + name, "ASSET(" + name.Replace('.', '_') + "): static/" + name + " isn't in the project (Project > Add path files adds it); this follow() was skipped");
                return null;
            }
            var pts = CodeProject.ParsePath(text);
            if (pts.Count < 2) {
                p.Warn("asset:" + name, name + " has no path points (path.jerryio's \"x, y, speed\" lines); this follow() was skipped");
                return null;
            }
            return pts;
        }
    }
}
