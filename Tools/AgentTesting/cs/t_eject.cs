Protobot.Motion.Sandbox.TestSticks = () => (new Vector2(0f, -0.3f), Vector2.zero);
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None))
    if ((Protobot.Motion.Controls.GivenName(m.gameObject) ?? "") == "Intake") m.Manual = 1f;
return "reversing and backing up";
