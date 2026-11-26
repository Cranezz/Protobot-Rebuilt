// (run going) Temp/tpush.txt: "go SPEED DX DY OX OY": a 1000 kg 1" box (robot layer, no gravity: an arm on a robot)
// starts at Toggle 0's middle + OX across the bar + OY up and moves at SPEED in/s along (DX across, DY up); "clear".
// Across = the bar's axis x up.
var cmd = System.IO.File.ReadAllText("Temp/tpush.txt").Trim();
var g = Protobot.Game.SkillsGame.Current; if (g == null) return "no game";
var old = GameObject.Find("Test Pusher"); if (old != null) UnityEngine.Object.Destroy(old);
if (cmd == "clear") return "cleared";
var t = g.Toggles[0]; var a = cmd.Split(' ');
var speed = float.Parse(a[1]); var dx = float.Parse(a[2]); var dy = float.Parse(a[3]); var ox = float.Parse(a[4]); var oy = float.Parse(a[5]);
var axis = t.transform.TransformDirection(t.axisLocal).normalized;
var middle = t.transform.TransformPoint(t.pivotLocal);
var across = Vector3.Cross(axis, Vector3.up).normalized;
var dir = (across * dx + Vector3.up * dy).normalized;
var go = new GameObject("Test Pusher") { layer = Protobot.Motion.Sandbox.Layer };
go.transform.SetPositionAndRotation(middle + across * ox + Vector3.up * oy, Quaternion.LookRotation(across, Vector3.up));
var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(4f, 1f, 1f);
var rb = go.AddComponent<Rigidbody>(); rb.mass = 1000f; rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeRotation;
rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; rb.linearVelocity = dir * speed;
var faces = ""; foreach (var f in t.faces) faces += " " + f.color + ":" + t.transform.TransformDirection(f.normal).ToString("F2");
return "pusher " + cmd + "; across " + across + " faces" + faces;
