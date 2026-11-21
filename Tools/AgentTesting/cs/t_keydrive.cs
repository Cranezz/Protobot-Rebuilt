// Drives the sandbox like the controller (Sandbox.TestSticks): Temp/sticks.txt "ly,rx" (split arcade; 0,0 releases).
// Starts the sandbox if it isn't running.
var a = System.IO.File.ReadAllText("Temp/sticks.txt").Trim().Split(',').Select(float.Parse).ToArray();
if (!Protobot.Motion.Sandbox.Running) { Protobot.RobotCode.RobotProgram.Stop("test"); Protobot.Motion.Sandbox.Begin(); }
var ly = a[0]; var rx = a[1];
Protobot.Motion.Sandbox.TestSticks = () => (new Vector2(0f, ly), new Vector2(rx, 0f));
return "sticks " + ly + ", " + rx;
