using System.Collections.Generic;
using System.Linq;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.Devices {
    public enum DeviceKind {
        Motor, Distance, Optical, Rotation, Inertial, Vision, Gps, Radio, Expander,
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

        public static T Get<T>(int port, bool threeWire = false) where T : V5Device =>
            Devices.OfType<T>().FirstOrDefault(d => d.threeWire == threeWire && port >= d.port && port < d.port + d.ports);

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
            var motors = MotorDrive.Live().Select(m => m.gameObject).Where(Mine).ToList();
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
            var nextSmart = 1;
            foreach (var d in found.Where(d => !d.threeWire && d.port == 0)) {
                while (smartUsed.Contains(nextSmart)) nextSmart++;
                d.port = nextSmart;
                smartUsed.Add(nextSmart);
            }
            foreach (var d in found.Where(d => d.threeWire && d.port == 0)) {
                var p = 1;
                while (true) {
                    if (d.ports == 2 && p % 2 == 0) p++;
                    if (Enumerable.Range(p, d.ports).All(q => !wireUsed.Contains(q))) break;
                    p++;
                }
                d.port = p;
                for (var q = p; q < p + d.ports; q++) wireUsed.Add(q);
            }
            devices.AddRange(found.Where(d => !d.threeWire).OrderBy(d => d.port));
            devices.AddRange(found.Where(d => d.threeWire).OrderBy(d => d.port));
            Loop.Ensure();
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

            public static void Ensure() {
                if (host != null) return;
                host = new GameObject("V5 Brain").AddComponent<Loop>();
                host.gameObject.hideFlags = HideFlags.HideInHierarchy;
            }

            private void Update() => Tick(Time.deltaTime, false);

            private void FixedUpdate() {
                if (Sandbox.Running) Tick(Time.fixedDeltaTime, true);
            }
        }
    }
}
