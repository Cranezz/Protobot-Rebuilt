// Play mode: a 5 hole hinge at (0, 0.5, 0), flat; its leaf, pin and angle. Then the kit row (colors).
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) UnityEngine.Object.Destroy(s.gameObject);
var h = Protobot.PartsManager.GeneratePart("VXHINGES-5 Hole Hinge (bottom)", new UnityEngine.Vector3(0f, 0.5f, 0f), UnityEngine.Quaternion.identity);
var hp = h.GetComponent<Protobot.Motion.HingePart>();
return "hinge " + (hp != null && hp.enabled) + " angle " + (hp != null ? hp.Angle : -1) + " pin " + (hp != null ? hp.PinPoint.ToString("0.00") + " axis " + hp.PinAxis.ToString("0.0") : "") + " renderers " + h.GetComponentsInChildren<UnityEngine.MeshRenderer>().Length;
