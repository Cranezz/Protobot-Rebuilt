var p = GameObject.Find("Test Pusher"); var t = Protobot.Game.SkillsGame.Current.Toggles[0];
if (p == null) return "none";
var rb = p.GetComponent<Rigidbody>(); var mid = t.transform.TransformPoint(t.pivotLocal);
var hits = ""; foreach (var c in Physics.OverlapBox(p.transform.position, new Vector3(2.1f, 0.6f, 0.6f), p.transform.rotation)) if (c.gameObject != p) hits += " " + c.name + "/L" + c.gameObject.layer;
return "from bar " + (p.transform.position - mid) + " v " + rb.linearVelocity + " sleeping " + rb.IsSleeping() + " touching:" + hits + " turned " + t.Turned.ToString("F0") + " axis " + t.transform.TransformDirection(t.axisLocal);
