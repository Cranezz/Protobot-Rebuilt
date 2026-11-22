// (Play) Orbit round the field's middle while recording. Temp/fieldshot.txt: NAME SECONDS RADIUS HEIGHT START SPEED HIDE
var c = System.IO.File.ReadAllText("Temp/fieldshot.txt").Trim().Split(' ');
System.Func<int, float> F = i => float.Parse(c[i], System.Globalization.CultureInfo.InvariantCulture);
var field = UnityEngine.Object.FindAnyObjectByType<Protobot.Game.GameField>();
if (field == null) return "no field";
var center = field.ToWorld(UnityEngine.Vector2.zero, 0f);
Protobot.Builds.FrameRecorder.Record("C:/Dev/ProtobotRebuiltV2/Video/shots/" + c[0], F(1));
if (c[6] == "1") Protobot.Builds.FrameRecorder.HideInterface(true);
Protobot.Builds.FrameRecorder.Orbit(center, null, F(2), F(3), F(4), F(5), 0f);
return "recording " + c[0] + " round " + center;
