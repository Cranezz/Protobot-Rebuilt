// (Play) Temp/air.txt: "setup TANKS [REG]" = a 50 mm cylinder + TANKS V5 tanks (+ a legacy regulator at REG psi);
// "sim" starts the sandbox; "fire N" switches the cylinder's valve N times; "read"; "stop".
var cmd = System.IO.File.ReadAllText("Temp/air.txt").Trim().Split(' ');
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
if (cmd[0] == "setup") {
  Protobot.PartsManager.DestroyLoadedObjects();
  var c = Protobot.PartsManager.GeneratePart("PNMT-50mm-Normal", new Vector3(0f, 3f, 0f), Quaternion.identity);
  var n = int.Parse(cmd[1]);
  for (var i = 0; i < n; i++) Protobot.PartsManager.GeneratePart("TANK-V5", new Vector3(6f + 4f * i, 3f, 0f), Quaternion.identity);
  if (cmd.Length > 2) { var r = Protobot.PartsManager.GeneratePart("VXPNEUMATICS-Pressure Regulator", new Vector3(-6f, 3f, 0f), Quaternion.identity); if (r != null) Protobot.Motion.Dynamics.SetToken(r, "reg", cmd[2]); }
  return "made " + (c != null) + " tanks " + n + " ids " + string.Join(",", System.Linq.Enumerable.Select(UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None), s => s.id));
}
if (cmd[0] == "sim") { Protobot.Motion.Sandbox.Begin(); return "running " + Protobot.Motion.Sandbox.Running; }
if (cmd[0] == "stop") { Protobot.Motion.Sandbox.Stop(); return "stopped"; }
var cyl = System.Linq.Enumerable.FirstOrDefault(Protobot.Motion.PneumaticCylinder.All, x => x != null);
var sb = Protobot.Motion.Sandbox.Instance;
if (cmd[0] == "fire") {
  var times = int.Parse(cmd[1]);
  for (var i = 0; i < times; i++) sb.SetAir(cyl, !sb.Extended(cyl));
}
var s0 = System.Linq.Enumerable.FirstOrDefault(Protobot.Motion.AirSupply.All);
return s0 == null ? "no supply (unlimited)" : "psi " + s0.psi.ToString("F2") + " moves " + s0.moves + " volume " + s0.volumeMl + " fill " + s0.fillPsi + " reg " + s0.regulatorPsi + " working " + s0.Working.ToString("F1") + " extended " + sb.Extended(cyl);
