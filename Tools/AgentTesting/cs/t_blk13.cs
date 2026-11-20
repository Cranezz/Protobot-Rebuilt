// (Code window open in blocks mode) the program's Python lines that are comments or defs, and the My Blocks.
var src = Protobot.RobotCode.CodeStore.Source;
var back = Protobot.RobotCode.Blocks.BlockProgram.FromSource(src);
return string.Join("\n", src.Split('\n').Skip(2).Where(l => l.TrimStart().StartsWith("#") && !l.Contains("Motor") || l.StartsWith("def ") || l.Contains("drive_side(") || l.Contains("lift_arm"))) + "\nmy blocks: " + string.Join(", ", back.myBlocks.Select(m => m.name + " " + m.parts.Count));
