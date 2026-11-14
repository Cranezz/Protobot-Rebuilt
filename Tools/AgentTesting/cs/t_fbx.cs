var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Protobot/Parts/Models/Structure/Hardware/Nut Thin Nylock.fbx");
var s = "";
foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) s += mf.name + " verts " + mf.sharedMesh.vertexCount + " bounds " + mf.sharedMesh.bounds.size.ToString("F4") + " c " + mf.sharedMesh.bounds.center.ToString("F4") + " rot " + mf.transform.localRotation.eulerAngles + " scale " + mf.transform.lossyScale + " mats " + string.Join(",", System.Linq.Enumerable.Select(mf.GetComponent<MeshRenderer>().sharedMaterials, m => m.name + "#" + ColorUtility.ToHtmlStringRGB(m.color))) + "\n";
return s;
