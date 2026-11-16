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

        /// <summary>The drive measured by the last Generate (examples fill their numbers in from it); null: none.</summary>
        public static (float diameter, float track, float ratio, float motorRpm, bool imu)? LastDrive;

        public static string Generate() {
            LastDrive = null;
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
            foreach (var d in sensors) {
                var line = SensorLine(d, used, out var name);
                if (line == null) continue;
                if (d == imu) imuName = name;
                sb.AppendLine(line);
            }
            var solenoids = devices.OfType<SolenoidDevice>().ToList();
            if (solenoids.Count > 0) {
                sb.AppendLine();
                sb.AppendLine("# Pneumatics: .set(True) extends, .set(False) retracts");
            }
            foreach (var s in solenoids) {
                var given = s.part != null ? Controls.GivenName(s.part) : s.own != null ? Controls.GivenName(s.own.gameObject) : null;
                var name = Name(given ?? "piston_" + Port3(s.port).ToLowerInvariant(), used);
                sb.AppendLine($"{name} = DigitalOut(brain.three_wire_port.{Port3(s.port).ToLowerInvariant()})");
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

        /// <summary>The program with its configuration block put in (replacing an old one), at the top.</summary>
        public static string InsertInto(string source) {
            var config = Generate().TrimEnd('\n', '\r');
            source ??= "";
            var start = source.IndexOf(Begin, StringComparison.Ordinal);
            var end = source.IndexOf(End, StringComparison.Ordinal);
            if (start >= 0 && end > start) return source.Substring(0, start) + config + source.Substring(end + End.Length);
            // A program that imports vex already: the block (which imports it too) goes first.
            return config + "\n\n" + source;
        }

        private static string SensorLine(V5Device d, HashSet<string> used, out string name) {
            name = null;
            string Smart(string cls, string fallback) {
                var n = Name(Controls.GivenName(d.part) ?? fallback, used);
                return $"{n} = {cls}(Ports.PORT{d.port})";
            }
            string Three(string cls, string fallback, bool twoPorts = false) {
                var n = Name(Controls.GivenName(d.part) ?? fallback, used);
                return $"{n} = {cls}(brain.three_wire_port.{Port3(d.port).ToLowerInvariant()})";
            }
            var line = d.kind switch {
                DeviceKind.Inertial => Smart("Inertial", "imu"),
                DeviceKind.Distance => Smart("Distance", "distance_" + d.port),
                DeviceKind.Optical => Smart("Optical", "optical_" + d.port),
                DeviceKind.Rotation => Smart("Rotation", "rotation_" + d.port),
                DeviceKind.Gps => null,
                DeviceKind.Vision => null,
                DeviceKind.Bumper => Three("Bumper", "bumper_" + Port3(d.port).ToLowerInvariant()),
                DeviceKind.Limit => Three("Limit", "limit_" + Port3(d.port).ToLowerInvariant()),
                DeviceKind.Potentiometer => Three("PotentiometerV2", "pot_" + Port3(d.port).ToLowerInvariant()),
                DeviceKind.Encoder => Three("Encoder", "encoder_" + Port3(d.port).ToLowerInvariant()),
                DeviceKind.LineTracker => Three("Line", "line_" + Port3(d.port).ToLowerInvariant()),
                DeviceKind.Light => Three("Light", "light_" + Port3(d.port).ToLowerInvariant()),
                DeviceKind.Ultrasonic => Three("Sonar", "sonar_" + Port3(d.port).ToLowerInvariant()),
                DeviceKind.Gyro => Three("Gyro", "gyro_" + Port3(d.port).ToLowerInvariant()),
                DeviceKind.Accelerometer => null,
                _ => null,
            };
            if (line != null) name = line.Substring(0, line.IndexOf(' '));
            else if (d.kind == DeviceKind.Gps || d.kind == DeviceKind.Vision)
                line = "# " + V5Device.KindName(d.kind) + " sensor on port " + d.port + ": not simulated yet";
            return line;
        }

        // ------------------------------------------------------------------ the drive, measured

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
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
