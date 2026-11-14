var sb = new System.Text.StringBuilder();
foreach (var t in Resources.LoadAll<Protobot.PartType>("Part Prefabs")) if (!t.id.StartsWith("VX")) sb.Append(t.id + " ");
return sb.ToString();
