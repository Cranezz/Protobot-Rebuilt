// (Play) station 2 idler: what its shaft touches
var R = Protobot.EditorTools.MotionTestRig.TensionerParts;
var idler = R["2 idler"];
var sb = new System.Text.StringBuilder();
foreach (var n in Protobot.GroupManager.Neighbours(idler)) { sb.AppendLine("idler nb " + n.name + " " + n.transform.position.ToString("F2")); foreach (var m in Protobot.GroupManager.Neighbours(n)) sb.AppendLine("   nb " + m.name + " " + m.transform.position.ToString("F2")); }
var mech = Protobot.Motion.Linkage.Analyze(Protobot.ConnectedObjExtensions.GetConnectedObjects(idler, true));
sb.AppendLine("bodies " + mech.bodies.Count + " pins " + mech.pins.Count + " idler body grounded " + mech.bodyOf[idler].grounded + " objs " + mech.bodyOf[idler].objects.Count);
return sb.ToString();
