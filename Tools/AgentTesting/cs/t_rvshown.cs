var r = UnityEngine.Object.FindFirstObjectByType<Protobot.Game.ScoreReveal>();
if (r == null) return "none";
var f = typeof(Protobot.Game.ScoreReveal).GetField("shown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var d = typeof(Protobot.Game.ScoreReveal).GetField("done", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
return "shown " + f.GetValue(r) + " done " + d.GetValue(r) + " t " + UnityEngine.Time.unscaledTime.ToString("0.0");
