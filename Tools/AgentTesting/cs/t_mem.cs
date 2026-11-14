long meshBytes = 0; int meshCount = 0;
foreach (var m in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>()) { meshBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m); meshCount++; }
long texBytes = 0;
foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Texture>()) texBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
long robotTris = 0, fieldTris = 0, otherTris = 0;
var fieldObj = UnityEngine.GameObject.Find("Game Field");
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None)) {
  if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
  var f = r.GetComponent<UnityEngine.MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
  long tris = 0; var m = f.sharedMesh; for (int s = 0; s < m.subMeshCount; s++) tris += m.GetIndexCount(s) / 3;
  if (fieldObj != null && r.transform.IsChildOf(fieldObj.transform)) fieldTris += tris;
  else if (r.GetComponentInParent<Protobot.SavedObject>() != null) robotTris += tris; else otherTris += tris;
}
return "meshes " + meshCount + " = " + (meshBytes >> 20) + " MB, textures " + (texBytes >> 20) + " MB, reserved " + (UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20) + " MB; drawn tris robot " + robotTris + " field " + fieldTris + " other " + otherTris;
