using System.Collections.Generic;
using System.Linq;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.Devices {
    public enum DeviceKind {
        Motor, Distance, Optical, Rotation, Inertial, Vision, Gps, Radio, Expander, AiVision,
        Light, LineTracker, Potentiometer, Bumper, Limit, Encoder, Ultrasonic, Accelerometer, Gyro, Solenoid,
    }

    // V2: the robot's V5 Brain as robot code (and the Devices panel) sees it: every motor, sensor and solenoid in the
    // build on its port. A device wired to the brain (a cable from it to a brain port) is on that port; the others
    // take the free ports in order, motors first, as if they were plugged in (wiring is only for looks). Smart ports
    // are 1-21; 3-wire ports A-H are 1-8 (an encoder or a range finder takes two, an accelerometer three). Readings
    // are worked out from the build (or the simulation while it runs): see each device. This is also the API robot
    // code will use (`V5Brain.Get<DistanceSensor>(3).Distance`).
    public static class V5Brain {
        private static readonly List<V5Device> devices = new();
        private static long seenVersion = -1;
        private static int seenWires = -1, seenParts = -1;
        private static System.Func<GameObject, int> seenRobotOf;

        /// <summary>V2 (Phase 9): online, several robots are on the field: this computer's Brain is only its own
        /// robot's (the others' devices belong to their players' Brains).</summary>
        private static bool Mine(GameObject part) => Sandbox.RobotOf == null || part == null || Sandbox.RobotOf(part) == Sandbox.LocalRobot;
        private static bool wasRunning;

        /// <summary>Every device, smart ones by port, then the 3-wire ones by port.</summary>
        public static IReadOnlyList<V5Device> Devices {
            get {
                Refresh();
                return devices;
            }
        }

        /// <summary>Set while something reads the sensors (the Devices panel, robot code): raycast sensors only work then.</summary>
        public static float WantedUntil;
        public static bool Wanted => Time.unscaledTime < WantedUntil;
        public static void Want(float seconds = 1f) => WantedUntil = Mathf.Max(WantedUntil, Time.unscaledTime + seconds);

        /// <summary>The simulated battery, 0..1 (drains with the motors' current while simulating).</summary>
        public static float Battery { get; internal set; } = 1f;

        /// <summary>The device on a port (3-wire on an expander: expander x 100 + letter, as `V5Device.CodePort`).</summary>
        public static T Get<T>(int port, bool threeWire = false) where T : V5Device =>
            Devices.OfType<T>().FirstOrDefault(d => d.threeWire == threeWire && On(d, port));

        /// <summary>V2: whether a device takes this port (an encoder, range finder or accelerometer takes several).</summary>
        public static bool On(V5Device d, int port) {
            var expander = d.threeWire ? port / 100 : 0;
            var letter = d.threeWire ? port % 100 : port;
            return d.expander == expander && letter >= d.port && letter < d.port + d.ports;
        }

        public static V5Device Of(GameObject part) => part == null ? null : Devices.FirstOrDefault(d => d.part == part || (d is SolenoidDevice s && s.cylinders.Any(c => c != null && c.gameObject == part)));

        public static int SmartPortOf(GameObject part) {
            var device = Of(part);
            return device != null && !device.threeWire ? device.port : 0;
        }

        // ------------------------------------------------------------------ what's in the build

        public static string IdOf(GameObject part) => part != null && part.TryGetComponent(out SavedObject saved) ? saved.id ?? "" : "";

        /// <summary>What a part is as a V5 device, or null.</summary>
        public static DeviceKind? KindOf(GameObject part) {
            var id = IdOf(part);
            if (id.StartsWith("MOTR")) return DeviceKind.Motor;
            if (id.StartsWith("RDIO")) return DeviceKind.Radio;
            if (id.StartsWith("SNSR-")) {
                var name = id.Substring(5);
                var v5 = name.EndsWith("-V5");
                if (name.StartsWith("Distance")) return v5 ? DeviceKind.Distance : DeviceKind.Ultrasonic;
                if (name.StartsWith("Rotation")) return v5 ? DeviceKind.Rotation : DeviceKind.Encoder;
                if (name.StartsWith("Optical")) return v5 ? DeviceKind.Optical : DeviceKind.Light;
                if (name.StartsWith("Vision")) return DeviceKind.Vision;
                if (name.StartsWith("Inertial")) return DeviceKind.Inertial;
                if (name.StartsWith("Line Tracker")) return DeviceKind.LineTracker;
                if (name.StartsWith("Potentiometer")) return DeviceKind.Potentiometer;
                return null;
            }
            if (id.StartsWith("SWCH-")) return id.Contains("Limit") ? DeviceKind.Limit : DeviceKind.Bumper;
            if (id.StartsWith("VXSENSORS-") || id.StartsWith("VXV5ELECTRONICS-")) {
                if (id.Contains("3 Wire Expander")) return DeviceKind.Expander;
                if (id.Contains("GPS")) return DeviceKind.Gps;
                if (id.Contains("Light Sensor")) return DeviceKind.Light;
                if (id.Contains("Line Tracker")) return DeviceKind.LineTracker;
                if (id.Contains("Optical Shaft Encoder")) return DeviceKind.Encoder;
                if (id.Contains("Ultrasonic")) return DeviceKind.Ultrasonic;
                if (id.Contains("Potentiometer")) return DeviceKind.Potentiometer;
                if (id.Contains("Accelerometer")) return DeviceKind.Accelerometer;
                if (id.Contains("Gyro")) return DeviceKind.Gyro;
                if (id.Contains("Distance")) return DeviceKind.Distance;
                if (id.Contains("Optical Sensor")) return DeviceKind.Optical;
                if (id.Contains("Rotation")) return DeviceKind.Rotation;
                if (id.Contains("Inertial")) return DeviceKind.Inertial;
                if (id.Contains("AI Vision")) return DeviceKind.AiVision;   // V2
                if (id.Contains("Vision")) return DeviceKind.Vision;
                return null;
            }
            if (id.StartsWith("VXPNEUMATICS") && id.Contains("Solenoid")) return DeviceKind.Solenoid;
            return null;
        }

        public static bool IsThreeWire(DeviceKind kind) => kind >= DeviceKind.Light;

        /// <summary>A sensor a shaft turns in (it measures the shaft: it doesn't turn with it).</summary>
        public static bool MeasuresShaft(GameObject part) {
            var kind = KindOf(part);
            return kind == DeviceKind.Rotation || kind == DeviceKind.Encoder || kind == DeviceKind.Potentiometer;
        }

        private static V5Device Make(DeviceKind kind, GameObject part) => kind switch {
            DeviceKind.Motor => new MotorDevice(part),
            DeviceKind.Distance => new DistanceSensor(part),
            DeviceKind.Ultrasonic => new UltrasonicSensor(part),
            DeviceKind.Optical => new OpticalSensor(part),
            DeviceKind.Light => new LightSensor(part),
            DeviceKind.LineTracker => new LineTracker(part),
            DeviceKind.Rotation or DeviceKind.Encoder or DeviceKind.Potentiometer => new ShaftSensor(part, kind),
            DeviceKind.Inertial => new InertialSensor(part),
            DeviceKind.Accelerometer => new Accelerometer(part),
            DeviceKind.Gyro => new YawGyro(part),
            DeviceKind.Bumper or DeviceKind.Limit => new SwitchSensor(part, kind),
            DeviceKind.Vision => new VisionSensor(part),   // V2
            DeviceKind.Gps => new GpsSensor(part),         // V2
            DeviceKind.AiVision => new AiVisionSensor(part),   // V2
            _ => new PlainDevice(part, kind),
        };

        private static int CountParts() => SavedObject.Changes;

        /// <summary>Finds the devices again when the build changed.</summary>
        public static void Refresh(bool force = false) {
            var wires = Wire.Wires.Sum(w => w != null ? w.nodes.Count + (w.gameObject.IsDeleted() ? 1000 : 0) : 0);
            if (!force && seenVersion == Dynamics.Version && seenWires == wires && seenParts == CountParts() && seenRobotOf == Sandbox.RobotOf) return;
            seenRobotOf = Sandbox.RobotOf;
            seenVersion = Dynamics.Version;
            seenWires = wires;
            seenParts = CountParts();

            // (Devices whose parts were destroyed all have key "0:<kind>", e.g. the replay viewer's temporary parts:
            // keep the first; they never match a live part.)
            var old = new Dictionary<string, V5Device>();
            foreach (var d in devices)
                if (!old.ContainsKey(d.Key)) old[d.Key] = d;
            devices.Clear();
            var found = new List<V5Device>();
            var parts = Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.InstanceID)
                        .Where(s => s != null && !s.gameObject.IsDeleted() && s.gameObject.activeInHierarchy && Mine(s.gameObject)).Select(s => s.gameObject).ToList();
            // Motors in the order they were added (their numbers as before), then everything else in build order.
            // (only live ones: a build opened this frame still has the old one's motors, switched off until the frame
            // ends; counted, they took the ports and the new motors' kept ports were overwritten)
            var motors = MotorDrive.Live().Select(m => m.gameObject).Where(m => m.activeInHierarchy && Mine(m)).ToList();
            foreach (var part in motors.Concat(parts.Where(p => !motors.Contains(p)))) {
                var kind = KindOf(part);
                if (kind == null || kind == DeviceKind.Solenoid) continue;
                var key = part.GetInstanceID() + ":" + kind;
                found.Add(old.TryGetValue(key, out var same) ? same : Make(kind.Value, part));
            }
            found.AddRange(Solenoids(parts, old));

            // Ports: wired ones first, then the free ones in order.
            var smartUsed = new HashSet<int>();
            var wireUsed = new HashSet<int>();
            foreach (var d in found) {
                d.port = 0;
                d.expander = 0;
                d.wired = false;
                var port = d.part != null ? WirePorts.BrainPortOf(d.part) : null;
                if (port == null) continue;
                if (port.kind == WireKind.Smart && !d.threeWire && smartUsed.Add(port.number)) {
                    d.port = port.number;
                    d.wired = true;
                }
                else if (port.kind == WireKind.ThreeWire && d.threeWire) {
                    var first = d.ports == 2 ? port.number - (port.number + 1) % 2 : port.number;   // pairs start on A, C, E, G
                    if (Enumerable.Range(first, d.ports).All(p => p <= 8 && !wireUsed.Contains(p))) {
                        for (var p = first; p < first + d.ports; p++) wireUsed.Add(p);
                        d.port = first;
                        d.wired = true;
                    }
                }
            }
            // V2: a device keeps the port it was given (saved with the part: "port:"), as a plugged-in device stays in
            // its port. Free ports went by the order the parts were made, which isn't the same after a build is opened
            // again: a saved program's Inertial and Rotation sensors swapped ports.
            int? Kept(V5Device d) => d.part != null && int.TryParse(Dynamics.Token(d.part, "port"), out var kept) ? kept : null;
            foreach (var d in found.Where(d => !d.threeWire && d.port == 0))
                if (Kept(d) is int k && k >= 1 && k <= 21 && smartUsed.Add(k)) d.port = k;
            var nextSmart = 1;
            foreach (var d in found.Where(d => !d.threeWire && d.port == 0)) {
                while (smartUsed.Contains(nextSmart)) nextSmart++;
                d.port = nextSmart;
                smartUsed.Add(nextSmart);
            }
            // V2: 3-wire devices that don't fit on the brain's A-H go on the 3-Wire Expanders (in port order), A-H on each.
            var expanders = found.Where(d => d.kind == DeviceKind.Expander).Select(d => d.port).OrderBy(p => p).ToList();
            var expanderUsed = expanders.ToDictionary(e => e, _ => new HashSet<int>());
            static int Fit(V5Device d, HashSet<int> used, int limit) {
                for (var p = 1; p + d.ports - 1 <= limit; p++) {
                    if (d.ports == 2 && p % 2 == 0) continue;   // pairs start on A, C, E, G
                    if (Enumerable.Range(p, d.ports).All(q => !used.Contains(q))) return p;
                }
                return 0;
            }
            foreach (var d in found.Where(d => d.threeWire && d.port == 0)) {   // V2: kept 3-wire ports (expander x 100 + letter)
                if (!(Kept(d) is int k) || k % 100 < 1 || k % 100 + d.ports - 1 > 8) continue;
                var e = k / 100;
                var used = e == 0 ? wireUsed : expanderUsed.TryGetValue(e, out var u) ? u : null;
                if (used == null || Enumerable.Range(k % 100, d.ports).Any(q => used.Contains(q))) continue;
                d.port = k % 100;
                d.expander = e;
                for (var q = d.port; q < d.port + d.ports; q++) used.Add(q);
            }
            foreach (var d in found.Where(d => d.threeWire && d.port == 0)) {
                d.expander = 0;
                var p = Fit(d, wireUsed, 8);
                foreach (var e in expanders) {
                    if (p != 0) break;
                    p = Fit(d, expanderUsed[e], 8);
                    if (p != 0) {
                        d.expander = e;
                        for (var q = p; q < p + d.ports; q++) expanderUsed[e].Add(q);
                    }
                }
                if (p == 0) p = Fit(d, wireUsed, 99);   // no room: past H (not on the brain)
                d.port = p;
                if (d.expander == 0)
                    for (var q = p; q < p + d.ports; q++) wireUsed.Add(q);
            }
            foreach (var d in found)   // V2: remembered for next time
                if (d.part != null && d.port > 0 && d.OnBrain && Kept(d) != d.CodePort) Dynamics.SetToken(d.part, "port", d.CodePort.ToString());
            devices.AddRange(found.Where(d => !d.threeWire).OrderBy(d => d.port));
            devices.AddRange(found.Where(d => d.threeWire).OrderBy(d => d.port));
            Loop.Ensure();
        }

        // ------------------------------------------------------------------ V2 (build 74): choosing a port

        /// <summary>The ports a device can be put on, as (code port, label): smart 1-21; 3-wire A-H on the brain and on
        /// each 3-Wire Expander (pairs start on A, C, E, G). The label says who's on it now.</summary>
        public static List<(int port, string label)> PortChoices(V5Device d) {
            var list = new List<(int, string)>();
            string Who(int code) {
                var on = Devices.FirstOrDefault(o => o != d && o.threeWire == d.threeWire && Covers(o, code, d.ports));
                return on == null ? "" : "  ·  " + on.Name + (on.wired ? " (cable)" : "");
            }
            if (!d.threeWire) {
                for (var p = 1; p <= 21; p++) list.Add((p, "Port " + p + Who(p)));
                return list;
            }
            var places = new List<int> { 0 };
            places.AddRange(Devices.Where(o => o.kind == DeviceKind.Expander).Select(o => o.port).OrderBy(p => p));
            foreach (var e in places)
                for (var p = 1; p + d.ports - 1 <= 8; p++) {
                    if (d.ports == 2 && p % 2 == 0) continue;
                    var name = d.ports > 1 ? (char)('A' + p - 1) + "-" + (char)('A' + p + d.ports - 2) : ((char)('A' + p - 1)).ToString();
                    var code = e * 100 + p;
                    list.Add((code, (e > 0 ? "Expander " + e + " " : "") + name + Who(code)));
                }
            return list;
        }

        // Whether a device takes any of `size` ports from code port `code` (3-wire: expander x 100 + letter).
        private static bool Covers(V5Device o, int code, int size) {
            if (!o.threeWire) return o.port == code;
            if (o.expander != code / 100) return false;
            var start = code % 100;
            return o.port < start + size && start < o.port + o.ports;
        }

        /// <summary>Puts a device on a port (kept with its part, as a plugged-in device stays in its port). A device on
        /// the port already is moved to this one's old port (same size) or to the next free one. Null when it worked,
        /// else why not (a Smart Cable from an older build decides its devices' ports).</summary>
        public static string SetPort(V5Device d, int code) {
            if (d?.part == null) return "This one can't be moved to another port";
            if (d.wired) return "It's on a cable to port " + d.PortLabel + ": delete the cable to choose its port here";
            if (code == d.CodePort) return null;
            var others = Devices.Where(o => o != d && o.threeWire == d.threeWire && Covers(o, code, d.ports)).ToList();
            var cabled = others.FirstOrDefault(o => o.wired);
            if (cabled != null) return (d.threeWire ? "That port" : "Port " + code) + " has a cable to " + cabled.Name + ": delete the cable first";
            var old = d.CodePort;
            foreach (var o in others) {
                if (o.part == null) continue;
                // The same size: they swap. Otherwise it finds a free port again.
                Dynamics.SetToken(o.part, "port", o.ports == d.ports && d.OnBrain ? old.ToString() : null);
            }
            Dynamics.SetToken(d.part, "port", code.ToString());
            Refresh(true);
            return null;
        }

        // Solenoids: each valve part, with the cylinders its tubing reaches (through fittings and tees); a cylinder no
        // valve reaches gets one of its own (as if it had a valve), so every cylinder is on a port.
        private static IEnumerable<V5Device> Solenoids(List<GameObject> parts, Dictionary<string, V5Device> old) {
            var cylinders = Controls.Cylinders();
            var reached = new HashSet<PneumaticCylinder>();
            var tubes = Object.FindObjectsByType<PneumaticTube>(FindObjectsSortMode.None).Where(t => t != null && !(t is Wire) && !t.gameObject.IsDeleted()).ToList();
            var result = new List<V5Device>();
            foreach (var valve in parts.Where(p => KindOf(p) == DeviceKind.Solenoid)) {
                var found = Reach(valve, tubes);
                foreach (var c in found) reached.Add(c);
                var key = valve.GetInstanceID() + ":" + DeviceKind.Solenoid;
                var device = old.TryGetValue(key, out var same) ? (SolenoidDevice)same : new SolenoidDevice(valve);
                device.cylinders.Clear();
                device.cylinders.AddRange(found);
                result.Add(device);
            }
            foreach (var c in cylinders.Where(c => !reached.Contains(c) && Mine(c.gameObject))) {
                var key = c.gameObject.GetInstanceID() + ":cylinder";
                var device = old.TryGetValue(key, out var same) ? (SolenoidDevice)same : new SolenoidDevice(null) { own = c };
                device.cylinders.Clear();
                device.cylinders.Add(c);
                result.Add(device);
            }
            return result;
        }

        private static List<PneumaticCylinder> Reach(GameObject valve, List<PneumaticTube> tubes) {
            var seen = new HashSet<GameObject> { valve };
            var queue = new Queue<GameObject>();
            queue.Enqueue(valve);
            var cylinders = new List<PneumaticCylinder>();
            while (queue.Count > 0) {
                var at = queue.Dequeue();
                var next = new List<GameObject>();
                // Tubing from here (its ends on this part, or on a fitting screwed into it).
                var here = new HashSet<GameObject> { at };
                foreach (var n in GroupManager.Neighbours(at)) if (IsFitting(n)) here.Add(n);
                foreach (var tube in tubes) {
                    if (tube.nodes.Count < 2) continue;
                    var a = tube.nodes[0].part;
                    var b = tube.nodes[^1].part;
                    if (a != null && here.Contains(a) && b != null) next.Add(b);
                    if (b != null && here.Contains(b) && a != null) next.Add(a);
                }
                foreach (var part in next) {
                    // A fitting: what it's screwed into.
                    var targets = new List<GameObject> { part };
                    if (IsFitting(part)) targets.AddRange(GroupManager.Neighbours(part));
                    foreach (var t in targets) {
                        if (t == null || !seen.Add(t)) continue;
                        if (t.TryGetComponent(out PneumaticCylinder cylinder)) {
                            if (!cylinders.Contains(cylinder)) cylinders.Add(cylinder);
                            continue;
                        }
                        var id = IdOf(t);
                        if (KindOf(t) == DeviceKind.Solenoid || id.Contains("Reservoir") || id.Contains("Regulator")) continue;   // air comes from there
                        if (IsFitting(t) || id.Contains("Tee") || id.Contains("T Fitting") || id.Contains("Manifold") || id.Contains("Flow")) queue.Enqueue(t);
                    }
                }
            }
            return cylinders;
        }

        private static bool IsFitting(GameObject part) {
            var id = IdOf(part);
            return id.StartsWith("VXPNEUMATICS") && (id.Contains("Fitting") || id.Contains("Tee") || id.Contains("Plug") || id.Contains("Valve Fitting"));
        }

        // ------------------------------------------------------------------ the readings loop

        internal static void Tick(float dt, bool fixedStep) {
            Refresh();
            if (Sandbox.Running != wasRunning) {
                wasRunning = Sandbox.Running;
                Battery = 1f;
                foreach (var d in devices) d.PowerOn();
            }
            var wanted = Wanted;
            foreach (var d in devices) {
                if (d.part == null && !(d is SolenoidDevice)) continue;
                if (d.FastSample != fixedStep && Sandbox.Running) continue;
                if (!d.AlwaysSample && !wanted) continue;
                try { d.Sample(dt); }
                catch (System.Exception e) {
                    if (!d.failed) Debug.LogException(e);
                    d.failed = true;
                }
            }
            if (Sandbox.Running && !fixedStep) {
                // 1100 mAh; the brain itself about 0.3 A.
                var amps = 0.3f + devices.OfType<MotorDevice>().Sum(m => m.Current);
                Battery = Mathf.Max(0f, Battery - amps * dt / 3600f / 1.1f);
            }
        }

        private class Loop : MonoBehaviour {
            private static Loop host;

            // V2: only ever one. Copies piled up in the editor (one made in edit mode was saved into the play scene, and
            // a script reload forgets `host`): with five of them every device was sampled five times a step, so a
            // Rotation sensor's velocity read a fifth of its speed and the battery drained five times as fast.
            public static void Ensure() {
                if (host != null) return;
                host = FindAnyObjectByType<Loop>();
                if (host != null) return;
                host = new GameObject("V5 Brain").AddComponent<Loop>();
                host.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            private bool Duplicate() {
                if (host == null) host = this;
                if (host == this) return false;
                Destroy(gameObject);
                return true;
            }

            private void Update() {
                if (Duplicate()) return;
                Tick(Time.deltaTime, false);
            }

            private void FixedUpdate() {
                if (Duplicate()) return;
                if (Sandbox.Running) Tick(Time.fixedDeltaTime, true);
            }
        }
    }
}
