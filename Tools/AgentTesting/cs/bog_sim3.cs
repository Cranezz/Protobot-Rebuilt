Protobot.Motion.Sandbox.Stop();
Protobot.PartsManager.DestroyLoadedObjects();
var w = Protobot.PartsManager.GeneratePart("VXWHEELS-2.75\" Traction Wheel", new Vector3(0f, 2f, 0f), Quaternion.identity);
var axis = w.GetComponentsInChildren<Protobot.HoleCollider>()[0].transform.forward;
w.transform.rotation = Quaternion.FromToRotation(axis, Vector3.forward);
var b = Protobot.PartsManager.GeneratePart("VXWHEELS-Bogie Wheel Single", new Vector3(10f, 2f, 0f), Quaternion.Euler(90f, 0f, 0f));
Protobot.Motion.Sandbox.Begin();
return "ok";
