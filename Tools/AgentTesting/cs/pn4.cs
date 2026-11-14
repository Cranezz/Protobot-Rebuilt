var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.Screw>(FindObjectsSortMode.None)) sb.Append(s.name + " connects " + s.CanFormConnection + " neighbours " + string.Join(",", System.Linq.Enumerable.Select(Protobot.GroupManager.Neighbours(s.gameObject), g => g.name)) + "\n");
// render the cylinder's rear and front
var camGo = new GameObject("RC"); var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.85f, 0.85f); cam.enabled = false;
var rt = new RenderTexture(900, 500, 24); cam.targetTexture = rt; var tex = new Texture2D(900, 500, TextureFormat.RGB24, false);
camGo.transform.rotation = Quaternion.Euler(25f, 160f, 0f); camGo.transform.position = new Vector3(2f, 0f, 0f) - camGo.transform.forward * 30f; cam.orthographicSize = 1.6f; cam.farClipPlane = 100f;
cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 900, 500), 0, 0); tex.Apply(); RenderTexture.active = null;
System.IO.File.WriteAllBytes(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/Screenshots/pneumatic.png", tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(camGo); rt.Release();
return sb.ToString();
