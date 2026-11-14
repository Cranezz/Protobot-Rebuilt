UnityEditor.EditorApplication.isPaused = false;
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
var pb = Protobot.Interface.PartsBrowser.Instance;
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
typeof(Protobot.Interface.PartsBrowser).GetMethod("SetCategory", bf).Invoke(pb, new object[] { 1 });
var set = (System.Collections.Generic.HashSet<string>)typeof(Protobot.Interface.PartsBrowser).GetField("folded", bf).GetValue(pb);
set.Clear(); set.Add("Hardware/Washers"); set.Add("Hardware/Shaft collars");
typeof(Protobot.Interface.PartsBrowser).GetMethod("Refresh", bf).Invoke(pb, null);
return "ok";
