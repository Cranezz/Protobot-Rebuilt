var g = Protobot.PartsManager.GetPartType("SNDF").GetComponent<Protobot.PartGenerator>();
g.param1.value = "2.00in";
var d = g.Generate(new Vector3(2.5f, 1f, 0f), Quaternion.Euler(-90f, 0f, 0f));
Physics.SyncTransforms();
return "post at " + d.transform.position;
