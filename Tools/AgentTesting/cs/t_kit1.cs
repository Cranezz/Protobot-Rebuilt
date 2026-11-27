// Play mode: test rigs for the advanced gear kit at y = 20 (bevel pair, worm + wheel, rack + pinion, lead screw +
// nut, cam + follower), then what GearTrain finds for each and a turn by MotorRunner. Result -> Temp/kit1.txt too.
var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) if (s.transform.position.y > 15f && s.transform.position.y < 30f) UnityEngine.Object.Destroy(s.gameObject);
System.Func<string, UnityEngine.Vector3, UnityEngine.Vector3, UnityEngine.GameObject> OnAxis = (id, point, axis) => {
  var p = Protobot.PartsManager.GeneratePart(id, point, UnityEngine.Quaternion.identity);
  var sh = Protobot.Motion.GearKit.Measure(p);
  var rot = UnityEngine.Quaternion.FromToRotation(sh.axis, axis);
  p.transform.SetPositionAndRotation(point - rot * sh.center, rot);
  return p;
};
System.Func<UnityEngine.Vector3, UnityEngine.Vector3, int, UnityEngine.GameObject> Shaft = (c, axis, len) => Protobot.PartsManager.GeneratePart("SHFT-Normal-" + len, c, UnityEngine.Quaternion.LookRotation(axis));
var X = UnityEngine.Vector3.right; var Y = UnityEngine.Vector3.up; var Z = UnityEngine.Vector3.forward;
// A: bevels
var O = new UnityEngine.Vector3(0f, 20f, 0f);
var s1 = Shaft(O + X * 1.2f, X, 2); OnAxis("VXADVANCEDGEARS-12T Bevel Gear", O + X * 0.688f, X);
var s2 = Shaft(O + Y * 1.0f, Y, 2); OnAxis("VXADVANCEDGEARS-36T Bevel Gear", O + Y * 0.378f, Y);
// B: worm + wheel
var W = new UnityEngine.Vector3(5f, 20f, 0f);
var s3 = Shaft(W, X, 3); OnAxis("VXADVANCEDGEARS-Worm Gear", W, X);
var s4 = Shaft(W + Y * 0.74f, Z, 2); OnAxis("VXADVANCEDGEARS-36T Worm Wheel", W + Y * 0.74f, Z);
// C: rack + 12T pinion above it
var R = new UnityEngine.Vector3(10f, 20f, 0f);
var rack = Protobot.PartsManager.GeneratePart("MCMS-Rack-", R, UnityEngine.Quaternion.identity);
var rb = rack.GetComponent<UnityEngine.MeshFilter>().sharedMesh.bounds;
var rackTop = rack.transform.TransformPoint(rb.center + UnityEngine.Vector3.up * rb.extents.y);
var gearAt = new UnityEngine.Vector3(R.x, rackTop.y + 12f / 48f - 1f / 24f, rack.transform.TransformPoint(rb.center).z);
var s5 = Shaft(gearAt, Z, 2); OnAxis("GEAR-Normal-12T", gearAt, Z);
// D: lead screw + nut
var L = new UnityEngine.Vector3(15f, 20f, 0f);
var s6 = Shaft(L, X, 3); OnAxis("VXADVANCEDGEARS-Lead Screw Segment", L, X);
var nut = Protobot.PartsManager.GeneratePart("MCMS-Screw Kit-Worm Nut", L, UnityEngine.Quaternion.identity);
var ns = Protobot.Motion.GearKit.Measure(nut); var nrot = UnityEngine.Quaternion.FromToRotation(ns.longAxis, X);
nut.transform.SetPositionAndRotation(L - nrot * nut.GetComponent<UnityEngine.MeshFilter>().sharedMesh.bounds.center, nrot);
// E: cam + follower above it
var C = new UnityEngine.Vector3(22f, 20f, 0f);
var s7 = Shaft(C, Z, 2); var cam = OnAxis("MCMS-Drop Off Cam-", C, Z);
var fol = Protobot.PartsManager.GeneratePart("MCMS-Cam Follower-", C, UnityEngine.Quaternion.identity);
var fs = Protobot.Motion.GearKit.Measure(fol); var frot = UnityEngine.Quaternion.FromToRotation(fs.longAxis, Y);
var fb = fol.GetComponent<UnityEngine.MeshFilter>().sharedMesh.bounds;
fol.transform.SetPositionAndRotation(C + Y * (2.1f + fb.extents.y) - frot * fb.center, frot);
UnityEngine.PlayerPrefs.SetString("t_kit_shafts", "ok");
return "built";
