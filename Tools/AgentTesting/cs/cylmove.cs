var c = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.PneumaticCylinder>();
var list = Protobot.ConnectedObjExtensions.GetConnectedObjects(c.gameObject, true);
var names = ""; foreach (var o in list) names += " " + o.name.Replace("(Clone)", "");
// Move it the way the move tools do: everything onto a pivot, shift, let go.
var pivot = Protobot.Pivot.Create("Test Pivot");
pivot.AddObjects(list); pivot.transform.position += new Vector3(3f, 0f, 0f); pivot.DetachObjects();
UnityEngine.Object.Destroy(pivot.gameObject);
var th = c.Rod.GetComponentInChildren<Protobot.Motion.RodThread>(true);
return "connected:" + names + "\nafter move: thread " + (th != null ? "still in the rod" : "LEFT the rod") + ", rod parent " + c.Rod.parent.name;
