// Averages the last N profiler frames: top-level markers under PlayerLoop (and their biggest children), main thread.
UnityEditorInternal.ProfilerDriver.enabled = false;
var last = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
var first = System.Math.Max(UnityEditorInternal.ProfilerDriver.firstFrameIndex, last - 59);
var totals = new System.Collections.Generic.Dictionary<string, double>();
var frames = 0; double frameMs = 0;
for (var f = first; f <= last; f++) {
  using (var view = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime, false)) {
    if (view == null || !view.valid) continue;
    frames++;
    frameMs += view.frameTimeMs;
    var stack = new System.Collections.Generic.List<int>();
    var children = new System.Collections.Generic.List<int>();
    System.Action<int, string, int> walk = null;
    walk = (id, path, depth) => {
      view.GetItemChildren(id, children);
      var copy = new System.Collections.Generic.List<int>(children);
      foreach (var c in copy) {
        var name = view.GetItemName(c);
        var ms = view.GetItemColumnDataAsDouble(c, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime);
        var p = path.Length == 0 ? name : path + " > " + name;
        double v; totals.TryGetValue(p, out v); totals[p] = v + ms;
        if (depth < 7 && ms > 0.1) walk(c, p, depth + 1);
      }
    };
    walk(view.GetRootItemID(), "", 0);
  }
}
var sb = new System.Text.StringBuilder();
sb.AppendLine("frames " + frames + " avg frame " + (frameMs / System.Math.Max(1, frames)).ToString("0.00") + " ms");
foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(totals, kv => kv.Value), 80))
  if (kv.Value / frames > 0.15) sb.AppendLine((kv.Value / frames).ToString("0.00") + "  " + kv.Key);
return sb.ToString();
