Protobot.PartsManager.DestroyLoadedObjects();
var uj = Protobot.PartsManager.GeneratePart("VXADVANCEDGEARS-Universal Joint", new Vector3(0f, 3f, 0f), Quaternion.identity);
var a = Protobot.PartsManager.GeneratePart("SHFT-Normal-2", new Vector3(0f, 3f - 0.67f, 0f), Quaternion.LookRotation(Vector3.up));
var b = Protobot.PartsManager.GeneratePart("SHFT-Normal-2", new Vector3(0f, 3f + 1.76f, 0f), Quaternion.LookRotation(Vector3.up));
return "made " + a.GetComponent<MeshFilter>().sharedMesh.bounds.ToString() + " scale " + a.transform.localScale;
