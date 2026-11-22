// (Play) Drives the age question like a player: Month > March, year 2000, I agree. Expects "13+" (adult: the
// signed-in account isn't touched). Temp/privui.txt "fresh" first.
var dialog = UnityEngine.Object.FindObjectsByType<Protobot.Interface.Dialog>(UnityEngine.FindObjectsSortMode.None);
if (dialog.Length == 0) return "no dialog";
var d = dialog[0];
var choice = d.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(b => b.name == "Choice");
choice.onClick.Invoke();
var march = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(UnityEngine.FindObjectsSortMode.None).FirstOrDefault(b => b.name == "March");
if (march == null) return "no month list";
march.onClick.Invoke();
var year = d.GetComponentsInChildren<TMPro.TMP_InputField>(true).FirstOrDefault(f => f.name == "Year");
year.text = "2000";
var label = choice.GetComponentInChildren<TMPro.TMP_Text>().text;
d.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(b => b.name == "Continue").onClick.Invoke();
return "month shows " + label + "; age now " + UnityEngine.PlayerPrefs.GetString("V2.Age", "-") + ", agreed " + Protobot.Online.AgeCheck.Agreed + ", account still " + Protobot.Online.Accounts.Username;
