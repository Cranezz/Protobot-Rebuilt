// Snaps each Toggle to its nearest resting notch (startAngle + k*120), keeping the face that's up now.
var g = Protobot.Game.SkillsGame.Current;
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var ts = (System.Collections.Generic.List<Protobot.Game.Toggle>)typeof(Protobot.Game.SkillsGame).GetField("toggles", bf).GetValue(g);
var am = typeof(Protobot.Game.Toggle).GetMethod("Angle", bf);
var sa = typeof(Protobot.Game.Toggle).GetField("startAngle", bf);
var sb = new System.Text.StringBuilder();
foreach (var t in ts) {
    var axis = t.transform.TransformDirection(t.axisLocal).normalized;
    for (int pass = 0; pass < 2; pass++) {
        float angle = (float)am.Invoke(t, null), start = (float)sa.GetValue(t);
        float target = start + UnityEngine.Mathf.Round((angle - start) / 120f) * 120f;
        float delta = UnityEngine.Mathf.DeltaAngle(angle, target);
        // try one direction; if it got worse, the other
        t.transform.rotation = UnityEngine.Quaternion.AngleAxis(delta, axis) * t.transform.rotation;
        float after = (float)am.Invoke(t, null);
        if (UnityEngine.Mathf.Abs(UnityEngine.Mathf.DeltaAngle(after, target)) > 1f)
            t.transform.rotation = UnityEngine.Quaternion.AngleAxis(-2f * delta, axis) * t.transform.rotation;
        if (t.body != null) t.body.rotation = t.transform.rotation;
    }
    sb.Append(t.quadrant + " " + t.SetColor + " | ");
}
var s = g.Evaluate();
return sb + "red " + s.red + " blue " + s.blue;
