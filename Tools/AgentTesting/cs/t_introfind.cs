var sb = new System.Text.StringBuilder();
foreach (var c in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(UnityEngine.FindObjectsSortMode.None)) if (c.sortingOrder >= 400) sb.Append(c.name + " ");
return "canvases: " + sb + " | intro type in scene: " + (UnityEngine.Object.FindFirstObjectByType<Protobot.Interface.Intro>() != null) + " | args " + string.Join(" ", System.Environment.GetCommandLineArgs());
