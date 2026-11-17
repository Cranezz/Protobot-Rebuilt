// A log from another thread reaches the session log without an exception.
var t = new System.Threading.Thread(() => UnityEngine.Debug.Log("thread log test"));
t.Start(); t.Join();
Protobot.Builds.ActionLog.Flush();
var lines = System.IO.File.ReadAllLines(Protobot.Builds.ActionLog.CurrentFile).Where(l => l.Contains("thread log test")).ToList();
return lines.Count > 0 ? lines.Last() : "not logged";
