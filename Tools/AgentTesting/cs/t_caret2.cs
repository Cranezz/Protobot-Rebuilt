var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
var f = typeof(Protobot.RobotCode.CodePanel).GetField("field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(panel) as TMPro.TMP_InputField;
var tc = f.textComponent; var info = tc.textInfo;
var idx = System.Math.Min(f.stringPosition, info.characterCount - 1);
var ln = info.characterInfo[idx].lineNumber;
return "caret str " + f.stringPosition + " ln " + ln + " lines " + info.lineCount + " asc0 " + info.lineInfo[0].ascender + " ascLn " + info.lineInfo[ln].ascender + " lineH " + info.lineInfo[0].lineHeight + " view " + f.textViewport.rect.height + " pos " + tc.rectTransform.anchoredPosition.y + " chars " + info.characterCount + " textlen " + f.text.Length;
