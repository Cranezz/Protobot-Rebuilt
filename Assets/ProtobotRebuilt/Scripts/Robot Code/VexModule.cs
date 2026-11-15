using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Scripting;

namespace Protobot.RobotCode {
    /// <summary>A vex object (a motor, the brain...): its methods and values by name.</summary>
    public abstract class VexObject : IPyObject, INamedAttrs {
        private readonly Dictionary<string, object> members = new();
        public abstract string TypeName { get; }

        protected void Method(string name, Func<Args, object> fn) => members[name] = new PyBuiltin(name, fn);
        protected void Field(string name, object value) => members[name] = value;

        public bool TryGetAttr(string name, out object value) => members.TryGetValue(name, out value);

        public bool TrySetAttr(string name, object value) {
            members[name] = value;   // programs may keep their own values on an object
            return true;
        }

        public IEnumerable<string> AttrNames => members.Keys;
    }

    /// <summary>
    /// V2 (Phase 9, robot code): the "vex" module, laid out like VEXcode V5 Python's (from vex import *): Brain,
    /// Controller, Motor, MotorGroup, DriveTrain, SmartDrive, the sensors, Pneumatics/DigitalOut, Timer, Thread,
    /// Competition, wait and the unit constants. Robot code drives the simulated robot through these.
    /// </summary>
    public static class VexModule {
        public static readonly Dictionary<string, PyEnum> Units = new();

        private static PyEnum E(PyModule m, string type, string name, int value) {
            var e = new PyEnum(type, name, value);
            m.globals[name] = e;
            Units[name] = e;
            return e;
        }

        /// <summary>A class of enum values (RotationUnits.DEG = DEGREES...).</summary>
        private static void EnumClass(PyModule m, string name, params (string attr, PyEnum value)[] values) {
            var c = new PyClass(name, null);
            foreach (var (attr, value) in values) c.dict[attr] = value;
            m.globals[name] = c;
        }

