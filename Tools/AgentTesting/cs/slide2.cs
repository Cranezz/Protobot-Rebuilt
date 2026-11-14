var sb = new System.Text.StringBuilder();
Protobot.PartsManager.DestroyLoadedObjects();
var member = Protobot.PartsManager.GeneratePart("VXSLIDEASSEMBLYP-Outer Slide Member", new Vector3(0f, 3f, 0f), Quaternion.Euler(-90f, 0f, 0f));
var truck = Protobot.PartsManager.GeneratePart("VXSLIDEASSEMBLYP-Inner Acetal Slide Truck", Vector3.zero, Quaternion.identity);
var d = Protobot.Motion.SlideTruck.Seat(member, member.transform.TransformPoint(new Vector3(2f, 0f, 0f)));
truck.transform.SetPositionAndRotation(d.translation.Position, d.rotation.Orientation);
truck.GetComponent<Protobot.Motion.SlideTruck>().Check();
sb.Append("linked " + (truck.GetComponent<Protobot.Motion.SlideTruck>().Member == member) + " neighbours " + string.Join(",", System.Linq.Enumerable.Select(Protobot.GroupManager.Neighbours(truck), g => g.name)) + "\n");
// overlap check: truck vs member colliders
var pen = Physics.ComputePenetration(truck.GetComponent<Collider>(), truck.transform.position, truck.transform.rotation, member.GetComponent<Collider>(), member.transform.position, member.transform.rotation, out var dir, out var dist);
sb.Append("penetrates " + pen + " " + dist.ToString("F3") + "\n");
var camGo = new GameObject("RC"); var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.85f, 0.85f); cam.enabled = false;
var rt = new RenderTexture(900, 600, 24); cam.targetTexture = rt; var tex = new Texture2D(900, 600, TextureFormat.RGB24, false);
camGo.transform.rotation = Quaternion.Euler(20f, 60f, 0f); camGo.transform.position = truck.transform.position - camGo.transform.forward * 30f; cam.orthographicSize = 1.5f; cam.farClipPlane = 100f;
cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 900, 600), 0, 0); tex.Apply(); RenderTexture.active = null;
System.IO.File.WriteAllBytes(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/Screenshots/slide.png", tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(camGo); rt.Release();
return sb.ToString();
