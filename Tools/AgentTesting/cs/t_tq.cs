var g = Protobot.Game.SkillsGame.Current; var t = g.Toggles[0];
var j = t.GetComponent<ConfigurableJoint>();
return "state " + g.state + " kin " + t.body.isKinematic + " sleeping " + t.body.IsSleeping() + " v " + t.body.linearVelocity + " joint " + (j != null) + " hinge " + (t.GetComponent<HingeJoint>() != null) + " ymot " + (j != null ? j.yMotion.ToString() : "") + " anchor " + (j != null ? j.connectedAnchor.ToString() : "") + " pos " + t.transform.position + " frame " + Time.frameCount + " sim " + Protobot.Motion.Sandbox.Running;
