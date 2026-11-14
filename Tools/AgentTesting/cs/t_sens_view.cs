Protobot.PartsManager.DestroyLoadedObjects();
var names = new[] { "Distance Sensor", "Optical Sensor", "Inertial Sensor", "Rotation Sensor", "GPS Sensor", "Vision Sensor", "Potentiometer", "Potentiometer V2", "Line Tracker V2", "Ultrasonic Range Finder", "Optical Shaft Encoder", "Line Sensor Mounting Bar", "Light Sensor", "Analog Accelerometer" };
var mat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit")); mat.color = UnityEngine.Color.red;
var root = new UnityEngine.GameObject("Hole View");
for (int i = 0; i < names.Length; i++) {
  var go = Protobot.PartsManager.GeneratePart("VXSENSORS-" + names[i], new UnityEngine.Vector3((i % 7) * 4f, 3f, (i / 7) * 4f), UnityEngine.Quaternion.identity);
  foreach (var h in go.GetComponentsInChildren<Protobot.HoleCollider>(true)) {
    var c = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cylinder);
    UnityEngine.Object.Destroy(c.GetComponent<UnityEngine.Collider>());
    c.transform.SetParent(root.transform);
    c.transform.position = h.transform.position;
    c.transform.rotation = h.transform.rotation * UnityEngine.Quaternion.Euler(90f, 0f, 0f);
    var s = h.transform.lossyScale; c.transform.localScale = new UnityEngine.Vector3(s.x, s.z / 2f + 0.15f, s.y);
    c.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = mat;
  }
}
Protobot.PivotCamera.Main.SetTransform(new UnityEngine.Vector3(12f, 3f, 2f), new UnityEngine.Vector3(55f, 0f, 0f), -26f);
return "placed";
