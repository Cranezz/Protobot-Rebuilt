var game = Protobot.Game.SkillsGame.Current;
var fieldT = ((UnityEngine.Component)game.GetType().GetField("field").GetValue(game)).transform;
var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None);
var b = new UnityEngine.Bounds();
bool first = true;
foreach (var p in parts) { if (!p.gameObject.activeInHierarchy) continue; foreach (var r in p.GetComponentsInChildren<UnityEngine.Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); } }
var robot = b.center;
var center = fieldT.position;
var dir = center - robot; dir.y = 0;
var yaw = UnityEngine.Quaternion.LookRotation(dir.normalized).eulerAngles.y;
var cam = Protobot.PivotCamera.Main;
cam.transform.position = robot + dir.normalized * 34f + UnityEngine.Vector3.up * 3f;
cam.transform.eulerAngles = new UnityEngine.Vector3(24f, yaw + 20f, 0f);
var lp = cam.camera.transform.localPosition; lp.z = -60f; cam.camera.transform.localPosition = lp;
return "robot " + robot + " center " + center + " yaw " + yaw;
