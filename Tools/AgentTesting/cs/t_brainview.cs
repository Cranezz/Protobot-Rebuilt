var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var g = Protobot.PartsManager.GetPartType("BRAN").GetComponent<Protobot.PartGenerator>();
var o = g.Generate(new Vector3(0f, 1f, 0f), Quaternion.identity);
var a = System.IO.File.ReadAllText("Temp/cam.txt").Trim().Split(' ');
Protobot.PivotCamera.Main.SetTransform(new Vector3(0f, 1f, 0f), new Vector3(float.Parse(a[0]), float.Parse(a[1]), 0f), float.Parse(a[2]));
return "brain " + o.name + " children " + o.transform.childCount;