        public static PyModule Build(RobotProgram program) {
            var m = new PyModule("vex");
            Units.Clear();

            // ---------------------------------------------------------- constants
            var forward = E(m, "DirectionType", "FORWARD", 0);
            var reverse = E(m, "DirectionType", "REVERSE", 1);
            EnumClass(m, "DirectionType", ("FORWARD", forward), ("FWD", forward), ("REVERSE", reverse), ("REV", reverse));
            var left = E(m, "TurnType", "LEFT", 0);
            var right = E(m, "TurnType", "RIGHT", 1);
            EnumClass(m, "TurnType", ("LEFT", left), ("RIGHT", right));
            var percent = E(m, "PercentUnits", "PERCENT", 0);
            var rpm = E(m, "VelocityUnits", "RPM", 1);
            var dps = E(m, "VelocityUnits", "DPS", 2);
            EnumClass(m, "VelocityUnits", ("PERCENT", percent), ("PCT", percent), ("RPM", rpm), ("DPS", dps));
            EnumClass(m, "PercentUnits", ("PERCENT", percent), ("PCT", percent));
            var degrees = E(m, "RotationUnits", "DEGREES", 0);
            var turns = E(m, "RotationUnits", "TURNS", 1);
            var raw = E(m, "RotationUnits", "RAW", 2);
            EnumClass(m, "RotationUnits", ("DEG", degrees), ("REV", turns), ("RAW", raw));
            var msec = E(m, "TimeUnits", "MSEC", 0);
            var seconds = E(m, "TimeUnits", "SECONDS", 1);
            EnumClass(m, "TimeUnits", ("SECONDS", seconds), ("SEC", seconds), ("MSEC", msec));
            var mm = E(m, "DistanceUnits", "MM", 0);
            var inches = E(m, "DistanceUnits", "INCHES", 1);
            var cm = E(m, "DistanceUnits", "CM", 2);
            EnumClass(m, "DistanceUnits", ("MM", mm), ("IN", inches), ("CM", cm));
            var volt = E(m, "VoltageUnits", "VOLT", 0);
            var mv = E(m, "VoltageUnits", "MV", 1);
            EnumClass(m, "VoltageUnits", ("VOLT", volt), ("MV", mv));
            var amp = E(m, "CurrentUnits", "AMP", 0);
            EnumClass(m, "CurrentUnits", ("AMP", amp));
            var nm = E(m, "TorqueUnits", "NM", 0);
            var inlb = E(m, "TorqueUnits", "INLB", 1);
            EnumClass(m, "TorqueUnits", ("NM", nm), ("INLB", inlb));
            var watt = E(m, "PowerUnits", "WATT", 0);
            EnumClass(m, "PowerUnits", ("WATT", watt));
            var celsius = E(m, "TemperatureUnits", "CELSIUS", 0);
            var fahrenheit = E(m, "TemperatureUnits", "FAHRENHEIT", 1);
            EnumClass(m, "TemperatureUnits", ("CELSIUS", celsius), ("FAHRENHEIT", fahrenheit));
            var coast = E(m, "BrakeType", "COAST", 0);
            var brake = E(m, "BrakeType", "BRAKE", 1);
            var hold = E(m, "BrakeType", "HOLD", 2);
            EnumClass(m, "BrakeType", ("COAST", coast), ("BRAKE", brake), ("HOLD", hold));
            var primary = E(m, "ControllerType", "PRIMARY", 0);
            var partner = E(m, "ControllerType", "PARTNER", 1);
            EnumClass(m, "ControllerType", ("PRIMARY", primary), ("PARTNER", partner));
            var xaxis = E(m, "AxisType", "XAXIS", 0);
            var yaxis = E(m, "AxisType", "YAXIS", 1);
            var zaxis = E(m, "AxisType", "ZAXIS", 2);
            EnumClass(m, "AxisType", ("XAXIS", xaxis), ("YAXIS", yaxis), ("ZAXIS", zaxis));
            var roll = E(m, "OrientationType", "ROLL", 0);
            var pitch = E(m, "OrientationType", "PITCH", 1);
            var yaw = E(m, "OrientationType", "YAW", 2);
            EnumClass(m, "OrientationType", ("ROLL", roll), ("PITCH", pitch), ("YAW", yaw));
            var ledOn = E(m, "LedStateType", "ON", 1);
            var ledOff = E(m, "LedStateType", "OFF", 0);
            EnumClass(m, "LedStateType", ("ON", ledOn), ("OFF", ledOff));
            EnumClass(m, "ObjectSizeType", ("NONE", new PyEnum("ObjectSizeType", "NONE", 0)), ("SMALL", new PyEnum("ObjectSizeType", "SMALL", 1)),
                ("MEDIUM", new PyEnum("ObjectSizeType", "MEDIUM", 2)), ("LARGE", new PyEnum("ObjectSizeType", "LARGE", 3)));
            var gears = new PyClass("GearSetting", null);
            gears.dict["RATIO_36_1"] = E(m, "GearSetting", "RATIO_36_1", 100);
            gears.dict["RATIO_18_1"] = E(m, "GearSetting", "RATIO_18_1", 200);
            gears.dict["RATIO_6_1"] = E(m, "GearSetting", "RATIO_6_1", 600);
            m.globals["GearSetting"] = gears;
            var ports = new PyClass("Ports", null);
            for (var i = 1; i <= 21; i++) ports.dict["PORT" + i] = new PyEnum("Ports", "PORT" + i, i);
            m.globals["Ports"] = ports;
            var colors = new PyClass("Color", null);
            foreach (var (n, i) in new[] { ("RED", 0), ("GREEN", 1), ("BLUE", 2), ("YELLOW", 3), ("ORANGE", 4), ("PURPLE", 5), ("CYAN", 6), ("WHITE", 7), ("BLACK", 8), ("TRANSPARENT", 9) })
                colors.dict[n] = new PyEnum("Color", n, i);
            m.globals["Color"] = colors;
            var fonts = new PyClass("FontType", null);
            foreach (var n in new[] { "MONO12", "MONO15", "MONO20", "MONO30", "MONO40", "MONO60", "PROP20", "PROP30", "PROP40", "PROP60" })
                fonts.dict[n] = new PyEnum("FontType", n, 0);
            m.globals["FontType"] = fonts;

            // ---------------------------------------------------------- functions
            m.globals["wait"] = new PyBuiltin("wait", a => {
                var t = a.Number(0, "time", 0);
                var unit = UnitName(a.Get(1, "units"), "MSEC");
                return new Suspend(a.vm.Time + Math.Max(0, unit == "SECONDS" ? t : t / 1000.0));
            });
            m.globals["sleep"] = new PyBuiltin("sleep", a => new Suspend(a.vm.Time + Math.Max(0, a.Number(0, "time", 0) / 1000.0)));

            // ---------------------------------------------------------- classes
            void Class(string name, Func<Args, object> make) => m.globals[name] = new PyClass(name, null) { construct = (_, a) => make(a) };
            Class("Brain", _ => program.Brain);
            Class("Controller", a => program.Controller(UnitName(a.Get(0, "type"), "PRIMARY") == "PARTNER"));
            Class("Motor", a => new VexMotor(program, a));
            Class("Motor55", a => new VexMotor(program, a));
            Class("MotorGroup", a => new VexMotorGroup(program, a.pos.Select(o => o as VexMotor ?? throw new PyError("TypeError", "MotorGroup takes Motors, not " + Ops.Describe(o))).ToArray()));
            Class("DriveTrain", a => new VexDrive(program, a, false));
            Class("SmartDrive", a => new VexDrive(program, a, true));
            Class("Inertial", a => new VexInertial(program, a));
            Class("Distance", a => new VexDistance(program, a));
            Class("Optical", a => new VexOptical(program, a));
            Class("Rotation", a => new VexRotation(program, a));
            Class("Bumper", a => new VexSwitch(program, a, "Bumper"));
            Class("Limit", a => new VexSwitch(program, a, "Limit"));
            Class("DigitalIn", a => new VexSwitch(program, a, "DigitalIn"));
            Class("DigitalOut", a => new VexSolenoid(program, a, "DigitalOut"));
            Class("Pneumatics", a => new VexSolenoid(program, a, "Pneumatics"));
            Class("Encoder", a => new VexShaft(program, a, "Encoder"));
            Class("Potentiometer", a => new VexShaft(program, a, "Potentiometer"));
            Class("PotentiometerV2", a => new VexShaft(program, a, "PotentiometerV2"));
            Class("Line", a => new VexAnalog(program, a, "Line"));
            Class("Light", a => new VexAnalog(program, a, "Light"));
            Class("Sonar", a => new VexAnalog(program, a, "Sonar"));
            Class("Gyro", a => new VexAnalog(program, a, "Gyro"));
            Class("Timer", _ => new VexTimer(program));
            Class("Thread", a => new VexThread(program, a));
            Class("Competition", a => program.SetCompetition(a.Need(0, "driver_control"), a.Need(1, "autonomous")));
            Class("Event", a => new VexEvent(program, a));
            return m;
        }

