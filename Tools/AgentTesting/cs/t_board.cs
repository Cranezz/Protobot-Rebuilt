// Opens Tools > Protobot > Players; "read" reports what it loaded (counts, and the top rows without names).
var t = System.Type.GetType("Protobot.EditorTools.PlayersBoard, Assembly-CSharp-Editor");
var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var mode = System.IO.File.Exists("Temp/board.txt") ? System.IO.File.ReadAllText("Temp/board.txt").Trim() : "open";
if (mode == "open") { t.GetMethod("Open", f).Invoke(null, null); return "opened"; }
var w = UnityEditor.EditorWindow.GetWindow(t);
var s = "status " + t.GetField("status", f).GetValue(w) + " | players " + t.GetField("players", f).GetValue(w) + " accounts " + t.GetField("accounts", f).GetValue(w)
      + " online " + t.GetField("online", f).GetValue(w) + " today " + t.GetField("today", f).GetValue(w) + " week " + t.GetField("week", f).GetValue(w);
var areas = (System.Collections.Generic.Dictionary<string, double>)t.GetField("areaTotals", f).GetValue(w);
s += " | areas " + string.Join(", ", areas.Select(a => a.Key + " " + System.Math.Round(a.Value)));
return s;
