// The band tool hooked on the first standoff, drawn as if the mouse were on the post at Temp/aim.txt.
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var et = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.ElasticTool>();
var a = System.IO.File.ReadAllText("Temp/aim.txt").Trim().Split(' ');
var aim = new Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2]));
var cam = Camera.main;
Protobot.Motion.BandPost p; Protobot.Motion.ElasticTool.PostAt(new Ray(cam.transform.position, (aim - cam.transform.position).normalized), out p);
typeof(Protobot.Motion.ElasticTool).GetMethod("Draw", flags).Invoke(et, new object[] { cam, p, false });
var look = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
var n = 0; foreach (var mf in look) if (mf.name == "Band Preview" && mf.gameObject.activeInHierarchy) n = mf.sharedMesh.vertexCount;
return "hover " + (p != null ? p.what : "none") + " preview verts " + n;
