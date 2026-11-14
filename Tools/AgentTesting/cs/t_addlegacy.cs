// Adds VEX's legacy cylinders to the Cylinder (PNMT) family: the retracted body + rod from the VEX-CAD family, as one
// mesh (PneumaticCylinder splits them again into body and moving rod).
var sb = new System.Text.StringBuilder();
var family = Resources.Load<GameObject>("Part Prefabs/VEX/Pneumatics");
var prefabPath = "Assets/ProtobotRebuilt/Resources/Part Prefabs/3Pneumatics/Cylinder.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
Transform template = null;
foreach (Transform c in root.transform) { var d = c.GetComponent<Protobot.PartData>(); if (d != null && d.param1Value == "25mm" && d.param2Value == "Normal") template = c; }
foreach (var size in new[] { "Legacy Double Acting", "Legacy Single Acting" }) {
  var body = family.transform.Find(size + " Piston (body)");
  var rod = family.transform.Find(size + " Piston (rod)");
  var combine = new[] {
    new CombineInstance { mesh = body.GetComponent<MeshFilter>().sharedMesh, transform = Matrix4x4.identity },
    new CombineInstance { mesh = rod.GetComponent<MeshFilter>().sharedMesh, transform = Matrix4x4.identity } };
  var mesh = new Mesh { name = size + " Cylinder", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
  mesh.CombineMeshes(combine, true, true);
  mesh.RecalculateBounds();
  var meshPath = "Assets/ProtobotRebuilt/Models/V2/" + size + " Cylinder.asset";
  UnityEditor.AssetDatabase.CreateAsset(mesh, meshPath);
  var old = root.transform.Find(size); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
  var copy = UnityEngine.Object.Instantiate(template.gameObject, root.transform);
  copy.name = size;
  foreach (var h in copy.GetComponentsInChildren<Protobot.HoleCollider>(true)) UnityEngine.Object.DestroyImmediate(h.gameObject);
  copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity; copy.transform.localScale = Vector3.one;
  copy.GetComponent<MeshFilter>().sharedMesh = mesh;
  if (copy.TryGetComponent(out MeshCollider mc)) mc.sharedMesh = mesh;
  copy.GetComponent<MeshRenderer>().sharedMaterials = body.GetComponent<MeshRenderer>().sharedMaterials;
  var data = copy.GetComponent<Protobot.PartData>(); data.param1Value = size; data.param2Value = "Normal";
  var pn = copy.GetComponent<Parts_List.PartName>(); pn.name = size + " Cylinder (legacy)"; pn.weightInGrams = size.Contains("Single") ? 50f : 45f;
  sb.Append(size + " mesh " + mesh.bounds.size.ToString("F2") + " children " + copy.transform.childCount + "\n");
}
UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
return sb.ToString();
