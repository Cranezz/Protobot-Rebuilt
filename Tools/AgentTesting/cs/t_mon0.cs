// Did the Monitor build compile? (CodeMonitor and Sandbox.StepOnce exist)
var t = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } }).FirstOrDefault(x => x.FullName == "Protobot.RobotCode.CodeMonitor");
var s = typeof(Protobot.Motion.Sandbox).GetMethod("StepOnce");
return "monitor " + (t != null) + " step " + (s != null) + " compiling " + UnityEditor.EditorApplication.isCompiling;
