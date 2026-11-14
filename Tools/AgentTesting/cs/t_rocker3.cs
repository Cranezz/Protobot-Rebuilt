var root = Protobot.Motion.Sandbox.Instance.Root;
UnityEngine.Rigidbody arm = null;
foreach (var rb in root.GetComponentsInChildren<UnityEngine.Rigidbody>()) if (rb.name == "Bogie Rocker") arm = rb;
if (arm == null) return "no rocker";
var frame = arm.GetComponent<UnityEngine.ConfigurableJoint>().connectedBody;
var rel = UnityEngine.Quaternion.Inverse(frame.rotation) * arm.rotation;
var key = "rockerStart";
var start = System.AppDomain.CurrentDomain.GetData(key);
if (start == null) { System.AppDomain.CurrentDomain.SetData(key, new float[] { rel.x, rel.y, rel.z, rel.w }); return "start saved"; }
var s = (float[])start; var q0 = new UnityEngine.Quaternion(s[0], s[1], s[2], s[3]);
var turn = UnityEngine.Quaternion.Angle(q0, rel);
var sb = new System.Text.StringBuilder("rocker turned " + turn.ToString("F1") + " deg on its pin; wheels y: ");
foreach (var rb in root.GetComponentsInChildren<UnityEngine.Rigidbody>()) if (rb.name == "Bogie Wheel") sb.Append(rb.worldCenterOfMass.y.ToString("F2") + " ");
sb.Append("| bracket y " + frame.worldCenterOfMass.y.ToString("F2"));
return sb.ToString();
