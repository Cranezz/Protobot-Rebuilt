var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
var f = typeof(Protobot.RobotCode.CodePanel).GetField("field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel) as TMPro.TMP_InputField;
var text = f.text;
var line = 1 + text.Substring(0, System.Math.Min(f.stringPosition, text.Length)).Count(c => c == '\n');
return "caret line " + line + " focused " + f.isFocused + " popup " + Protobot.Interface.Popup.IsOpen + " scroll " + f.textComponent.rectTransform.anchoredPosition.y;
