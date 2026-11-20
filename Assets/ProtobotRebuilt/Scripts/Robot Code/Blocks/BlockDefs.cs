using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.RobotCode.Blocks {
    // V2 (Phase 9, Blocks): what each block is. Laid out like VEXcode V5 Blocks (its toolbox: Drivetrain, Motion, Looks,
    // Events, Control, Sensing, Operators, Variables), with the wording of VEX's Blocks reference (api.vex.com/v5/home/
    // blocks). A block is a spec ("drive {dir} for (200) {dist} ▶") and the Python it makes ("%dt.drive_for($0, $1,
    // $2, wait=$T)"): the program runs as Python on the same interpreter as typed code.
    //
    // Spec: words are labels; (5) a number slot, [hi] a text slot, <> a boolean slot (each takes a reporter dropped in);
    // {name} a dropdown from Options; {@kind} one of the robot's devices of that kind; {$num} / {$bool} / {$event} a
    // variable or message; {=num} / {=bool} the variable's name as the block's label; ▶ at the end the block's
    // expandable option (VEXcode's ▶ / ◀: "and don't wait", "and set cursor to next row").
    // Python: $0, $1... the slots in order, $T the option's text (ToggleOn / ToggleOff), %dt the drivetrain; "\n" makes
    // more lines (indented with the block).
    public enum BlockShape { Hat, Stack, C, CElse, Forever, Cap, Reporter, Boolean }

    public enum SlotKind { Number, Text, Boolean, Dropdown, Device, Variable, Name }

    public sealed class Slot {
        public SlotKind kind;
        public string key;       // dropdown: options key; device: kind; variable: num / bool / event
        public string initial;   // the default value (a number, a text, an option's value)
    }

    public sealed class BlockPart {
        public string label;     // or
        public Slot slot;
    }

    public sealed class BlockCategory {
        public string id, name;
        public Color color;
        public BlockCategory(string id, string name, Color color) { this.id = id; this.name = name; this.color = color; }
    }

    public sealed class BlockDef {
        public string id, category, spec, py;
        public BlockShape shape;
        public readonly List<BlockPart> parts = new();
        public readonly List<Slot> slots = new();
        public bool hasToggle;
        public string toggleLabel, toggleOn = "", toggleOff = "";
        public bool toggleDefault;
        public string elseLabel = "else";
        public bool hidden;      // made by the palette (variables), not listed by itself

        public bool IsReporter => shape == BlockShape.Reporter || shape == BlockShape.Boolean;
        public bool IsC => shape == BlockShape.C || shape == BlockShape.CElse || shape == BlockShape.Forever;
        public int Mouths => shape == BlockShape.CElse ? 2 : IsC ? 1 : 0;
        /// <summary>Something can go under it.</summary>
        public bool HasNext => shape == BlockShape.Hat || shape == BlockShape.Stack || shape == BlockShape.C || shape == BlockShape.CElse;
        /// <summary>It can go under something.</summary>
        public bool HasPrevious => shape == BlockShape.Stack || shape == BlockShape.C || shape == BlockShape.CElse || shape == BlockShape.Forever || shape == BlockShape.Cap;
    }

    public static class BlockDefs {
        public static readonly List<BlockCategory> Categories = new() {
            new("drivetrain", "Drivetrain", Hex("#4C97FF")),
            new("motion", "Motion", Hex("#3373CC")),
            new("looks", "Looks", Hex("#9966FF")),
            new("events", "Events", Hex("#FFBF00")),
            new("control", "Control", Hex("#FFAB19")),
            new("sensing", "Sensing", Hex("#5CB1D6")),
            new("operators", "Operators", Hex("#59C059")),
            new("variables", "Variables", Hex("#FF8C1A")),
            new("myblocks", "My Blocks", Hex("#FF6680")),
        };

        public static readonly Dictionary<string, BlockDef> ById = new();
        public static readonly List<BlockDef> All = new();

        /// <summary>Dropdown choices: (shown, Python).</summary>
        public static readonly Dictionary<string, (string label, string value)[]> Options = new() {
            ["dir"] = new[] { ("forward", "FORWARD"), ("reverse", "REVERSE") },
            ["turn"] = new[] { ("right", "RIGHT"), ("left", "LEFT") },
            ["dist"] = new[] { ("mm", "MM"), ("inches", "INCHES") },
            ["pct"] = new[] { ("%", "PERCENT"), ("rpm", "RPM") },
            ["vel"] = new[] { ("%", "PERCENT"), ("rpm", "RPM"), ("dps", "DPS") },
            ["rpmdps"] = new[] { ("rpm", "RPM"), ("dps", "DPS") },
            ["rot"] = new[] { ("degrees", "DEGREES"), ("turns", "TURNS") },
            ["brake"] = new[] { ("brake", "BRAKE"), ("coast", "COAST"), ("hold", "HOLD") },
            ["torque"] = new[] { ("Nm", "NM"), ("InLb", "INLB") },
            ["lowhigh"] = new[] { ("low", "False"), ("high", "True") },
            ["button"] = new[] { ("L1", "buttonL1"), ("L2", "buttonL2"), ("R1", "buttonR1"), ("R2", "buttonR2"), ("Up", "buttonUp"), ("Down", "buttonDown"),
                                 ("Left", "buttonLeft"), ("Right", "buttonRight"), ("X", "buttonX"), ("B", "buttonB"), ("Y", "buttonY"), ("A", "buttonA") },
            ["axis"] = new[] { ("1", "axis1"), ("2", "axis2"), ("3", "axis3"), ("4", "axis4") },
            ["pressrel"] = new[] { ("pressed", "pressed"), ("released", "released") },
            ["rumble"] = new[] { ("long", "\"--\""), ("short", "\"..\""), ("pulse", "\".-.-\"") },
            ["color"] = new[] { ("red", "Color.RED"), ("green", "Color.GREEN"), ("blue", "Color.BLUE"), ("white", "Color.WHITE"), ("black", "Color.BLACK"),
                                ("yellow", "Color.YELLOW"), ("orange", "Color.ORANGE"), ("purple", "Color.PURPLE"), ("cyan", "Color.CYAN"), ("transparent", "Color.TRANSPARENT") },
            ["ocolor"] = new[] { ("red", "Color.RED"), ("green", "Color.GREEN"), ("blue", "Color.BLUE") },
            ["font"] = new[] { ("monospaced small", "FontType.MONO12"), ("monospaced medium", "FontType.MONO20"), ("monospaced large", "FontType.MONO30"),
                               ("monospaced extra large", "FontType.MONO40"), ("proportional medium", "FontType.PROP20"), ("proportional large", "FontType.PROP30") },
            ["xy"] = new[] { ("x", "x"), ("y", "y") },
            ["size"] = new[] { ("small", "ObjectSizeType.SMALL"), ("medium", "ObjectSizeType.MEDIUM"), ("large", "ObjectSizeType.LARGE") },
            ["onoff"] = new[] { ("on", "LedStateType.ON"), ("off", "LedStateType.OFF") },
            ["axis3"] = new[] { ("x", "XAXIS"), ("y", "YAXIS"), ("z", "ZAXIS") },
            ["orient"] = new[] { ("roll", "ROLL"), ("pitch", "PITCH"), ("yaw", "YAW") },
            ["mathop"] = new[] { ("+", "+"), ("-", "-"), ("*", "*"), ("/", "/"), ("^", "**") },
            ["cmp"] = new[] { ("=", "=="), ("<", "<"), (">", ">"), ("≤", "<="), ("≥", ">="), ("≠", "!=") },
            ["lt"] = new[] { ("<", "<"), ("≤", "<=") },
            ["andor"] = new[] { ("and", "and"), ("or", "or") },
            ["tf"] = new[] { ("true", "True"), ("false", "False") },
            ["mathfn"] = new[] { ("abs", "abs(#)"), ("floor", "math.floor(#)"), ("ceiling", "math.ceil(#)"), ("square root", "math.sqrt(#)"),
                                 ("sin", "math.sin(math.radians(#))"), ("cos", "math.cos(math.radians(#))"), ("tan", "math.tan(math.radians(#))"),
                                 ("asin", "math.degrees(math.asin(#))"), ("acos", "math.degrees(math.acos(#))"), ("atan", "math.degrees(math.atan(#))"),
                                 ("ln", "math.log(#)"), ("log", "math.log10(#)"), ("e ^", "math.exp(#)"), ("10 ^", "(10 ** (#))") },
            ["conv"] = new[] { ("text", "str(#)"), ("number", "float(#)") },
            ["rowscol"] = new[] { ("rows", "len(#)"), ("columns", "len(#[0])") },
        };

        /// <summary>The device kinds the dropdowns list, and the Python classes that make them.</summary>
        public static readonly Dictionary<string, string[]> DeviceClasses = new() {
            ["motor"] = new[] { "Motor", "MotorGroup", "Motor55" },
            ["drivetrain"] = new[] { "DriveTrain", "SmartDrive" },
            ["inertial"] = new[] { "Inertial", "Gyro" },
            ["distance"] = new[] { "Distance" },
            ["optical"] = new[] { "Optical" },
            ["rotation"] = new[] { "Rotation" },
            ["bumper"] = new[] { "Bumper", "Limit" },
            ["digitalout"] = new[] { "DigitalOut", "Pneumatics" },
            ["encoder"] = new[] { "Encoder" },
            ["pot"] = new[] { "PotentiometerV2", "Potentiometer" },
            ["line"] = new[] { "Line" },
            ["light"] = new[] { "Light" },
            ["sonar"] = new[] { "Sonar" },
        };

        /// <summary>What a device of that kind is called when the robot has none (the block says so).</summary>
        public static string KindName(string kind) => kind switch {
            "motor" => "motor", "drivetrain" => "drivetrain", "inertial" => "inertial sensor", "distance" => "distance sensor", "optical" => "optical sensor",
            "rotation" => "rotation sensor", "bumper" => "bumper or limit switch", "digitalout" => "pneumatics (digital out)", "encoder" => "encoder",
            "pot" => "potentiometer", "line" => "line tracker", "light" => "light sensor", "sonar" => "range finder", _ => kind,
        };

        static BlockDefs() {
            // ---------------------------------------------------------------- Drivetrain
            const string dt = "drivetrain";
            Def(dt, "drive", BlockShape.Stack, "drive {dir}", "%dt.drive($0)");
            Def(dt, "drive_for", BlockShape.Stack, "drive {dir} for (200) {dist} ▶", "%dt.drive_for($0, $1, $2, wait=$T)").Wait();
            Def(dt, "turn", BlockShape.Stack, "turn {turn}", "%dt.turn($0)");
            Def(dt, "turn_for", BlockShape.Stack, "turn {turn} for (90) degrees ▶", "%dt.turn_for($0, $1, DEGREES, wait=$T)").Wait();
            Def(dt, "turn_to_heading", BlockShape.Stack, "turn to heading (90) degrees ▶", "%dt.turn_to_heading($0, DEGREES, wait=$T)").Wait();
            Def(dt, "turn_to_rotation", BlockShape.Stack, "turn to rotation (90) degrees ▶", "%dt.turn_to_rotation($0, DEGREES, wait=$T)").Wait();
            Def(dt, "stop_driving", BlockShape.Stack, "stop driving", "%dt.stop()");
            Def(dt, "set_drive_velocity", BlockShape.Stack, "set drive velocity to (50) {pct}", "%dt.set_drive_velocity($0, $1)");
            Def(dt, "set_turn_velocity", BlockShape.Stack, "set turn velocity to (50) {pct}", "%dt.set_turn_velocity($0, $1)");
            Def(dt, "set_drive_stopping", BlockShape.Stack, "set drive stopping to {brake}", "%dt.set_stopping($0)");
            Def(dt, "set_drive_timeout", BlockShape.Stack, "set drive timeout to (1) seconds", "%dt.set_timeout($0, SECONDS)");
            Def(dt, "set_drive_heading", BlockShape.Stack, "set drive heading to (0) degrees", "%dt.set_heading($0, DEGREES)");
            Def(dt, "set_drive_rotation", BlockShape.Stack, "set drive rotation to (0) degrees", "%dt.set_rotation($0, DEGREES)");
            Def(dt, "drive_is_done", BlockShape.Boolean, "drive is done?", "%dt.is_done()");
            Def(dt, "drive_is_moving", BlockShape.Boolean, "drive is moving?", "%dt.is_moving()");
            Def(dt, "drive_heading", BlockShape.Reporter, "drive heading in degrees", "%dt.heading(DEGREES)");
            Def(dt, "drive_rotation", BlockShape.Reporter, "drive rotation in degrees", "%dt.rotation(DEGREES)");
            Def(dt, "drive_velocity", BlockShape.Reporter, "drive velocity in {pct}", "%dt.velocity($0)");

            // ---------------------------------------------------------------- Motion (motors, pneumatics)
            const string mo = "motion";
            Def(mo, "spin", BlockShape.Stack, "spin {@motor} {dir}", "$0.spin($1)");
            Def(mo, "spin_for", BlockShape.Stack, "spin {@motor} {dir} for (90) {rot} ▶", "$0.spin_for($1, $2, $3, wait=$T)").Wait();
            Def(mo, "spin_to", BlockShape.Stack, "spin {@motor} to position (90) {rot} ▶", "$0.spin_to_position($1, $2, wait=$T)").Wait();
            Def(mo, "stop_motor", BlockShape.Stack, "stop {@motor}", "$0.stop()");
            Def(mo, "set_motor_position", BlockShape.Stack, "set {@motor} position to (0) {rot}", "$0.set_position($1, $2)");
            Def(mo, "set_motor_velocity", BlockShape.Stack, "set {@motor} velocity to (50) {pct}", "$0.set_velocity($1, $2)");
            Def(mo, "set_motor_stopping", BlockShape.Stack, "set {@motor} stopping to {brake}", "$0.set_stopping($1)");
            Def(mo, "set_motor_torque", BlockShape.Stack, "set {@motor} max torque to (50) %", "$0.set_max_torque($1, PERCENT)");
            Def(mo, "set_motor_timeout", BlockShape.Stack, "set {@motor} timeout to (1) seconds", "$0.set_timeout($1, SECONDS)");
            Def(mo, "motor_done", BlockShape.Boolean, "{@motor} is done?", "$0.is_done()");
            Def(mo, "motor_spinning", BlockShape.Boolean, "{@motor} is spinning?", "$0.is_spinning()");
            Def(mo, "motor_position", BlockShape.Reporter, "{@motor} position in {rot}", "$0.position($1)");
            Def(mo, "motor_velocity", BlockShape.Reporter, "{@motor} velocity in {vel}", "$0.velocity($1)");
            Def(mo, "motor_current", BlockShape.Reporter, "{@motor} current in amps", "$0.current(AMP)");
            Def(mo, "motor_power", BlockShape.Reporter, "{@motor} power in watts", "$0.power(WATT)");
            Def(mo, "motor_torque", BlockShape.Reporter, "{@motor} torque in {torque}", "$0.torque($1)");
            Def(mo, "motor_efficiency", BlockShape.Reporter, "{@motor} efficiency in %", "$0.efficiency(PERCENT)");
            Def(mo, "motor_temperature", BlockShape.Reporter, "{@motor} temperature in °C", "$0.temperature(CELSIUS)");
            Def(mo, "digital_out", BlockShape.Stack, "set {@digitalout} {lowhigh}", "$0.set($1)");

            // ---------------------------------------------------------------- Looks (the Brain's and controller's screens, the console)
            const string lo = "looks";
            Def(lo, "print", BlockShape.Stack, "print [VEXcode] on screen ▶", "brain.screen.print($0)$T").Option("and set cursor to next row", "\nbrain.screen.next_row()", "", true);
            Def(lo, "set_cursor", BlockShape.Stack, "set cursor to row (1) column (1) on screen", "brain.screen.set_cursor($0, $1)");
            Def(lo, "next_row", BlockShape.Stack, "set cursor to next row on screen", "brain.screen.next_row()");
            Def(lo, "clear_row", BlockShape.Stack, "clear row (1) on screen", "brain.screen.clear_row($0)");
            Def(lo, "clear_screen", BlockShape.Stack, "clear screen", "brain.screen.clear_screen()");
            Def(lo, "set_font", BlockShape.Stack, "set font to {font} on screen", "brain.screen.set_font($0)");
            Def(lo, "set_pen_width", BlockShape.Stack, "set pen width to (10) on screen", "brain.screen.set_pen_width($0)");
            Def(lo, "set_pen_color", BlockShape.Stack, "set pen / font color to {color} on screen", "brain.screen.set_pen_color($0)");
            Def(lo, "set_fill_color", BlockShape.Stack, "set fill color to {color} on screen", "brain.screen.set_fill_color($0)");
            Def(lo, "draw_pixel", BlockShape.Stack, "draw pixel (0) (0) on screen", "brain.screen.draw_pixel($0, $1)");
            Def(lo, "draw_line", BlockShape.Stack, "draw line (0) (0) (10) (10) on screen", "brain.screen.draw_line($0, $1, $2, $3)");
            Def(lo, "draw_rectangle", BlockShape.Stack, "draw rectangle (0) (0) (10) (10) on screen", "brain.screen.draw_rectangle($0, $1, $2, $3)");
            Def(lo, "draw_circle", BlockShape.Stack, "draw circle (0) (0) (10) on screen", "brain.screen.draw_circle($0, $1, $2)");
            Def(lo, "cursor_column", BlockShape.Reporter, "screen cursor column", "brain.screen.column()");
            Def(lo, "cursor_row", BlockShape.Reporter, "screen cursor row", "brain.screen.row()");
            Def(lo, "controller_print", BlockShape.Stack, "print [VEXcode] on controller ▶", "controller.screen.print($0)$T").Option("and set cursor to next row", "\ncontroller.screen.next_row()", "", true);
            Def(lo, "controller_cursor", BlockShape.Stack, "set cursor to row (1) column (1) on controller", "controller.screen.set_cursor($0, $1)");
            Def(lo, "controller_clear_row", BlockShape.Stack, "clear row (1) on controller", "controller.screen.clear_row($0)");
            Def(lo, "controller_clear", BlockShape.Stack, "clear controller screen", "controller.screen.clear_screen()");
            Def(lo, "rumble", BlockShape.Stack, "play rumble {rumble} on controller", "controller.rumble($0)");
            Def(lo, "console_print", BlockShape.Stack, "print [VEXcode] on console ▶", "print($0$T)").Option("and set cursor to next row", "", ", end=\"\"", true);
            Def(lo, "console_clear", BlockShape.Stack, "clear all rows on console", "clear_console()");
            Def(lo, "console_color", BlockShape.Stack, "set font color to {color} on console", "set_console_text_color($0)");

            // ---------------------------------------------------------------- Events (hats: made into functions by BlockCode)
            const string ev = "events";
            Def(ev, "when_started", BlockShape.Hat, "when started", "");
            Def(ev, "when_autonomous", BlockShape.Hat, "when autonomous", "");
            Def(ev, "when_driver", BlockShape.Hat, "when driver control", "");
            Def(ev, "when_button", BlockShape.Hat, "when Controller button {button} {pressrel}", "");
            Def(ev, "when_axis", BlockShape.Hat, "when Controller axis {axis} is changed", "");
            Def(ev, "when_receive", BlockShape.Hat, "when I receive {$event}", "");
            Def(ev, "broadcast", BlockShape.Stack, "broadcast {$event}", "$0.broadcast()");
            Def(ev, "broadcast_wait", BlockShape.Stack, "broadcast {$event} and wait", "$0.broadcast_and_wait()");
            Def(ev, "when_timer", BlockShape.Hat, "when timer > (1) seconds", "");
            Def(ev, "when_screen", BlockShape.Hat, "when Brain screen {pressrel}", "");
            Def(ev, "when_bumper", BlockShape.Hat, "when {@bumper} {pressrel}", "");

            // ---------------------------------------------------------------- Control (C blocks: made by BlockCode)
            const string co = "control";
            Def(co, "wait", BlockShape.Stack, "wait (1) seconds", "wait($0, SECONDS)");
            Def(co, "wait_until", BlockShape.Stack, "wait until <>", "while not $0:\n    wait(5, MSEC)");
            Def(co, "repeat", BlockShape.C, "repeat (10)", "");
            Def(co, "forever", BlockShape.Forever, "forever", "");
            Def(co, "repeat_until", BlockShape.C, "repeat until <>", "");
            Def(co, "while", BlockShape.C, "while <>", "");
            Def(co, "if", BlockShape.C, "if <> then", "");
            Def(co, "if_else", BlockShape.CElse, "if <> then", "");
            Def(co, "break", BlockShape.Stack, "break", "break");
            Def(co, "stop_project", BlockShape.Cap, "stop project", "brain.program_stop()");

            // ---------------------------------------------------------------- Sensing
            const string se = "sensing";
            Def(se, "controller_pressed", BlockShape.Boolean, "Controller {button} pressed?", "controller.$0.pressing()");
            Def(se, "controller_position", BlockShape.Reporter, "Controller axis {axis} position", "controller.$0.position()");
            Def(se, "reset_timer", BlockShape.Stack, "reset timer", "brain.timer.clear()");
            Def(se, "timer", BlockShape.Reporter, "timer in seconds", "brain.timer.time(SECONDS)");
            Def(se, "battery", BlockShape.Reporter, "battery capacity in %", "brain.battery.capacity()");
            Def(se, "screen_pressed", BlockShape.Boolean, "screen pressed?", "brain.screen.pressing()");
            Def(se, "screen_position", BlockShape.Reporter, "screen {xy} position", "brain.screen.$0_position()");
            Def(se, "imu_calibrate", BlockShape.Stack, "calibrate {@inertial}", "$0.calibrate()");
            Def(se, "imu_set_heading", BlockShape.Stack, "set {@inertial} heading to (0) degrees", "$0.set_heading($1, DEGREES)");
            Def(se, "imu_set_rotation", BlockShape.Stack, "set {@inertial} rotation to (0) degrees", "$0.set_rotation($1, DEGREES)");
            Def(se, "imu_heading", BlockShape.Reporter, "{@inertial} heading in degrees", "$0.heading(DEGREES)");
            Def(se, "imu_rotation", BlockShape.Reporter, "{@inertial} rotation in degrees", "$0.rotation(DEGREES)");
            Def(se, "imu_orientation", BlockShape.Reporter, "{@inertial} orientation of {orient} in degrees", "$0.orientation($1, DEGREES)");
            Def(se, "imu_acceleration", BlockShape.Reporter, "{@inertial} acceleration of {axis3} axis in g", "$0.acceleration($1)");
            Def(se, "imu_rate", BlockShape.Reporter, "{@inertial} gyro rate of {axis3} axis in dps", "$0.gyro_rate($1)");
            Def(se, "distance_found", BlockShape.Boolean, "{@distance} found an object?", "$0.is_object_detected()");
            Def(se, "distance_distance", BlockShape.Reporter, "{@distance} object distance in {dist}", "$0.object_distance($1)");
            Def(se, "distance_velocity", BlockShape.Reporter, "{@distance} object velocity in m/s", "$0.object_velocity()");
            Def(se, "distance_size", BlockShape.Boolean, "{@distance} object size is {size}?", "($0.object_size() == $1)");
            Def(se, "optical_light", BlockShape.Stack, "set {@optical} light {onoff}", "$0.set_light($1)");
            Def(se, "optical_power", BlockShape.Stack, "set {@optical} light power to (50) %", "$0.set_light_power($1)");
            Def(se, "optical_found", BlockShape.Boolean, "{@optical} found an object?", "$0.is_near_object()");
            Def(se, "optical_detects", BlockShape.Boolean, "{@optical} detects {ocolor}?", "($0.color() == $1)");
            Def(se, "optical_brightness", BlockShape.Reporter, "{@optical} brightness in %", "$0.brightness()");
            Def(se, "optical_hue", BlockShape.Reporter, "{@optical} hue in degrees", "$0.hue()");
            Def(se, "rotation_set", BlockShape.Stack, "set {@rotation} position to (0) degrees", "$0.set_position($1, DEGREES)");
            Def(se, "rotation_angle", BlockShape.Reporter, "{@rotation} angle in degrees", "$0.angle()");
            Def(se, "rotation_position", BlockShape.Reporter, "{@rotation} position in {rot}", "$0.position($1)");
            Def(se, "rotation_velocity", BlockShape.Reporter, "{@rotation} velocity in {rpmdps}", "$0.velocity($1)");
            Def(se, "bumper_pressed", BlockShape.Boolean, "{@bumper} pressed?", "$0.pressing()");
            Def(se, "encoder_set", BlockShape.Stack, "set {@encoder} position to (0) degrees", "$0.set_position($1, DEGREES)");
            Def(se, "encoder_position", BlockShape.Reporter, "{@encoder} position in {rot}", "$0.position($1)");
            Def(se, "encoder_velocity", BlockShape.Reporter, "{@encoder} velocity in {rpmdps}", "$0.velocity($1)");
            Def(se, "pot_angle", BlockShape.Reporter, "{@pot} angle in degrees", "$0.angle(DEGREES)");
            Def(se, "line_reflectivity", BlockShape.Reporter, "{@line} reflectivity in %", "$0.reflectivity()");
            Def(se, "light_brightness", BlockShape.Reporter, "{@light} brightness in %", "$0.brightness()");
            Def(se, "sonar_found", BlockShape.Boolean, "{@sonar} found an object?", "$0.found_object()");
            Def(se, "sonar_distance", BlockShape.Reporter, "{@sonar} distance in {dist}", "$0.distance($1)");

            // ---------------------------------------------------------------- Operators
            const string op = "operators";
            Def(op, "math", BlockShape.Reporter, "(0) {mathop} (0)", "($0 $1 $2)");
            Def(op, "compare", BlockShape.Boolean, "(0) {cmp} (0)", "($0 $1 $2)");
            Def(op, "range", BlockShape.Boolean, "(0) {lt} (0) {lt} (0)", "($0 $1 $2 $3 $4)");
            Def(op, "logic", BlockShape.Boolean, "<> {andor} <>", "($0 $1 $2)");
            Def(op, "not", BlockShape.Boolean, "not <>", "(not $0)");
            Def(op, "true_false", BlockShape.Boolean, "{tf}", "$0");
            Def(op, "random", BlockShape.Reporter, "pick random (1) to (10)", "random.randint(int($0), int($1))");
            Def(op, "round", BlockShape.Reporter, "round (0) to (0) decimal places", "round($0, int($1))");
            Def(op, "mathfn", BlockShape.Reporter, "{mathfn} of (0)", "#$0($1)");
            Def(op, "atan2", BlockShape.Reporter, "atan2 of x: (4) y: (3)", "math.degrees(math.atan2($1, $0))");
            Def(op, "remainder", BlockShape.Reporter, "remainder of (0) / (1)", "($0 % $1)");
            Def(op, "join", BlockShape.Reporter, "join [apple] [banana]", "(str($0) + str($1))");
            Def(op, "letter", BlockShape.Reporter, "letter (1) of [apple]", "str($1)[int($0) - 1]");
            Def(op, "length", BlockShape.Reporter, "length of [apple]", "len(str($0))");
            Def(op, "contains", BlockShape.Boolean, "[apple] contains [a]?", "(str($1) in str($0))");
            Def(op, "convert", BlockShape.Reporter, "convert (0) to {conv}", "#$1($0)");

            // ---------------------------------------------------------------- Variables (the palette makes one reporter per variable)
            const string va = "variables";
            Def(va, "var", BlockShape.Reporter, "{=num}", "$0").hidden = true;
            Def(va, "set_var", BlockShape.Stack, "set {$num} to (0)", "$0 = $1");
            Def(va, "change_var", BlockShape.Stack, "change {$num} by (1)", "$0 += $1");
            Def(va, "bool", BlockShape.Boolean, "{=bool}", "$0").hidden = true;
            Def(va, "set_bool", BlockShape.Stack, "set {$bool} to <>", "$0 = $1");
            // Lists (VEX's Variables reference: positions start at 1); each list's "set" is made per list (RegisterSetList).
            Def(va, "list_item", BlockShape.Reporter, "item (1) of {$list}", "$1[int($0) - 1]").hidden = true;
            Def(va, "list_replace", BlockShape.Stack, "replace item (1) of {$list} to (1)", "$1[int($0) - 1] = $2").hidden = true;
            Def(va, "list_length", BlockShape.Reporter, "length of {$list}", "len($0)").hidden = true;
            Def(va, "list2_item", BlockShape.Reporter, "item (1) (1) of {$list2}", "$2[int($0) - 1][int($1) - 1]").hidden = true;
            Def(va, "list2_replace", BlockShape.Stack, "replace item (1) (1) of {$list2} to (1)", "$2[int($0) - 1][int($1) - 1] = $3").hidden = true;
            Def(va, "list2_length", BlockShape.Reporter, "length of {$list2} {rowscol}", "#$1($0)").hidden = true;

            // ---------------------------------------------------------------- My Blocks (each one's call and define hat: RegisterMyBlock)
            const string my = "myblocks";
            Def(my, "arg", BlockShape.Reporter, "{=num}", "$0").hidden = true;
            Def(my, "argbool", BlockShape.Boolean, "{=bool}", "$0").hidden = true;
        }

        /// <summary>Words that can go in a spec as a label (no slot brackets).</summary>
        public static string Words(string text) {
            var s = new System.Text.StringBuilder();
            foreach (var c in text ?? "") if ("()[]{}<>▶$#%".IndexOf(c) < 0) s.Append(c);
            return s.ToString().Trim();
        }

        /// <summary>A My Block's two kinds: the call (a stack block with its inputs) and its define hat.</summary>
        public static void RegisterMyBlock(MyBlock m) {
            var spec = new System.Text.StringBuilder(Words(m.name));
            var args = new List<string>();
            foreach (var part in m.parts) {
                if (part.kind == "label") {
                    var w = Words(part.text);
                    if (w.Length > 0) spec.Append(' ').Append(w);
                    continue;
                }
                spec.Append(part.kind == "boolean" ? " <>" : " (0)");
                args.Add("$" + args.Count);
            }
            if (args.Count > 10) args.RemoveRange(10, args.Count - 10);   // ($0-$9: the dialog allows ten inputs)
            var name = spec.ToString().Trim();
            if (name.Length == 0) name = "block";
            Register(new BlockDef { id = "call:" + m.key, category = "myblocks", shape = BlockShape.Stack, spec = name, py = m.Function + "(" + string.Join(", ", args) + ")", hidden = true });
            Register(new BlockDef { id = "define:" + m.key, category = "myblocks", shape = BlockShape.Hat, spec = "define " + name, py = "", hidden = true });
        }

        /// <summary>A list's "set" block: a number slot per item (a 2D list's rows apart).</summary>
        public static void RegisterSetList(ListVar l, bool twoD) {
            var spec = new System.Text.StringBuilder("set " + Words(l.name) + " to");
            var py = new System.Text.StringBuilder(BlockCode.Ident(l.name) + " = [");
            var n = 0;
            if (!twoD) {
                for (var i = 0; i < Mathf.Clamp(l.size, 1, 10); i++) {
                    spec.Append(" (0)");
                    py.Append(i > 0 ? ", " : "").Append("$" + n++);
                }
            }
            else {
                for (var r = 0; r < Mathf.Clamp(l.rows, 1, 10); r++) {
                    if (r > 0) spec.Append(" |");
                    py.Append(r > 0 ? ", [" : "[");
                    for (var c = 0; c < Mathf.Clamp(l.cols, 1, 10) && n < 10; c++) {
                        spec.Append(" (0)");
                        py.Append(c > 0 ? ", " : "").Append("$" + n++);
                    }
                    py.Append("]");
                }
            }
            py.Append("]");
            Register(new BlockDef { id = "setlist:" + l.name, category = "variables", shape = BlockShape.Stack, spec = spec.ToString(), py = py.ToString(), hidden = true });
        }

        /// <summary>Whether a list's items all fit in its "set" block (ten slots at most; bigger lists start at 0 and
        /// are set item by item).</summary>
        public static bool SetFits(ListVar l, bool twoD) => twoD ? l.rows * l.cols <= 10 && l.rows <= 10 : l.size <= 10;

        private static void Register(BlockDef d) {
            Parse(d);
            ById[d.id] = d;
        }

        private static BlockDef Wait(this BlockDef d) => d.Option("and don't wait", "False", "True", false);

        private static BlockDef Option(this BlockDef d, string label, string on, string off, bool initial) {
            d.hasToggle = true;
            d.toggleLabel = label;
            d.toggleOn = on;
            d.toggleOff = off;
            d.toggleDefault = initial;
            return d;
        }

        private static BlockDef Def(string category, string id, BlockShape shape, string spec, string py) {
            var d = new BlockDef { id = id, category = category, shape = shape, spec = spec, py = py };
            Parse(d);
            ById[id] = d;
            All.Add(d);
            return d;
        }

        public static BlockCategory Category(string id) => Categories.FirstOrDefault(c => c.id == id) ?? Categories[0];

        // The spec into labels and slots.
        private static void Parse(BlockDef d) {
            var s = d.spec.Trim();
            if (s.EndsWith("▶")) {
                d.hasToggle = true;
                s = s.Substring(0, s.Length - 1).TrimEnd();
            }
            var word = new System.Text.StringBuilder();
            void Flush() {
                var text = word.ToString().Trim();
                if (text.Length > 0) d.parts.Add(new BlockPart { label = text });
                word.Clear();
            }
            for (var i = 0; i < s.Length; i++) {
                var c = s[i];
                char close;
                SlotKind kind;
                if (c == '(') { close = ')'; kind = SlotKind.Number; }
                else if (c == '[') { close = ']'; kind = SlotKind.Text; }
                else if (c == '<' && i + 1 < s.Length && s[i + 1] == '>') { close = '>'; kind = SlotKind.Boolean; }
                else if (c == '{') { close = '}'; kind = SlotKind.Dropdown; }
                else { word.Append(c); continue; }
                var end = s.IndexOf(close, i + 1);
                if (end < 0) { word.Append(c); continue; }
                Flush();
                var inner = s.Substring(i + 1, end - i - 1);
                var slot = new Slot { kind = kind, initial = inner };
                if (kind == SlotKind.Dropdown) {
                    if (inner.StartsWith("@")) { slot.kind = SlotKind.Device; slot.key = inner.Substring(1); slot.initial = ""; }
                    else if (inner.StartsWith("$")) { slot.kind = SlotKind.Variable; slot.key = inner.Substring(1); slot.initial = ""; }
                    else if (inner.StartsWith("=")) { slot.kind = SlotKind.Name; slot.key = inner.Substring(1); slot.initial = ""; }
                    else { slot.key = inner; slot.initial = Options[inner][0].value; }
                }
                else if (kind == SlotKind.Boolean) slot.initial = "";
                d.parts.Add(new BlockPart { slot = slot });
                d.slots.Add(slot);
                i = end;
            }
            Flush();
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;

        /// <summary>The label an option shows (its value when it isn't one of them).</summary>
        public static string OptionLabel(string key, string value) {
            if (!Options.TryGetValue(key, out var options)) return value;
            foreach (var (label, v) in options) if (v == value) return label;
            return options.Length > 0 ? options[0].label : value;
        }
    }
}
