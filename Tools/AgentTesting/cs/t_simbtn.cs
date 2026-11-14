UnityEngine.UI.Button b = null;
foreach (var x in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)) if (x.name == "Simulate") b = x;
if (b == null) return "no button";
var rt = (RectTransform)b.transform; var c = new Vector3[4]; rt.GetWorldCorners(c);
var info = "interactable " + b.interactable + " active " + b.isActiveAndEnabled + " corners " + c[0] + " " + c[2];
b.onClick.Invoke();
return info + " -> running " + Protobot.Motion.Sandbox.Running;
