// Lists the AI Vision Sensor part in the VEX Sensors family: size, materials, holes.
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProtobotRebuilt/Resources/Part Prefabs/VEX/Sensors.prefab");
var sb = new System.Text.StringBuilder();
foreach (Transform c in prefab.transform) sb.Append(c.name + " | ");
sb.AppendLine();
var p = prefab.transform.Cast<Transform>().First(c => c.name.Contains("AI Vision"));
var mf = p.GetComponent<MeshFilter>();
sb.AppendLine(p.name + " mesh " + mf.sharedMesh.bounds.size.ToString("F3") + " center " + mf.sharedMesh.bounds.center.ToString("F3") + " tris " + mf.sharedMesh.triangles.Length / 3);
sb.AppendLine("materials " + string.Join(", ", p.GetComponent<MeshRenderer>().sharedMaterials.Select(m => m.name)));
foreach (var h in p.GetComponentsInChildren<Protobot.HoleCollider>(true))
    sb.AppendLine("hole " + h.holeType + " at " + h.transform.localPosition.ToString("F3") + " fwd " + h.transform.localRotation * Vector3.forward + " scale " + h.transform.localScale.ToString("F3"));
var pn = p.GetComponent<Parts_List.PartName>();
if (pn != null) sb.AppendLine("weight g " + pn.weightInGrams);
return sb.ToString();
