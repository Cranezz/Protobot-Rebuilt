var t = Protobot.Game.SkillsGame.Current.Toggles[0]; var p = GameObject.Find("Test Pusher");
var mid = t.transform.TransformPoint(t.pivotLocal);
return "turned " + t.Turned.ToString("F1") + " lift " + t.Lifted.ToString("F2") + " robot " + t.TouchedByRobot + " kin " + t.body.isKinematic + " sleep " + t.body.IsSleeping() + (p != null ? " box " + (p.transform.position - mid).ToString("F2") : "") + " t " + Time.time.ToString("F1");
