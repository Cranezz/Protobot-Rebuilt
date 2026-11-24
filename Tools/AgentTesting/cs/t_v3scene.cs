// (Play) Tutorial 3's start: a new build with a 1x2 C-channel (25 holes), a 5 x 10 plate and a 6" high strength shaft.
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
UnityEngine.PlayerPrefs.SetFloat("V2.DrillBit", 0.25f);
Protobot.PartsManager.DestroyLoadedObjects();
Protobot.Builds.DemoPlayer.Target.Clear();
var a = Protobot.PartsManager.GeneratePart("CCHL-1x2-25", new Vector3(0f, 0.5f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
var b = Protobot.PartsManager.GeneratePart("PLTE-5-10", new Vector3(0f, 0.023f, -4f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
var c = Protobot.PartsManager.GeneratePart("SHFT-High Strength-6", new Vector3(0f, 0.125f, 4f), Quaternion.LookRotation(Vector3.right));
return "made " + a.name + " " + b.name + " " + c.name + " | " + a.GetComponent<Renderer>().bounds + " | " + c.GetComponent<Renderer>().bounds;
