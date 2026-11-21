// Re-makes the pods' bands as kind K with W wraps (Temp/bandkind.txt "K,W") and says how hard each pulls.
var a = System.IO.File.ReadAllText("Temp/bandkind.txt").Trim().Split(',');
var sb = new System.Text.StringBuilder();
foreach (var b in UnityEngine.Object.FindObjectsByType<Protobot.Motion.ElasticBand>(UnityEngine.FindObjectsSortMode.None)) {
    b.UseKind(Protobot.Motion.ElasticKind.Of(a[0]));
    b.Set(b.posts.Select(p => p.Copy()).ToList(), b.bands, int.Parse(a[1]), 0f, Vector3.zero);
    sb.AppendLine(a[0] + " x" + a[1] + ": " + b.Describe());
}
return sb.ToString();
