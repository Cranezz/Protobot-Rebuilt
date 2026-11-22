// Where the turning center is: the drive motors' middle and the drive wheels' middle, in world inches (the build's
// origin is (0, y, 0) before the sandbox moves anything)
Vector3 left = Vector3.zero, right = Vector3.zero; int l = 0, r = 0;
foreach (var m in Protobot.Motion.MotorDrive.Live()) { var s = Protobot.Motion.Controls.Side(m.gameObject); if (s == "L") { left += m.transform.position; l++; } else if (s == "R") { right += m.transform.position; r++; } }
var motors = l > 0 && r > 0 ? (left / l + right / r) / 2f : Vector3.zero;
var wheels = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(o => o.id.StartsWith("OMNI") && !o.id.Contains("2.75")).Select(o => o.transform.position).ToList();
var wc = Vector3.zero; foreach (var w in wheels) wc += w; wc /= Mathf.Max(1, wheels.Count);
return "motors L" + l + " R" + r + " middle " + motors.ToString("F2") + "  drive wheels' middle (" + wheels.Count + ") " + wc.ToString("F2");
