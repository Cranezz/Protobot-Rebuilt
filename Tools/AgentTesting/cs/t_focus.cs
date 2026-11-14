Bounds b = new Bounds(); bool any = false;
foreach (var o in Protobot.PartsManager.FindLoadedObjects()) { var r = o.GetComponent<Renderer>(); if (r == null) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
Protobot.PivotCamera.Main.MoveFocusPosition(b.center);
return "focus " + b.center.ToString("F1") + " size " + b.size.ToString("F1") + " cam " + Camera.main.transform.position.ToString("F1");
