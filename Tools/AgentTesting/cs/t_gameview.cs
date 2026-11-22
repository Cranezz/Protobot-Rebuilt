// Sets the Game view to a fixed 1920 x 1080 (adds the size if needed) for trailer footage. Reflection on UnityEditor's
// GameViewSizes (internal). Result: the size the game view now has.
var asm = typeof(UnityEditor.EditorWindow).Assembly;
var sizesType = asm.GetType("UnityEditor.GameViewSizes");
var singleType = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType);
var instance = singleType.GetProperty("instance").GetValue(null);
var group = sizesType.GetMethod("GetGroup").Invoke(instance, new object[] { (int)UnityEditor.GameViewSizeGroupType.Standalone });
var groupType = group.GetType();
var count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
int index = -1;
for (int i = 0; i < count; i++) {
    var size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
    var w = (int)size.GetType().GetProperty("width").GetValue(size);
    var h = (int)size.GetType().GetProperty("height").GetValue(size);
    var sizeTypeProp = size.GetType().GetProperty("sizeType").GetValue(size).ToString();
    if (w == 1920 && h == 1080 && sizeTypeProp == "FixedResolution") { index = i; break; }
}
if (index < 0) {
    var gvsType = asm.GetType("UnityEditor.GameViewSize");
    var gvstType = asm.GetType("UnityEditor.GameViewSizeType");
    var ctor = gvsType.GetConstructor(new[] { gvstType, typeof(int), typeof(int), typeof(string) });
    var newSize = ctor.Invoke(new object[] { System.Enum.Parse(gvstType, "FixedResolution"), 1920, 1080, "Trailer 1080p" });
    groupType.GetMethod("AddCustomSize").Invoke(group, new object[] { newSize });
    index = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
}
var gameViewType = asm.GetType("UnityEditor.GameView");
var gameView = UnityEditor.EditorWindow.GetWindow(gameViewType, false, null, false);
var prop = gameViewType.GetProperty("selectedSizeIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
prop.SetValue(gameView, index);
return "game view size index " + index + " (1920x1080); screen now " + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height;
