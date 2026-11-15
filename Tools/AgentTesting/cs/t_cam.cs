var go = Protobot.PartsManager.FindLoadedObjects()[0];
var c = go.GetComponent<Renderer>().bounds.center;
Protobot.PivotCamera.Main.SetTransform(c, new Vector3(15f, 90f, 0f), -7f);
return "at " + c;
