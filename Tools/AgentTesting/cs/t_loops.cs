// How many V5Brain sampling loops exist (each samples every device every physics step).
var t = typeof(Protobot.Devices.V5Brain).GetNestedType("Loop", System.Reflection.BindingFlags.NonPublic);
var all = Resources.FindObjectsOfTypeAll(t).Cast<Component>().Where(c => c.gameObject.scene.IsValid()).ToList();
return all.Count + " loops; named V5 Brain: " + string.Join(", ", Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.name == "V5 Brain" && g.scene.IsValid()).Select(g => string.Join("/", g.GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().Name))));
