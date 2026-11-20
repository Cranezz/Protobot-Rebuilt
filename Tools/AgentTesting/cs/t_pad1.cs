// Controller without real hardware: a virtual gamepad, then a virtual plain joystick (HID-like layout), read through
// V5Controller's bindings (defaults) and press-to-bind. Play mode. Removes its devices after.
// Runs inside the game's input update (an editor script reads the editor's copy of the input state).
UnityEngine.PlayerPrefs.SetString("t_pad", "pending");
System.Action run = null;
run = () => {
    if (UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType != UnityEngine.InputSystem.LowLevel.InputUpdateType.Dynamic) return;
    UnityEngine.InputSystem.InputSystem.onAfterUpdate -= run;
    try {
        var C = typeof(Protobot.Devices.V5Controller);
        var sb = new System.Text.StringBuilder();
        foreach (var d in UnityEngine.InputSystem.InputSystem.devices.ToArray()) if (d.layout == "TestJoy" || d.name.StartsWith("TestPad")) UnityEngine.InputSystem.InputSystem.RemoveDevice(d);
        try { UnityEngine.InputSystem.InputSystem.RemoveLayout("TestJoy"); } catch { }
        sb.Append("none: " + (Protobot.Devices.V5Controller.DeviceName ?? "null") + " | ");
        var gp = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("TestPad");
        UnityEngine.InputSystem.LowLevel.InputState.Change(gp.leftStick, new UnityEngine.Vector2(0f, 1f));
        UnityEngine.InputSystem.LowLevel.InputState.Change(gp.rightStick, new UnityEngine.Vector2(-1f, 0f));
        sb.Append("gamepad: " + Protobot.Devices.V5Controller.DeviceName + " axis3 " + Protobot.Devices.V5Controller.AxisValue(Protobot.Devices.V5Axis.Axis3) + " axis1 " + Protobot.Devices.V5Controller.AxisValue(Protobot.Devices.V5Axis.Axis1) + " | ");
        UnityEngine.InputSystem.InputSystem.RemoveDevice(gp);
        UnityEngine.InputSystem.InputSystem.RegisterLayout(@"{ ""name"": ""TestJoy"", ""extend"": ""Joystick"", ""format"": ""TJOY"", ""stateSizeInBytes"": 24, ""controls"": [
          { ""name"": ""stick"", ""layout"": ""Stick"", ""format"": ""VEC2"", ""offset"": 0 },
          { ""name"": ""stick/x"", ""format"": ""FLT"", ""offset"": 0 }, { ""name"": ""stick/y"", ""format"": ""FLT"", ""offset"": 4 },
          { ""name"": ""stick/up"", ""offset"": 4 }, { ""name"": ""stick/down"", ""offset"": 4 }, { ""name"": ""stick/left"", ""offset"": 0 }, { ""name"": ""stick/right"", ""offset"": 0 },
          { ""name"": ""trigger"", ""layout"": ""Button"", ""format"": ""FLT"", ""offset"": 8 },
          { ""name"": ""button2"", ""layout"": ""Button"", ""format"": ""FLT"", ""offset"": 12 },
          { ""name"": ""z"", ""layout"": ""Axis"", ""format"": ""FLT"", ""offset"": 16 },
          { ""name"": ""rz"", ""layout"": ""Axis"", ""format"": ""FLT"", ""offset"": 20 } ] }");
        var joy = (UnityEngine.InputSystem.Joystick)UnityEngine.InputSystem.InputSystem.AddDevice("TestJoy");
        var stick = joy.TryGetChildControl<UnityEngine.InputSystem.Controls.StickControl>("stick");
        var rz = joy.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("rz");
        var z = joy.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("z");
        var b2 = joy.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("button2");
        UnityEngine.InputSystem.LowLevel.InputState.Change(stick, new UnityEngine.Vector2(0f, 1f));
        UnityEngine.InputSystem.LowLevel.InputState.Change(rz, -1f);
        sb.Append("joystick: " + Protobot.Devices.V5Controller.DeviceName + " is gamepad " + (Protobot.Devices.V5Controller.Device is UnityEngine.InputSystem.Gamepad) + " axis3 " + Protobot.Devices.V5Controller.AxisValue(Protobot.Devices.V5Axis.Axis3) + " axis2 " + Protobot.Devices.V5Controller.AxisValue(Protobot.Devices.V5Axis.Axis2) + " | ");
        // Press-to-bind: first call takes the axes' rest, then z pushed past 0.6.
        UnityEngine.InputSystem.LowLevel.InputState.Change(stick, UnityEngine.Vector2.zero);
        var first = Protobot.Devices.V5Controller.PressedPadControl();
        UnityEngine.InputSystem.LowLevel.InputState.Change(z, 0.9f);
        var second = Protobot.Devices.V5Controller.PressedPadControl();
        sb.Append("bind: " + (first ?? "null") + " then " + (second ?? "null") + " | ");
        // A button bound to L1.
        var hadOwn = Protobot.Devices.V5Controller.HasOwn;
        var old = Protobot.Devices.V5Controller.PadOf("L1");
        Protobot.Devices.V5Controller.Set("L1", null, "<Joystick>/button2");
        UnityEngine.InputSystem.LowLevel.InputState.Change(b2, 1f);
        sb.Append("b2 " + b2.ReadValue() + " ");
        sb.Append("L1 held " + Protobot.Devices.V5Controller.Held(Protobot.Devices.V5Button.L1) + " label " + Protobot.Devices.V5Controller.PadLabel("L1") + " / " + Protobot.Devices.V5Controller.PadLabel("Axis2+") + " | ");
        if (hadOwn) Protobot.Devices.V5Controller.Set("L1", null, old); else Protobot.Devices.V5Controller.ResetToDefault();
        UnityEngine.InputSystem.InputSystem.RemoveDevice(joy);
        UnityEngine.InputSystem.InputSystem.RemoveLayout("TestJoy");
        sb.Append("after: " + (Protobot.Devices.V5Controller.DeviceName ?? "null"));
        UnityEngine.PlayerPrefs.SetString("t_pad", sb.ToString());
        
    } catch (System.Exception e) { Protobot.Devices.V5Controller.ResetToDefault(); UnityEngine.PlayerPrefs.SetString("t_pad", "error " + e); }
};
UnityEngine.InputSystem.InputSystem.onAfterUpdate += run;
return "queued";
