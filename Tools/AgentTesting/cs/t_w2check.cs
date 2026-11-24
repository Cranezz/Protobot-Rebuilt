// After video 2's paste: the parts near the second wheel's spot (left side, z -1.5) and whether the wheel and gear are fastened.
var sb = new System.Text.StringBuilder();
var near = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).Where(s => s.gameObject.activeInHierarchy && s.transform.position.x < -5f && Mathf.Abs(s.transform.position.z + 1.5f) < 1.3f).ToList();
foreach (var s in near.OrderBy(s => s.id)) sb.AppendLine(s.id + " at " + s.transform.position.ToString("0.000"));
var wheels = near.Where(s => s.id.StartsWith("OMNI")).ToList();
foreach (var w in wheels) sb.AppendLine("wheel group: " + string.Join(", ", Protobot.ConnectedObjExtensions.GetConnectedObjects(w.gameObject, true, true).Select(o => o.GetComponent<Protobot.SavedObject>()?.id ?? o.name)));
return sb.ToString();
