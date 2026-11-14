var type = Protobot.PartsManager.GetPartType("VXSHAFTPARTS");
var gen = type.GetComponent<Protobot.PartGenerator>(); gen.param1.value = "6\" Capped Shaft";
var mesh = gen.GetMesh();
var m = typeof(Protobot.CappedShaftPlaceDisplacement).GetMethod("ShapeOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var shape = m.Invoke(null, new object[] { mesh });
if (shape == null) return "no shape; bounds " + mesh.bounds;
var t = shape.GetType().GetGenericArguments().Length > 0 ? shape.GetType().GetGenericArguments()[0] : shape.GetType();
var val = shape.GetType().GetProperty("Value") != null ? shape.GetType().GetProperty("Value").GetValue(shape) : shape;
return "bounds " + mesh.bounds.center.ToString("F3") + " size " + mesh.bounds.size.ToString("F3") + " capToTip " + ((Vector3)val.GetType().GetField("capToTip").GetValue(val)).ToString("F2") + " underCap " + ((Vector3)val.GetType().GetField("underCap").GetValue(val)).ToString("F3") + " id " + gen.GetId();
