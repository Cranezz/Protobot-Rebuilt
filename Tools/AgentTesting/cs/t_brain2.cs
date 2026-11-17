// Online: the Brain lists only this computer's robot (a second copy of the build tagged as robot 1).
var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).Select(s => s.gameObject).ToList();
foreach (var p in parts) { var t = p.GetComponent<Protobot.Online.NetPart>() ?? p.AddComponent<Protobot.Online.NetPart>(); t.robot = 0; }
Protobot.Devices.V5Brain.Refresh(true);
var alone = Protobot.Devices.V5Brain.Devices.Count;
var copies = new List<GameObject>();
foreach (var p in parts) { var c = UnityEngine.Object.Instantiate(p, p.transform.position + new Vector3(40, 0, 0), p.transform.rotation); c.GetComponent<Protobot.Online.NetPart>().robot = 1; copies.Add(c); }
Protobot.Devices.V5Brain.Refresh(true);
var both = Protobot.Devices.V5Brain.Devices.Count;
Protobot.Motion.Sandbox.RobotOf = part => { var tag = part != null ? part.GetComponentInParent<Protobot.Online.NetPart>() : null; return tag != null ? tag.robot : -1; };
Protobot.Motion.Sandbox.LocalRobot = 0;
Protobot.Devices.V5Brain.Refresh();
var mine0 = Protobot.Devices.V5Brain.Devices.Count;
var ok0 = Protobot.Devices.V5Brain.Devices.All(d => d.part == null || d.part.GetComponentInParent<Protobot.Online.NetPart>().robot == 0);
var ports0 = string.Join(",", Protobot.Devices.V5Brain.Devices.Select(d => d.port));
Protobot.Motion.Sandbox.LocalRobot = 1;
Protobot.Devices.V5Brain.Refresh(true);
var ok1 = Protobot.Devices.V5Brain.Devices.All(d => d.part == null || d.part.GetComponentInParent<Protobot.Online.NetPart>().robot == 1);
var ports1 = string.Join(",", Protobot.Devices.V5Brain.Devices.Select(d => d.port));
Protobot.Motion.Sandbox.RobotOf = null; Protobot.Motion.Sandbox.LocalRobot = 0;
foreach (var c in copies) UnityEngine.Object.Destroy(c);
foreach (var p in parts) UnityEngine.Object.Destroy(p.GetComponent<Protobot.Online.NetPart>());
return "one robot " + alone + " devices; two robots, no filter " + both + "; robot 0 sees " + mine0 + " (only its own " + ok0 + ", ports " + ports0 + "); robot 1 only its own " + ok1 + " (ports " + ports1 + ")";
