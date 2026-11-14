Protobot.PartsManager.DestroyLoadedObjects();
var go = Protobot.PartsManager.GeneratePart("VXWHEELS-Bogie Wheel Double", new UnityEngine.Vector3(0f, 3f, 0f), UnityEngine.Quaternion.Euler(-90f, 0f, 0f) * UnityEngine.Quaternion.Euler(0f, 15f, 0f));
return "made " + go.name;
