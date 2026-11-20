// Opens the controller screen with a virtual gamepad plugged in and saves a screenshot (Assets/Screenshots/pad_screen.png).
var close = UnityEngine.GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
foreach (var d in UnityEngine.InputSystem.InputSystem.devices.ToArray()) if (d.name.StartsWith("TestPad")) UnityEngine.InputSystem.InputSystem.RemoveDevice(d);
if (UnityEngine.PlayerPrefs.GetString("t_pad_with") == "1") UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("TestPad");
Protobot.Devices.ControllerScreen.Open();
return "open";
