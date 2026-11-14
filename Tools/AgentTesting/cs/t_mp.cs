var launch = UnityEngine.GameObject.Find("Launch Canvas");
var b = System.Linq.Enumerable.First(launch.GetComponentsInChildren<UnityEngine.UI.Button>(true), x => x.name == "Multiplayer Button");
b.onClick.Invoke();
return "clicked";
