// VS Code sync without opening VS Code: starts watching main.py the way OpenExternally does (no editor launched),
// then t_sync2/3 save the file from outside and change the language. Play mode, code window closed.
var t = typeof(Protobot.RobotCode.CodeStore);
var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
Protobot.RobotCode.CodeStore.Load("print('one')\n");
var folder = Protobot.RobotCode.CodeStore.Folder;
System.IO.Directory.CreateDirectory(folder);
t.GetField("watchedPath", F).SetValue(null, System.IO.Path.Combine(folder, "main.py"));
t.GetProperty("Watching").GetSetMethod(true).Invoke(null, new object[] { true });
t.GetMethod("WriteExternal", F).Invoke(null, null);
t.GetNestedType("Watcher", F).GetMethod("Ensure").Invoke(null, null);
return "watching " + System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "main.py")).Trim();
