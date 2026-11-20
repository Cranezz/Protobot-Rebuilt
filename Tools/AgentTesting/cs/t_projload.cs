string summary;
var text = Protobot.RobotCode.CodeProject.FromFolder(@"C:/Users/kille/AppData/Local/Temp/claude/5b0c4ec0a185a9e3/8d21650f-8a3d-4b32-911f-851c0b975844/scratchpad/spinup/VEX PROGRAMS SPIN UP/WorldsLastResortSpinUp", out summary);
Protobot.RobotCode.CodeStore.Set(text);
return summary;
