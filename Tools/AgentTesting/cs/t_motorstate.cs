var p = Protobot.RobotCode.RobotProgram.Current;
if (p == null) return "no program";
var dict = (System.Collections.IDictionary)typeof(Protobot.RobotCode.RobotProgram).GetField("motors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(p);
var sb = new System.Text.StringBuilder();
foreach (System.Collections.DictionaryEntry e in dict) {
  var c = e.Value; var t = c.GetType();
  System.Func<string, object> F = n => { var f = t.GetField(n); return f != null ? f.GetValue(c) : t.GetProperty(n).GetValue(c); };
  sb.AppendLine(((UnityEngine.Component)e.Key).name + " mode=" + F("mode") + " deg=" + System.Convert.ToDouble(F("degrees")).ToString("0.0") + " target=" + System.Convert.ToDouble(F("targetDeg")).ToString("0.0") + " done=" + F("done") + " level=" + System.Convert.ToSingle(F("Level")).ToString("0.000") + " stop=" + F("stopping"));
}
return sb.ToString();
