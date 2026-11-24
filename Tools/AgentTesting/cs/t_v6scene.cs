// (Play) Tutorial 6's start: a plate with a motor and three sprockets on shafts (15T driven, 24T, 40T), for a chain;
// and beside it three motors with 36T gears meshing in a row.
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
Protobot.Builds.DemoPlayer.Target.Clear();
var a = Protobot.EditorTools.MotionTestRig.Sprockets(Vector3.zero, new[] { "SPKT-Normal-15T", "SPKT-Normal-24T", "SPKT-Normal-40T" }, new[] { new Vector2(-3f, -2.5f), new Vector2(3f, -2.5f), new Vector2(0f, 2.5f) });
var g = Protobot.EditorTools.MotionTestRig.Gears(new Vector3(-10f, 0f, 0f), 3);
return "parts " + a.Count + " + " + g.Count;
