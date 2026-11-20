// Reads what the app has now; then (step 2) the app's own edit turns the program into C++ and writes it out.
var folder = Protobot.RobotCode.CodeStore.Folder;
var now = "app: " + Protobot.RobotCode.CodeStore.Source.Trim();
Protobot.RobotCode.CodeStore.Set("#include \"main.h\"\nvoid opcontrol() { printf(\"three\n\"); }\n");
return now + " | main.py after app edit: " + System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "main.py")).Replace("\n", " ") + " | main.cpp exists " + System.IO.File.Exists(System.IO.Path.Combine(folder, "main.cpp"));
