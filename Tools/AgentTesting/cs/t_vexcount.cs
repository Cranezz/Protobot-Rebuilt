long total = 0; int meshes = 0; var per = new System.Collections.Generic.List<string>();
foreach (var path in System.IO.Directory.GetFiles("Assets/ProtobotRebuilt/VEX Parts/Meshes", "*.asset")) {
  long t = 0; int n = 0;
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path)) { var m = o as UnityEngine.Mesh; if (m == null) continue; n++; for (int s = 0; s < m.subMeshCount; s++) t += m.GetIndexCount(s) / 3; }
  total += t; meshes += n; per.Add(System.IO.Path.GetFileNameWithoutExtension(path) + " " + n + " meshes " + (t / 1000) + "k");
}
per.Sort((a, b) => b.Split(' ')[3].Length != a.Split(' ')[3].Length ? b.Split(' ')[3].Length - a.Split(' ')[3].Length : string.CompareOrdinal(b.Split(' ')[3], a.Split(' ')[3]));
return "total " + meshes + " meshes, " + (total / 1000) + "k tris\n" + string.Join("\n", per.GetRange(0, System.Math.Min(15, per.Count)));
