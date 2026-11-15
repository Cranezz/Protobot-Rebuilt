var g = Protobot.Game.SkillsGame.Current;
Rigidbody biggest = null; foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && (biggest == null || rb.mass > biggest.mass)) biggest = rb;
var sb = new System.Text.StringBuilder("state " + g.state + " drive " + Protobot.Motion.Sandbox.DriveEnabled + " chassis " + biggest.position.ToString("F2") + " v " + biggest.linearVelocity.ToString("F1") + " pin " + g.Pins[0].body.position.ToString("F2") + "; ");
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) sb.Append((Protobot.Motion.Controls.GivenName(m.gameObject) ?? "?") + " cmd " + m.Command.ToString("F2") + " man " + m.Manual.ToString("F2") + "; ");
return sb.ToString();
