// Blocks: a program made in code (when started: drive a square, print; a button event; a variable; a competition),
// its Python, the round trip through the saved text, and that the Python compiles.
var D = Protobot.RobotCode.Blocks.BlockDefs.ById;
System.Func<string, Protobot.RobotCode.Blocks.Block> B = id => Protobot.RobotCode.Blocks.Block.Make(D[id]);
var p = new Protobot.RobotCode.Blocks.BlockProgram();
p.config = "from vex import *\n\nbrain = Brain()\ncontroller = Controller()\nleft_motor_a = Motor(Ports.PORT1, GearSetting.RATIO_18_1, False)\nright_motor_a = Motor(Ports.PORT2, GearSetting.RATIO_18_1, True)\nleft_drive = MotorGroup(left_motor_a)\nright_drive = MotorGroup(right_motor_a)\ndrivetrain = DriveTrain(left_drive, right_drive, 10.21, 12, 12, INCHES, 1)";
var start = B("when_started"); start.x = 20; start.y = 20;
var set = B("set_var"); set.fields[1] = "3";
start.next = set;
var rep = B("repeat"); rep.fields[0] = "4";
set.next = rep;
var drive = B("drive_for"); drive.fields[1] = "12"; drive.fields[2] = "INCHES";
var turn = B("turn_for");
drive.next = turn;
rep.mouths[0] = drive;
var pr = B("print");
var join = B("join"); join.fields[0] = "heading ";
join.inputs[1] = B("drive_heading");
pr.inputs[0] = join;
rep.next = pr;
var ifb = B("if_else");
var cmp = B("compare"); cmp.inputs[0] = B("var"); cmp.inputs[0].fields[0] = "myVariable"; cmp.fields[1] = ">"; cmp.fields[2] = "2";
ifb.inputs[0] = cmp;
ifb.mouths[0] = B("console_print");
pr.next = ifb;
var fn = B("mathfn"); fn.fields[0] = "math.sqrt(#)"; fn.fields[1] = "16";
var change = B("change_var"); change.inputs[1] = fn;
ifb.mouths[1] = change;
p.stacks.Add(start);
var btn = B("when_button"); btn.fields[0] = "buttonA"; btn.x = 300;
btn.next = B("spin");   // a motor block: the robot's first motor
p.stacks.Add(btn);
var auto = B("when_autonomous"); auto.next = B("drive"); auto.next.next = B("forever"); auto.next.next.mouths[0] = B("wait");
p.stacks.Add(auto);
var loose = B("stop_driving"); p.stacks.Add(loose);   // not under a hat: not in the program
var source = p.ToSource(out var code);
var back = Protobot.RobotCode.Blocks.BlockProgram.FromSource(source);
var same = back != null && back.ToSource(out _) == source;
string compile;
try { Protobot.Scripting.Compiler.Compile(source); compile = "compiles"; } catch (System.Exception e) { compile = "ERROR " + e.Message; }
var lines = source.Split('\n');
var mapped = 0; for (var i = 0; i < code.lineBlocks.Count; i++) if (code.lineBlocks[i] != null) mapped++;
return "round trip " + same + ", " + compile + ", " + mapped + " lines from blocks\n" + string.Join("\n", lines.Skip(2));
