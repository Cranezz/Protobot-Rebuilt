Protobot.Motion.Sandbox.TestSticks = () => (Vector2.zero, Vector2.zero);
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None))
    if ((Protobot.Motion.Controls.GivenName(m.gameObject) ?? "") == "Arm") m.Manual = -0.5f;
return "arm down backward";
