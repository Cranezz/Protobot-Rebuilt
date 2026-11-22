// V2 privacy: turns off Unity's hardware statistics (the player sending this PC's hardware to Unity) if this license allows.
var so = new UnityEditor.SerializedObject(UnityEditor.Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
var p = so.FindProperty("submitAnalytics");
if (p == null) return "no property";
var before = p.boolValue;
p.boolValue = false;
so.ApplyModifiedProperties();
UnityEditor.AssetDatabase.SaveAssets();
var after = new UnityEditor.SerializedObject(UnityEditor.Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings")).FindProperty("submitAnalytics").boolValue;
return "hardware statistics: was " + before + ", now " + after;
