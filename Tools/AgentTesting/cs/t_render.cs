// args via file: center, size
var lines = System.IO.File.ReadAllLines(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/render_args.txt");
var p = lines[0].Split(','); var center = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])); var size = float.Parse(lines[1]); var name = lines[2];
foreach (var mr in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) if (mr.name.StartsWith("Stud Mark")) mr.enabled = true;
var camGo = new GameObject("RC"); var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.8f, 0.8f, 0.82f); cam.enabled = false;
cam.cullingMask = ~((1 << 5) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11));
var rt = new RenderTexture(900, 700, 24); cam.targetTexture = rt; var tex = new Texture2D(900, 700, TextureFormat.RGB24, false);
camGo.transform.rotation = Quaternion.Euler(25f, -35f, 0f); camGo.transform.position = center - camGo.transform.forward * 40f; cam.orthographicSize = size; cam.farClipPlane = 100f;
cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 900, 700), 0, 0); tex.Apply(); RenderTexture.active = null;
System.IO.File.WriteAllBytes(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/Screenshots/" + name + ".png", tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(camGo); rt.Release();
return "saved";
