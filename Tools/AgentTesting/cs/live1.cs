Protobot.PartsManager.DestroyLoadedObjects();
var a = Protobot.PartsManager.GeneratePart("CCHL-1x2-20", new Vector3(0f, 0.5f, 0f), Quaternion.identity);
var b = Protobot.PartsManager.GeneratePart("CCHL-1x2-20", new Vector3(0f, 0.5f, 6f), Quaternion.identity);
var cam = Camera.main.GetComponentInParent<Protobot.PivotCamera>();
UnityEditor.EditorApplication.isPaused = false;
return "ok " + (cam != null);
