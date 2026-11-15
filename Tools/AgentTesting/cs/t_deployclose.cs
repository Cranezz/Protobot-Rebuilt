foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w.titleContent.text == "Deployment") w.Close();
return "closed";
