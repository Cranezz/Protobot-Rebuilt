// Renders the main camera from a pose (PlayerPrefs "t_camshot" = "fx,fy,fz,pitch,yaw,dist,name") into a PNG in the
// scratchpad (no grid plane, no UI), then puts the camera back.
var a = UnityEngine.PlayerPrefs.GetString("t_camshot", "0,1.5,0,-25,135,22,odom").Split(',');
System.Func<int, float> F = i => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
var cam = Camera.main;
var oldPos = cam.transform.position; var oldRot = cam.transform.rotation; var oldMask = cam.cullingMask;
var focus = new Vector3(F(0), F(1), F(2));
var rot = Quaternion.Euler(F(3), F(4), 0f);
cam.transform.rotation = rot;
cam.transform.position = focus - rot * Vector3.forward * F(5);
cam.cullingMask = oldMask & ~(1 << 9) & ~(1 << 5) & ~(1 << 8) & ~(1 << 10) & ~(1 << 11);
var rt = new RenderTexture(1200, 800, 24);
var old = cam.targetTexture;
cam.targetTexture = rt;
cam.Render();
RenderTexture.active = rt;
var tex = new Texture2D(1200, 800, TextureFormat.RGB24, false);
tex.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0);
tex.Apply();
RenderTexture.active = null;
cam.targetTexture = old;
cam.transform.position = oldPos; cam.transform.rotation = oldRot; cam.cullingMask = oldMask;
var path = @"C:/Users/kille/AppData/Local/Temp/claude/5b0c4ec0a185a9e3/8d21650f-8a3d-4b32-911f-851c0b975844/scratchpad/" + a[6] + ".png";
System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
return path;
