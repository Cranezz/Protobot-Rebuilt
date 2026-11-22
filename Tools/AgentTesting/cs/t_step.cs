// Steps Play mode N frames (Temp/steps.txt, default 20) for when the Unity window is covered (it doesn't advance then).
var n = System.IO.File.Exists("Temp/steps.txt") ? int.Parse(System.IO.File.ReadAllText("Temp/steps.txt").Trim()) : 20;
var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
var f = t.GetMethod("Frames").Invoke(null, new object[] { n });
UnityEditor.EditorApplication.isPaused = false;
return "frame " + f;
