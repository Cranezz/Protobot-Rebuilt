// Temp/ehint.txt: "point" (mouse onto the straight fitting's port), "check", "press" (E down), "release".
var arg = System.IO.File.ReadAllText("Temp/ehint.txt").Trim();
GameObject f1 = null; Protobot.HoleCollider port = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None))
  if (s0.id == "VXPNEUMATICS-Straight Pneumatic Fitting" && Protobot.Motion.TubeTool.PortNear(s0.gameObject, s0.transform.position) != null) { f1 = s0.gameObject; port = Protobot.Motion.TubeTool.PortNear(f1, f1.transform.position); }
if (f1 == null) {
  var vx = Protobot.PartsManager.GetPartType("VXPNEUMATICS").GetComponent<Protobot.PartGenerator>();
  vx.param1.value = "Straight Pneumatic Fitting";
  f1 = vx.Generate(new Vector3(-2f, 0.5f, 0f), Quaternion.Euler(-90f, 0f, 0f));
  return "made a fitting";
}
if (arg == "point") {
  var screen = Camera.main.WorldToScreenPoint(port.transform.position);
  UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, new UnityEngine.InputSystem.LowLevel.MouseState { position = new Vector2(screen.x, screen.y) });
}
var kb = UnityEngine.InputSystem.Keyboard.current;
if (arg == "press") UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.E));
if (arg == "release") UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
var hint = GameObject.Find("Key Hint");
var panel = hint != null ? hint.transform.Find("Panel") : null;
var words = "";
if (panel != null) foreach (var t in panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>()) words += t.text + "|";
return arg + ": hint " + (panel != null && panel.gameObject.activeSelf) + " " + words + " tool " + Protobot.Motion.TubeTool.Active + " placing " + Protobot.Placement.IsPlacing + " mouse " + UnityEngine.InputSystem.Mouse.current.position.ReadValue();
