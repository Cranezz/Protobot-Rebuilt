var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "VXSLIDEASSEMBLYP-Outer Slide Member", "VXSLIDEASSEMBLYP-Inner Acetal Slide Truck", "VXSLIDEASSEMBLYP-Inner Slide Member" }) {
  var p = Protobot.PartsManager.GeneratePart(id, new Vector3(500,500,500), Quaternion.identity); var m = p.GetComponent<MeshFilter>().sharedMesh;
  sb.Append(id + " bounds " + m.bounds.min.ToString("F3") + " .. " + m.bounds.max.ToString("F3") + "\n");
  // cross-section at the middle of its length (x): slices of rays along z at many y
  UnityEngine.Object.DestroyImmediate(p);
}
return sb.ToString();
