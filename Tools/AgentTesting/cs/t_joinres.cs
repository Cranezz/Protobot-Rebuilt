var t = System.AppDomain.CurrentDomain.GetData("joinTask") as System.Threading.Tasks.Task<string>;
if (t == null || !t.IsCompleted) return "waiting";
return "join: " + (t.Result ?? "OK");
