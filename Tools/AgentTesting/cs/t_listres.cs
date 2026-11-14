var t = System.AppDomain.CurrentDomain.GetData("listTask") as System.Threading.Tasks.Task;
if (t == null) return "no task";
if (!t.IsCompleted) return "still running";
var r = t.GetType().GetProperty("Result").GetValue(t, null);
var list = r.GetType().GetField("Item1").GetValue(r) as System.Collections.IList;
var problem = r.GetType().GetField("Item2").GetValue(r);
var sb = new System.Text.StringBuilder("problem: " + (problem ?? "none") + " | matches: " + (list == null ? 0 : list.Count));
if (list != null) foreach (var m in list) { sb.Append(" |"); foreach (var f in m.GetType().GetFields()) sb.Append(" " + f.Name + "=" + f.GetValue(m)); }
return sb.ToString();
