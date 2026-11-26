// (Play) probe: collar + C-channel arm pointing -z, and some plates; prints holes
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var sb = new System.Text.StringBuilder();
var up = Vector3.up; var r = Vector3.left; var h = new Vector3(0, 2, 0);
var collarTurn = Quaternion.LookRotation(up, -r);
var collar = Protobot.PartsManager.GeneratePart("CLMP-Normal-Normal", h - collarTurn * new Vector3(0f, 0.007f, 0f), collarTurn);
var channelTurn = Quaternion.LookRotation(-r, up);
var ch = Protobot.PartsManager.GeneratePart("CCHL-1x2-8", h + r * (0.2165f + 0.0555f) + channelTurn * new Vector3(0.75f, 0f, 0f), channelTurn);
foreach (var id in new[] { "PLTE-1-6", "PLTE-1-7", "PLTE-6-1" }) { var p = Protobot.PartsManager.GeneratePart(id, new Vector3(5, 0, 0), Quaternion.LookRotation(up, Vector3.forward)); sb.AppendLine(id + " " + (p != null) + " holes " + (p != null ? p.GetComponentsInChildren<Protobot.HoleCollider>(true).Length : 0) + (p!=null? " bounds " + p.GetComponent<Renderer>().bounds.ToString("F3"):"")); if (p != null) UnityEngine.Object.Destroy(p); }
Physics.SyncTransforms();
sb.AppendLine("collar " + collar.GetComponent<Renderer>().bounds.ToString("F3"));
sb.AppendLine("channel " + ch.GetComponent<Renderer>().bounds.ToString("F3"));
foreach (var hc in ch.GetComponentsInChildren<Protobot.HoleCollider>(true)) sb.AppendLine(" hole " + hc.transform.position.ToString("F3") + " ax " + hc.transform.forward.ToString("F2"));
var sp = Protobot.PartsManager.GeneratePart("VXSPACERS-1.00\" Nylon Spacer (3/8\" OD)", new Vector3(-5,0,0), Quaternion.identity);
sb.AppendLine("spacer " + (sp != null) + (sp!=null? " " + sp.GetComponent<Renderer>().bounds.ToString("F3") + " frame " + Protobot.Motion.ChainLoop.IdlerFrame(sp).ToString() + " idler " + Protobot.Motion.ChainFamily.IsIdler(sp):""));
return sb.ToString();
