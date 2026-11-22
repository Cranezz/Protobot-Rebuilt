var g = Protobot.Game.SkillsGame.Current; var t = g.Toggles[0];
t.body.position += Vector3.up * 0.8f; t.transform.position += Vector3.up * 0.8f; t.body.WakeUp();
return "lifted to " + (t.transform.position.y - t.restHeight);
