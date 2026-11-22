using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Protobot.Devices;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9): the robot's configuration as VEXcode writes it at the top of a program ("Insert devices" in the
    /// code window): every motor on its port with its cartridge and direction, the drive's motor groups and a
    /// DriveTrain (or SmartDrive with an Inertial sensor) measured from the build (wheel size, track width, gear
    /// ratio from the gear train), the sensors and the pneumatics. It goes between two marker comments, so inserting
    /// again replaces it (after the robot changed) and leaves the rest of the program alone.
    /// </summary>
    public static class DeviceConfig {
        public const string Begin = "# ---- Robot configuration (code window: Insert devices) ----";
        public const string End = "# ---- End of robot configuration ----";
        public const string CppBegin = "// ---- Robot configuration (code window: Insert devices) ----";
        public const string CppEnd = "// ---- End of robot configuration ----";

        public static bool UsesVexcode(string source) => source.Contains("vex.h") || source.Contains("using namespace vex") || source.Contains("vex::");

        /// <summary>V2: the configuration in C++: PROS (signed ports, pros::MotorGroup, pros::Imu, adi) or VEXcode C++
        /// (motor(PORT1, ratio18_1, false), motor_group, drivetrain/smartdrive), measured as the Python one.</summary>
        public static string GenerateCpp(bool pros, bool ez = false) {
            LastDrive = null;
            LastImu = null;
            V5Brain.Refresh(true);
            var sb = new StringBuilder();
            var used = new HashSet<string> { "controller", "brain", "Brain", "Controller1" };
            sb.AppendLine(CppBegin);
            if (pros) sb.AppendLine("#include \"main.h\"");
            else {
                sb.AppendLine("#include \"vex.h\"");
                sb.AppendLine("using namespace vex;");
            }
            sb.AppendLine();
            sb.AppendLine(pros ? "pros::Controller controller(pros::E_CONTROLLER_MASTER);" : "brain Brain;\ncontroller Controller1 = controller(primary);");
            var devices = V5Brain.Devices.ToList();
            var motors = devices.OfType<MotorDevice>().Where(m => m.motor != null).ToList();
            var leftMotors = motors.Where(m => Controls.Side(m.part) == "L").ToList();
            var rightMotors = motors.Where(m => Controls.Side(m.part) == "R").ToList();
            string Gear(int rpm) => pros ? (rpm == 100 ? "pros::MotorGearset::red" : rpm == 600 ? "pros::MotorGearset::blue" : "pros::MotorGearset::green")
                                         : (rpm == 100 ? "ratio36_1" : rpm == 600 ? "ratio6_1" : "ratio18_1");
            long Signed(MotorDevice m) => Controls.Reversed(m.part) ? -m.port : m.port;
            var names = new Dictionary<MotorDevice, string>();
            var lefts = 0;
            var rights = 0;
            foreach (var m in motors) {
                var side = Controls.Side(m.part);
                var given = Controls.GivenName(m.part);
                names[m] = Name(given ?? (side == "L" ? "left_motor_" + Letter(lefts++) : side == "R" ? "right_motor_" + Letter(rights++) : "motor_" + m.port), used);
            }
            if (motors.Any(m => !pros || !(leftMotors.Contains(m) || rightMotors.Contains(m)))) {
                sb.AppendLine();
                sb.AppendLine(pros ? "// Motors: port (negative: reversed), cartridge (red 100 rpm, green 200, blue 600)"
                                   : "// Motors: port, cartridge (ratio36_1 red 100 rpm, ratio18_1 green 200, ratio6_1 blue 600), reversed");
            }
            foreach (var m in motors) {
                if (pros && (leftMotors.Contains(m) || rightMotors.Contains(m))) continue;   // (in the drive's groups below)
                sb.AppendLine(pros ? $"pros::Motor {names[m]}({Signed(m)}, {Gear(m.motor.Rpm)});"
                                   : $"motor {names[m]} = motor(PORT{m.port}, {Gear(m.motor.Rpm)}, {(Controls.Reversed(m.part) ? "true" : "false")});");
            }
            string imuName = null;
            var sensors = devices.Where(d => d.kind != DeviceKind.Motor && d.kind != DeviceKind.Solenoid && d.kind != DeviceKind.Radio && d.kind != DeviceKind.Expander).ToList();
            if (sensors.Count > 0) {
                sb.AppendLine();
                sb.AppendLine("// Sensors");
            }
            // V2: 3-Wire Expanders holding devices (the ones past the brain's A-H)
            if (!pros)
                foreach (var e in devices.Where(d => d.threeWire && d.expander > 0).Select(d => d.expander).Distinct().OrderBy(e => e))
                    sb.AppendLine($"triport Expander{e} = triport(PORT{e});");
            foreach (var d in sensors) {
                // V2: PROS ports on an expander are {{smart port, 'A'}}; two-wire sensors name both ports.
                string Pros3(int count = 1) {
                    var letters = string.Join(", ", Enumerable.Range(0, count).Select(i => "'" + Port3(d.port + i) + "'"));
                    return d.expander > 0 ? "{{" + d.expander + ", " + letters + "}}" : letters;
                }
                string Line(string prosType, string vexType, string fallback, bool threeWire, int prosPorts = 1, string prosExtra = "", string vexExtra = "") {
                    var n = Name(Controls.GivenName(d.part) ?? fallback, used);
                    if (d.kind == DeviceKind.Inertial) LastImu = imuName = n;
                    if (pros) return threeWire ? $"pros::adi::{prosType} {n}({Pros3(prosPorts)}{prosExtra});" : $"pros::{prosType} {n}({d.port});";
                    return threeWire ? $"{vexType} {n} = {vexType}({Ref3(d, "vex")}{vexExtra});" : $"{vexType} {n} = {vexType}(PORT{d.port});";
                }
                string Accel() {
                    var n = Name(Controls.GivenName(d.part) ?? "accel_" + Tag3(d), used);
                    return string.Join("\n", Enumerable.Range(0, 3).Select(i => {
                        var axis = i == 0 ? n : Name(n + "_" + "yz"[i - 1], used);
                        var prosPort = d.expander > 0 ? "{{" + d.expander + ", '" + Port3(d.port + i) + "'}}" : "'" + Port3(d.port + i) + "'";
                        return pros ? $"pros::adi::AnalogIn {axis}({prosPort});" + (i == 0 ? "   // accelerometer X (Y, Z below): get_value() 0-4095" : "")
                                    : $"accelerometer {axis} = accelerometer({Ref3(d, "vex", i)}, true);" + (i == 0 ? "   // X (Y, Z below), in g" : "");
                    }));
                }
                if (d.kind == DeviceKind.Vision || d.kind == DeviceKind.Gps || d.kind == DeviceKind.AiVision) {   // V2
                    var text = d.kind == DeviceKind.Vision ? VisionLines(d, used, pros ? "pros" : "vex", out _) : d.kind == DeviceKind.Gps ? GpsLines(d, used, pros ? "pros" : "vex", out _) : AiVisionLines(d, used, pros ? "pros" : "vex", out _);
                    foreach (var l in text.Split('\n')) sb.AppendLine(l);
                    continue;
                }
                var line = d.kind switch {
                    DeviceKind.Inertial => Line("Imu", "inertial", "imu", false),
                    DeviceKind.Distance => Line("Distance", "distance", "distance_" + d.port, false),
                    DeviceKind.Optical => Line("Optical", "optical", "optical_" + d.port, false),
                    DeviceKind.Rotation => Line("Rotation", "rotation", "rotation_" + d.port, false),
                    DeviceKind.Bumper => Line("DigitalIn", "bumper", "bumper_" + Tag3(d), true),
                    DeviceKind.Limit => Line("DigitalIn", "limit", "limit_" + Tag3(d), true),
                    DeviceKind.Encoder => Line("Encoder", "encoder", "encoder_" + Tag3(d), true, 2, ", false"),
                    DeviceKind.Potentiometer => Line("Potentiometer", "potV2", "pot_" + Tag3(d), true, 1, ", pros::E_ADI_POT_V2"),
                    DeviceKind.LineTracker => Line("LineSensor", "line", "line_" + Tag3(d), true),
                    DeviceKind.Light => Line("LightSensor", "light", "light_" + Tag3(d), true),
                    DeviceKind.Ultrasonic => Line("Ultrasonic", "sonar", "sonar_" + Tag3(d), true, 2),
                    DeviceKind.Gyro => Line("Gyro", "gyro", "gyro_" + Tag3(d), true),
                    DeviceKind.Accelerometer => Accel(),
                    _ => "// " + V5Device.KindName(d.kind) + " on port " + d.port + ": not in the C++ libraries here yet",
                };
                sb.AppendLine(line);
            }
            var solenoids = devices.OfType<SolenoidDevice>().ToList();
            if (solenoids.Count > 0) {
                sb.AppendLine();
                sb.AppendLine(pros ? "// Pneumatics: .set_value(true) extends, .set_value(false) retracts" : "// Pneumatics: .set(true) extends, .set(false) retracts");
            }
            foreach (var sol in solenoids) {
                var given = sol.part != null ? Controls.GivenName(sol.part) : sol.own != null ? Controls.GivenName(sol.own.gameObject) : null;
                var n = Name(given ?? "piston_" + Tag3(sol), used);
                var prosPort = sol.expander > 0 ? "{{" + Ref3(sol, "pros") + "}}" : Ref3(sol, "pros");
                sb.AppendLine(pros ? $"pros::adi::DigitalOut {n}({prosPort});" : $"digital_out {n} = digital_out({Ref3(sol, "vex")});");
            }
            if (leftMotors.Count > 0 && rightMotors.Count > 0) {
                var (diameter, track, ratio, measured) = MeasureDrive(leftMotors, rightMotors);
                LastDrive = (diameter, track, ratio, leftMotors[0].motor.Rpm, imuName != null);
                var wheelRpm = leftMotors[0].motor.Rpm / Math.Max(0.01f, ratio);
                if (Mathf.Abs(wheelRpm - Mathf.Round(wheelRpm)) < 0.5f) wheelRpm = Mathf.Round(wheelRpm);   // (360, not 359.928)
                sb.AppendLine();
                sb.AppendLine("// Drivetrain" + (measured ? " (measured from the build: " + F(diameter) + "\" wheels, track width " + F(track) + "\", " + F(wheelRpm) + " rpm at the wheels)" : ""));
                if (pros && ez) {
                    // V2: EZ-Template makes the drive's motors itself.
                    var imuPort = devices.FirstOrDefault(d => d.kind == DeviceKind.Inertial)?.port ?? 0;
                    sb.AppendLine("// EZ-Template: left ports, right ports (negative: reversed), IMU port, wheel diameter, rpm at the wheels");
                    if (imuPort == 0) sb.AppendLine("// (no Inertial sensor on the robot: EZ-Template needs one for turns; add one and Insert devices again)");
                    sb.AppendLine($"ez::Drive chassis({{{string.Join(", ", leftMotors.Select(Signed))}}}, {{{string.Join(", ", rightMotors.Select(Signed))}}}, {imuPort}, {F(diameter)}, {F(wheelRpm)});");
                }
                else if (pros) {
                    sb.AppendLine($"pros::MotorGroup left_drive({{{string.Join(", ", leftMotors.Select(Signed))}}}, {Gear(leftMotors[0].motor.Rpm)});");
                    sb.AppendLine($"pros::MotorGroup right_drive({{{string.Join(", ", rightMotors.Select(Signed))}}}, {Gear(rightMotors[0].motor.Rpm)});");
                    sb.AppendLine($"// LemLib: lemlib::Drivetrain drivetrain(&left_drive, &right_drive, {F(track)}, {F(diameter)}, {F(wheelRpm)}, 2);");
                }
                else {
                    sb.AppendLine($"motor_group left_drive = motor_group({string.Join(", ", leftMotors.Select(m => names[m]))});");
                    sb.AppendLine($"motor_group right_drive = motor_group({string.Join(", ", rightMotors.Select(m => names[m]))});");
                    var travel = F(Mathf.PI * diameter);
                    sb.AppendLine(imuName != null
                        ? $"smartdrive Drivetrain = smartdrive(left_drive, right_drive, {imuName}, {travel}, {F(track)}, {F(track)}, inches, {F(ratio)});"
                        : $"drivetrain Drivetrain = drivetrain(left_drive, right_drive, {travel}, {F(track)}, {F(track)}, inches, {F(ratio)});");
                }
            }
            sb.AppendLine(CppEnd);
            return sb.ToString();
        }

        /// <summary>The drive measured by the last Generate (examples fill their numbers in from it); null: none.</summary>
        public static (float diameter, float track, float ratio, float motorRpm, bool imu)? LastDrive;
        /// <summary>V2: the name the last configuration gave the Inertial sensor (null: none), for the examples.</summary>
        public static string LastImu;

        public static string Generate() {
            LastDrive = null;
            LastImu = null;
            V5Brain.Refresh(true);
            var sb = new StringBuilder();
            var used = new HashSet<string> { "brain", "controller" };
            sb.AppendLine(Begin);
            sb.AppendLine("from vex import *");
            sb.AppendLine();
            sb.AppendLine("brain = Brain()");
            sb.AppendLine("controller = Controller()");

            var devices = V5Brain.Devices.ToList();
            var motors = devices.OfType<MotorDevice>().Where(m => m.motor != null).ToList();
            var left = new List<string>();
            var right = new List<string>();
            var leftMotors = new List<MotorDevice>();
            var rightMotors = new List<MotorDevice>();
            if (motors.Count > 0) {
                sb.AppendLine();
                sb.AppendLine("# Motors: port, cartridge (red 36:1 100 rpm, green 18:1 200 rpm, blue 6:1 600 rpm), reversed");
            }
            var lefts = 0;
            var rights = 0;
            foreach (var m in motors) {
                var side = Controls.Side(m.part);
                var given = Controls.GivenName(m.part);
                var name = Name(given ?? (side == "L" ? "left_motor_" + Letter(lefts) : side == "R" ? "right_motor_" + Letter(rights) : "motor_" + m.port), used);
                if (side == "L") { lefts++; left.Add(name); leftMotors.Add(m); }
                else if (side == "R") { rights++; right.Add(name); rightMotors.Add(m); }
                var ratio = m.motor.Rpm == 100 ? "RATIO_36_1" : m.motor.Rpm == 600 ? "RATIO_6_1" : "RATIO_18_1";
                var rev = Controls.Reversed(m.part) ? "True" : "False";
                sb.AppendLine($"{name} = Motor(Ports.PORT{m.port}, GearSetting.{ratio}, {rev})");
            }

            var imu = devices.FirstOrDefault(d => d.kind == DeviceKind.Inertial);
            string imuName = null;
            var sensors = devices.Where(d => d.kind != DeviceKind.Motor && d.kind != DeviceKind.Solenoid && d.kind != DeviceKind.Radio && d.kind != DeviceKind.Expander).ToList();
            if (sensors.Count > 0) {
                sb.AppendLine();
                sb.AppendLine("# Sensors");
            }
            // V2: 3-Wire Expanders holding devices (the ones past the brain's A-H)
            foreach (var e in devices.Where(d => d.threeWire && d.expander > 0).Select(d => d.expander).Distinct().OrderBy(e => e))
                sb.AppendLine($"expander_{e} = Triport(Ports.PORT{e})");
            foreach (var d in sensors) {
                var line = SensorLine(d, used, out var name);
                if (line == null) continue;
                if (d == imu) LastImu = imuName = name;
                sb.AppendLine(line);
            }
            var solenoids = devices.OfType<SolenoidDevice>().ToList();
            if (solenoids.Count > 0) {
                sb.AppendLine();
                sb.AppendLine("# Pneumatics: .set(True) extends, .set(False) retracts");
            }
            foreach (var s in solenoids) {
                var given = s.part != null ? Controls.GivenName(s.part) : s.own != null ? Controls.GivenName(s.own.gameObject) : null;
                var name = Name(given ?? "piston_" + Tag3(s), used);
                sb.AppendLine($"{name} = DigitalOut({Ref3(s, "py")})");
            }

            if (left.Count > 0 && right.Count > 0) {
                var (diameter, track, ratio, measured) = MeasureDrive(leftMotors, rightMotors);
                LastDrive = (diameter, track, ratio, leftMotors[0].motor.Rpm, imuName != null);
                sb.AppendLine();
                sb.AppendLine("# Drivetrain" + (measured ? " (measured from the build: " + F(diameter) + "\" wheels, track width " + F(track) + "\", "
                                                       + F(ratio) + " motor turns per wheel turn)" : " (set the sides in Edit > Devices: Left/Right)"));
                sb.AppendLine($"left_drive = MotorGroup({string.Join(", ", left)})");
                sb.AppendLine($"right_drive = MotorGroup({string.Join(", ", right)})");
                var travel = F(Mathf.PI * diameter);
                if (imuName != null)
                    sb.AppendLine($"drivetrain = SmartDrive(left_drive, right_drive, {imuName}, {travel}, {F(track)}, {F(track)}, INCHES, {F(ratio)})");
                else
                    sb.AppendLine($"drivetrain = DriveTrain(left_drive, right_drive, {travel}, {F(track)}, {F(track)}, INCHES, {F(ratio)})");
            }
            sb.AppendLine(End);
            return sb.ToString();
        }

        /// <summary>V2: an EZ-Template program (its chassis is an ez::Drive).</summary>
        public static bool UsesEz(string source) => source != null && (source.Contains("ez::") || source.Contains("EZ-Template"));

        /// <summary>The program with its configuration block put in (replacing an old one), at the top.</summary>
        public static string InsertInto(string source) {
            source ??= "";
            // V2: a C++ program gets C++ (PROS style, or VEXcode C++ when it uses vex.h).
            var cpp = Scripting.CppTranslator.LooksLikeCpp(source);
            var config = (cpp ? GenerateCpp(!UsesVexcode(source), UsesEz(source)) : Generate()).TrimEnd('\n', '\r');
            var begin = cpp ? CppBegin : Begin;
            var stop = cpp ? CppEnd : End;
            var start = source.IndexOf(begin, StringComparison.Ordinal);
            var end = source.IndexOf(stop, StringComparison.Ordinal);
            if (start >= 0 && end > start) return source.Substring(0, start) + config + source.Substring(end + stop.Length);
            // A program that imports vex already: the block (which imports it too) goes first.
            return config + "\n\n" + source;
        }

        private static string SensorLine(V5Device d, HashSet<string> used, out string name) {
            name = null;
            if (d.kind == DeviceKind.Vision) return VisionLines(d, used, "py", out name);   // V2
            if (d.kind == DeviceKind.Gps) return GpsLines(d, used, "py", out name);         // V2
            if (d.kind == DeviceKind.AiVision) return AiVisionLines(d, used, "py", out name);   // V2
            string Smart(string cls, string fallback) {
                var n = Name(Controls.GivenName(d.part) ?? fallback, used);
                return $"{n} = {cls}(Ports.PORT{d.port})";
            }
            string Three(string cls, string fallback, bool twoPorts = false) {
                var n = Name(Controls.GivenName(d.part) ?? fallback, used);
                return $"{n} = {cls}({Ref3(d, "py")})";
            }
            string Accel() {   // V2: VEXcode makes one Accelerometer per axis lead (X, Y, Z on three ports in a row)
                var n = Name(Controls.GivenName(d.part) ?? "accel_" + Tag3(d), used);
                var lines = new List<string>();
                for (var i = 0; i < 3; i++)
                    lines.Add($"{(i == 0 ? n : Name(n + "_" + "yz"[i - 1], used))} = Accelerometer({Ref3(d, "py", i)}, True)" + (i == 0 ? "  # X; Y and Z below (g)" : ""));
                return string.Join("\n", lines);
            }
            var line = d.kind switch {
                DeviceKind.Inertial => Smart("Inertial", "imu"),
                DeviceKind.Distance => Smart("Distance", "distance_" + d.port),
                DeviceKind.Optical => Smart("Optical", "optical_" + d.port),
                DeviceKind.Rotation => Smart("Rotation", "rotation_" + d.port),
                DeviceKind.Gps => null,
                DeviceKind.Vision => null,
                DeviceKind.Bumper => Three("Bumper", "bumper_" + Tag3(d)),
                DeviceKind.Limit => Three("Limit", "limit_" + Tag3(d)),
                DeviceKind.Potentiometer => Three("PotentiometerV2", "pot_" + Tag3(d)),
                DeviceKind.Encoder => Three("Encoder", "encoder_" + Tag3(d)),
                DeviceKind.LineTracker => Three("Line", "line_" + Tag3(d)),
                DeviceKind.Light => Three("Light", "light_" + Tag3(d)),
                DeviceKind.Ultrasonic => Three("Sonar", "sonar_" + Tag3(d)),
                DeviceKind.Gyro => Three("Gyro", "gyro_" + Tag3(d)),
                DeviceKind.Accelerometer => Accel(),
                _ => null,
            };
            if (line != null) name = line.Substring(0, line.IndexOf(' '));
            return line;
        }

        /// <summary>V2: an AI Vision Sensor with the color descriptions kept on it (its card's colors), as VEXcode writes
        /// them (ai_vision_3__Red = Colordesc(1, ...); ai_vision_3 = AiVision(Ports.PORT3, ai_vision_3__Red,
        /// AiVision.ALL_AIOBJS)). PROS: the colors as aivision_color_s_t values for set_color.</summary>
        internal static string AiVisionLines(V5Device d, HashSet<string> used, string language, out string name) {
            var n = Name(Controls.GivenName(d.part) ?? (language == "vex" ? "AIVision" + d.port : "ai_vision_" + d.port), used);
            name = n;
            var lines = new List<string>();
            var names = new List<string>();
            string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            foreach (var c in AiVisionSensor.Saved(d.part).Where(c => c.HasValue).Select(c => c.Value)) {
                var cn = n + "__" + VisionSignature.Clean(c.name);
                for (var i = 2; used.Contains(cn); i++) cn = n + "__" + VisionSignature.Clean(c.name) + "_" + i;
                used.Add(cn);
                names.Add(cn);
                var values = string.Join(", ", c.id, c.r, c.g, c.b, F(c.hue), F(c.saturation));
                lines.Add(language switch {
                    "pros" => $"pros::aivision_color_s_t {cn} = pros::AIVision::Color({values});",
                    "vex" => $"aivision::colordesc {cn} = aivision::colordesc({values});",
                    _ => $"{cn} = Colordesc({values})",
                });
            }
            var args = string.Join(", ", new[] { language == "py" ? "Ports.PORT" + d.port : "PORT" + d.port }.Concat(names)
                .Concat(new[] { language == "py" ? "AiVision.ALL_AIOBJS" : "aivision::ALL_AIOBJS" }));
            lines.Add(language switch {
                "pros" => $"pros::AIVision {n}({d.port});" + (names.Count > 0 ? $"   // give it its colors in initialize(): {n}.set_color({names[0]});" : ""),
                "vex" => $"aivision {n} = aivision({args});",
                _ => $"{n} = AiVision({args})",
            });
            return string.Join("\n", lines);
        }

        /// <summary>V2: a GPS Sensor with where it sits on the robot, measured from the build as VEXcode's device window
        /// asks for it: its X/Y offset from the turning center (the middle between the left and right drive motors; x to
        /// the right, y forward) and the way it faces from the robot's front (180: on the back facing back).</summary>
        internal static string GpsLines(V5Device d, HashSet<string> used, string language, out string name) {
            var n = Name(Controls.GivenName(d.part) ?? (language == "vex" ? "GPS" + d.port : "gps_" + d.port), used);
            name = n;
            var (ox, oy, heading) = GpsMount(d);
            string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            return language switch {
                "pros" => $"pros::Gps {n}({d.port}, {F2(ox * 0.0254)}, {F2(oy * 0.0254)});   // offset in meters" +
                          (Math.Abs(heading) > 0.5 ? $"; it faces {Math.Round(heading)}° from the robot's front: get_heading() - {Math.Round(heading)} is the robot's heading" : ""),
                "vex" => $"gps {n} = gps(PORT{d.port}, {F2(ox * 25.4)}, {F2(oy * 25.4)}, mm, {F2(heading)}, right);",
                _ => $"{n} = Gps(Ports.PORT{d.port}, {F2(ox * 25.4)}, {F2(oy * 25.4)}, MM, {F2(heading)}, RIGHT)",
            };
        }

        /// <summary>Where a GPS sits on the robot: inches right and forward of the turning center, and its turn
        /// clockwise from the robot's front (no left/right drive motors: no offset).</summary>
        /// <summary>V2: the robot's turning center and axes: the middle of the left and right drive wheels (the wheels on
        /// the gear trains of the L and R drive motors; a robot turns about its wheels, and on a geared drive the motors
        /// can sit inches away: the 6-motor drive's are 1.5" ahead of its wheels, so odometry measured from them moved
        /// an inch in every turn), else the middle of the left and right drive motors. rightAxis: left to right, front:
        /// forward, both level. False without left and right drive motors (center zero, the build's axes).</summary>
        internal static bool DriveFrame(List<Rotor> rotors, out Vector3 center, out Vector3 rightAxis, out Vector3 front) {
            Vector3 left = Vector3.zero, right = Vector3.zero;
            int l = 0, r = 0;
            foreach (var motor in MotorDrive.Live()) {
                var side = Controls.Side(motor.gameObject);
                if (side == "L") { left += motor.transform.position; l++; }
                else if (side == "R") { right += motor.transform.position; r++; }
            }
            center = Vector3.zero;
            rightAxis = Vector3.right;
            front = Vector3.forward;
            if (l == 0 || r == 0) return false;
            var across = right / r - left / l;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-4f) return false;
            rightAxis = across.normalized;
            front = Vector3.Cross(rightAxis, Vector3.up);
            center = (left / l + right / r) / 2f;
            if (rotors == null) return true;
            Vector3 wl = Vector3.zero, wr = Vector3.zero;
            int nl = 0, nr = 0;
            var seen = new HashSet<GameObject>();
            foreach (var rotor in rotors) {
                if (!rotor.motors.Any()) continue;
                var system = GearTrain.SystemOf(rotor, rotors);
                var sides = system.Motors.Select(m => Controls.Side(m.gameObject)).Distinct().ToList();
                if (sides.Count != 1 || sides[0] != "L" && sides[0] != "R") continue;
                foreach (var sysRotor in system.rotors)
                    foreach (var part in sysRotor.parts) {
                        if (part == null || !seen.Add(part)) continue;
                        var id = part.GetComponent<SavedObject>()?.id ?? "";
                        if (!(id.StartsWith("OMNI") || id.StartsWith("TWHL") || id.StartsWith("WHL") || id.Contains("Omni") || id.Contains("Wheel"))) continue;
                        var at = part.TryGetComponent(out MeshFilter f) && f.sharedMesh != null ? part.transform.TransformPoint(f.sharedMesh.bounds.center) : part.transform.position;
                        if (sides[0] == "L") { wl += at; nl++; }
                        else { wr += at; nr++; }
                    }
            }
            if (nl > 0 && nr > 0) center = (wl / nl + wr / nr) / 2f;
            return true;
        }

        /// <summary>V2: a tracking wheel found on the robot: a Rotation sensor or encoder whose shaft carries an
        /// unpowered wheel. vertical: it rolls forward; offset: inches from the turning center (DriveFrame) to its side (vertical:
        /// right +, left -) or along (horizontal: front +, back -), as LemLib and the motion module take it;
        /// reversed: the sensor counts down going forward (or right) as mounted, so code reverses it.</summary>
        internal struct TrackingWheelInfo {
            public ShaftSensor sensor;
            public bool vertical, reversed;
            public double diameter, offset;
        }

        private static readonly double[] WheelSizes = { 2.125, 2.75, 3.25, 4.0 };

        internal static List<TrackingWheelInfo> TrackingWheels() {
            var found = new List<TrackingWheelInfo>();
            // The robot's frame: its turning center (DriveFrame), else the build's axes.
            List<Rotor> rotors;
            try { rotors = GearTrain.Current(); }
            catch { return found; }
            DriveFrame(rotors, out var center, out var rightAxis, out var front);
            foreach (var d in V5Brain.Devices.OfType<ShaftSensor>()) {
                if (d.kind == DeviceKind.Potentiometer || d.part == null) continue;
                var rotor = rotors.FirstOrDefault(x => x.shaft != null && x.bearings.Any(b => b.part == d.part));
                if (rotor == null || GearTrain.SystemOf(rotor, rotors).Motors.Any()) continue;
                var wheel = rotor.parts.FirstOrDefault(p => {
                    var id = p.GetComponent<SavedObject>()?.id ?? "";
                    return id.StartsWith("OMNI") || id.StartsWith("TWHL") || id.Contains("Omni") || id.Contains("Wheel");
                });
                if (wheel == null || !wheel.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                var axle = rotor.axis.normalized;
                // its size: the rim's farthest point from the axle, a VEX size when it's within 0.03"
                var mesh = filter.sharedMesh;
                var axisLocal = wheel.transform.InverseTransformDirection(axle).normalized;
                var most = 0f;
                if (mesh.isReadable) foreach (var v in mesh.vertices) most = Mathf.Max(most, Vector3.ProjectOnPlane(v - mesh.bounds.center, axisLocal).sqrMagnitude);
                double diameter = Math.Round(2f * Mathf.Sqrt(most) * wheel.transform.lossyScale.x, 3);
                foreach (var size in WheelSizes) if (Math.Abs(size - diameter) < 0.03) diameter = size;
                var at = wheel.transform.TransformPoint(mesh.bounds.center) - center;
                var vertical = Mathf.Abs(Vector3.Dot(axle, front)) < 0.5f;
                // Rolling along `way`, a wheel turns about up x way; the sensor counts up turning about its own axis.
                var way = vertical ? front : rightAxis;
                var counts = Vector3.Dot(Vector3.Cross(Vector3.up, way), d.AxisWorld) * (Controls.Reversed(d.part) ? -1f : 1f);
                found.Add(new TrackingWheelInfo {
                    sensor = d, vertical = vertical, reversed = counts < 0f, diameter = diameter,
                    offset = Math.Round(Vector3.Dot(at, vertical ? rightAxis : front), 2),
                });
            }
            return found;
        }

        /// <summary>V2: the odometry sensors for the motion module / LemLib from the robot's tracking wheels (two of
        /// each kind at most): the lines that make them and the OdomSensors arguments, or null with none.</summary>
        internal static (List<string> lines, string args)? TrackingLines(bool cpp, string imu) {
            var wheels = TrackingWheels();
            if (wheels.Count == 0) return null;
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var lines = new List<string>();
            var args = new string[4];
            var counts = new int[2];
            foreach (var w in wheels.OrderBy(w => w.vertical ? 0 : 1).ThenBy(w => w.offset)) {
                var k = w.vertical ? 0 : 1;
                if (counts[k] >= 2) continue;
                var n = (w.vertical ? "vertical" : "horizontal") + (counts[k] > 0 ? "2" : "");
                var where = w.vertical ? (w.offset < 0 ? "left of" : w.offset > 0 ? "right of" : "on") : (w.offset < 0 ? "behind" : w.offset > 0 ? "ahead of" : "on");
                var note = $"{(w.vertical ? "rolls forward" : "rolls sideways")}, {F(Math.Abs(w.offset))}\" {where} the turning center";
                if (cpp) {
                    lines.Add($"pros::Rotation {n}_rotation({(w.reversed ? "-" : "")}{w.sensor.port});");
                    lines.Add($"lemlib::TrackingWheel {n}_wheel(&{n}_rotation, {F(w.diameter)}, {F(w.offset)});   // {note}");
                    args[k * 2 + counts[k]] = "&" + n + "_wheel";
                }
                else {
                    lines.Add($"{n}_wheel = TrackingWheel(Rotation(Ports.PORT{w.sensor.port}, {(w.reversed ? "True" : "False")}), {F(w.diameter)}, {F(w.offset)})   # {note}");
                    args[k * 2 + counts[k]] = n + "_wheel";
                }
                counts[k]++;
            }
            var none = cpp ? "nullptr" : "None";
            return (lines, string.Join(", ", args[0] ?? none, args[1] ?? none, args[2] ?? none, args[3] ?? none, imu));
        }

        internal static (double ox, double oy, double heading) GpsMount(V5Device d) {
            List<Rotor> rotors = null;
            try { rotors = GearTrain.Current(); }
            catch { }
            if (!DriveFrame(rotors, out var center, out var rightAxis, out var front)) return (0, 0, 0);
            var (o, dir) = SensorFrame.Look(d.part);
            var at = d.part.transform.TransformPoint(o) - center;
            var look = Vector3.ProjectOnPlane(d.part.transform.TransformDirection(dir), Vector3.up);
            var heading = look.sqrMagnitude < 1e-4f ? 0f : Mathf.Repeat(Vector3.SignedAngle(front, look, Vector3.up), 360f);
            if (heading > 359.5f) heading = 0f;
            return (Math.Round(Vector3.Dot(at, rightAxis), 2), Math.Round(Vector3.Dot(at, front), 2), Math.Round(heading, 1));
        }

        /// <summary>V2: a Vision Sensor and the signatures kept on it (made in its Vision Utility), as VEXcode writes
        /// them: vision_5__RED = Signature(1, ...), vision_5 = Vision(Ports.PORT5, 50, vision_5__RED). PROS keeps the
        /// signatures on the sensor, so only their values are written (set_signature puts them back from code).</summary>
        internal static string VisionLines(V5Device d, HashSet<string> used, string language, out string name) {
            var n = Name(Controls.GivenName(d.part) ?? "vision_" + d.port, used);
            name = n;
            var bright = VisionSensor.SavedBrightness(d.part);
            var lines = new List<string>();
            var sigNames = new List<string>();
            foreach (var s in VisionSensor.Saved(d.part).Where(s => s.HasValue).Select(s => s.Value)) {
                var sn = n + "__" + VisionSignature.Clean(s.name);
                for (var i = 2; used.Contains(sn); i++) sn = n + "__" + VisionSignature.Clean(s.name) + "_" + i;
                used.Add(sn);
                sigNames.Add(sn);
                var values = string.Join(", ", s.id, s.uMin, s.uMax, s.uMean, s.vMin, s.vMax, s.vMean, s.range.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture), s.type);
                lines.Add(language switch {
                    "pros" => $"pros::vision_signature_s_t {sn} = pros::Vision::signature_from_utility({values});",
                    "vex" => $"vision::signature {sn} = vision::signature({values});",
                    _ => $"{sn} = Signature({values})",
                });
            }
            var args = string.Join(", ", new[] { language == "py" ? "Ports.PORT" + d.port : "PORT" + d.port, bright.ToString() }.Concat(sigNames));
            lines.Add(language switch {
                "pros" => $"pros::Vision {n}({d.port});" + (sigNames.Count > 0 ? "   // its signatures are kept on the sensor" : "   // no signatures yet: make them in Devices > its card > Vision Utility"),
                "vex" => $"vision {n} = vision({args});",
                _ => $"{n} = Vision({args})" + (sigNames.Count == 0 ? "   # no signatures yet: make them in Devices > its card > Vision Utility" : ""),
            });
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ the drive, measured

        /// <summary>The track width (inches) between the wheels these motors drive, measured from the build; 12 when it
        /// can't be (EZ-Template without an IMU turns by it).</summary>
        internal static double TrackWidth(IEnumerable<VexMotor> left, IEnumerable<VexMotor> right) {
            var l = left.Select(m => V5Brain.Get<MotorDevice>(m.port)).Where(d => d != null).ToList();
            var r = right.Select(m => V5Brain.Get<MotorDevice>(m.port)).Where(d => d != null).ToList();
            if (l.Count == 0 || r.Count == 0) return 12;
            var (_, track, _, measured) = MeasureDrive(l, r);
            return measured && track > 1 ? track : 12;
        }

        private static (float diameter, float track, float ratio, bool measured) MeasureDrive(List<MotorDevice> left, List<MotorDevice> right) {
            try {
                var rotors = GearTrain.Current();
                var leftWheels = Wheels(left, rotors, out var leftRatio);
                var rightWheels = Wheels(right, rotors, out var rightRatio);
                if (leftWheels.Count == 0 || rightWheels.Count == 0) return (4f, 12f, 1f, false);
                var all = leftWheels.Concat(rightWheels).ToList();
                var diameter = Standard(all.Average(w => w.diameter));
                var axis = all[0].axis;
                var leftMid = leftWheels.Aggregate(Vector3.zero, (s, w) => s + w.center) / leftWheels.Count;
                var rightMid = rightWheels.Aggregate(Vector3.zero, (s, w) => s + w.center) / rightWheels.Count;
                var track = Mathf.Abs(Vector3.Dot(rightMid - leftMid, axis));
                var ratio = (leftRatio + rightRatio) / 2f;
                return (diameter, Mathf.Round(track * 8f) / 8f, Mathf.Round(ratio * 1000f) / 1000f, true);
            }
            catch (Exception e) {
                Debug.LogWarning("V2: measuring the drive: " + e.Message);
                return (4f, 12f, 1f, false);
            }
        }

        private struct Wheel {
            public Vector3 center, axis;
            public float diameter;
        }

        private static List<Wheel> Wheels(List<MotorDevice> motors, List<Rotor> rotors, out float ratio) {
            var wheels = new List<Wheel>();
            var ratios = new List<float>();
            foreach (var m in motors) {
                var system = GearTrain.SystemOf(m.motor, rotors);
                if (system == null) continue;
                var motorRotor = system.RotorOf(m.motor);
                if (motorRotor == null || !system.ratio.TryGetValue(motorRotor, out var motorTurns) || Mathf.Abs(motorTurns) < 1e-5f) continue;
                foreach (var rotor in system.rotors)
                    foreach (var part in rotor.parts) {
                        if (!IsWheel(part)) continue;
                        var renderer = part.GetComponentInChildren<MeshRenderer>();
                        if (renderer == null) continue;
                        var axis = rotor.axis.normalized;
                        var size = renderer.bounds.size;
                        // The size across the axle (axles lie along a world axis on almost every robot).
                        var across = new[] { size.x, size.y, size.z }.Where((_, i) => Mathf.Abs(axis[i]) < 0.7f).DefaultIfEmpty(size.magnitude).Max();
                        if (wheels.Any(w => (w.center - renderer.bounds.center).sqrMagnitude < 0.01f)) continue;
                        wheels.Add(new Wheel { center = renderer.bounds.center, axis = axis, diameter = across });
                        if (system.ratio.TryGetValue(rotor, out var wheelTurns) && Mathf.Abs(wheelTurns) > 1e-5f)
                            ratios.Add(Mathf.Abs(motorTurns / wheelTurns));
                    }
            }
            ratio = ratios.Count > 0 ? ratios.Average() : 1f;
            return wheels;
        }

        private static bool IsWheel(GameObject part) {
            var id = part.GetComponent<SavedObject>()?.id ?? "";
            return id.StartsWith("OMNI-") || id.StartsWith("TWHL-") || id.StartsWith("FWHL-") || id.StartsWith("MECN")
                   || (id.StartsWith("VX") && (id.Contains("WHEEL") || id.Contains("OMNI") || id.Contains("TIRE")));
        }

        // VEX wheel sizes snap to their nominal size (a 3.25" omni measures a little over).
        private static float Standard(float d) {
            foreach (var size in new[] { 2f, 2.75f, 3.25f, 4f, 5f, 6f })
                if (Mathf.Abs(d - size) < 0.15f) return size;
            return Mathf.Round(d * 100f) / 100f;
        }

        // ------------------------------------------------------------------ names

        private static readonly HashSet<string> Reserved = new() {
            "False", "None", "True", "and", "as", "assert", "break", "class", "continue", "def", "del", "elif", "else", "except",
            "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
            "return", "try", "while", "with", "yield", "print", "wait", "Motor", "Brain", "Controller",
        };

        /// <summary>A Python name from what the user called the device ("Left front" -> left_front), never twice.</summary>
        public static string Name(string wanted, HashSet<string> used) {
            var sb = new StringBuilder();
            foreach (var ch in (wanted ?? "device").Trim().ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '_');
            var name = sb.ToString().Trim('_');
            while (name.Contains("__")) name = name.Replace("__", "_");
            if (name.Length == 0) name = "device";
            if (char.IsDigit(name[0])) name = "d_" + name;
            if (Reserved.Contains(name)) name += "_1";
            var unique = name;
            for (var i = 2; used.Contains(unique); i++) unique = name + "_" + i;
            used.Add(unique);
            return unique;
        }

        private static string Letter(int i) => ((char)('a' + Mathf.Clamp(i, 0, 25))).ToString();
        private static string Port3(int port) => ((char)('A' + Mathf.Clamp(port - 1, 0, 7))).ToString();

        /// <summary>V2: a 3-wire device's port in a name ("a", on an expander "e5a").</summary>
        private static string Tag3(V5Device d) => (d.expander > 0 ? "e" + d.expander : "") + Port3(d.port).ToLowerInvariant();

        /// <summary>V2: a 3-wire device's port (plus `offset`) as code names it: Python brain.three_wire_port.a /
        /// expander_5.a, VEXcode C++ Brain.ThreeWirePort.A / Expander5.A, PROS 'A' / 5, 'A' (the caller puts an
        /// expander's in PROS's double braces: {{5, 'A'}}).</summary>
        private static string Ref3(V5Device d, string language, int offset = 0) {
            var letter = Port3(d.port + offset);
            return language switch {
                "py" => (d.expander > 0 ? "expander_" + d.expander : "brain.three_wire_port") + "." + letter.ToLowerInvariant(),
                "vex" => (d.expander > 0 ? "Expander" + d.expander : "Brain.ThreeWirePort") + "." + letter,
                _ => d.expander > 0 ? d.expander + ", '" + letter + "'" : "'" + letter + "'",
            };
        }
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
