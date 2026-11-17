// Writes the VS Code headers to the scratch folder in PlayerPrefs "t_hdr_dir" and lists what was written.
var dir = UnityEngine.PlayerPrefs.GetString("t_hdr_dir");
Protobot.RobotCode.CppHeaders.Write(dir);
return string.Join(" | ", System.IO.Directory.GetFiles(dir, "*", System.IO.SearchOption.AllDirectories).Select(f => f.Substring(dir.Length) + " " + new System.IO.FileInfo(f).Length));
