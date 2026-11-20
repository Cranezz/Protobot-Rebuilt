// The tank-drive blocks example in driver control with both sticks 80% forward: the robot should drive forward.
var config = Protobot.RobotCode.DeviceConfig.Generate().TrimEnd('\n', '\r');
var src = Protobot.RobotCode.Blocks.BlockExamples.Make(1, config).ToSource(out _);
Protobot.RobotCode.CodeStore.Set(src);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_blk_start", rb.position.x + "," + rb.position.z + "," + rb.rotation.eulerAngles.y);
Protobot.Motion.Sandbox.TestSticks = () => (new Vector2(0f, 0.8f), new Vector2(0f, 0.8f));
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(src, Protobot.RobotCode.RobotProgram.Period.Driver, 0, out err, out line);
return "started " + ok + " " + err;
