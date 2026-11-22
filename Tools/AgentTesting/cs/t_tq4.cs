// Pushes the first Toggle up hard for a moment, then follows its lift for 40 physics steps (Physics.Simulate).
var g = Protobot.Game.SkillsGame.Current; var t = g.Toggles[0];
var mode = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
var sb = new System.Text.StringBuilder();
try {
  for (var i = 0; i < 40; i++) {
    if (i < 3) t.body.AddForce(Vector3.up * 3000f, ForceMode.Force);
    Physics.Simulate(Time.fixedDeltaTime);
    if (i % 3 == 0) sb.Append((t.body.position.y - t.restHeight).ToString("F2") + (t.Seated ? "" : "*") + " ");
  }
} finally { Physics.simulationMode = mode; }
return sb.ToString() + " color " + t.SetColor;
