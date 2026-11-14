var raw = PlayerPrefs.GetString("recentBuilds");
var kept = new System.Collections.Generic.List<string>(); var removed = new System.Collections.Generic.List<string>();
foreach (var p in raw.Split('\n')) { if (p.Contains("scratchpad") || p.Contains("AgentTesting") || p.Contains("Temp/claude")) removed.Add(p); else kept.Add(p); }
PlayerPrefs.SetString("recentBuilds", string.Join("\n", kept)); PlayerPrefs.Save();
return "kept: " + string.Join(" | ", kept) + "\nremoved " + removed.Count;
