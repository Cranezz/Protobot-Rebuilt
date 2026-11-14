var sb = new System.Text.StringBuilder();
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) {
  if (!so.id.StartsWith("MOTR")) continue;
  var t = System.Type.GetType("Protobot.Motion.WirePorts, Assembly-CSharp");
  var m = t.GetMethod("Of", new[] { typeof(UnityEngine.GameObject) });
  var r = m != null ? m.Invoke(null, new object[] { so.gameObject }) as System.Collections.ICollection : null;
  var mf = so.GetComponentInChildren<UnityEngine.MeshFilter>();
  sb.Append("motor ports " + (r != null ? r.Count.ToString() : "?") + ", mesh " + mf.sharedMesh.name + " " + mf.sharedMesh.triangles.Length / 3 + " tris, holes " + System.Linq.Enumerable.Count(so.GetComponentsInChildren<UnityEngine.Component>(), c => c.GetType().Name == "HoleCollider"));
  break;
}
return sb.ToString();
