var m = Protobot.Motion.MotorDrive.Live()[0];
bool gray;
var tex = (UnityEngine.Texture2D)Protobot.Devices.DevicePictures.For(m.gameObject, out gray);
System.IO.File.WriteAllBytes("Assets/Screenshots/motorpic.png", UnityEngine.ImageConversion.EncodeToPNG(tex));
var mf = m.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
var axis = m.transform.InverseTransformDirection(m.ShaftHole.transform.forward);
var v = mf.vertices; var t = mf.GetTriangles(1); var mid = UnityEngine.Vector3.zero; foreach (var i in t) mid += v[i]; mid /= t.Length;
return "axis " + axis + " window mid " + mid + " bounds " + mf.bounds + " subs " + mf.subMeshCount + " tris1 " + t.Length / 3;
