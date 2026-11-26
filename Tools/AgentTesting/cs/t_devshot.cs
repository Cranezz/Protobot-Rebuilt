// (Play) opens the Devices window on a cylinder (unfolded) and saves a screenshot (Temp/devshot.png) at the end of the frame.
var c = System.Linq.Enumerable.FirstOrDefault(Protobot.Motion.PneumaticCylinder.All, x => x != null);
if (c != null) Protobot.Devices.DevicesPanel.Focus(c.gameObject, true); else Protobot.Devices.DevicesPanel.Open();
UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Temp/devshot.png"));
return "ok";
