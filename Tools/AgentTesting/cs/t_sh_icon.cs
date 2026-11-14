var dir = "C:/Users/kille/AppData/Local/Temp/claude/5b0c4ec0a185a9e3/4fe6ed7d-e179-4b4f-b31d-ea600198646e/scratchpad/";
var a = Protobot.Custom.Thumbnails.ForVexPart("VXHARDWARE", "2.50\" Shoulder Screw (0.04\" shoulder)");
var b = Protobot.Custom.Thumbnails.ForVexPart("VXHARDWARE", "0.29\" Shoulder Screw (0.09\" shoulder)");
System.Func<UnityEngine.Texture2D, byte[]> png = t => { var rt = UnityEngine.RenderTexture.GetTemporary(t.width, t.height); UnityEngine.Graphics.Blit(t, rt); var prev = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt; var c = new UnityEngine.Texture2D(t.width, t.height); c.ReadPixels(new UnityEngine.Rect(0,0,t.width,t.height),0,0); c.Apply(); UnityEngine.RenderTexture.active = prev; UnityEngine.RenderTexture.ReleaseTemporary(rt); return c.EncodeToPNG(); };
if (a != null) System.IO.File.WriteAllBytes(dir + "icon_250.png", png(a));
if (b != null) System.IO.File.WriteAllBytes(dir + "icon_029.png", png(b));
var inCatalog = false;
foreach (var p in Protobot.PartsManager.GetPartType("VXHARDWARE").GetComponent<Protobot.PartGenerator>().GetParam1Options()) if (p.StartsWith("2.50\" Shoulder")) inCatalog = true;
return "icons " + (a != null) + " " + (b != null) + " | 2.50 in options " + inCatalog;
