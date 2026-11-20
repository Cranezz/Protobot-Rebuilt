var gp = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("TestPad");
UnityEngine.InputSystem.LowLevel.InputState.Change(gp.leftStick, new UnityEngine.Vector2(0f, 1f));
var direct = gp.leftStick.ReadValue();
var up = gp.TryGetChildControl("leftStick/up");
var r = "direct " + direct + " up " + (up == null ? "null" : ((UnityEngine.InputSystem.Controls.AxisControl)up).ReadValue().ToString()) + " pad " + Protobot.Devices.V5Controller.PadOf("Axis3+") + " update " + UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType;
UnityEngine.InputSystem.InputSystem.RemoveDevice(gp);
return r;
