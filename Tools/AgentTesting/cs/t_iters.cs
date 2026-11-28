// (Play) every simulated body's solver iterations from Temp/iters.txt ("POS VEL").
var f = System.IO.File.ReadAllText("Temp/iters.txt").Trim().Split(' ');
int p = int.Parse(f[0]), v = int.Parse(f[1]); var n = 0;
foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) { if (rb.isKinematic) continue; rb.solverIterations = p; rb.solverVelocityIterations = v; n++; }
return n + " bodies " + p + "/" + v;
