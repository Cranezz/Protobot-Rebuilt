// (Play, after t_sketchcam1) Queues one wheel notch and reports the camera distance, then a pan drag with the middle button.
var cam = Protobot.PivotCamera.Main;
var mouse = UnityEngine.InputSystem.Mouse.current;
var d0 = cam.focusDistance; var p0 = cam.focusPosition;
UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = new Vector2(1200, 700), scroll = new Vector2(0, 1) });
return "queued; dist before " + d0 + " focus " + p0;
