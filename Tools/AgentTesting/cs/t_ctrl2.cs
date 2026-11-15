// Bindings round trip: set R1 to O, the build data carries it, a reload restores it, reset clears it.
var C = typeof(Protobot.Devices.V5Controller);
var sb = new System.Text.StringBuilder();
Protobot.Devices.V5Controller.Set("R1", UnityEngine.InputSystem.Key.O, null);
sb.AppendLine("R1 key " + Protobot.Devices.V5Controller.KeyLabel("R1") + ", own " + Protobot.Devices.V5Controller.HasOwn);
var data = Protobot.Builds.SceneBuild.ToBuildData();
sb.AppendLine("saved token " + (data.controller ?? "").Length + " chars");
Protobot.Devices.V5Controller.Load("");
sb.AppendLine("after clearing: R1 " + Protobot.Devices.V5Controller.KeyLabel("R1"));
Protobot.Devices.V5Controller.Load(data.controller);
sb.AppendLine("after loading the build's: R1 " + Protobot.Devices.V5Controller.KeyLabel("R1") + ", L1 " + Protobot.Devices.V5Controller.KeyLabel("L1") + ", Axis3+ " + Protobot.Devices.V5Controller.KeyLabel("Axis3+"));
// Setting I on L1 takes it off the other control that had it.
Protobot.Devices.V5Controller.Set("L1", UnityEngine.InputSystem.Key.O, null);
sb.AppendLine("O moved to L1: R1 " + Protobot.Devices.V5Controller.KeyLabel("R1") + ", L1 " + Protobot.Devices.V5Controller.KeyLabel("L1"));
Protobot.Devices.V5Controller.ResetToDefault();
sb.AppendLine("reset: R1 " + Protobot.Devices.V5Controller.KeyLabel("R1") + ", L1 " + Protobot.Devices.V5Controller.KeyLabel("L1") + ", own " + Protobot.Devices.V5Controller.HasOwn);
sb.AppendLine("drive layout: " + string.Join(" | ", System.Array.ConvertAll(Protobot.Motion.Controls.DriveLayout(Protobot.Motion.Controls.Drive), x => x.keys + " " + x.action)));
return sb.ToString();
