var cam = Camera.main;
var w = cam.WorldToScreenPoint(new Vector3(-7.335f, 5.249f, 9.57f));
Protobot.Screw sc = null; foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.Screw>(FindObjectsSortMode.None)) if (s.GetComponent<Protobot.SavedObject>().id == "SCRW-1.75in") sc = s;
var head = sc.transform.position; var down = -sc.transform.forward;
var a = cam.WorldToScreenPoint(head + down * 0.45f); var b = cam.WorldToScreenPoint(head + down * 1.3f);
return "screen " + Screen.width + "x" + Screen.height + " wheel " + w.ToString("F0") + " near-head " + a.ToString("F0") + " near-tip " + b.ToString("F0");
