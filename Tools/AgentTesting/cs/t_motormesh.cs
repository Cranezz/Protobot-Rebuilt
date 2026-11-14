var sb = new System.Text.StringBuilder();
foreach (var go in UnityEngine.Resources.LoadAll<UnityEngine.GameObject>("Part Prefabs")) {
  if (!go.name.Contains("Motor") && !go.name.Contains("Screw")) continue;
  foreach (var mf in go.GetComponentsInChildren<UnityEngine.MeshFilter>(true)) if (mf.sharedMesh != null) sb.Append(go.name + " -> " + mf.sharedMesh.name + " @ " + UnityEditor.AssetDatabase.GetAssetPath(mf.sharedMesh) + "\n");
}
var gens = UnityEngine.Resources.LoadAll<UnityEngine.ScriptableObject>("Part Prefabs");
return sb.ToString().Length > 0 ? sb.ToString() : "none";
