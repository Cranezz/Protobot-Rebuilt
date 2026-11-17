var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.Online.OnlineWindow.Open();
var w = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
var t = typeof(Protobot.Online.OnlineScreen);
var m = t.GetMethod("Show");
m.Invoke(w, new object[] { System.Enum.Parse(m.GetParameters()[0].ParameterType, "Host") });
return "shown";
