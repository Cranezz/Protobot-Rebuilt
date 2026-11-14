var low = GameObject.Find("LOW"); var b = UnityEngine.Object.FindAnyObjectByType<Protobot.Motion.ElasticBand>();
var p = Protobot.Motion.Sandbox.SimPoint(low, low.GetComponent<Renderer>().bounds.center);
return "low sim center " + p.ToString("F2") + " joints " + UnityEngine.Object.FindObjectsByType<SpringJoint>(FindObjectsSortMode.None).Length;
