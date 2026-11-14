var sb = new System.Text.StringBuilder();
var uj = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.UniversalJoint>();
var T = typeof(Protobot.Motion.UniversalJoint); var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
sb.Append("yoke holes " + uj.Yoke.GetComponentsInChildren<Protobot.HoleCollider>().Length + " root holes " + uj.GetComponentsInChildren<Protobot.HoleCollider>().Length + "\n");
var rotors = Protobot.Motion.GearTrain.Rotors();
sb.Append("rotors " + rotors.Count + "\n");
foreach (var r in rotors) sb.Append(" rotor " + r.Name + " parts " + string.Join(",", r.parts.ConvertAll(p => p.name)) + "\n");
if (rotors.Count > 0) { var sys = Protobot.Motion.GearTrain.SystemOf(rotors[0], rotors); sb.Append("system rotors " + sys.rotors.Count + " jammed " + sys.jammed + " " + sys.jamReason + "\n"); }
T.GetMethod("BeginManual", F).Invoke(uj, null); T.GetMethod("ManualTo", F).Invoke(uj, new object[] { 30f, 90f }); T.GetMethod("EndManual", F).Invoke(uj, null);
sb.Append("bend " + uj.Bend + "\n");
var rotors2 = Protobot.Motion.GearTrain.Rotors();
if (rotors2.Count > 0) { var sys = Protobot.Motion.GearTrain.SystemOf(rotors2[0], rotors2); sb.Append("after bend: rotors " + rotors2.Count + " system " + sys.rotors.Count + " jammed " + sys.jammed + "\n"); }
var camGo = new GameObject("RC"); var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.85f, 0.85f); cam.enabled = false;
var rt = new RenderTexture(700, 700, 24); cam.targetTexture = rt; var tex = new Texture2D(700, 700, TextureFormat.RGB24, false);
camGo.transform.rotation = Quaternion.Euler(10f, 20f, 0f); camGo.transform.position = new Vector3(0.3f, 3.5f, 0f) - camGo.transform.forward * 30f; cam.orthographicSize = 1.6f; cam.farClipPlane = 100f;
cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 700, 700), 0, 0); tex.Apply(); RenderTexture.active = null;
System.IO.File.WriteAllBytes(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/Screenshots/uj.png", tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(camGo); rt.Release();
return sb.ToString();
