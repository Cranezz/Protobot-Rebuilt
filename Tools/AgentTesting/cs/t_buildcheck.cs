// After a build loads: motors, chains, gear systems, bands, tubes; anything odd.
var sb = new System.Text.StringBuilder();
var motors = UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(FindObjectsSortMode.None);
sb.Append("motors " + motors.Length);
var chains = UnityEngine.Object.FindObjectsByType<Protobot.Motion.ChainLoop>(FindObjectsSortMode.None);
int badChains = 0; foreach (var c in chains) if (c.Links == null || c.Links.Count == 0) badChains++;
sb.Append(" chains " + chains.Length + " (no links " + badChains + ")");
var systems = Protobot.Motion.GearTrain.Current();
sb.Append(" rotors " + systems.Count);
return sb.ToString();
