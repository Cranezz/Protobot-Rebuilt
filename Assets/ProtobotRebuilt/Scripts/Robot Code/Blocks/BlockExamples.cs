using System.Linq;

namespace Protobot.RobotCode.Blocks {
    /// <summary>V2 (Blocks): starting programs made of blocks (the code window's Blocks menu), on the robot's own
    /// devices (its configuration from the build).</summary>
    public static class BlockExamples {
        public static readonly string[] Names = { "Example: drive a square", "Example: tank drive with the controller", "Example: autonomous and driver control", "Example: a rectangle with a My Block and a list" };

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
                case 3: {
                    // A My Block "drive side (inches)" (drive forward, turn right 90) used four times with lengths from a list.
                    var m = new MyBlock { key = "side0001", name = "drive side", parts = { new MyBlockPart { kind = "number", text = "inches", id = "in000001" } } };
                    p.myBlocks.Add(m);
                    var sides = new ListVar { name = "sides", size = 4 };
                    p.lists.Add(sides);
                    p.RegisterDefs();
                    var define = B("define:" + m.key);
                    define.comment = "One side: drive its length, then turn the corner.";
                    var drive = B("drive_for");
                    drive.fields[2] = "INCHES";
                    var arg = B("arg");
                    arg.fields[0] = "inches";
                    drive.inputs[1] = arg;
                    Chain(define, drive, B("turn_for"));
                    define.x = 20f;
                    define.y = 410f;
                    p.stacks.Add(define);

                    var start = B("when_started");
                    var set = B("setlist:sides");
                    set.fields[0] = "24";
                    set.fields[1] = "12";
                    set.fields[2] = "24";
                    set.fields[3] = "12";
                    set.comment = "A 24 x 12 inch rectangle.";
                    var counter = B("set_var");
                    counter.fields[0] = "myVariable";
                    counter.fields[1] = "1";
                    var repeat = B("repeat");
                    repeat.fields[0] = "4";
                    var call = B("call:" + m.key);
                    var item = B("list_item");
                    item.fields[1] = "sides";
                    var counterValue = B("var");
                    counterValue.fields[0] = "myVariable";
                    item.inputs[0] = counterValue;
                    call.inputs[0] = item;
                    var step = B("change_var");
                    step.fields[0] = "myVariable";
                    call.next = step;
                    repeat.mouths[0] = call;
                    Chain(start, counter, set, repeat);
                    start.x = 20f;
                    start.y = 20f;
                    p.stacks.Add(start);
                    p.notes.Add(new Note { text = "Right-click a block or the canvas to add a comment.", x = 20f, y = 320f });
                    p.SyncMyBlocks();
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
