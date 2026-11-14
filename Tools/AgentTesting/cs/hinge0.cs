var sb = new System.Text.StringBuilder();
var root = new Vector3(1000f, 1000f, 1000f);
var parts = new System.Collections.Generic.List<GameObject>();
foreach (var id in new[] { "VXHINGES-3 Hole Hinge (bottom)", "VXHINGES-3 Hole Hinge (top)", "VXHINGES-3 Hole Hinge Pin" }) {
  var p = Protobot.PartsManager.GeneratePart(id, root, Quaternion.identity); parts.Add(p);
  foreach (var t in p.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 15;
  var m = p.GetComponent<MeshFilter>().sharedMesh; sb.Append(id + " bounds " + m.bounds.center.ToString("F3") + " " + m.bounds.size.ToString("F3") + "\n");
  foreach (var h in p.GetComponentsInChildren<Protobot.HoleCollider>(true)) { h.gameObject.layer = 11; sb.Append("  hole " + h.transform.localPosition.ToString("F3") + " fwd " + h.transform.localRotation * Vector3.forward + " " + h.transform.localScale.ToString("F3") + "\n"); }
}
var camGo = new GameObject("RC"); var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.85f, 0.85f); cam.cullingMask = 1 << 15; cam.enabled = false;
var light = new GameObject("RL").AddComponent<Light>(); light.type = LightType.Directional; light.cullingMask = 1 << 15;
var rt = new RenderTexture(900, 600, 24); cam.targetTexture = rt; var tex = new Texture2D(900, 600, TextureFormat.RGB24, false);
camGo.transform.rotation = Quaternion.Euler(35f, 30f, 0f); light.transform.rotation = camGo.transform.rotation;
camGo.transform.position = root + new Vector3(-0.3f, -0.8f, 0f) - camGo.transform.forward * 30f; cam.orthographicSize = 2.2f; cam.farClipPlane = 100f;
cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 900, 600), 0, 0); tex.Apply(); RenderTexture.active = null;
System.IO.File.WriteAllBytes(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/Screenshots/hinge.png", tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(light.gameObject); rt.Release();
foreach (var p in parts) UnityEngine.Object.DestroyImmediate(p);
return sb.ToString();
