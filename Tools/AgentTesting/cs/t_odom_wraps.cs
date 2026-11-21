// Re-wraps the odometry pods' bands (PlayerPrefs "t_odom_wraps", default 3) and says how hard they pull.
var n = int.Parse(UnityEngine.PlayerPrefs.GetString("t_odom_wraps", "3"));
var sb = new System.Text.StringBuilder();
foreach (var b in UnityEngine.Object.FindObjectsByType<Protobot.Motion.ElasticBand>(UnityEngine.FindObjectsSortMode.None)) {
    var posts = b.posts.Select(p => p.Copy()).ToList();
    b.Set(posts, b.bands, n, 0f, Vector3.zero);
    sb.AppendLine(b.Describe());
}
return sb.ToString();
