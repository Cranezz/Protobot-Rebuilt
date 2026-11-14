using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Motion {
    public enum WireKind { Smart, ThreeWire }

    /// <summary>A socket a cable plugs into: on the brain (numbered smart ports 1-21, 3-wire A-H), a motor's or V5
    /// sensor's smart port, or (a 3-wire sensor's lead, a device whose socket isn't modelled) a spot on the part.</summary>
    public class WirePort {
        public GameObject part;
        public Vector3 local;     // the socket's mouth, in the part's space
        public Vector3 outLocal;  // out of the socket
        public Vector3 upLocal;   // its long side (an RJ plug's width, a 3-wire plug's row of pins)
        public WireKind kind;
        public string label;      // "Port 11", "Port A", "" (a spot on a part)
        public int number;        // brain: 1-21 smart, 1-8 for A-H; else 0
        public bool brain;

        public Vector3 Position => part.transform.TransformPoint(local);
        public Vector3 Out => part.transform.TransformDirection(outLocal).normalized;
        public Vector3 Up => part.transform.TransformDirection(upLocal).normalized;

        public WirePort On(GameObject p) {
            var copy = (WirePort)MemberwiseClone();
            copy.part = p;
            return copy;
        }
    }

    // V2: where cables plug in. The brain's ports were measured on its model (the original's "Brain": smart ports
    // 1-10 along one long side and 11-20 along the other, in recesses under its rim, 0.35" apart; 21 on the battery
    // end; 3-wire A-H on the other end, 0.2" apart). A motor's or V5 sensor's smart port is found on its model (the
    // 0.315" x 0.441" blind socket HoleScanner sees). 3-wire sensors have their lead where you click them.
    public static class WirePorts {
        private static readonly float[] BrainX = { 1.845f, 1.495f, 1.155f, 0.815f, 0.455f, -0.045f, -0.385f, -0.735f, -1.085f, -1.425f };

        private static List<WirePort> brainPorts;

        private static List<WirePort> BrainPorts() {
            if (brainPorts != null) return brainPorts;
            brainPorts = new List<WirePort>();
            // 1-10 on the +z side from the 3-wire end, 11-20 back along the -z side.
            for (var i = 0; i < 10; i++)
                brainPorts.Add(new WirePort { local = new Vector3(BrainX[i], 0.47f, 1.5f), outLocal = Vector3.forward, upLocal = Vector3.right,
                                              kind = WireKind.Smart, number = i + 1, label = "Port " + (i + 1), brain = true });
            for (var i = 0; i < 10; i++)
                brainPorts.Add(new WirePort { local = new Vector3(BrainX[9 - i], 0.47f, -1.5f), outLocal = Vector3.back, upLocal = Vector3.right,
                                              kind = WireKind.Smart, number = 11 + i, label = "Port " + (11 + i), brain = true });
            brainPorts.Add(new WirePort { local = new Vector3(-2.72f, 0.05f, -1.05f), outLocal = Vector3.left, upLocal = Vector3.forward,
                                          kind = WireKind.Smart, number = 21, label = "Port 21", brain = true });
            for (var i = 0; i < 8; i++)
                brainPorts.Add(new WirePort { local = new Vector3(2.72f, -0.005f, 0.694f - 0.2f * i), outLocal = Vector3.right, upLocal = Vector3.up,
                                              kind = WireKind.ThreeWire, number = i + 1, label = "Port " + (char)('A' + i), brain = true });
            return brainPorts;
        }

        public static string Id(GameObject part) => part != null ? part.GetComponent<SavedObject>()?.id ?? "" : "";

        public static bool IsBrain(GameObject part) => Id(part) == "BRAN" || Id(part).StartsWith("BRAN-");

        /// <summary>What a part plugs in with (null: nothing; the brain takes both).</summary>
        public static WireKind? DeviceKind(GameObject part) {
            var id = Id(part);
            if (id.StartsWith("MOTR") || id.StartsWith("RDIO")) return WireKind.Smart;
            if (id.StartsWith("SNSR-")) return id.EndsWith("-V5") && !id.Contains("Potentiometer") ? WireKind.Smart : WireKind.ThreeWire;
            if (id.StartsWith("SWCH-")) return WireKind.ThreeWire;
            if (id.StartsWith("VXSENSORS-")) {
                var name = id.Substring("VXSENSORS-".Length);
                return name.Contains("GPS") || name.Contains("3 Wire Expander") || name.Contains("Vision") || name.Contains("Inertial")
                       || name.Contains("Optical Sensor") || name.Contains("Distance") || name.Contains("Rotation") ? WireKind.Smart : WireKind.ThreeWire;
            }
            if (id.StartsWith("VXPNEUMATICS") && id.Contains("Solenoid")) return WireKind.ThreeWire;
            return null;
        }

        public static bool Takes(GameObject part, WireKind kind) => IsBrain(part) || DeviceKind(part) == kind;

        // A device's own sockets, found once per model.
        private static readonly Dictionary<Mesh, List<WirePort>> found = new();

        /// <summary>The sockets on a part (the brain's numbered ones; a device's modelled smart socket).</summary>
        public static List<WirePort> Of(GameObject part) {
            if (part == null) return new List<WirePort>();
            if (IsBrain(part)) return BrainPorts().Select(p => p.On(part)).ToList();
            if (DeviceKind(part) != WireKind.Smart || !part.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return new List<WirePort>();
            if (!found.TryGetValue(filter.sharedMesh, out var list)) {
                list = new List<WirePort>();
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices.Select(v => v * 0.0254f).ToList();
                foreach (var h in Geometry.HoleScanner.Scan(vertices, mesh.triangles)) {
                    var a = h.size / 0.0254f;
                    var b = h.size2 / 0.0254f;
                    if (h.through || !h.square || Mathf.Min(a, b) < 0.27f || Mathf.Min(a, b) > 0.36f || Mathf.Max(a, b) < 0.4f || Mathf.Max(a, b) > 0.5f) continue;
                    var up = h.side.sqrMagnitude > 1e-6f ? (b >= a ? h.side : Vector3.Cross(h.axis, h.side)) : Vector3.up;
                    list.Add(new WirePort { local = h.entry / 0.0254f, outLocal = -h.axis, upLocal = up.normalized, kind = WireKind.Smart, label = "Smart port" });
                }
                found[filter.sharedMesh] = list;
            }
            return list.Select(p => p.On(part)).ToList();
        }

        /// <summary>The socket a cable of this kind would plug into under a ray: a part's own socket near where the
        /// ray hits it, else (a device that takes this kind but has no modelled socket) the spot hit.</summary>
        public static WirePort Under(Ray ray, WireKind kind, Wire except = null) {
            var hits = Physics.RaycastAll(ray, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"), QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits) {
                var saved = hit.collider.GetComponentInParent<SavedObject>();
                if (saved == null || saved.gameObject.IsDeleted()) continue;
                var part = SelectAs.Resolve(hit.collider.gameObject) ?? saved.gameObject;
                if (part.GetComponent<PneumaticTube>() != null || part.GetComponent<ElasticBand>() != null) continue;
                if (!Takes(part, kind)) return null;
                WirePort best = null;
                // The socket nearest the mouse as you see it (off the ray, not off where the ray hit: the brain's
                // recesses sit under its rim, so a hit there is well in front of the socket the mouse is over).
                var bestD = IsBrain(part) ? 0.25f : 0.6f;
                foreach (var port in Of(part)) {
                    if (port.kind != kind) continue;
                    if (Vector3.Dot(port.Out, -ray.direction) < -0.2f) continue;   // facing away
                    var d = Vector3.Cross(ray.direction, port.Position - ray.origin).magnitude;
                    if (d < bestD) {
                        bestD = d;
                        best = port;
                    }
                }
                if (best != null) return Taken(best, except) ? null : best;
                if (IsBrain(part) || Of(part).Count > 0) return null;
                // A spot on the device (its lead comes out here).
                var t = part.transform;
                var outward = hit.normal;
                var spot = new WirePort {
                    part = part, local = t.InverseTransformPoint(hit.point), outLocal = t.InverseTransformDirection(outward),
                    upLocal = t.InverseTransformDirection(Vector3.ProjectOnPlane(Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.forward, outward).normalized),
                    kind = kind, label = "",
                };
                return Taken(spot, except) ? null : spot;
            }
            return null;
        }

        /// <summary>Whether a cable already ends in this socket.</summary>
        public static bool Taken(WirePort port, Wire except = null) {
            var at = port.Position;
            foreach (var wire in Wire.Wires)
                if (wire != except && wire != null && !wire.gameObject.IsDeleted())
                    foreach (var n in new[] { wire.nodes.FirstOrDefault(), wire.nodes.LastOrDefault() })
                        if (n != null && n.kind == TubeNode.Port && (n.Position(wire.transform) - at).sqrMagnitude < 0.01f) return true;
            return false;
        }

        /// <summary>The socket of a part nearest a point (a cable's end), or null.</summary>
        public static WirePort Near(GameObject part, Vector3 point, float within = 0.15f) {
            WirePort best = null;
            var bestD = within;
            foreach (var port in Of(part)) {
                var d = Vector3.Distance(port.Position, point);
                if (d < bestD) {
                    bestD = d;
                    best = port;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ what's plugged in where

        private static long seen = -1;
        private static int seenWires = -1;
        private static readonly Dictionary<GameObject, WirePort> brainPortOf = new();

        /// <summary>The brain port a device is wired to (by a cable from it to the brain), or null.</summary>
        public static WirePort BrainPortOf(GameObject device) {
            if (device == null) return null;
            var count = Wire.Wires.Sum(w => w != null ? w.nodes.Count + (w.gameObject.IsDeleted() ? 1000 : 0) : 0);
            if (seen != Dynamics.Version || seenWires != count) {
                seen = Dynamics.Version;
                seenWires = count;
                brainPortOf.Clear();
                foreach (var wire in Wire.Wires) {
                    if (wire == null || wire.gameObject.IsDeleted() || wire.nodes.Count < 2) continue;
                    var a = wire.nodes[0];
                    var b = wire.nodes[^1];
                    if (a.kind != TubeNode.Port || b.kind != TubeNode.Port || a.part == null || b.part == null) continue;
                    if (IsBrain(b.part) && !IsBrain(a.part)) (a, b) = (b, a);
                    if (!IsBrain(a.part) || IsBrain(b.part)) continue;
                    var port = Near(a.part, a.Position(wire.transform), 0.2f);
                    if (port != null) brainPortOf[b.part] = port;
                }
            }
            return brainPortOf.TryGetValue(device, out var p) ? p : null;
        }
    }
}
