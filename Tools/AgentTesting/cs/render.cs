// Renders each listed part (ids in render_ids.txt: id|yaw|pitch) to Assets/Screenshots/render_<n>.png, 400 px, ortho.
var lines = System.IO.File.ReadAllLines(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/render_ids.txt");
var camGo = new GameObject("RenderCam"); var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.85f, 0.85f); cam.cullingMask = 1 << 15; cam.enabled = false;
var light = new GameObject("RenderLight").AddComponent<Light>(); light.type = LightType.Directional; light.cullingMask = 1 << 15; light.intensity = 1.2f;
var rt = new RenderTexture(400, 400, 24); cam.targetTexture = rt; var tex = new Texture2D(400, 400, TextureFormat.RGB24, false);
int n = 0; var sb = new System.Text.StringBuilder();
foreach (var line in lines) {
  if (line.Trim().Length == 0) continue; var f = line.Split('|');
  var part = Protobot.PartsManager.GeneratePart(f[0], new Vector3(1000f, 1000f, 1000f), Quaternion.identity);
  foreach (var t in part.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 15;
  foreach (var h in part.GetComponentsInChildren<Protobot.HoleCollider>(true)) h.gameObject.layer = 11;
  var bounds = new Bounds(); bool first = true; foreach (var r in part.GetComponentsInChildren<MeshRenderer>()) { if (r.gameObject.layer != 15) continue; if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
  float zoom = f.Length > 3 ? float.Parse(f[3]) : 1f; var focus = bounds.center; if (f.Length > 4) { var c = f[4].Split(','); focus = part.transform.TransformPoint(new Vector3(float.Parse(c[0]), float.Parse(c[1]), float.Parse(c[2]))); }
  var rot = Quaternion.Euler(float.Parse(f[2]), float.Parse(f[1]), 0f);
  camGo.transform.rotation = rot; camGo.transform.position = focus - rot * Vector3.forward * 20f; light.transform.rotation = rot * Quaternion.Euler(20f, 20f, 0f);
  cam.orthographicSize = bounds.extents.magnitude * 1.05f / zoom; cam.nearClipPlane = 0.01f; cam.farClipPlane = 100f;
  cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 400, 400), 0, 0); tex.Apply(); RenderTexture.active = null;
  System.IO.File.WriteAllBytes(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/Screenshots/render_" + n + ".png", tex.EncodeToPNG());
  sb.Append(n + ": " + f[0] + " size " + bounds.size.ToString("F2") + "\n"); n++;
  UnityEngine.Object.DestroyImmediate(part);
}
UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(light.gameObject); rt.Release();
return sb.ToString();
