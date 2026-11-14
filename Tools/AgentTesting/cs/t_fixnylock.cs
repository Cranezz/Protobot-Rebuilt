// The "Low Profile Nylock" child (kept for old builds) takes the real low profile nylock's model: the original's
// "Low Profile" nut (VEX's 1/4" hex low profile nylock).
var prefabPath = "Assets/ProtobotRebuilt/Resources/Part Prefabs/5Structure/Nut.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
var copy = root.transform.Find("Low Profile Nylock");
var lp = root.transform.Find("Low Profile");
var mesh = lp.GetComponent<MeshFilter>().sharedMesh;
copy.GetComponent<MeshFilter>().sharedMesh = mesh;
copy.GetComponent<MeshCollider>().sharedMesh = mesh;
copy.GetComponent<MeshRenderer>().sharedMaterials = lp.GetComponent<MeshRenderer>().sharedMaterials;
copy.localPosition = lp.localPosition; copy.localRotation = lp.localRotation; copy.localScale = lp.localScale;
var h0 = lp.GetComponentInChildren<Protobot.HoleCollider>(true).transform;
var h1 = copy.GetComponentInChildren<Protobot.HoleCollider>(true).transform;
h1.localPosition = h0.localPosition; h1.localRotation = h0.localRotation; h1.localScale = h0.localScale;
copy.GetComponent<Parts_List.PartName>().name = "Low Profile Nylock Nut";
copy.GetComponent<Parts_List.PartName>().weightInGrams = lp.GetComponent<Parts_List.PartName>().weightInGrams;
lp.GetComponent<Parts_List.PartName>().name = "Low Profile Nylock Nut";
UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
return "ok, hole " + h0.localScale.ToString("F3") + " grams " + lp.GetComponent<Parts_List.PartName>().weightInGrams;
