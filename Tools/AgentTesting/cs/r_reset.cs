UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current, new UnityEngine.InputSystem.LowLevel.KeyboardState());
Protobot.Motion.Sandbox.TestSticks = null;
var g = Protobot.Game.SkillsGame.Current; g.Exit();
var def = Resources.LoadAll<Protobot.Game.GameDefinition>("Games")[0];
Protobot.Game.SkillsGame.Open(def, true);
return "reopened";
