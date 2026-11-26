// (Play) renders the main camera from a set pose into a PNG (Temp/tensshot.txt: "x y z  tx ty tz  path")
var a = System.IO.File.ReadAllText("Temp/tensshot.txt").Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
var inv = System.Globalization.CultureInfo.InvariantCulture;
System.Func<int, float> F = i => float.Parse(a[i], inv);
var cam = Camera.main;
var oldPos = cam.transform.position; var oldRot = cam.transform.rotation; var oldOrtho = cam.orthographic;
cam.orthographic = false;
cam.transform.position = new Vector3(F(0), F(1), F(2));
cam.transform.LookAt(new Vector3(F(3), F(4), F(5)));
var rt = new RenderTexture(1280, 800, 24);
var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
RenderTexture.active = rt;
var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); tex.Apply();
RenderTexture.active = null;
System.IO.File.WriteAllBytes(a[6], tex.EncodeToPNG());
UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex);
cam.transform.SetPositionAndRotation(oldPos, oldRot); cam.orthographic = oldOrtho;
return "saved " + a[6];
