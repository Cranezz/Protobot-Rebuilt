// Imports the VEX "Sensors" family again (after a sensor model was added to parts_manifest.json); progress in Temp/VexImport.txt. Runs on the editor's update ticks (bring Unity forward).
var t = System.Type.GetType("VexPartImporter, Assembly-CSharp-Editor");
t.GetMethod("ImportMatching").Invoke(null, new object[] { "Sensors" });
return "started";
