var sb = new System.Text.StringBuilder();
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None))
    sb.Append(Protobot.Motion.Controls.GivenName(m.gameObject) + " " + m.Rpm + "rpm f" + Protobot.Motion.Controls.ForwardKey(m.gameObject) + " hold " + Protobot.Motion.Controls.Hold(m.gameObject) + "; ");
Protobot.PivotCamera.Main.SetTransform(new Vector3(0f, 5f, 2f), new Vector3(28f, 215f, 0f), -36f);
return sb.ToString();
