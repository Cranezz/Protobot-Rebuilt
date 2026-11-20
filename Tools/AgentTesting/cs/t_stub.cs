// Writes the Python stubs (vex.pyi, motion.pyi) and the Python examples to <t_hdr_dir>/py for checks.
var dir = System.IO.Path.Combine(UnityEngine.PlayerPrefs.GetString("t_hdr_dir"), "py");
System.IO.Directory.CreateDirectory(dir);
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "vex.pyi"), Protobot.RobotCode.ApiStub.Text);
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "motion.pyi"), Protobot.RobotCode.ApiStub.Motion);
int n = 0;
for (int i = 0; i < Protobot.RobotCode.Examples.All.Length; i++) {
    var code = Protobot.RobotCode.Examples.Fill(Protobot.RobotCode.Examples.All[i].code);
    if (Protobot.Scripting.CppTranslator.LooksLikeCpp(code)) continue;
    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "ex" + i.ToString("00") + ".py"), code); n++;
}
return "examples " + n;
