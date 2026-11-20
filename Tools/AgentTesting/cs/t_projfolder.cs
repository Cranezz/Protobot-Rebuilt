// CodeProject.FromFolder on a few of the team's project folders: summary, files, translation.
var root = @"C:/Users/kille/AppData/Local/Temp/claude/5b0c4ec0a185a9e3/8d21650f-8a3d-4b32-911f-851c0b975844/scratchpad/spinup/VEX PROGRAMS SPIN UP/";
var sb = new System.Text.StringBuilder();
foreach (var d in new[] { "WorldsLastResortSpinUp", "WorldsLastResortSpinUp/Odometry - Monte/test", "WorldsLastResortSpinUp/Odometry - Monte/Skillz", "RedTeamClose OLD" }) {
    string summary;
    var text = Protobot.RobotCode.CodeProject.FromFolder(root + d, out summary);
    sb.Append("== " + d + ": " + summary + "\n");
    foreach (var s in Protobot.RobotCode.CodeProject.Split(text)) sb.Append("   " + s.path + " (marker line " + s.markerLine + ")\n");
    System.Collections.Generic.List<string> w;
    try { Protobot.Scripting.Compiler.CompileModule(Protobot.Scripting.CppTranslator.Translate(Protobot.RobotCode.CodeProject.CodeOnly(text), out w)); sb.Append("   translates ok; files " + Protobot.RobotCode.CodeProject.Files(text).Count + "\n"); }
    catch (System.Exception e) { sb.Append("   ERROR " + e.Message + "\n"); }
    // round trip: Split then Join gives the same text
    var again = Protobot.RobotCode.CodeProject.Join(Protobot.RobotCode.CodeProject.Split(text).Select(s => (s.path, s.text)));
    sb.Append("   round trip " + (again == text) + "\n");
}
return sb.ToString();
