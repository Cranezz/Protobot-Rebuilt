// Writes every C++ example (devices filled in) to <t_hdr_dir>/progs/NN.cpp for compile checks.
var dir = System.IO.Path.Combine(UnityEngine.PlayerPrefs.GetString("t_hdr_dir"), "progs");
System.IO.Directory.CreateDirectory(dir);
var names = new System.Collections.Generic.List<string>();
for (int i = 0; i < Protobot.RobotCode.Examples.All.Length; i++) {
    var e = Protobot.RobotCode.Examples.All[i];
    var code = Protobot.RobotCode.Examples.Fill(e.code);
    if (!Protobot.Scripting.CppTranslator.LooksLikeCpp(code)) continue;
    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, i.ToString("00") + ".cpp"), code);
    names.Add(i + " " + e.name);
}
return string.Join(" | ", names);
