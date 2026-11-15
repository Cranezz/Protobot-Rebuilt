var launch = UnityEngine.GameObject.Find("Launch Canvas");
if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen") { b.onClick.Invoke(); break; }
Protobot.RobotCode.CodeStore.Set("");
Protobot.RobotCode.CodePanel.Close();
Protobot.RobotCode.CodePanel.Open();
return "open";
