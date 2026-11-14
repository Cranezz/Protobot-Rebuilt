var pl = Protobot.Placement.Instance;
var f = typeof(Protobot.Placement).GetField("currentPlacementData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var data = f.GetValue(pl) as Protobot.GameObjectPlacementData;
if (!pl.placing || data == null) return "not placing";
var go = data.GetGameObject(); var face = go.GetComponent<Protobot.HoleFace>();
if (face == null) return "placing " + go.name + " (no hole face)";
return "grabbed " + face.hole.part.name + " face dir " + face.direction.ToString("F2") + " hole axis " + face.hole.forward.ToString("F2") + " camera fwd " + Camera.main.transform.forward.ToString("F2");
