using System.Linq;

namespace Protobot.RobotCode.Blocks {
    /// <summary>V2 (Blocks): starting programs made of blocks (the code window's Blocks menu), on the robot's own
    /// devices (its configuration from the build).</summary>
    public static class BlockExamples {
        public static readonly string[] Names = { "Example: drive a square", "Example: tank drive with the controller", "Example: autonomous and driver control" };

        private static Block B(string id) => Block.Make(BlockDefs.ById[id]);

        private static Block Chain(params Block[] blocks) {
            for (var i = 0; i + 1 < blocks.Length; i++) blocks[i].next = blocks[i + 1];
            return blocks[0];
        }

        public static BlockProgram Make(int index, string config) {
            var p = new BlockProgram { config = config };
            var devices = BlockCode.Devices(config);
            var left = devices["motor"].FirstOrDefault(n => n == "left_drive") ?? devices["motor"].FirstOrDefault() ?? "";
            var right = devices["motor"].FirstOrDefault(n => n == "right_drive") ?? devices["motor"].Skip(1).FirstOrDefault() ?? left;
            switch (index) {
                case 0: {
                    // when started: repeat 4 (drive forward 24 inches, turn right 90 degrees).
                    var start = B("when_started");
                    var velocity = B("set_drive_velocity");
                    var repeat = B("repeat");
                    repeat.fields[0] = "4";
                    var drive = B("drive_for");
                    drive.fields[1] = "24";
                    drive.fields[2] = "INCHES";
                    drive.next = B("turn_for");
                    repeat.mouths[0] = drive;
                    var print = B("print");
                    print.fields[0] = "Square done";
                    Chain(start, velocity, repeat, print);
                    start.x = 20f;
                    start.y = 20f;
                    p.stacks.Add(start);
                    break;
                }
                case 1: {
                    // when driver control: forever (each side's velocity = its stick, spin).
                    var hat = B("when_driver");
                    var forever = B("forever");
                    Block Side(string motor, string axis) {
                        var set = B("set_motor_velocity");
                        set.fields[0] = motor;
                        var stick = B("controller_position");
                        stick.fields[0] = axis;
                        set.inputs[1] = stick;
                        var spin = B("spin");
                        spin.fields[0] = motor;
                        set.next = spin;
                        return set;
                    }
                    var l = Side(left, "axis3");
                    l.Last().next = Side(right, "axis2");
                    forever.mouths[0] = l;
                    hat.next = forever;
                    hat.x = 20f;
                    hat.y = 20f;
                    p.stacks.Add(hat);
                    break;
                }
                default: {
                    // when autonomous: drive forward 24", turn to heading 90; when driver control: print, R1 runs the first other motor.
                    var auton = B("when_autonomous");
                    var drive = B("drive_for");
                    drive.fields[1] = "24";
                    drive.fields[2] = "INCHES";
                    Chain(auton, drive, B("turn_for"));
                    auton.x = 20f;
                    auton.y = 20f;
                    p.stacks.Add(auton);
                    var driver = B("when_driver");
                    var print = B("print");
                    print.fields[0] = "Driver control";
                    driver.next = print;
                    driver.x = 20f;
                    driver.y = 200f;
                    p.stacks.Add(driver);
                    var other = devices["motor"].FirstOrDefault(n => n != left && n != right && !n.StartsWith("left_") && !n.StartsWith("right_"));
                    if (other != null) {
                        var button = B("when_button");
                        button.fields[0] = "buttonR1";
                        var spin = B("spin_for");
                        spin.fields[0] = other;
                        spin.fields[2] = "360";
                        button.next = spin;
                        button.x = 20f;
                        button.y = 330f;
                        p.stacks.Add(button);
                    }
                    break;
                }
            }
            return p;
        }
    }
}