        // ------------------------------------------------------------------ argument helpers

        /// <summary>A unit or enum argument's name ("DEGREES"), or the fallback when it isn't given.</summary>
        public static string UnitName(object o, string fallback) => o switch {
            PyEnum e => e.name,
            null or PyNone => fallback,
            bool b => b ? "true" : "false",
            _ => throw new PyError("TypeError", "expected a unit like DEGREES or PERCENT, got " + Ops.Describe(o)),
        };

        public static bool IsUnit(object o) => o is PyEnum;

        public static double Seconds(double value, string unit) => unit == "MSEC" ? value / 1000.0 : value;

        /// <summary>Degrees in a rotation unit.</summary>
        public static double FromDegrees(double deg, string unit) => unit switch {
            "TURNS" => deg / 360.0,
            "RAW" => deg,
            _ => deg,
        };

        public static double ToDegrees(double v, string unit) => unit == "TURNS" ? v * 360.0 : v;

        public static double Mm(double v, string unit) => unit switch {
            "INCHES" => v * 25.4,
            "CM" => v * 10.0,
            _ => v,
        };

        public static double FromMm(double mmValue, string unit) => unit switch {
            "INCHES" => mmValue / 25.4,
            "CM" => mmValue / 10.0,
            _ => mmValue,
        };

        /// <summary>A port argument: Ports.PORT1, a number, or a 3-wire port (brain.three_wire_port.a).</summary>
        public static (int port, bool threeWire) Port(object o, string what) => o switch {
            PyEnum e when e.type == "Ports" => (e.value, false),
            long l => ((int)l, false),
            ThreeWirePort t => (t.index, true),
            _ => throw new PyError("TypeError", what + " needs a port (like Ports.PORT1 or brain.three_wire_port.a), not " + Ops.Describe(o)),
        };
    }
}
