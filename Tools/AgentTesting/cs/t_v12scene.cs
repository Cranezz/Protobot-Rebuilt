// (Play) Tutorial 12's start: a new build with a standing 60T HS gear, a 5 x 10 plate with two 2" standoffs (rubber
// band posts) and a red license plate.
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
Protobot.Builds.DemoPlayer.Target.Clear();
var gear = Protobot.PartsManager.GeneratePart("GEAR-High Strength-60T", new Vector3(-4f, 1.35f, 0f), Quaternion.LookRotation(Vector3.right));
var plate = Protobot.PartsManager.GeneratePart("PLTE-5-10", new Vector3(4f, 0.023f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
var s1 = Protobot.PartsManager.GeneratePart("SNDF-2.00in", new Vector3(4f, 1.047f, -2f), Quaternion.LookRotation(Vector3.up));
var s2 = Protobot.PartsManager.GeneratePart("SNDF-2.00in", new Vector3(4f, 1.047f, 2f), Quaternion.LookRotation(Vector3.up));
var lp = Protobot.PartsManager.GeneratePart("VXMISCELLANEOUS-Red License Plate", new Vector3(0f, 0.12f, -5f), Quaternion.identity);
var lb = lp.GetComponent<Renderer>().bounds;
return "gear " + gear.GetComponent<Renderer>().bounds + " | standoff " + s1.GetComponent<Renderer>().bounds + " | plate " + lb;
