var sb = new System.Text.StringBuilder();
foreach (var o in Protobot.PartsManager.FindLoadedObjects()) sb.Append(o.name + " " + o.transform.position.ToString("F3") + " rot " + o.transform.eulerAngles.ToString("F1") + "\n");
return sb.ToString();
