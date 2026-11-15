Protobot.Game.SkillsGame.Current.StartRun();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_code_start", rb.position.x + "," + rb.position.z);
return "run";
