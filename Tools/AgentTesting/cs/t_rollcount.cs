// The free (tracking) wheels' steps: rolling on a contact, rolling near the floor, spinning free. Resets them.
var t = typeof(Protobot.Motion.Sandbox);
var f = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var r = "contact " + t.GetField("rollingSteps", f).GetValue(null) + " near " + t.GetField("nearSteps", f).GetValue(null) + " free " + t.GetField("freeSteps", f).GetValue(null);
foreach (var n in new[] { "rollingSteps", "nearSteps", "freeSteps" }) t.GetField(n, f).SetValue(null, 0);
return r;
