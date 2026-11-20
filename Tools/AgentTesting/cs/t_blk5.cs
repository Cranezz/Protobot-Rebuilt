// A blocks program whose print fails (letter 10 of "apple"): Run, then (t_blk6) the failing block is red.
var D = Protobot.RobotCode.Blocks.BlockDefs.ById;
var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
var T = typeof(Protobot.RobotCode.CodePanel);
var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var editor = (Protobot.RobotCode.Blocks.BlockEditor)T.GetField("blocks", F).GetValue(panel);
UnityEngine.PlayerPrefs.SetString("t_blk_undo", editor.program.Json());
var p = Protobot.RobotCode.Blocks.BlockProgram.FromJson(editor.program.Json());
var start = Protobot.RobotCode.Blocks.Block.Make(D["when_started"]); start.x = 20; start.y = 300;
var pr = Protobot.RobotCode.Blocks.Block.Make(D["print"]);
var letter = Protobot.RobotCode.Blocks.Block.Make(D["letter"]); letter.fields[0] = "10";
pr.inputs[0] = letter;
start.next = Protobot.RobotCode.Blocks.Block.Make(D["wait"]);
start.next.next = pr;
p.stacks.Clear();
p.stacks.Add(start);
Protobot.RobotCode.CodeStore.Set(p.ToSource(out _));
T.GetMethod("Run", F).Invoke(panel, null);
return "running";
