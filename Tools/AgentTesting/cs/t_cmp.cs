var parts = Protobot.PartsManager.FindLoadedObjects();
var center = UnityEngine.Vector3.zero; foreach (var go in parts) center += go.transform.position; center /= parts.Count;
var sb = new System.Text.StringBuilder("center " + center.ToString("F1") + "\n");
foreach (var go in parts) { var s = go.GetComponent<Protobot.SavedObject>(); if ((go.transform.position - center).magnitude > 12f || s.id.Contains("C-Channel") || s.id.StartsWith("ALUM") || s.id.StartsWith("CCHL")) sb.Append(s.id + " @" + go.transform.position.ToString("F1") + " active " + go.activeInHierarchy + " layer " + go.layer + "\n"); }
return sb.ToString();
