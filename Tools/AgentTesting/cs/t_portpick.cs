// (Play) Opens the Devices window and the first device card's port picker (screenshot after).
if (System.IO.File.ReadAllText("Temp/portpick.txt").Trim() == "open") { Protobot.Devices.DevicesPanel.Open(); return "opened"; }
var badge = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Where(b => b.name == "Port" && b.gameObject.activeInHierarchy).OrderByDescending(b => b.transform.position.y).FirstOrDefault();
if (badge == null) return "no port button";
badge.onClick.Invoke(); return "picker open";
