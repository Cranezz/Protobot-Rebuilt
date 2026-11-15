var all = UnityEngine.Object.FindObjectsByType<Protobot.Motion.Sandbox>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
var sb = new System.Text.StringBuilder();
foreach (var s in all) sb.Append(s.gameObject.name).Append(" id ").Append(s.GetInstanceID()).Append(" active ").Append(s.isActiveAndEnabled).Append(" isInstance ").Append(s == Protobot.Motion.Sandbox.Instance).Append(" parent ").Append(s.transform.parent != null ? s.transform.parent.name : "-").Append(" bodies ").Append(s.Bodies().Count()).Append("\n");
return sb.ToString();
