var g = Protobot.Game.SkillsGame.Current; var t = g.Toggles[0];
var j = t.GetComponent<ConfigurableJoint>();
var s = "pos " + t.transform.position.y.ToString("F3") + " body " + t.body.position.y.ToString("F3") + " rest " + t.restHeight.ToString("F3") + " mass " + t.body.mass + " grav " + t.body.useGravity + " constraints " + t.body.constraints + " lim " + j.linearLimit.limit + " jointbreak " + j.breakForce + " connectedBody " + (j.connectedBody != null);
t.body.linearVelocity = Vector3.up * 30f; 
return s;
