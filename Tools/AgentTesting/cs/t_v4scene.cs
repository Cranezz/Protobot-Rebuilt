// (Play) Tutorial 4's start: a 15-hole C-channel standing on its edge with a 10-hole C-channel bolted across its end
// (two screws and keps nuts, as C makes them), to measure and mirror.
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
Protobot.Builds.DemoPlayer.Target.Clear();
var a = Protobot.PartsManager.GeneratePart("CCHL-1x2-15", new Vector3(0f, 0.5f, 0f), Quaternion.LookRotation(Vector3.right, Vector3.up));
var down = Quaternion.LookRotation(Vector3.down, Vector3.Cross(Vector3.down, Vector3.right));
var b = Protobot.PartsManager.GeneratePart("CCHL-1x2-10", new Vector3(2.0f, 1.055f, 3.25f), down);
Physics.SyncTransforms();
var ok1 = Protobot.EditorTools.MotionTestRig.Bolt(new Vector3(-0.25f, 1.2f, 3.0f), Vector3.down);
var ok2 = Protobot.EditorTools.MotionTestRig.Bolt(new Vector3(-0.25f, 1.2f, 3.5f), Vector3.down);
return "bolts " + ok1 + " " + ok2 + " | a " + a.GetComponent<Renderer>().bounds + " b " + b.GetComponent<Renderer>().bounds;
