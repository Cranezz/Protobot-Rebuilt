var sb = new System.Text.StringBuilder();
Protobot.PartsManager.DestroyLoadedObjects();
var so = Protobot.PartsManager.GeneratePart("SNDF-3.00in", new Vector3(0f, 1f, 0f), Quaternion.identity);
GameObject sh = null; try { sh = Protobot.PartsManager.GeneratePart("SHFT-High Strength-6", new Vector3(3f, 1f, 0f), Quaternion.identity); } catch (System.Exception e) { sb.Append("shaft err " + e.Message + "\n"); }
Protobot.Tools.PartBend.Add(so, new Protobot.Tools.PartBend.Bend { start = 1.2f, angle = 45f, toward = 0f, radius = 0.375f });
sb.Append("standoff state " + so.GetComponent<Protobot.SavedObject>().state + " mesh " + so.GetComponent<MeshFilter>().sharedMesh.vertexCount + " holes " + so.GetComponentsInChildren<Protobot.HoleCollider>().Length + "\n");
foreach (var h in so.GetComponentsInChildren<Protobot.HoleCollider>()) sb.Append("  hole " + h.transform.localPosition.ToString("F2") + " fwd " + h.transform.forward.ToString("F2") + " depth " + h.transform.localScale.z.ToString("F2") + "\n");
if (sh != null) {
  sb.Append("shaft id " + sh.GetComponent<Protobot.SavedObject>().id + " scale " + sh.transform.localScale.ToString("F2") + " bendable " + Protobot.Tools.PartBend.IsBendable(sh) + "\n");
  Protobot.Tools.PartBend.Add(sh, new Protobot.Tools.PartBend.Bend { start = 2f, angle = 90f, toward = 90f, radius = 0.375f });
  sb.Append("shaft bounds " + sh.GetComponent<Renderer>().bounds.size.ToString("F2") + "\n");
}
// render
var camGo = new GameObject("RC"); var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.85f, 0.85f); cam.enabled = false;
var rt = new RenderTexture(900, 600, 24); cam.targetTexture = rt; var tex = new Texture2D(900, 600, TextureFormat.RGB24, false);
camGo.transform.rotation = Quaternion.Euler(30f, -30f, 0f); camGo.transform.position = new Vector3(2f, 2f, 1f) - camGo.transform.forward * 30f; cam.orthographicSize = 3.5f; cam.farClipPlane = 100f;
cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 900, 600), 0, 0); tex.Apply(); RenderTexture.active = null;
System.IO.File.WriteAllBytes(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/Screenshots/bend.png", tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(camGo); rt.Release();
return sb.ToString();
