var g = Protobot.Game.SkillsGame.Current;
var bodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
return "frame " + Time.frameCount + " bodies " + bodies.Length + " fps " + (1f / Time.smoothDeltaTime).ToString("F0") + " sandbox " + Protobot.Motion.Sandbox.Running;
