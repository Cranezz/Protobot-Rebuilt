// (Play) Physics cost: the frame counter's text and the number of colliders on the game-piece layer.
var n = 0; foreach (var c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)) if (c.gameObject.layer == Protobot.Motion.Sandbox.ElementLayer && c.enabled) n++;
var t = ""; foreach (var tmp in UnityEngine.Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None)) if (tmp.text.Contains("physics")) t = tmp.text.Replace("\n", " ");
var awake = 0; var bodies = 0; foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (rb.gameObject.layer == Protobot.Motion.Sandbox.ElementLayer) { bodies++; if (!rb.IsSleeping()) awake++; }
return "piece colliders " + n + ", piece bodies " + bodies + " (awake " + awake + "), counter: " + t;
