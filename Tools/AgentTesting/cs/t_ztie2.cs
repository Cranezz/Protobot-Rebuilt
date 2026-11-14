var tube = UnityEngine.Object.FindAnyObjectByType<Protobot.Motion.PneumaticTube>();
var sb = new System.Text.StringBuilder("nodes " + tube.nodes.Count + ": ");
foreach (var n in tube.nodes) sb.Append((n.part != null ? n.part.name : "air") + (n.tie ? " TIE" : "") + "; ");
sb.Append(" verts " + tube.GetComponent<UnityEngine.MeshFilter>().sharedMesh.vertexCount + " token " + Protobot.Motion.Dynamics.Token(tube.gameObject, "tube"));
return sb.ToString();
