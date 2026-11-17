// Offline match with autonomous: the same program, the same robot, the red Left start.
Protobot.Online.OnlineWindow.Close();
var games = Protobot.Game.GameDefinition.All();
Protobot.Game.SkillsGame.Open(games[0], true, Protobot.Game.SkillsGame.Mode.Match);
var g = Protobot.Game.SkillsGame.Current;
g.SetSkipAutonomous(false);
g.Apply();
var ch = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.InstanceID).First(s => s.id.StartsWith("CC")).gameObject;
System.AppDomain.CurrentDomain.SetData("spinPart", ch);
g.StartRun();
return "state " + g.state + " run " + g.RunSeconds;
