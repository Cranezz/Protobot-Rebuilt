var sign = System.IO.File.Exists("C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Temp/armsign.txt") ? float.Parse(System.IO.File.ReadAllText("C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Temp/armsign.txt")) : 1f;
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None))
    if ((Protobot.Motion.Controls.GivenName(m.gameObject) ?? "") == "Arm") m.Manual = sign;
return "arm " + sign;
