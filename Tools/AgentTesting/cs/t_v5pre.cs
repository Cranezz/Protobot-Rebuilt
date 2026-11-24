// (Play) Tutorial 5's start: the home menu closed, "Show only this part" off, nothing in the build (the demo loads its target).
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
UnityEngine.PlayerPrefs.SetInt("V2.Sketch.OnlyThisPart", 0);
Protobot.PartsManager.DestroyLoadedObjects();
Protobot.Builds.DemoPlayer.Target.Clear();
return "ready";
