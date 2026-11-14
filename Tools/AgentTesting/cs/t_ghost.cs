var t = Protobot.Placement.Instance.transform;
var face = UnityEngine.Object.FindFirstObjectByType<Protobot.HoleFace>();
var mc = UnityEngine.Object.FindObjectsByType<Protobot.MouseCast>(FindObjectsSortMode.None);
var s = "ghost pos " + t.position.ToString("F3") + " fwd " + t.forward.ToString("F2") + " placing " + Protobot.Placement.IsPlacing
  + " | face " + (face != null ? face.position.ToString("F3") + " dir " + face.direction.ToString("F2") : "none")
  + " | mouse " + UnityEngine.InputSystem.Mouse.current.position.ReadValue();
foreach (var m in mc) s += " | cast " + m.name + " -> " + (m.gameObject != null ? m.gameObject.name : "null");
return s;
