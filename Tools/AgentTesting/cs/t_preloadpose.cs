var g = Protobot.Game.SkillsGame.Current;
return "preload " + g.preload + " custom " + (g.customPreload.HasValue ? g.customPreload.Value.Item1.ToString("F2") + " up " + (g.customPreload.Value.Item2 * Vector3.forward).ToString("F2") : "none");
