var m = Protobot.Online.OnlineMatch.Current; var t = m.GetType(); var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var trying = (System.Collections.Generic.Dictionary<int,bool>)t.GetField("trying", bf).GetValue(m);
var speed = (float)t.GetMethod("RobotSpeed", bf).Invoke(m, new object[] { 1 });
var seatOf = t.GetMethod("SeatOf", bf);
System.Func<UnityEngine.Collider, bool> byRed = c => (int)seatOf.Invoke(m, new object[] { c }) == 0;
System.Func<UnityEngine.Collider, bool> byField = c => c.gameObject.layer == 19;
var touches = t.GetMethod("RobotTouches", bf);
var tr = trying.ContainsKey(1) ? trying[1].ToString() : "none";
return "trying[1]=" + tr + " speed1=" + speed.ToString("F2") + " touchRed=" + touches.Invoke(m, new object[] { 1, byRed }) + " touchField=" + touches.Invoke(m, new object[] { 1, byField }) + " state " + Protobot.Game.SkillsGame.Current.state + " auto " + Protobot.Game.SkillsGame.Current.InAutonomous;
