var gen = Protobot.PartsManager.GetPartType("VXSHAFTPARTS").GetComponent<Protobot.PartGenerator>(); gen.param1.value = "6\" Capped Shaft";
var mesh = gen.GetMesh(); var sb = new System.Text.StringBuilder();
var bins = new float[24];
foreach (var v in mesh.vertices) { int i = Mathf.Clamp((int)((-v.y) / 0.25f), 0, 23); bins[i] = Mathf.Max(bins[i], new Vector2(v.x, v.z).magnitude); }
for (int i = 0; i < 24; i++) sb.Append((i * 0.25f).ToString("F2") + ":" + bins[i].ToString("F3") + " ");
return sb.ToString();
