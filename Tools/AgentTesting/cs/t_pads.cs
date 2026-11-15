var sb = new System.Text.StringBuilder();
foreach (var d in UnityEngine.InputSystem.InputSystem.devices) sb.Append(d.GetType().Name + " '" + d.displayName + "' (" + d.description.interfaceName + "); ");
sb.Append(" | Gamepad.current: " + (UnityEngine.InputSystem.Gamepad.current != null ? UnityEngine.InputSystem.Gamepad.current.displayName : "none"));
sb.Append(" | Joystick.current: " + (UnityEngine.InputSystem.Joystick.current != null ? UnityEngine.InputSystem.Joystick.current.displayName : "none"));
return sb.ToString();
