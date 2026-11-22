// Turns the outline render pass off (or on with "on" in Temp/outlines.txt): trailer footage without outlines.
var on = System.IO.File.Exists("Temp/outlines.txt") && System.IO.File.ReadAllText("Temp/outlines.txt").Trim() == "on";
var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
var field = asset.GetType().GetField("m_RendererDataList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var list = field.GetValue(asset) as UnityEngine.Rendering.Universal.ScriptableRendererData[];
int n = 0;
foreach (var data in list) if (data != null) foreach (var f in data.rendererFeatures) if (f != null && f.GetType().Name == "OutlineFeature") { f.SetActive(on); n++; }
return "outline features " + (on ? "on" : "off") + ": " + n;
