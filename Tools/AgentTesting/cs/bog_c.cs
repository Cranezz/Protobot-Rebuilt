Protobot.Motion.Sandbox.Begin();
var n = 0; foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) n++;
return "running, bodies " + n;
