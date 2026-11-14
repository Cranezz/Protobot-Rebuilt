Protobot.PartsManager.DestroyLoadedObjects();
// A vertical 2" standoff, the link threaded into its top, a 2" standoff on the link's far stud.
var a = Protobot.PartsManager.GeneratePart("SNDF-2.00in", new Vector3(0f, 1f, 0f), Quaternion.LookRotation(Vector3.up));
var l = Protobot.PartsManager.GeneratePart("VXHARDWARE-Rubber Link", new Vector3(0f, 2f - 0.365f, 0f), Quaternion.LookRotation(Vector3.down));
var b = Protobot.PartsManager.GeneratePart("SNDF-2.00in", new Vector3(0f, 2f - 0.365f + 1.23f - 0.365f + 1f, 0f), Quaternion.LookRotation(Vector3.up));
return "made";
