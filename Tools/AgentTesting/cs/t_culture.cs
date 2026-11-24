// (Edit or Play mode) A comma-decimal PC: "0.0033" parsed with the PC's format is 33. Checks the original Setting reads
// its camera speeds right under German, and (Play) that the app runs invariant and the camera has the right pan speed.
var de = new System.Globalization.CultureInfo("de-DE");
var keep = System.Threading.Thread.CurrentThread.CurrentCulture;
var log = new System.Text.StringBuilder();
System.Threading.Thread.CurrentThread.CurrentCulture = de;
float.TryParse("0.0033", out var raw);
log.Append("de-DE raw parse 0.0033 -> " + raw.ToString(System.Globalization.CultureInfo.InvariantCulture));
var go = new UnityEngine.GameObject("t_culture");
var s = go.AddComponent<Protobot.Setting>();
foreach (var v in new[] { "0.0033", "0,0033", "0.1", "5", "true" }) { s.currentValue = v; log.Append(" | " + v + " -> " + System.Convert.ToString(s.RealValue, System.Globalization.CultureInfo.InvariantCulture)); }
UnityEngine.Object.DestroyImmediate(go);
System.Threading.Thread.CurrentThread.CurrentCulture = keep;
log.Append(" | now " + System.Globalization.CultureInfo.CurrentCulture.Name + "(empty = invariant)");
if (UnityEngine.Application.isPlaying) {
    log.Append(" | pan " + Protobot.MousePivotCameraInput.PanSens + " zoom " + Protobot.MousePivotCameraInput.ZoomSens + " orbit " + Protobot.MousePivotCameraInput.OrbitSens);
}
return log.ToString();
