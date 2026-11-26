// (Play) renders the Override field from straight above (its match layout) into
// Assets/ProtobotRebuilt/Resources/Games/Override Map.png: 2048 px covering 152" (field frame: +X right, +Y up, middle
// in the middle). The path planner draws on it. Refresh the asset database in Edit mode afterwards.
var def = Resources.Load<Protobot.Game.GameDefinition>("Games/Override 2026-27");
if (def == null) return "no game definition";
var field = Protobot.Game.GameField.Create(def, false, false);
field.transform.position = new Vector3(0f, -20000f, 0f);
var go = new GameObject("Map Camera");
var cam = go.AddComponent<Camera>();
cam.orthographic = true; cam.orthographicSize = 76f; cam.aspect = 1f;
cam.transform.position = field.transform.position + Vector3.up * 400f;
cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
cam.nearClipPlane = 1f; cam.farClipPlane = 600f;
cam.cullingMask = 1 << 0;
cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
var rt = new RenderTexture(2048, 2048, 24) { antiAliasing = 4 };
cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
RenderTexture.active = rt;
var tex = new Texture2D(2048, 2048, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 2048, 2048), 0, 0); tex.Apply();
RenderTexture.active = null;
System.IO.File.WriteAllBytes("Assets/ProtobotRebuilt/Resources/Games/Override Map.png", tex.EncodeToPNG());
UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(field.gameObject);
return "saved";
