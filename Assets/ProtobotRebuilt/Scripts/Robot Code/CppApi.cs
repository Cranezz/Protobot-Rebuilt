using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Devices;
using Protobot.Scripting;
using UnityEngine;
using static Protobot.RobotCode.VexModule;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9, C++): the robot libraries C++ teams use, over the same simulated devices as Python:
    ///  - PROS ("pros"): Motor / MotorGroup (signed ports: negative is reversed; move -127..127, move_velocity,
    ///    move_absolute...), Controller (get_analog, get_digital, get_digital_new_press, print, rumble), Imu, Rotation
    ///    (centidegrees, as PROS), Distance, Optical, adi::DigitalOut / Pneumatics / DigitalIn / Encoder, Task, delay,
    ///    millis, lcd (print with printf formats, on the Brain's screen), and its enums (E_CONTROLLER_*, MotorGearset,
    ///    E_MOTOR_BRAKE_*...);
    ///  - VEXcode C++ ("vex"): the Python API's classes and units under their C++ names (motor, motor_group, brain,
    ///    controller, fwd, pct, msec, ratio18_1, PORT1, vex::color::red...), competition with autonomous(f) and
    ///    drivercontrol(f), task / this_thread sleeping; members answer to camelCase (VexObject.TryGetAttr);
    ///  - LemLib ("lemlib"): the motion module (it takes PROS's motor groups, sensors and Imu), telemetrySink().
    /// </summary>
    public static class CppApi {
        public static void Install(RobotProgram program, PyModule vexPython, PyModule motion, string source = null) {
            var vm = program.vm;
            var g = vm.main.globals;
            var pros = Pros(program);
            var vex = VexCpp(program, vexPython);
            vm.modules["cpp:pros"] = pros;
            vm.modules["cpp:vex"] = vex;
            vm.modules["cpp:lemlib"] = motion;
            g["pros"] = pros;
            g["vex"] = vex;
            g["lemlib"] = motion;
            motion.globals["telemetrySink"] = new PyBuiltin("telemetrySink", _ => new Telemetry(program));
            // PROS's controller names are macros (usable without pros::).
            foreach (var kv in pros.globals.Where(kv => kv.Key.StartsWith("E_CONTROLLER_")).ToList()) g[kv.Key.Substring("E_CONTROLLER_".Length)] = kv.Value;
            foreach (var kv in pros.globals.Where(kv => kv.Key.StartsWith("E_MOTOR_")).ToList()) g[kv.Key.Substring(2)] = kv.Value;   // MOTOR_BRAKE_HOLD
            // V2: the rest of PROS's short names (as its headers define them).
            g["CONTROLLER_MASTER"] = pros.globals["E_CONTROLLER_MASTER"];
            g["CONTROLLER_PARTNER"] = pros.globals["E_CONTROLLER_PARTNER"];
            g["MOTOR_GEARSET_6"] = pros.globals["E_MOTOR_GEARSET_06"];
            // V2: the LLEMU buttons (read_buttons() & LCD_BTN_LEFT).
            g["LCD_BTN_LEFT"] = 4L;
            g["LCD_BTN_CENTER"] = 2L;
            g["LCD_BTN_RIGHT"] = 1L;
            // EZ-Template (ez::), and the names its files make global (master, fwd, rev).
            var ez = EzTemplate.Build(program);
            vm.modules["cpp:ez"] = ez;
            g["ez"] = ez;
            if (source != null && (source.Contains("ez::") || source.Contains("EZ-Template")))
                EzTemplate.Globals(program, ez, pros);
            // VEXcode's robot-config: a Brain and Controller1 are there unless the program makes its own.
            g["Brain"] = program.Brain;
            g["Controller1"] = program.Controller(false);
            g["__competition"] = new PyBuiltin("__competition", a => program.SetCompetition(a.Get(0, "driver"), a.Get(1, "autonomous")));
            g["__asset"] = new PyBuiltin("__asset", a => new PathAsset(a.vm.StrOf(a.Need(0, "name"))));   // V2: LemLib's ASSET(name_txt)
        }

        internal static object Call(RobotProgram p, VexObject o, string method, params object[] args) {
            if (!o.TryGetAttr(method, out var f)) throw new PyError("AttributeError", o.TypeName + " has no " + method);
            return p.vm.CallSync(f, args);
        }

        private static Args A(RobotProgram p, string name, params object[] pos) => new Args(p.vm, name, pos, null);

        // ================================================================== VEXcode C++

        private static PyModule VexCpp(RobotProgram program, PyModule py) {
            var m = new PyModule("vex");
            foreach (var kv in py.globals) m.globals[kv.Key] = kv.Value;
            // V2: Python's Brain class isn't VEXcode C++'s (that's brain): with using namespace vex it hid the robot's
            // Brain (robot-config's), so Brain.ThreeWirePort.A failed in a program that doesn't make its own.
            m.globals.Remove("Brain");
            var u = Units;
            void Alias(string name, string target) { if (u.TryGetValue(target, out var e)) m.globals[name] = e; }
            foreach (var (n, t) in new[] {
                ("fwd", "FORWARD"), ("forward", "FORWARD"), ("reverse", "REVERSE"), ("left", "LEFT"), ("right", "RIGHT"),
                ("pct", "PERCENT"), ("percent", "PERCENT"), ("rpm", "RPM"), ("dps", "DPS"), ("deg", "DEGREES"), ("degrees", "DEGREES"),
                ("rev", "TURNS"), ("turns", "TURNS"), ("raw", "RAW"), ("msec", "MSEC"), ("sec", "SECONDS"), ("seconds", "SECONDS"),
                ("mm", "MM"), ("inches", "INCHES"), ("cm", "CM"), ("volt", "VOLT"), ("mV", "MV"), ("amp", "AMP"), ("Nm", "NM"),
                ("InLb", "INLB"), ("watt", "WATT"), ("celsius", "CELSIUS"), ("fahrenheit", "FAHRENHEIT"), ("brake", "BRAKE"),
                ("coast", "COAST"), ("hold", "HOLD"), ("primary", "PRIMARY"), ("partner", "PARTNER"), ("xaxis", "XAXIS"),
                ("yaxis", "YAXIS"), ("zaxis", "ZAXIS"), ("roll", "ROLL"), ("pitch", "PITCH"), ("yaw", "YAW"),
                ("ratio36_1", "RATIO_36_1"), ("ratio18_1", "RATIO_18_1"), ("ratio6_1", "RATIO_6_1"),
            }) Alias(n, t);
            if (py.globals["Ports"] is PyClass ports)
                foreach (var kv in ports.dict) m.globals[kv.Key] = kv.Value;   // PORT1..PORT21
            // Enum classes: vex::directionType::fwd, velocityUnits::pct...
            void Holder(string name, params (string, string)[] items) {
                var c = new PyClass(name, null);
                foreach (var (n, t) in items) if (u.TryGetValue(t, out var e)) c.dict[n] = e;
                m.globals[name] = c;
            }
            Holder("directionType", ("fwd", "FORWARD"), ("rev", "REVERSE"), ("undefined", "FORWARD"));
            Holder("turnType", ("left", "LEFT"), ("right", "RIGHT"));
            Holder("velocityUnits", ("pct", "PERCENT"), ("rpm", "RPM"), ("dps", "DPS"));
            Holder("percentUnits", ("pct", "PERCENT"));
            Holder("rotationUnits", ("deg", "DEGREES"), ("rev", "TURNS"), ("raw", "RAW"));
            Holder("timeUnits", ("sec", "SECONDS"), ("msec", "MSEC"));
            Holder("distanceUnits", ("mm", "MM"), ("in", "INCHES"), ("cm", "CM"));
            Holder("brakeType", ("coast", "COAST"), ("brake", "BRAKE"), ("hold", "HOLD"));
            Holder("gearSetting", ("ratio36_1", "RATIO_36_1"), ("ratio18_1", "RATIO_18_1"), ("ratio6_1", "RATIO_6_1"));
            Holder("voltageUnits", ("volt", "VOLT"), ("mV", "MV"));
            Holder("currentUnits", ("amp", "AMP"));
            Holder("torqueUnits", ("Nm", "NM"), ("InLb", "INLB"));
            Holder("powerUnits", ("watt", "WATT"));
            Holder("temperatureUnits", ("celsius", "CELSIUS"), ("fahrenheit", "FAHRENHEIT"));
            Holder("controllerType", ("primary", "PRIMARY"), ("partner", "PARTNER"));
            Holder("axisType", ("xaxis", "XAXIS"), ("yaxis", "YAXIS"), ("zaxis", "ZAXIS"));
            Holder("orientationType", ("roll", "ROLL"), ("pitch", "PITCH"), ("yaw", "YAW"));
            // Colors (vex::color::red, or color(255, 0, 0)) and fonts (mono20, vex::fontType::prop40).
            var colors = new PyClass("color", null) { construct = (_, a) => VexColorValue.Make(a) };
            if (py.globals["Color"] is PyClass pc)
                foreach (var kv in pc.dict) {
                    colors.dict[kv.Key.ToLowerInvariant()] = kv.Value;
                    m.globals[kv.Key.ToLowerInvariant()] = kv.Value;
                }
            m.globals["color"] = colors;
            var fonts = new PyClass("fontType", null);
            if (py.globals["FontType"] is PyClass pf)
                foreach (var kv in pf.dict) {
                    fonts.dict[kv.Key.ToLowerInvariant()] = kv.Value;
                    m.globals[kv.Key.ToLowerInvariant()] = kv.Value;
                }
            m.globals["fontType"] = fonts;
            // Classes under their C++ names.
            foreach (var (cpp, python) in new[] {
                ("motor", "Motor"), ("motor_group", "MotorGroup"), ("drivetrain", "DriveTrain"), ("smartdrive", "SmartDrive"),
                ("brain", "Brain"), ("controller", "Controller"), ("inertial", "Inertial"), ("rotation", "Rotation"),
                ("distance", "Distance"), ("optical", "Optical"), ("bumper", "Bumper"), ("limit", "Limit"), ("digital_out", "DigitalOut"),
                ("digital_in", "DigitalIn"), ("pneumatics", "Pneumatics"), ("encoder", "Encoder"), ("pot", "Potentiometer"),
                ("potV2", "PotentiometerV2"), ("line", "Line"), ("light", "Light"), ("sonar", "Sonar"), ("gyro", "Gyro"),
                ("timer", "Timer"), ("thread", "Thread"), ("event", "Event"), ("vision", "Vision"), ("gps", "Gps"), ("aivision", "AiVision"),
                ("signature", "Signature"),   // V2: using signature = vision::signature; (VEXcode's robot-config)
            })
                if (py.globals.TryGetValue(python, out var cls)) m.globals[cpp] = cls;
            // motor(PORT1, ratio18_1, false) as Python's; motor_group(m1, m2) too. task(f) = thread(f), and sleeping.
            var task = new PyClass("task", null) { construct = (_, a) => new VexThread(program, a) };
            task.dict["sleep"] = new PyBuiltin("sleep", a => new Suspend(a.vm.Time + Math.Max(0, a.Number(0, "time", 0)) / 1000.0));
            m.globals["task"] = task;
            var thisThread = new PyModule("this_thread");
            thisThread.globals["sleep_for"] = new PyBuiltin("sleep_for", a => new Suspend(a.vm.Time + Math.Max(0, a.Number(0, "time", 0)) / 1000.0));
            thisThread.globals["yield"] = new PyBuiltin("yield", a => new Suspend(a.vm.Time));
            m.globals["this_thread"] = thisThread;
            m.globals["competition"] = new PyClass("competition", null) { construct = (_, a) => new CppCompetition(program) };
            return m;
        }

        // VEXcode C++'s competition: Competition.autonomous(f); Competition.drivercontrol(f);
        private sealed class CppCompetition : VexObject {
            public override string TypeName => "competition";
            private object driver, autonomous;

            public CppCompetition(RobotProgram p) {
                Method("autonomous", a => {
                    autonomous = a.Need(0, "callback");
                    p.SetCompetition(driver, autonomous);
                    return PyNone.Value;
                });
                Method("drivercontrol", a => {
                    driver = a.Need(0, "callback");
                    p.SetCompetition(driver, autonomous);
                    return PyNone.Value;
                });
                Method("is_enabled", _ => p.Enabled);
                Method("is_driver_control", _ => p.IsDriver);
                Method("is_autonomous", _ => p.IsAutonomous);
                Method("is_competition_switch", _ => false);
                Method("is_field_control", _ => false);
            }
        }

        private sealed class Telemetry : VexObject {
            public override string TypeName => "TelemetrySink";

            public Telemetry(RobotProgram p) {
                foreach (var level in new[] { "debug", "info", "warn", "error", "log" }) {
                    var lv = level;
                    Method(lv, a => {
                        // LemLib's telemetry: "{}" placeholders filled in order.
                        var fmt = a.vm.StrOf(a.Need(0, "format"));
                        var i = 1;
                        var sb = new System.Text.StringBuilder();
                        for (var k = 0; k < fmt.Length; k++) {
                            if (fmt[k] == '{' && k + 1 < fmt.Length && fmt[k + 1] == '}' && i < a.Count) {
                                sb.Append(a.pos[i] is double d ? d.ToString("0.###", Ops.Inv) : a.vm.StrOf(a.pos[i]));
                                i++;
                                k++;
                            }
                            else sb.Append(fmt[k]);
                        }
                        p.Print("[" + lv + "] " + sb + "\n");
                        return PyNone.Value;
                    });
                }
            }
        }

        // ================================================================== PROS

        private static PyModule Pros(RobotProgram program) {
            var m = new PyModule("pros");
            var vm = program.vm;
            PyEnum E(string type, string name, int value) {
                var e = new PyEnum(type, name, value);
                m.globals[name] = e;
                return e;
            }
            // Controller ids, sticks and buttons.
            E("controller_id_e_t", "E_CONTROLLER_MASTER", 0);
            E("controller_id_e_t", "E_CONTROLLER_PARTNER", 1);
            foreach (var (n, i) in new[] { ("LEFT_X", 4), ("LEFT_Y", 3), ("RIGHT_X", 1), ("RIGHT_Y", 2) }) E("controller_analog_e_t", "E_CONTROLLER_ANALOG_" + n, i);
            foreach (var n in new[] { "L1", "L2", "R1", "R2", "UP", "DOWN", "LEFT", "RIGHT", "X", "B", "Y", "A" }) E("controller_digital_e_t", "E_CONTROLLER_DIGITAL_" + n, 0);
            var ids = new PyClass("ControllerId", null);
            ids.dict["master"] = m.globals["E_CONTROLLER_MASTER"];
            ids.dict["partner"] = m.globals["E_CONTROLLER_PARTNER"];
            m.globals["ControllerId"] = ids;
            // Gearsets, brake modes, encoder units (both PROS 3 and 4 names).
            var gearsets = new PyClass("MotorGearset", null);
            foreach (var (n, rpm) in new[] { ("red", 100), ("green", 200), ("blue", 600), ("rpm_100", 100), ("rpm_200", 200), ("rpm_600", 600), ("ratio_36_to_1", 100), ("ratio_18_to_1", 200), ("ratio_6_to_1", 600), ("invalid", 0) })
                gearsets.dict[n] = new PyEnum("MotorGearset", n, rpm);
            m.globals["MotorGearset"] = gearsets;
            m.globals["MotorGears"] = gearsets;   // V2: PROS 4's name
            m.globals["motor_gearset_e"] = gearsets;
            foreach (var (n, rpm) in new[] { ("E_MOTOR_GEARSET_36", 100), ("E_MOTOR_GEARSET_18", 200), ("E_MOTOR_GEARSET_06", 600), ("E_MOTOR_GEAR_RED", 100), ("E_MOTOR_GEAR_GREEN", 200), ("E_MOTOR_GEAR_BLUE", 600),
                                             ("E_MOTOR_GEAR_100", 100), ("E_MOTOR_GEAR_200", 200), ("E_MOTOR_GEAR_600", 600) })
                E("MotorGearset", n, rpm);
            var brakes = new PyClass("MotorBrake", null);
            foreach (var (n, i) in new[] { ("coast", 0), ("brake", 1), ("hold", 2) }) brakes.dict[n] = new PyEnum("MotorBrake", n, i);
            m.globals["MotorBrake"] = brakes;
            m.globals["motor_brake_mode_e"] = brakes;
            E("MotorBrake", "E_MOTOR_BRAKE_COAST", 0);
            E("MotorBrake", "E_MOTOR_BRAKE_BRAKE", 1);
            E("MotorBrake", "E_MOTOR_BRAKE_HOLD", 2);
            var units = new PyClass("MotorUnits", null);
            foreach (var (n, i) in new[] { ("degrees", 0), ("rotations", 1), ("counts", 2) }) units.dict[n] = new PyEnum("MotorUnits", n, i);
            m.globals["MotorUnits"] = units;
            m.globals["MotorEncoderUnits"] = units;
            E("MotorUnits", "E_MOTOR_ENCODER_DEGREES", 0);
            E("MotorUnits", "E_MOTOR_ENCODER_ROTATIONS", 1);
            E("MotorUnits", "E_MOTOR_ENCODER_COUNTS", 2);

            void Class(string name, Func<Args, object> make) => m.globals[name] = new PyClass(name, null) { construct = (_, a) => make(a) };
            Class("Motor", a => new ProsMotor(program, a));
            Class("MotorGroup", a => new ProsMotorGroup(program, a));
            Class("Controller", a => new ProsController(program, a));
            Class("Imu", a => new ProsImu(program, a));
            m.globals["IMU"] = m.globals["Imu"];   // V2
            Class("Rotation", a => new ProsRotation(program, a));
            Class("Distance", a => new ProsSensor(program, a, "Distance"));
            Class("Optical", a => new ProsSensor(program, a, "Optical"));
            // V2: the Vision Sensor (PROS: pros::Vision, signature_from_utility, get_by_sig / get_by_size / get_by_code).
            Class("Gps", a => new ProsGps(program, a));   // V2
            ProsAiVision.Register(program, m);            // V2
            m.globals["GPS"] = m.globals["Gps"];
            Class("Vision", a => new ProsVision(program, a));
            var fromUtility = new PyBuiltin("signature_from_utility", a => new VexVisionSignature(a));
            ((PyClass)m.globals["Vision"]).dict["signature_from_utility"] = fromUtility;
            m.globals["vision_signature_from_utility"] = fromUtility;
            m.globals["VISION_OBJECT_ERR_SIG"] = 255L;
            m.globals["VISION_FOV_WIDTH"] = 316L;
            m.globals["VISION_FOV_HEIGHT"] = 212L;
            E("vision_zero_e_t", "E_VISION_ZERO_TOPLEFT", 0);
            E("vision_zero_e_t", "E_VISION_ZERO_CENTER", 1);
            E("vision_object_type_e_t", "E_VISION_OBJECT_NORMAL", 0);
            E("vision_object_type_e_t", "E_VISION_OBJECT_COLOR_CODE", 1);
            Class("Task", a => new ProsTask(program, a));
            // (PROS 3's names)
            m.globals["Motor_Group"] = m.globals["MotorGroup"];
            m.globals["ADIDigitalOut"] = new PyClass("ADIDigitalOut", null) { construct = (_, a) => new ProsDigitalOut(program, a, false) };
            m.globals["ADIDigitalIn"] = new PyClass("ADIDigitalIn", null) { construct = (_, a) => new ProsDigitalIn(program, a) };
            m.globals["ADIEncoder"] = new PyClass("ADIEncoder", null) { construct = (_, a) => new ProsEncoder(program, a) };
            var adi = new PyModule("adi");
            adi.globals["DigitalOut"] = new PyClass("DigitalOut", null) { construct = (_, a) => new ProsDigitalOut(program, a, false) };
            adi.globals["Pneumatics"] = new PyClass("Pneumatics", null) { construct = (_, a) => new ProsDigitalOut(program, a, true) };
            adi.globals["DigitalIn"] = new PyClass("DigitalIn", null) { construct = (_, a) => new ProsDigitalIn(program, a) };
            adi.globals["Button"] = adi.globals["DigitalIn"];
            adi.globals["Encoder"] = new PyClass("Encoder", null) { construct = (_, a) => new ProsEncoder(program, a) };
            m.globals["adi"] = adi;

            m.globals["delay"] = new PyBuiltin("delay", a => new Suspend(a.vm.Time + Math.Max(0, a.Number(0, "milliseconds", 0)) / 1000.0));
            m.globals["millis"] = new PyBuiltin("millis", a => (long)Math.Round(program.Time * 1000));
            m.globals["micros"] = new PyBuiltin("micros", a => (long)Math.Round(program.Time * 1e6));
            var task = (PyClass)m.globals["Task"];
            task.dict["delay"] = m.globals["delay"];
            task.dict["delay_until"] = new PyBuiltin("delay_until", a => new Suspend(a.vm.Time + Math.Max(0, a.Number(1, "delta", 0)) / 1000.0));

            // LLEMU (pros::lcd): 8 text lines on the Brain's screen.
            var lcd = new PyModule("lcd");
            var screen = program.Brain.screen;
            object Line(long line, string text) {
                var row = (int)Math.Max(0, Math.Min(7, line)) + 1;
                Call(program, screen, "clear_row", (long)row);
                Call(program, screen, "set_cursor", (long)row, 1L);
                Call(program, screen, "print", text);
                return true;
            }
            lcd.globals["initialize"] = new PyBuiltin("initialize", _ => true);
            lcd.globals["is_initialized"] = new PyBuiltin("is_initialized", _ => true);
            lcd.globals["shutdown"] = new PyBuiltin("shutdown", _ => true);
            lcd.globals["print"] = new PyBuiltin("print", a => Line(Ops.ToLong(a.Need(0, "line")), CppRuntime.CFormat(a.vm, a.vm.StrOf(a.Need(1, "format")), a.pos.Skip(2).ToArray())));
            lcd.globals["set_text"] = new PyBuiltin("set_text", a => Line(Ops.ToLong(a.Need(0, "line")), a.vm.StrOf(a.Need(1, "text"))));
            lcd.globals["clear_line"] = new PyBuiltin("clear_line", a => Call(program, screen, "clear_row", Ops.ToLong(a.Need(0, "line")) + 1));
            lcd.globals["clear"] = new PyBuiltin("clear", _ => Call(program, screen, "clear_screen"));
            foreach (var n in new[] { "register_btn0_cb", "register_btn1_cb", "register_btn2_cb", "set_background_color", "set_text_color" })
                lcd.globals[n] = new PyBuiltin(n, _ => true);
            lcd.globals["read_buttons"] = new PyBuiltin("read_buttons", _ => 0L);
            m.globals["lcd"] = lcd;

            // pros::competition: the simulator isn't plugged into a field.
            var comp = new PyModule("competition");
            comp.globals["is_connected"] = new PyBuiltin("is_connected", _ => false);
            comp.globals["is_field_control"] = new PyBuiltin("is_field_control", _ => false);
            comp.globals["is_competition_switch"] = new PyBuiltin("is_competition_switch", _ => false);
            comp.globals["is_autonomous"] = new PyBuiltin("is_autonomous", _ => program.IsAutonomous);
            comp.globals["is_disabled"] = new PyBuiltin("is_disabled", _ => !program.Enabled);
            comp.globals["get_status"] = new PyBuiltin("get_status", _ => program.IsAutonomous ? 1L : 0L);
            m.globals["competition"] = comp;
            return m;
        }

        // ---------------------------------------------------------------- PROS devices

        private static int Rpm(object gear) => gear is PyEnum e && e.value > 0 ? e.value : 200;

        private static PyEnum Gearing(int rpm) => Units[rpm == 100 ? "RATIO_36_1" : rpm == 600 ? "RATIO_6_1" : "RATIO_18_1"];

        private sealed class ProsMotor : VexObject, IMotorSet {
            public override string TypeName => "Motor";
            internal readonly VexMotor motor;
            private readonly int signedPort;
            private string units = "degrees";
            public VexMotor[] Motors => new[] { motor };

            public ProsMotor(RobotProgram p, Args a) : this(p, SignedPort(a), a.Get(1, "gearset") is bool ? null : a.Get(1, "gearset")) {
                var u = a.Get(1, "gearset") is bool ? null : a.Get(2, "encoder_units") is bool ? a.Get(3, "encoder_units") : a.Get(2, "encoder_units");
                if (u is PyEnum e) units = e.name.Replace("E_MOTOR_ENCODER_", "").ToLowerInvariant();
            }

            // V2: PROS 4: Motor(-port, gearset, units) is reversed; PROS 3: Motor(port, gearset, reverse, units) and
            // Motor(port, reverse) (a reversed flag in place of the sign).
            private static int SignedPort(Args a) {
                var port = (int)Ops.ToLong(a.Need(0, "port"));
                var reverse = a.Get(1, "gearset") is bool r1 ? r1 : a.Get(2, "encoder_units") is bool r2 && r2;
                if (a.kw != null && a.kw.TryGetValue("reverse", out var kr)) reverse = Ops.Truthy(kr);
                return reverse ? -Math.Abs(port) : port;
            }

            public ProsMotor(RobotProgram p, int port, object gearset) {
                signedPort = port;
                motor = new VexMotor(p, A(p, "Motor", (long)Math.Abs(port), gearset is PyEnum ? Gearing(Rpm(gearset)) : (object)PyNone.Value, port < 0));
                Members(p, this);
            }

            // The PROS motor API, for one motor (a group fans out to its motors).
            internal static void Members(RobotProgram p, VexObject o) {
                VexMotor[] All() => o is ProsMotor pm ? new[] { pm.motor } : ((ProsMotorGroup)o).Motors;
                object Each(Action<VexMotor> act) {
                    foreach (var m in All()) act(m);
                    return 1L;
                }
                object First(Func<VexMotor, object> read) => read(All()[0]);
                object Volts(VexMotor m, double share) {
                    m.Control?.Volts((float)(Math.Max(-1, Math.Min(1, share)) * m.Sign));
                    return null;
                }
                double Rotations(object o2) => o2 is PyEnum e && e.name.Contains("ROTATION") || o2 is PyEnum e2 && e2.name == "rotations" ? 360 : 1;
                Method(o, "move", a => Each(m => Volts(m, Ops.ToDouble(a.Need(0, "voltage")) / 127.0)));
                Method(o, "move_voltage", a => Each(m => Volts(m, Ops.ToDouble(a.Need(0, "voltage")) / 12000.0)));
                Method(o, "move_velocity", a => Each(m => m.Control?.Spin((float)(Ops.ToDouble(a.Need(0, "velocity")) / (m.Control?.FreeRpm ?? 200f) * m.Sign))));
                Method(o, "move_absolute", a => Each(m => Call(p, m, "spin_to_position", a.Need(0, "position"), Units["DEGREES"], Math.Abs(Ops.ToDouble(a.Need(1, "velocity"))), Units["RPM"], false)));
                Method(o, "move_relative", a => Each(m => Call(p, m, "spin_for", Units["FORWARD"], a.Need(0, "position"), Units["DEGREES"], Math.Abs(Ops.ToDouble(a.Need(1, "velocity"))), Units["RPM"], false)));
                Method(o, "brake", _ => Each(m => Call(p, m, "stop")));
                Method(o, "set_brake_mode", a => Each(m => Call(p, m, "set_stopping", BrakeOf(a.Need(0, "mode")))));
                Method(o, "set_brake_mode_all", a => Each(m => Call(p, m, "set_stopping", BrakeOf(a.Need(0, "mode")))));
                Method(o, "tare_position", _ => Each(m => Call(p, m, "set_position", 0L, Units["DEGREES"])));
                Method(o, "tare_position_all", _ => Each(m => Call(p, m, "set_position", 0L, Units["DEGREES"])));
                Method(o, "set_zero_position", a => Each(m => Call(p, m, "set_position", -Ops.ToDouble(a.Need(0, "position")), Units["DEGREES"])));
                Method(o, "get_position", _ => First(m => m.Position()));
                Method(o, "get_position_all", _ => new PyList(All().Select(m => (object)m.Position())));
                Method(o, "get_actual_velocity", _ => First(m => Call(p, m, "velocity", Units["RPM"])));
                Method(o, "get_actual_velocity_all", _ => new PyList(All().Select(m => Call(p, m, "velocity", Units["RPM"]))));
                Method(o, "get_target_velocity", _ => First(m => Call(p, m, "command", Units["RPM"])));
                Method(o, "get_target_position", _ => First(m => m.Control != null ? m.Control.targetDeg * m.Sign : 0.0));
                Method(o, "get_current_draw", _ => First(m => (long)Math.Round(Ops.ToDouble(Call(p, m, "current")) * 1000)));
                Method(o, "get_torque", _ => First(m => Call(p, m, "torque", Units["NM"])));
                Method(o, "get_power", _ => First(m => Call(p, m, "power")));
                Method(o, "get_temperature", _ => 25.0);
                Method(o, "get_voltage", _ => First(m => (long)Math.Round((m.Control?.motor.Command ?? 0f) * 12000 * m.Sign)));
                Method(o, "get_efficiency", _ => 100.0);
                Method(o, "is_stopped", _ => First(m => !(m.Control != null && Mathf.Abs(m.Control.Level) > 0.01f)));
                Method(o, "is_over_temp", _ => false);
                Method(o, "is_reversed", _ => First(m => m.reversed));
                Method(o, "set_reversed", a => Each(m => m.reversed = Ops.Truthy(a.Need(0, "reverse"))));
                Method(o, "set_reversed_all", a => Each(m => m.reversed = Ops.Truthy(a.Need(0, "reverse"))));
                Method(o, "set_gearing", _ => 1L);
                Method(o, "set_gearing_all", _ => 1L);
                Method(o, "set_encoder_units", _ => 1L);
                Method(o, "set_encoder_units_all", _ => 1L);
                Method(o, "set_current_limit", _ => 1L);
                Method(o, "set_voltage_limit", _ => 1L);
                Method(o, "get_gearing", _ => First(m => (object)(long)(m.Control?.FreeRpm ?? 200)));
                Method(o, "size", _ => (long)All().Length);
                Method(o, "get_port", _ => First(m => (long)(m.port * (m.reversed ? -1 : 1))));
                _ = Rotations(null);
            }

            private static void Method(VexObject o, string name, Func<Args, object> fn) => o.Add(name, new PyBuiltin(name, fn));

            private static object BrakeOf(object mode) {
                var n = mode is PyEnum e ? e.name.ToUpperInvariant() : "COAST";
                return Units[n.Contains("HOLD") ? "HOLD" : n.Contains("BRAKE") && !n.EndsWith("COAST") ? "BRAKE" : "COAST"];
            }
        }

        private sealed class ProsMotorGroup : VexObject, IMotorSet {
            public override string TypeName => "MotorGroup";
            private readonly VexMotor[] motors;
            public VexMotor[] Motors => motors;

            public ProsMotorGroup(RobotProgram p, Args a) {
                // MotorGroup({1, -2, 3}, gearset) or MotorGroup({motor_a, motor_b})
                var first = a.Need(0, "ports");
                var gearset = a.Get(1, "gearset");
                var list = new List<VexMotor>();
                foreach (var item in first is PyList l ? l.items : Ops.Iterate(first).ToList()) {
                    if (item is ProsMotor pm) list.Add(pm.motor);
                    else list.Add(new ProsMotor(p, (int)Ops.ToLong(item), gearset).motor);
                }
                if (list.Count == 0) throw new PyError("ValueError", "a MotorGroup needs at least one motor");
                motors = list.ToArray();
                ProsMotor.Members(p, this);
            }
        }

        private sealed class ProsController : VexObject {
            public override string TypeName => "Controller";
            private readonly VexController controller;
            private readonly Dictionary<string, bool> last = new();

            public ProsController(RobotProgram p, Args a) {
                var partner = a.Get(0, "id") is PyEnum id && id.value == 1;
                controller = p.Controller(partner);
                Method("get_analog", a2 => {
                    var ch = a2.Need(0, "channel") as PyEnum;
                    var axis = ch?.value ?? 3;
                    return Call(p, (VexObject)Attr(controller, "axis" + axis), "value");
                });
                Method("get_digital", a2 => Pressing(p, a2.Need(0, "button")));
                Method("get_digital_new_press", a2 => {
                    // True once per press (since the last time it was asked).
                    var key = (a2.Need(0, "button") as PyEnum)?.name ?? "";
                    var now = Ops.Truthy(Pressing(p, a2.Need(0, "button")));
                    var was = last.TryGetValue(key, out var w) && w;
                    last[key] = now;
                    return now && !was;
                });
                Method("get_digital_new_release", a2 => {
                    var key = "r" + ((a2.Need(0, "button") as PyEnum)?.name ?? "");
                    var now = Ops.Truthy(Pressing(p, a2.Need(0, "button")));
                    var was = last.TryGetValue(key, out var w) && w;
                    last[key] = now;
                    return !now && was;
                });
                Method("print", a2 => {
                    var screen = (VexObject)Attr(controller, "screen");
                    Call(p, screen, "set_cursor", Ops.ToLong(a2.Need(0, "line")) + 1, Ops.ToLong(a2.Need(1, "col")) + 1);
                    Call(p, screen, "print", CppRuntime.CFormat(a2.vm, a2.vm.StrOf(a2.Need(2, "format")), a2.pos.Skip(3).ToArray()));
                    return 1L;
                });
                Method("set_text", a2 => {
                    var screen = (VexObject)Attr(controller, "screen");
                    Call(p, screen, "set_cursor", Ops.ToLong(a2.Need(0, "line")) + 1, Ops.ToLong(a2.Need(1, "col")) + 1);
                    Call(p, screen, "print", a2.vm.StrOf(a2.Need(2, "text")));
                    return 1L;
                });
                Method("clear", _ => Call(p, (VexObject)Attr(controller, "screen"), "clear_screen"));
                Method("clear_line", a2 => Call(p, (VexObject)Attr(controller, "screen"), "clear_row", Ops.ToLong(a2.Need(0, "line")) + 1));
                Method("rumble", a2 => Call(p, controller, "rumble", a2.Need(0, "pattern")));
                Method("get_battery_capacity", _ => 100L);
                Method("get_battery_level", _ => 100L);
                Method("is_connected", _ => 1L);
            }

            private object Pressing(RobotProgram p, object button) {
                var n = (button as PyEnum)?.name?.Replace("E_CONTROLLER_DIGITAL_", "") ?? "A";
                var vexName = n switch { "UP" => "Up", "DOWN" => "Down", "LEFT" => "Left", "RIGHT" => "Right", _ => n };
                return Call(p, (VexObject)Attr(controller, "button" + vexName), "pressing");
            }

            private static object Attr(VexObject o, string name) => o.TryGetAttr(name, out var v) ? v : throw new PyError("AttributeError", "no " + name);
        }

        private sealed class ProsImu : VexObject, IInertialSource {
            public override string TypeName => "Imu";
            private readonly VexInertial imu;
            public VexInertial Inertial => imu;

            public ProsImu(RobotProgram p, Args a) {
                imu = new VexInertial(p, A(p, "Inertial", Ops.ToLong(a.Need(0, "port"))));
                Method("reset", a2 => {
                    Call(p, imu, "calibrate");
                    if (!a2.Bool(0, "blocking", false)) return 1L;
                    return new Suspend(() => !Ops.Truthy(Call(p, imu, "is_calibrating")), 1L);
                });
                Method("is_calibrating", _ => Call(p, imu, "is_calibrating"));
                Method("get_heading", _ => imu.Heading());
                Method("get_rotation", _ => imu.Rotation());
                Method("set_heading", a2 => { imu.SetHeading(a2.Number(0, "target", 0)); return 1L; });
                Method("set_rotation", a2 => { imu.SetRotation(a2.Number(0, "target", 0)); return 1L; });
                Method("tare_heading", _ => { imu.SetHeading(0); return 1L; });
                Method("tare_rotation", _ => { imu.SetRotation(0); return 1L; });
                Method("tare", _ => { imu.SetHeading(0); imu.SetRotation(0); return 1L; });
                Method("get_yaw", _ => Call(p, imu, "orientation", Units["YAW"], Units["DEGREES"]));
                Method("get_pitch", _ => Call(p, imu, "orientation", Units["PITCH"], Units["DEGREES"]));
                Method("get_roll", _ => Call(p, imu, "orientation", Units["ROLL"], Units["DEGREES"]));
                Method("is_installed", _ => Call(p, imu, "installed"));
            }
        }

        private sealed class ProsRotation : VexObject, IAngleSource {
            public override string TypeName => "Rotation";
            private readonly VexRotation sensor;
            private readonly RobotProgram program;

            public ProsRotation(RobotProgram p, Args a) {
                program = p;
                var port = (int)Ops.ToLong(a.Need(0, "port"));
                var reversed = port < 0 || a.Bool(1, "reverse_flag", false);
                sensor = new VexRotation(p, A(p, "Rotation", (long)Math.Abs(port), reversed));
                // PROS reads rotation sensors in centidegrees.
                Method("get_position", _ => (long)Math.Round(Degrees() * 100));
                Method("get_angle", _ => (long)Math.Round(Ops.ToDouble(Call(p, sensor, "angle", Units["DEGREES"])) * 100));
                Method("get_velocity", _ => (long)Math.Round(Ops.ToDouble(Call(p, sensor, "velocity", Units["DPS"])) * 100));
                Method("reset_position", _ => { Call(p, sensor, "reset_position"); return 1L; });
                Method("reset", _ => { Call(p, sensor, "reset_position"); return 1L; });
                Method("set_position", a2 => { Call(p, sensor, "set_position", Ops.ToDouble(a2.Need(0, "position")) / 100.0, Units["DEGREES"]); return 1L; });
                Method("set_reversed", a2 => { Call(p, sensor, "set_reversed", a2.Need(0, "value")); return 1L; });
                Method("reverse", _ => { Call(p, sensor, "set_reversed", true); return 1L; });
                Method("set_data_rate", _ => 1L);
            }

            public double Degrees() => Ops.ToDouble(Call(program, sensor, "position", Units["DEGREES"]));
        }

        /// <summary>V2: PROS's pros::Gps on the simulated sensor: positions and offsets in meters, heading 0-360 clockwise
        /// (the sensor's own: PROS has no heading offset). Gps(port), (port, xOffset, yOffset), (port, xInitial, yInitial,
        /// headingInitial), (port, xInitial, yInitial, headingInitial, xOffset, yOffset).</summary>
        private sealed class ProsGps : VexObject {
            public override string TypeName => "Gps";

            public ProsGps(RobotProgram p, Args a) {
                var gps = new VexGps(p, A(p, "Gps", Ops.ToLong(a.Need(0, "port"))));
                object Mm(double meters) => meters * 1000.0;
                void Offset(double x, double y) => Call(p, gps, "set_origin", Mm(x), Mm(y), Units["MM"]);
                void Place(double x, double y, double h) => Call(p, gps, "set_location", Mm(x), Mm(y), Units["MM"], h, Units["DEGREES"]);
                double N(int i) => Ops.ToDouble(a.pos[i]);
                if (a.Count == 3) Offset(N(1), N(2));
                else if (a.Count >= 4) {
                    Place(N(1), N(2), N(3));
                    if (a.Count >= 6) Offset(N(4), N(5));
                }
                double X() => Ops.ToDouble(Call(p, gps, "x_position", Units["MM"])) / 1000.0;
                double Y() => Ops.ToDouble(Call(p, gps, "y_position", Units["MM"])) / 1000.0;
                double H() => gps.Heading();
                double O(string axis) => Ops.ToDouble(Call(p, gps, "orientation", Units[axis], Units["DEGREES"]));
                double G(string axis) => Ops.ToDouble(Call(p, gps, "gyro_rate", Units[axis], Units["DPS"]));
                double Acc(string axis) => Ops.ToDouble(Call(p, gps, "acceleration", Units[axis]));
                VexStruct S(string type, params (string, object)[] fields) => new VexStruct(type, fields);
                Method("initialize_full", a2 => {
                    Place(a2.Number(0, "xInitial", 0), a2.Number(1, "yInitial", 0), a2.Number(2, "headingInitial", 0));
                    Offset(a2.Number(3, "xOffset", 0), a2.Number(4, "yOffset", 0));
                    return 1L;
                });
                Method("set_offset", a2 => { Offset(a2.Number(0, "xOffset", 0), a2.Number(1, "yOffset", 0)); return 1L; });
                Method("get_offset", _ => S("gps_position_s_t", ("x", 0.0), ("y", 0.0)));
                Method("set_position", a2 => { Place(a2.Number(0, "xInitial", 0), a2.Number(1, "yInitial", 0), a2.Number(2, "headingInitial", 0)); return 1L; });
                Method("set_data_rate", _ => 1L);
                Method("get_error", _ => (double)(p.Device<GpsSensor>(PortOf(gps), false, "GPS Sensor")?.ErrorInches ?? 0f) * 0.0254);
                Method("get_status", _ => S("gps_status_s_t", ("x", X()), ("y", Y()), ("pitch", O("PITCH")), ("roll", O("ROLL")), ("yaw", O("YAW"))));
                Add("get_position_and_orientation", Member("get_status"));
                Method("get_position", _ => S("gps_position_s_t", ("x", X()), ("y", Y()), ("heading", H())));
                Method("get_position_x", _ => X());
                Method("get_position_y", _ => Y());
                Method("get_orientation", _ => S("gps_orientation_s_t", ("pitch", O("PITCH")), ("roll", O("ROLL")), ("yaw", O("YAW"))));
                Method("get_pitch", _ => O("PITCH"));
                Method("get_roll", _ => O("ROLL"));
                Method("get_yaw", _ => O("YAW"));
                Method("get_heading", _ => H());
                Method("get_heading_raw", _ => gps.Rotation());
                Method("get_rotation", _ => gps.Rotation());
                Method("set_rotation", a2 => { Call(p, gps, "set_rotation", a2.Number(0, "target", 0), Units["DEGREES"]); return 1L; });
                Method("tare_rotation", _ => { Call(p, gps, "reset_rotation"); return 1L; });
                Method("get_gyro_rate", _ => S("gps_gyro_s_t", ("x", G("XAXIS")), ("y", G("YAXIS")), ("z", G("ZAXIS"))));
                Method("get_gyro_rate_x", _ => G("XAXIS"));
                Method("get_gyro_rate_y", _ => G("YAXIS"));
                Method("get_gyro_rate_z", _ => G("ZAXIS"));
                Method("get_accel", _ => S("gps_accel_s_t", ("x", Acc("XAXIS")), ("y", Acc("YAXIS")), ("z", Acc("ZAXIS"))));
                Method("get_accel_x", _ => Acc("XAXIS"));
                Method("get_accel_y", _ => Acc("YAXIS"));
                Method("get_accel_z", _ => Acc("ZAXIS"));
                Method("is_installed", _ => Call(p, gps, "installed"));
                Method("get_port", _ => (long)PortOf(gps));
            }

            private static int PortOf(VexGps g) => g.PortNumber;
        }

        /// <summary>A plain C struct (PROS's gps_position_s_t and the like): fields only.</summary>
        private sealed class VexStruct : VexObject {
            private readonly string type;
            public override string TypeName => type;
            public VexStruct(string type, (string name, object value)[] fields) {
                this.type = type;
                foreach (var (n, v) in fields) Field(n, v);
            }
        }

        /// <summary>V2: PROS's pros::Vision on the simulated sensor. Objects are vision_object_s_t (signature, type,
        /// left_coord, top_coord, width, height, angle, x_middle_coord, y_middle_coord); none found: signature 255.
        /// Signatures kept on the sensor (the Vision Utility's) count until set_signature replaces one.</summary>
        private sealed class ProsVision : VexObject {
            public override string TypeName => "Vision";
            private bool center;

            public ProsVision(RobotProgram p, Args a) {
                var port = (int)Ops.ToLong(a.Need(0, "port"));
                if (a.Count > 1 && a.pos[1] is PyEnum z) center = z.value == 1;
                VisionSensor Sensor() => p.Device<VisionSensor>(port, false, "Vision Sensor");
                List<VisionBlob> All() {
                    var s = Sensor();
                    if (s == null) return new List<VisionBlob>();
                    s.Snapshot(0, 1);   // (keeps it taking pictures)
                    return s.blobs;
                }
                object Obj(VisionBlob? found) {
                    var o = new ProsVisionObject(found, center);
                    return o;
                }
                Method("get_by_sig", a2 => {
                    var size = (int)a2.Number(0, "size_id", 0);
                    var sig = (int)a2.Number(1, "sig_id", 1);
                    var list = All().Where(b => !b.code && b.id == sig).ToList();
                    return Obj(size < list.Count ? list[size] : (VisionBlob?)null);
                });
                Method("get_by_size", a2 => {
                    var size = (int)a2.Number(0, "size_id", 0);
                    var list = All();
                    return Obj(size < list.Count ? list[size] : (VisionBlob?)null);
                });
                Method("get_by_code", a2 => {
                    var size = (int)a2.Number(0, "size_id", 0);
                    var code = (int)a2.Number(1, "color_code", 0);
                    var list = All().Where(b => b.code && b.id == code).ToList();
                    return Obj(size < list.Count ? list[size] : (VisionBlob?)null);
                });
                Method("get_object_count", _ => (long)All().Count);
                Method("set_signature", a2 => {
                    var s = Sensor();
                    if (s != null && a2.Need(1, "signature") is VexVisionSignature sig) {
                        var v = sig.sig;
                        v.id = (int)a2.Number(0, "signature_id", v.id);
                        s.SetSignature(v);
                    }
                    return 1L;
                });
                Method("get_signature", a2 => {
                    var s = Sensor();
                    var id = (int)a2.Number(0, "signature_id", 1);
                    var sig = s != null && id >= 1 && id <= 7 ? s.Signatures[id - 1] : null;
                    return sig.HasValue ? new VexVisionSignature(sig.Value) : (object)PyNone.Value;
                });
                Method("create_color_code", a2 => {
                    var ids = a2.pos.Select(x => (int)Ops.ToLong(x)).Where(x => x >= 1 && x <= 7).ToArray();
                    var s = Sensor();
                    if (s != null && ids.Length >= 2 && !s.codes.Any(c => c.SequenceEqual(ids))) s.codes.Add(ids);
                    return (long)VisionSensor.CodeId(ids);
                });
                Method("set_zero_point", a2 => { center = a2.Need(0, "zero_point") is PyEnum e && e.value == 1; return 1L; });
                Method("set_exposure", a2 => {
                    var s = Sensor();
                    if (s != null) s.brightness = Mathf.Clamp(Mathf.RoundToInt((float)a2.Number(0, "exposure", 75) / 150f * 100f), 1, 100);
                    return 1L;
                });
                Method("get_exposure", _ => (long)Mathf.RoundToInt((Sensor()?.brightness ?? 50) * 1.5f));
                foreach (var n in new[] { "set_led", "clear_led", "set_auto_white_balance", "set_white_balance", "set_wifi_mode", "print_signature" })
                    Method(n, _ => 1L);
                Method("get_white_balance", _ => 0L);
                Method("get_port", _ => (long)port);
            }
        }

        private sealed class ProsVisionObject : VexObject {
            public override string TypeName => "vision_object_s_t";
            public ProsVisionObject(VisionBlob? found, bool center) {
                var b = found ?? default;
                var dx = center ? -VisionSensor.Width / 2 : 0;
                var dy = center ? -VisionSensor.Height / 2 : 0;
                Field("signature", found.HasValue ? (long)b.id : 255L);
                Field("type", b.code ? 1L : 0L);
                Field("left_coord", (long)(b.x + dx));
                Field("top_coord", (long)(b.y + dy));
                Field("width", (long)b.width);
                Field("height", (long)b.height);
                Field("angle", (long)Mathf.RoundToInt(b.angle));
                Field("x_middle_coord", (long)(b.CenterX + dx));
                Field("y_middle_coord", (long)(b.CenterY + dy));
            }
        }

        private sealed class ProsSensor : VexObject {
            public override string TypeName => kind;
            private readonly string kind;

            public ProsSensor(RobotProgram p, Args a, string kind) {
                this.kind = kind;
                var port = Ops.ToLong(a.Need(0, "port"));
                if (kind == "Distance") {
                    var d = new VexDistance(p, A(p, "Distance", port));
                    Method("get", _ => (long)Math.Round(Ops.ToDouble(Call(p, d, "object_distance", Units["MM"]))));
                    Method("get_distance", _ => (long)Math.Round(Ops.ToDouble(Call(p, d, "object_distance", Units["MM"]))));
                    Method("get_confidence", _ => 63L);
                    Method("get_object_size", _ => Call(p, d, "object_rawsize"));
                    Method("get_object_velocity", _ => Call(p, d, "object_velocity"));
                }
                else {
                    var o = new VexOptical(p, A(p, "Optical", port));
                    Method("get_hue", _ => Call(p, o, "hue"));
                    Method("get_saturation", _ => 1.0);
                    Method("get_brightness", _ => Ops.ToDouble(Call(p, o, "brightness")) / 100.0);
                    Method("get_proximity", _ => (long)Math.Round(Ops.ToDouble(Call(p, o, "is_near_object")) * 255));
                    Method("set_led_pwm", a2 => { Call(p, o, "set_light_power", a2.Need(0, "value")); return 1L; });
                }
            }
        }

        // ADI ports: 'A'..'H', 'a'..'h' or 1..8.
        internal static ThreeWirePort Adi(object port) {
            var n = port switch {
                string s when s.Length == 1 => char.ToUpperInvariant(s[0]) - 'A' + 1,
                long l => (int)l,
                PyTuple t when t.items.Length == 2 => (int)(t.items[1] is string ts ? char.ToUpperInvariant(ts[0]) - 'A' + 1 : Ops.ToLong(t.items[1])),
                PyList l2 when l2.items.Count == 2 => (int)(l2.items[1] is string ls ? char.ToUpperInvariant(ls[0]) - 'A' + 1 : Ops.ToLong(l2.items[1])),
                _ => throw new PyError("TypeError", "an ADI port is 'A' to 'H' (or 1 to 8)"),
            };
            if (n < 1 || n > 8) throw new PyError("ValueError", "an ADI port is 'A' to 'H'");
            return new ThreeWirePort(n);
        }

        private sealed class ProsDigitalOut : VexObject {
            public override string TypeName => "DigitalOut";
            private bool state;

            public ProsDigitalOut(RobotProgram p, Args a, bool pneumatics) {
                var out_ = new VexSolenoid(p, A(p, "DigitalOut", Adi(a.Need(0, "port"))), "DigitalOut");
                void Set(bool v) {
                    state = v;
                    Call(p, out_, "set", v);
                }
                if (a.Bool(1, "init_state", false)) Set(true);
                Method("set_value", a2 => { Set(Ops.Truthy(a2.Need(0, "value"))); return 1L; });
                Method("extend", _ => { Set(true); return 1L; });
                Method("retract", _ => { Set(false); return 1L; });
                Method("toggle", _ => { Set(!state); return 1L; });
                Method("is_extended", _ => state);
                Method("get_value", _ => state);
                _ = pneumatics;
            }
        }

        private sealed class ProsDigitalIn : VexObject {
            public override string TypeName => "DigitalIn";
            private bool last;

            public ProsDigitalIn(RobotProgram p, Args a) {
                var input = new VexSwitch(p, A(p, "DigitalIn", Adi(a.Need(0, "port"))), "DigitalIn");
                Method("get_value", _ => Ops.Truthy(Call(p, input, "pressing")) ? 1L : 0L);
                Method("get_new_press", _ => {
                    var now = Ops.Truthy(Call(p, input, "pressing"));
                    var press = now && !last;
                    last = now;
                    return press ? 1L : 0L;
                });
            }
        }

        private sealed class ProsEncoder : VexObject, IAngleSource {
            public override string TypeName => "Encoder";
            private readonly VexShaft encoder;
            private readonly RobotProgram program;
            private readonly bool reversed;

            public ProsEncoder(RobotProgram p, Args a) {
                program = p;
                encoder = new VexShaft(p, A(p, "Encoder", Adi(a.Need(0, "port_top"))), "Encoder");
                reversed = a.Bool(2, "reversed", false);
                Method("get_value", _ => (long)Math.Round(Degrees()));
                Method("reset", _ => { Call(p, encoder, "reset_position"); return 1L; });
            }

            public double Degrees() => Ops.ToDouble(Call(program, encoder, "position", Units["DEGREES"])) * (reversed ? -1 : 1);
        }

        private sealed class ProsTask : VexObject {
            public override string TypeName => "Task";

            public ProsTask(RobotProgram p, Args a) {
                var fn = a.Need(0, "function");
                // Task(function, [parameters], [name]) or Task(function, name)
                var args = a.Count > 1 && !(a.pos[1] is string) && !(a.pos[1] is PyNone) ? new[] { a.pos[1] } : null;
                var fiber = p.vm.Start(fn, args, a.pos.OfType<string>().FirstOrDefault() ?? "task");
                Method("remove", _ => { fiber.Stop(); return PyNone.Value; });
                Method("suspend", _ => PyNone.Value);
                Method("resume", _ => PyNone.Value);
                Method("get_state", _ => fiber.Done ? 4L : 1L);
                Method("get_name", _ => "task");
                Method("notify", _ => 1L);
            }
        }
    }
}
