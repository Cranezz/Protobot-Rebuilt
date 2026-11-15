// Where the test pin is relative to the robot: forward (from the robot's middle), up (from the floor), across.
var g = Protobot.Game.SkillsGame.Current;
var f = g.field;
var pin = g.Pins[0];
// The robot's middle now: its bodies' mean.
var sum = Vector3.zero; var n = 0;
foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer) { sum += rb.position; n++; }
var mid = sum / Mathf.Max(1, n);
var biggest = (Rigidbody)null; foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && (biggest == null || rb.mass > biggest.mass)) biggest = rb;
var local = biggest.transform.InverseTransformPoint(pin.body.position);
return "pin height " + f.HeightOf(pin.body.position).ToString("F2") + ", in the chassis frame " + local.ToString("F2") + " (chassis at " + f.HeightOf(biggest.position).ToString("F2") + " up)";
