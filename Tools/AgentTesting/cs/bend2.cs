Protobot.PartsManager.DestroyLoadedObjects();
var sh = Protobot.PartsManager.GeneratePart("SHFT-High Strength-6", new Vector3(0f, 0.5f, 0f), Quaternion.Euler(0f, 90f, 0f));
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
UnityEditor.EditorApplication.isPaused = false;
return "ok";
