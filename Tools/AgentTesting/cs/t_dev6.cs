var cyl = Protobot.PartsManager.GeneratePart("PNMT-50mm-Normal", new UnityEngine.Vector3(-14f, 1f, 0f), UnityEngine.Quaternion.identity);
var sol = Protobot.PartsManager.GeneratePart("VXPNEUMATICS-Solenoid   Double Acting", new UnityEngine.Vector3(-14f, 1f, 6f), UnityEngine.Quaternion.identity);
Protobot.Devices.V5Brain.Refresh(true);
var sb = new System.Text.StringBuilder("cyl " + (cyl != null) + " sol " + (sol != null) + "\n");
foreach (var d in Protobot.Devices.V5Brain.Devices) if (d.threeWire) sb.AppendLine(d.PortLabel + " " + d.Name + " " + d.Reading);
return sb.ToString();
