// (Play) Trailer game control. Temp/gamectl.txt: "open" (Robot Skills on the full field), "run" (start the run),
// "sticks L R" (test sticks: tank, left/right forward power -1..1), "stop sticks", "state".
var cmd = System.IO.File.ReadAllText("Temp/gamectl.txt").Trim().Split(' ');
var g = Protobot.Game.SkillsGame.Current;
if (cmd[0] == "open") {
    Protobot.Online.OnlineWindow.Close();
    Protobot.Game.SkillsGame.Open(Protobot.Game.GameDefinition.All()[0], false, Protobot.Game.SkillsGame.Mode.Skills);
    return "game " + (Protobot.Game.SkillsGame.Current != null);
}
if (cmd[0] == "openmatch") {
    Protobot.Online.OnlineWindow.Close();
    Protobot.Game.SkillsGame.Open(Protobot.Game.GameDefinition.All()[0], false, Protobot.Game.SkillsGame.Mode.Match);
    var m = Protobot.Game.SkillsGame.Current;
    m.SetSkipAutonomous(true);
    m.Apply();
    return "match " + (m != null);
}
if (cmd[0] == "run") { g.StartRun(); return "state " + g.state; }
if (cmd[0] == "sticks") {
    float l = float.Parse(cmd[1], System.Globalization.CultureInfo.InvariantCulture), r = float.Parse(cmd[2], System.Globalization.CultureInfo.InvariantCulture);
    Protobot.Motion.Sandbox.TestSticks = () => (new UnityEngine.Vector2(0f, l), new UnityEngine.Vector2(0f, r));
    return "sticks " + l + " " + r;
}
if (cmd[0] == "stop") { Protobot.Motion.Sandbox.TestSticks = null; return "sticks off"; }
if (cmd[0] == "state") return g == null ? "no game" : "state " + g.state + ", sandbox " + Protobot.Motion.Sandbox.Running;
return "?";
