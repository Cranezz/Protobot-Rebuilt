var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
var reds = UnityEngine.Object.FindObjectsByType<Protobot.RobotCode.Blocks.BlockView>(UnityEngine.FindObjectsSortMode.None).Where(v => v.highlight == 2).Select(v => v.block.type);
var p = Protobot.RobotCode.RobotProgram.Current;
return "status " + (p == null ? "none" : p.Status + " line " + p.ErrorLine) + "; red blocks: " + string.Join(",", reds);
