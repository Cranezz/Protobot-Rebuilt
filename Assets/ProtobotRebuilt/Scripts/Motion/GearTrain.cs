using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Protobot.Motion {
    // V2: what turns together and what drives what. A rotor is a shaft and everything that turns with it: what's
    // locked onto it (a square hole the shaft fits, or a clamp: gears, wheels, sprockets, collars) and what's
    // screwed to those. Holes it only passes through (round holes, bearings, channels) are where it turns; a motor's
    // square hole is what drives it. Gears on different rotors mesh when their axles are parallel, they're in one
    // plane and their middles are their pitch circles apart (VEX gears: 24 teeth to the inch of pitch diameter, so
    // 36T and 36T mesh 1.5" apart, 12T and 60T too). A drive system is rotors joined by meshing gears; each rotor
    // turns at a ratio of the first (minus: the other way round). The build mode's motors and the sandbox both use it.
    public class Rotor {
        public GameObject shaft;
        public Vector3 start, axis;     // the shaft's line: a point on it and its direction
        public float low, high, width;  // along the axis from start; the shaft's thinnest side
        public readonly List<GameObject> parts = new();                      // turn with the shaft (it's first)
        public readonly List<(GameObject part, Vector3 point)> bearings = new();  // where it turns in other parts
        public readonly List<MotorDrive> motors = new();
        public readonly List<Gear> gears = new();
        public bool fixedToFrame;       // what's locked on it is also fastened to what it turns in
        public string Name => shaft != null ? Interface.PartLabels.Short(shaft) : "shaft";
    }

    public class Gear {
        public GameObject part;
        public Rotor rotor;
        public int teeth;
        public bool sprocket;
        public Vector3 center;
        public float PitchRadius => teeth / 48f;
    }

    public class DriveSystem {
        public readonly List<Rotor> rotors = new();
        public readonly Dictionary<Rotor, float> ratio = new();             // turn of each about its own axis, per turn of the first
        public readonly List<(Gear a, Gear b)> meshes = new();
        // Chains in the system, each run by one of its rotors: inches of chain per radian that rotor turns.
        public readonly List<(ChainLoop chain, Rotor rotor, float inchesPerRadian)> chains = new();
        public bool jammed;
        public string jamReason;

        public IEnumerable<MotorDrive> Motors => rotors.SelectMany(r => r.motors).Distinct();

        public Rotor RotorOf(MotorDrive motor) => rotors.FirstOrDefault(r => r.motors.Contains(motor));

        /// <summary>The motor's turn (its own forward) per turn of the system's first rotor.</summary>
        public float MotorRatio(MotorDrive motor) {
            var rotor = RotorOf(motor);
            return rotor == null ? 0f : ratio[rotor] * Mathf.Sign(Vector3.Dot(motor.Forward, rotor.axis));
        }
    }

    public static class GearTrain {
        private const float OnLine = 0.06f;

        // ------------------------------------------------------------------ rotors

        // A hole as Fill needs it, read once per look at the build (transform reads are the slow part).
        private struct HoleInfo {
            public HoleCollider hole;
            public GameObject part;
            public Vector3 position, forward;
        }

        private static List<Rotor> cached;
        private static long cachedVersion = -1;

        /// <summary>Every shaft in the build as a rotor, kept until the build changes (an undo step, a part made or
        /// removed): use this unless parts were moved without an undo step.</summary>
        public static List<Rotor> Current() {
            if (cached == null || cachedVersion != Dynamics.Version) {
                cached = Rotors();
                cachedVersion = Dynamics.Version;
            }
            return cached;
        }

        /// <summary>Every shaft in the build as a rotor (looked at now).</summary>
        public static List<Rotor> Rotors() {
            var holes = new List<HoleInfo>();
            var gone = new Dictionary<GameObject, bool>();
            foreach (var h in Object.FindObjectsByType<HoleCollider>(FindObjectsSortMode.None)) {
                var parent = h.transform.parent;
                if (parent == null) continue;
                var part = parent.gameObject;
                if (!gone.TryGetValue(part, out var deleted)) gone[part] = deleted = part.IsDeleted();
                if (deleted) continue;
                var t = h.transform;
                holes.Add(new HoleInfo { hole = h, part = part, position = t.position, forward = t.forward });
            }
            var motors = MotorDrive.Live();
            var rotors = new List<Rotor>();
            foreach (var shaft in Object.FindObjectsByType<Shaft>(FindObjectsSortMode.None))
                if (!shaft.gameObject.IsDeleted() && Measure(shaft, out var rotor)) {
                    Fill(rotor, holes, motors);
                    rotors.Add(rotor);
                }
            // A wheel, gear or sprocket spinning on a screw (Linkage.SpinsOn: a screw joint, as teams build drive
            // wheels and idlers) is a rotor of its own: it and what's screwed to it turn about the screw, which stays
            // with the frame.
            var onScrews = new HashSet<GameObject>();
            foreach (var screw in Object.FindObjectsByType<Screw>(FindObjectsSortMode.None)) {
                if (screw.gameObject.IsDeleted()) continue;
                foreach (var part in GroupManager.Neighbours(screw.gameObject)) {
                    if (onScrews.Contains(part) || !Linkage.SpinsOn(screw.gameObject, part) || !MeasureScrew(screw, part, out var rotor)) continue;
                    FillScrew(rotor, screw.gameObject, holes);
                    foreach (var p in rotor.parts) onScrews.Add(p);
                    rotors.Add(rotor);
                }
            }
            FindGears(rotors);
            FindChains();
            return rotors;
        }

        private static bool Measure(Shaft shaft, out Rotor rotor) {
            rotor = null;
            if (!shaft.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return false;
            var bounds = filter.sharedMesh.bounds;
            var t = shaft.transform;
            var a = t.TransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.min.z));
            var b = t.TransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.max.z));
            if ((b - a).sqrMagnitude < 1e-6f) return false;
            rotor = new Rotor {
                shaft = shaft.gameObject, start = a, axis = (b - a).normalized, low = 0f, high = (b - a).magnitude,
                width = Mathf.Min(t.TransformVector(Vector3.right * bounds.size.x).magnitude, t.TransformVector(Vector3.up * bounds.size.y).magnitude)
            };
            return true;
        }

        // A part spinning on a screw: the rotor is the part (its "shaft"), on the screw's line (its shank along its
        // own z, 8-32: 0.164" across).
        private static bool MeasureScrew(Screw screw, GameObject part, out Rotor rotor) {
            rotor = null;
            if (!screw.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return false;
            var bounds = filter.sharedMesh.bounds;
            var t = screw.transform;
            var a = t.TransformPoint(new Vector3(0f, 0f, bounds.min.z));
            var b = t.TransformPoint(new Vector3(0f, 0f, bounds.max.z));
            if ((b - a).sqrMagnitude < 1e-6f) return false;
            rotor = new Rotor { shaft = part, start = a, axis = (b - a).normalized, low = 0f, high = (b - a).magnitude, width = 0.164f };
            return true;
        }

        // What turns with a part on a screw: what's fastened to it (a gear screwed to a wheel); it turns in whatever
        // else is on the screw's line (channels, spacers, nuts), and is held still if it's also fastened to those.
        private static void FillScrew(Rotor rotor, GameObject screw, List<HoleInfo> holes) {
            var fastened = Dynamics.Fastened(new[] { rotor.shaft }, new HashSet<GameObject>(), o => o.GetComponent<Shaft>() != null, out _);
            rotor.parts.Add(rotor.shaft);
            foreach (var part in fastened)
                if (!rotor.parts.Contains(part)) rotor.parts.Add(part);
            var seen = new HashSet<GameObject>();
            foreach (var info in holes) {
                if (rotor.parts.Contains(info.part) || info.part == screw) continue;
                if (Mathf.Abs(Vector3.Dot(info.forward, rotor.axis)) < 0.98f || !OnShaft(rotor, info.position) || !seen.Add(info.part)) continue;
                rotor.bearings.Add((info.part, info.position));
            }
            // Screwed tight to what the screw also holds (the wheel bolted to the channel): it can't spin.
            rotor.fixedToFrame = rotor.parts.Contains(screw) || GroupManager.Neighbours(screw).Any(n => n != rotor.shaft && rotor.parts.Contains(n) && Linkage.Fastens(screw, n));
        }

        /// <summary>Whether a point is on the rotor's shaft (on its line, within its length).</summary>
        public static bool OnShaft(Rotor rotor, Vector3 point, float slack = 0.05f) {
            var offset = point - rotor.start;
            if (Vector3.ProjectOnPlane(offset, rotor.axis).magnitude > OnLine) return false;
            var along = Vector3.Dot(offset, rotor.axis);
            return along >= rotor.low - slack && along <= rotor.high + slack;
        }

        private static void Fill(Rotor rotor, List<HoleInfo> holes, List<MotorDrive> motors) {
            var square = HoleShapes.instance != null ? HoleShapes.instance.GetShapeMesh("square") : null;
            var locked = new List<GameObject>();
            var turnsIn = new HashSet<GameObject>();
            foreach (var motor in motors)
                if (motor.ShaftHole != null && OnShaft(rotor, motor.ShaftHole.transform.position)
                    && Mathf.Abs(Vector3.Dot(motor.ShaftHole.transform.forward, rotor.axis)) > 0.98f)
                    rotor.motors.Add(motor);
            foreach (var info in holes) {
                var part = info.part;
                if (part == rotor.shaft) continue;
                if (Mathf.Abs(Vector3.Dot(info.forward, rotor.axis)) < 0.98f) continue;
                if (!OnShaft(rotor, info.position)) continue;
                if (locked.Contains(part)) continue;
                var hole = info.hole;
                if (rotor.motors.Any(m => m.gameObject == part)) continue;   // driven, not locked
                var size = Mathf.Max(hole.transform.lossyScale.x, hole.transform.lossyScale.y);
                var isSquare = hole.TryGetComponent(out MeshCollider mesh) && square != null && mesh.sharedMesh == square;
                // Wheels, gears and sprockets with a bigger hole than the shaft are held on it by an insert.
                if (hole.holeType == HoleCollider.HoleType.Clamp || (isSquare && size <= rotor.width + 0.035f) || TurnsWithShaft(part)) {
                    locked.Add(part);
                    turnsIn.Remove(part);
                    rotor.bearings.RemoveAll(b => b.part == part);
                }
                else if (turnsIn.Add(part))
                    rotor.bearings.Add((part, info.position));
            }
            var stop = new HashSet<GameObject>(turnsIn);
            foreach (var motor in rotor.motors) stop.Add(motor.gameObject);
            var fastened = Dynamics.Fastened(locked, stop, o => o.GetComponent<Shaft>() != null, out var frame);
            rotor.fixedToFrame = frame;
            rotor.parts.Add(rotor.shaft);
            foreach (var part in locked.Concat(fastened))
                if (!rotor.parts.Contains(part) && !stop.Contains(part)) rotor.parts.Add(part);
        }

        /// <summary>A wheel, gear or sprocket: it turns with the shaft through it (a square insert holds it).</summary>
        public static bool TurnsWithShaft(GameObject part) {
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id)) return false;
            var id = saved.id.ToUpperInvariant();
            return Teeth(part, out _) > 0 || id.StartsWith("OMNI-") || id.StartsWith("TWHL-") || id.StartsWith("FWHL-") || id.StartsWith("MECN")
                   || id.StartsWith("SPKT-") || (id.StartsWith("VX") && (id.Contains("WHEEL") || id.Contains("SPROCKET") || id.Contains("PULLEY")));
        }

        // ------------------------------------------------------------------ gears

        private static readonly Regex TeethPattern = new(@"(\d+)\s*T\b", RegexOptions.IgnoreCase);

        /// <summary>A spur gear's or sprocket's teeth from its part id (0: not one; bevel, worm and rack gears don't count).</summary>
        public static int Teeth(GameObject part, out bool sprocket) {
            sprocket = false;
            if (part == null || !part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id)) return 0;
            var id = saved.id;
            var upper = id.ToUpperInvariant();
            if (upper.Contains("BEVEL") || upper.Contains("WORM") || upper.Contains("RACK") || upper.Contains("RATCHET")) return 0;
            sprocket = upper.Contains("SPROCKET");
            sprocket |= upper.StartsWith("SPKT-");
            if (!(upper.StartsWith("GEAR-") || upper.StartsWith("VXGEARS-") || sprocket)) return 0;
            // VEX-CAD names its standard 15T sprocket "Normal 12T" (its model has 15 teeth, as VEX sells it).
            if (upper.StartsWith("VXSPROCKETS-") && upper.Contains("NORMAL 12T")) return 15;
            var match = TeethPattern.Match(id);
            return match.Success && int.TryParse(match.Groups[1].Value, out var teeth) ? teeth : 0;
        }

        private static void FindGears(List<Rotor> rotors) {
            foreach (var rotor in rotors)
                foreach (var part in rotor.parts) {
                    var teeth = Teeth(part, out var sprocket);
                    if (teeth <= 0 || !part.TryGetComponent(out Renderer renderer)) continue;
                    var c = renderer.bounds.center;
                    var along = Vector3.Dot(c - rotor.start, rotor.axis);
                    rotor.gears.Add(new Gear { part = part, rotor = rotor, teeth = teeth, sprocket = sprocket, center = rotor.start + rotor.axis * along });
                }
        }

        /// <summary>Whether two gears on different rotors mesh (parallel axles, one plane, pitch circles touching).</summary>
        public static bool Meshes(Gear a, Gear b) {
            if (a.rotor == b.rotor || a.sprocket || b.sprocket) return false;
            if (Mathf.Abs(Vector3.Dot(a.rotor.axis, b.rotor.axis)) < 0.995f) return false;
            var delta = b.center - a.center;
            var axial = Vector3.Dot(delta, a.rotor.axis);
            if (Mathf.Abs(axial) > 0.3f) return false;
            var apart = (delta - a.rotor.axis * axial).magnitude;
            return Mathf.Abs(apart - (a.PitchRadius + b.PitchRadius)) < 0.08f;
        }

        // ------------------------------------------------------------------ chains

        // Which chain each sprocket is on (and its wrap), and each chain's sprockets, found when the rotors are.
        private static readonly Dictionary<GameObject, List<(ChainLoop chain, int wrap)>> chainOf = new();
        private static readonly Dictionary<ChainLoop, List<(GameObject part, int wrap)>> sprocketsOf = new();

        private static void FindChains() {
            chainOf.Clear();
            sprocketsOf.Clear();
            foreach (var chain in ChainLoop.All)
                if (chain != null && !chain.gameObject.IsDeleted() && chain.Path != null && chain.Path.Valid) {
                    var found = chain.Sprockets();
                    sprocketsOf[chain] = found;
                    foreach (var (part, wrap) in found) {
                        if (!chainOf.TryGetValue(part, out var list)) chainOf[part] = list = new List<(ChainLoop, int)>();
                        list.Add((chain, wrap));
                    }
                }
        }

        // ------------------------------------------------------------------ systems

        /// <summary>The rotors joined to <paramref name="first"/> by meshing gears, with their ratios.</summary>
        public static DriveSystem SystemOf(Rotor first, List<Rotor> all) {
            var system = new DriveSystem();
            if (first == null) return system;
            system.rotors.Add(first);
            system.ratio[first] = 1f;
            // Which rotor each gear or sprocket turns with.
            var rotorOf = new Dictionary<GameObject, Rotor>();
            foreach (var r in all)
                foreach (var g in r.gears) rotorOf[g.part] = r;
            var queue = new Queue<Rotor>();
            queue.Enqueue(first);
            while (queue.Count > 0) {
                var rotor = queue.Dequeue();
                // Chains: every sprocket on a chain turns the same way round it (one wrapped from inside the other way),
                // at its pitch radius' ratio.
                foreach (var gear in rotor.gears) {
                    if (!gear.sprocket || !chainOf.TryGetValue(gear.part, out var onChains)) continue;
                    foreach (var (chain, wrap) in onChains) {
                        var n = chain.Normal;
                        var mine = chain.wraps[wrap];
                        var sa = Mathf.Sign(Vector3.Dot(rotor.axis, n));
                        if (!system.chains.Any(c => c.chain == chain))
                            system.chains.Add((chain, rotor, sa * chain.Rolling(wrap) / mine.way));
                        if (!sprocketsOf.TryGetValue(chain, out var onChain)) continue;
                        foreach (var (part, otherWrap) in onChain) {
                            if (otherWrap == wrap) continue;
                            if (!rotorOf.TryGetValue(part, out var other) || other == rotor) continue;
                            var theirs = chain.wraps[otherWrap];
                            var sb = Mathf.Sign(Vector3.Dot(other.axis, n));
                            // A chain moves one link per tooth: the ratio is the teeth's.
                            var ratio = system.ratio[rotor] * sa * (theirs.way * chain.Rolling(wrap)) / (mine.way * chain.Rolling(otherWrap)) * sb;
                            if (system.ratio.TryGetValue(other, out var known)) {
                                if (Mathf.Abs(known - ratio) > 0.01f * Mathf.Abs(known) && !system.jammed) {
                                    system.jammed = true;
                                    system.jamReason = "the chain and gears drive a shaft two ways at once";
                                }
                                continue;
                            }
                            system.ratio[other] = ratio;
                            system.rotors.Add(other);
                            queue.Enqueue(other);
                        }
                    }
                }
                foreach (var gear in rotor.gears)
                    foreach (var other in all) {
                        if (other == rotor) continue;
                        foreach (var otherGear in other.gears) {
                            if (!Meshes(gear, otherGear)) continue;
                            if (!system.meshes.Any(m => (m.a == otherGear && m.b == gear) || (m.a == gear && m.b == otherGear)))
                                system.meshes.Add((gear, otherGear));
                            var same = Mathf.Sign(Vector3.Dot(rotor.axis, other.axis));
                            var ratio = -system.ratio[rotor] * gear.teeth / otherGear.teeth * same;
                            if (system.ratio.TryGetValue(other, out var known)) {
                                if (Mathf.Abs(known - ratio) > 0.01f * Mathf.Abs(known) && !system.jammed) {
                                    system.jammed = true;
                                    system.jamReason = "the gears go round in a loop that can't turn";
                                }
                                continue;
                            }
                            system.ratio[other] = ratio;
                            system.rotors.Add(other);
                            queue.Enqueue(other);
                        }
                    }
            }
            var stuck = system.rotors.FirstOrDefault(r => r.fixedToFrame);
            if (stuck != null && !system.jammed) {
                system.jammed = true;
                system.jamReason = "what's locked on a shaft is also fastened to the frame it turns in";
            }
            // A part turning with two rotors at different speeds can't.
            var owner = new Dictionary<GameObject, Rotor>();
            foreach (var rotor in system.rotors)
                foreach (var part in rotor.parts) {
                    if (owner.TryGetValue(part, out var other) && Mathf.Abs(system.ratio[other] - system.ratio[rotor]) > 0.001f && !system.jammed) {
                        system.jammed = true;
                        system.jamReason = Interface.PartLabels.Short(part) + " is fastened to two shafts that turn differently";
                    }
                    owner[part] = rotor;
                }
            return system;
        }

        /// <summary>The system a motor drives (null when no shaft is in it).</summary>
        public static DriveSystem SystemOf(MotorDrive motor, List<Rotor> all = null) {
            all ??= Current();
            var rotor = all.FirstOrDefault(r => r.motors.Contains(motor));
            return rotor == null ? null : SystemOf(rotor, all);
        }
    }
}
