// (Edit mode) parts a script made by mistake in the open scene: lists them; "Temp/stray.txt" = "remove" deletes them.
var list = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None);
var remove = System.IO.File.Exists("Temp/stray.txt") && System.IO.File.ReadAllText("Temp/stray.txt").Trim() == "remove";
if (remove) foreach (var s in list) UnityEngine.Object.DestroyImmediate(s.gameObject);
return (remove ? "removed " : "found ") + list.Length + " dirty " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
