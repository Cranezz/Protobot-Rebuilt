var tube = Protobot.Motion.PneumaticTube.All[0];
var w = System.Diagnostics.Stopwatch.StartNew();
for (int i = 0; i < 20; i++) tube.Refresh(false);
return "rope step ms " + (w.Elapsed.TotalMilliseconds / 20).ToString("F3");
