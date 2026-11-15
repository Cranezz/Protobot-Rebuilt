// Drive 4.5" away from the wall (the run is on), then put the arm down backward.
var g = Protobot.Game.SkillsGame.Current;
Protobot.Motion.Sandbox.TestSticks = () => (new Vector2(0f, 0.25f), Vector2.zero);
return "driving";
