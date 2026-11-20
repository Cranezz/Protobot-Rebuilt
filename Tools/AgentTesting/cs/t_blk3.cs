var v = UnityEngine.Object.FindObjectsByType<Protobot.RobotCode.Blocks.BlockView>(UnityEngine.FindObjectsSortMode.None).First(b => b.block.type == "when_started" && !b.palette);
var l = v.parts[0].label;
return "text '" + l.text + "' size " + l.fontSize + " pref00 " + l.GetPreferredValues(l.text, 0f, 0f) + " prefInf " + l.GetPreferredValues(l.text, float.PositiveInfinity, float.PositiveInfinity) + " preferredWidth " + l.preferredWidth + " w " + v.width + " lossy " + v.transform.lossyScale;
