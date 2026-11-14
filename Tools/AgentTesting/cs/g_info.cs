var g = Protobot.Game.SkillsGame.Current;
var field = UnityEngine.Object.FindFirstObjectByType<Protobot.Game.GameField>();
var sb = new System.Text.StringBuilder("sandbox " + Protobot.Motion.Sandbox.Running + "\n");
foreach (var t in UnityEngine.Object.FindObjectsByType<Protobot.Game.Toggle>(FindObjectsSortMode.None)) sb.Append("toggle " + t.quadrant + " at " + field.ToField(t.transform.position).ToString("F1") + " color " + t.SetColor + "\n");
var counts = new System.Collections.Generic.Dictionary<string, int>();
var near = new System.Text.StringBuilder();
foreach (var b in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (b.gameObject.layer == 18) {
  var f = field.ToField(b.position);
  near.Append(b.name.Substring(0, System.Math.Min(12, b.name.Length)) + f.ToString("F0") + " ");
}
sb.Append("pieces: " + near.ToString().Substring(0, System.Math.Min(1500, near.Length)));
return sb.ToString();
