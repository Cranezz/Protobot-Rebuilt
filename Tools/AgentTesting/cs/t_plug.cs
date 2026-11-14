var t = Protobot.PartsManager.GetPartType("VXPNEUMATICS");
foreach (var pd in t.GetComponentsInChildren<Protobot.PartData>(true)) {
  if (!pd.name.Contains("Plug")) continue;
  var v = pd.GetComponent<MeshFilter>().sharedMesh.vertices; int bins = 22; var r = new float[bins]; float lo = -0.472f, hi = 0.606f;
  foreach (var p in v) { int k = Mathf.Clamp((int)((p.z - lo) / (hi - lo) * bins), 0, bins - 1); r[k] = Mathf.Max(r[k], new Vector2(p.x, p.y).magnitude); }
  var s = ""; for (int k = 0; k < bins; k++) s += (lo + (k + 0.5f) * (hi - lo) / bins).ToString("F2") + ":" + r[k].ToString("F3") + " ";
  return s;
}
return "none";
