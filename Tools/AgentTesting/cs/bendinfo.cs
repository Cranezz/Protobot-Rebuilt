var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "SHFT-HS-4in", "SNDF-2.00in" }) {
  GameObject p = null; try { p = Protobot.PartsManager.GeneratePart(id, new Vector3(500,500,500), Quaternion.identity); } catch (System.Exception e) { sb.Append(id + " ERR " + e.Message + "\n"); continue; }
  var m = p.GetComponent<MeshFilter>().sharedMesh; sb.Append(id + " -> " + p.GetComponent<Protobot.SavedObject>().id + " verts " + m.vertexCount + " tris " + m.triangles.Length / 3 + " sub " + m.subMeshCount + " bounds " + m.bounds.center.ToString("F2") + " " + m.bounds.size.ToString("F2") + " readable " + m.isReadable + " comps " + string.Join(",", System.Linq.Enumerable.Select(p.GetComponents<Component>(), c => c.GetType().Name)) + "\n");
  foreach (var h in p.GetComponentsInChildren<Protobot.HoleCollider>(true)) sb.Append("  hole " + h.transform.localPosition.ToString("F2") + " " + h.transform.localScale.ToString("F2") + " " + h.holeType + "\n");
  UnityEngine.Object.DestroyImmediate(p);
}
var g = Protobot.PartsManager.GetPartType("SHFT").GetComponent<Protobot.PartGenerator>(); sb.Append("SHFT p1 " + string.Join(",", g.GetParam1Options()) + " p2 " + string.Join(",", g.GetParam2Options()));
return sb.ToString();
