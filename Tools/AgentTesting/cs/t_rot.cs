foreach (var o in Protobot.PartsManager.FindLoadedObjects()) o.transform.rotation = Quaternion.Euler(0, 90, 0) * o.transform.rotation;
return "rotated";
