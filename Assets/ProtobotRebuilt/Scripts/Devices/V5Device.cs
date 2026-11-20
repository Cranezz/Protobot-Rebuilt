using System.Collections.Generic;
using System.Linq;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.Devices {
    /// <summary>V2: a device on the V5 Brain (a motor, a sensor, a solenoid): its port and its readings.</summary>
    public abstract class V5Device {
        public readonly GameObject part;
        public readonly DeviceKind kind;
        public int port;          // smart 1-21 (more: not on the brain), 3-wire 1-8 = A-H
        public int ports = 1;     // 3-wire ports it takes
        public bool wired;        // a cable goes from it to this port
        public bool threeWire;
        internal bool failed;

        protected V5Device(GameObject part, DeviceKind kind) {
            this.part = part;
            this.kind = kind;
            threeWire = V5Brain.IsThreeWire(kind);
        }

        public virtual string Key => (part != null ? part.GetInstanceID() : 0) + ":" + kind;

        /// <summary>Sampled every physics step while simulating (shaft angles: fast shafts), else every frame.</summary>
        public virtual bool FastSample => false;
        /// <summary>Sampled even when nothing reads it (its count must not miss a turn).</summary>
        public virtual bool AlwaysSample => false;

        public virtual void Sample(float dt) { }

        /// <summary>The robot was switched on (the simulation started or stopped): counters start again.</summary>
        public virtual void PowerOn() { }

        public static string KindName(DeviceKind kind) => kind switch {
            DeviceKind.Motor => "Motor",
            DeviceKind.Distance => "Distance",
            DeviceKind.Optical => "Optical",
            DeviceKind.Rotation => "Rotation",
            DeviceKind.Inertial => "Inertial",
            DeviceKind.Vision => "Vision",
            DeviceKind.Gps => "GPS",
            DeviceKind.Radio => "Radio",
            DeviceKind.Expander => "3-Wire Expander",
            DeviceKind.Light => "Light Sensor",
            DeviceKind.LineTracker => "Line Tracker",
            DeviceKind.Potentiometer => "Potentiometer",
            DeviceKind.Bumper => "Bumper",
            DeviceKind.Limit => "Limit Switch",
            DeviceKind.Encoder => "Shaft Encoder",
            DeviceKind.Ultrasonic => "Range Finder",
            DeviceKind.Accelerometer => "Accelerometer",
            DeviceKind.Gyro => "Gyro",
            DeviceKind.Solenoid => "Solenoid",
            _ => kind.ToString(),
        };

        /// <summary>Two or three letters for the port grid ("MTR").</summary>
        public static string Short(DeviceKind kind) => kind switch {
            DeviceKind.Motor => "MTR",
            DeviceKind.Distance => "DST",
            DeviceKind.Optical => "OPT",
            DeviceKind.Rotation => "ROT",
            DeviceKind.Inertial => "IMU",
            DeviceKind.Vision => "VIS",
            DeviceKind.Gps => "GPS",
            DeviceKind.Radio => "RAD",
            DeviceKind.Expander => "3W",
            DeviceKind.Light => "LGT",
            DeviceKind.LineTracker => "LIN",
            DeviceKind.Potentiometer => "POT",
            DeviceKind.Bumper => "BMP",
            DeviceKind.Limit => "LIM",
            DeviceKind.Encoder => "ENC",
            DeviceKind.Ultrasonic => "SON",
            DeviceKind.Accelerometer => "ACC",
            DeviceKind.Gyro => "GYR",
            DeviceKind.Solenoid => "SOL",
            _ => "?",
        };

        public virtual string Name => (part != null ? Controls.GivenName(part) : null) ?? KindName(kind);

        public string PortLabel => threeWire
            ? ports > 1 ? (char)('A' + port - 1) + "-" + (char)('A' + port + ports - 2) : ((char)('A' + port - 1)).ToString()
            : port.ToString();

        public bool OnBrain => threeWire ? port + ports - 1 <= 8 : port <= 21;

        /// <summary>The short reading shown when its card is folded ("677 mm").</summary>
        public virtual string Reading => "";

        /// <summary>The 3-wire port's mode and value as the Brain's 3-Wire screen shows them.</summary>
        public virtual (string mode, string value) PortState(int index) => ("", "");

        /// <summary>Its color in the panel (a motor: its cartridge).</summary>
        public virtual Color Tint => new(0.55f, 0.6f, 0.68f);
    }

    /// <summary>Where a build part is now: in the build, or carried by its body while the simulation runs.</summary>
    public static class Pose {
        public static Vector3 Point(GameObject part, Vector3 local) {
            var world = part.transform.TransformPoint(local);
            return Sandbox.Running ? Sandbox.SimPoint(part, world) : world;
        }

        public static Vector3 Dir(GameObject part, Vector3 local) {
            var world = part.transform.TransformDirection(local);
            return (Sandbox.Running ? Sandbox.SimDirection(part, world) : world).normalized;
        }

        public static Quaternion Rotation(GameObject part) => Quaternion.LookRotation(Dir(part, Vector3.forward), Dir(part, Vector3.up));

        /// <summary>What sensors see: the build (and a game's field and pieces), or the simulation's bodies.</summary>
        public static int Mask => Sandbox.Running ? (1 << 16) | (1 << 17) | (1 << 18) | (1 << 19)
            : LayerMask.GetMask("Default") | (1 << 18) | (1 << 19);

        /// <summary>The floor's color as a sensor sees it (the field's gray foam tiles).</summary>
        public static readonly Color FloorColor = new(0.17f, 0.17f, 0.18f);

        /// <summary>What a sensor's ray met: a collider, or (in the build) the floor the robot stands on.</summary>
        public struct Seen {
            public float distance;
            public Vector3 point;
            public Collider collider;
            public RaycastHit raw;
            public bool floor;
            public Vector3 Size => floor || collider == null ? new Vector3(100f, 0f, 100f) : collider.bounds.size;
        }

        /// <summary>The nearest thing along a ray that isn't the sensor itself (in the build, the floor at the grid
        /// counts too: a real robot stands on one).</summary>
        public static bool Cast(Vector3 origin, Vector3 direction, float range, GameObject self, out Seen seen) {
            seen = default;
            var hits = Physics.RaycastAll(origin, direction, range, Mask, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            var found = false;
            foreach (var h in hits) {
                if (h.distance >= best) continue;
                if (!Sandbox.Running && self != null && h.collider.transform.IsChildOf(self.transform)) continue;
                if (!Sandbox.Running && h.collider.GetComponentInParent<SavedObject>() is SavedObject saved && saved.gameObject.IsDeleted()) continue;
                best = h.distance;
                seen = new Seen { distance = h.distance, point = h.point, collider = h.collider, raw = h };
                found = true;
            }
            if (!Sandbox.Running && direction.y < -1e-4f && origin.y > 0f) {
                var t = -origin.y / direction.y;
                if (t < range && t < best) {
                    seen = new Seen { distance = t, point = origin + direction * t, floor = true };
                    found = true;
                }
            }
            return found;
        }

        /// <summary>The color of what a ray met (the material under it), as the eye sees it in the app.</summary>
        public static Color ColorAt(Seen seen) {
            if (seen.floor) return FloorColor;
            var hit = seen.raw;
            var collider = hit.collider;
            if (collider == null) return Color.gray;
            // A build part's own mesh: the sub-mesh of the triangle hit.
            if (collider is MeshCollider mc && !mc.convex && hit.triangleIndex >= 0 && collider.TryGetComponent(out MeshRenderer own) && mc.sharedMesh != null) {
                var mesh = mc.sharedMesh;
                var first = hit.triangleIndex * 3;
                for (var s = 0; s < mesh.subMeshCount && s < own.sharedMaterials.Length; s++) {
                    var d = mesh.GetSubMesh(s);
                    if (first >= d.indexStart && first < d.indexStart + d.indexCount) return MaterialColor(own.sharedMaterials[s]);
                }
                return MaterialColor(own.sharedMaterial);
            }
            // Else the renderer under the point: on the collider, or the smallest one round the point in its body.
            if (collider.TryGetComponent(out Renderer r) && r.enabled) return MaterialColor(r.sharedMaterial);
            if (collider.gameObject.layer == 17) return FloorColor;
            var root = collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : collider.transform.parent != null ? collider.transform.parent : collider.transform;
            Renderer best = null;
            var size = float.MaxValue;
            foreach (var candidate in root.GetComponentsInChildren<Renderer>()) {
                if (!candidate.enabled) continue;
                var b = candidate.bounds;
                b.Expand(0.1f);
                if (!b.Contains(hit.point)) continue;
                var v = b.size.x * b.size.y * b.size.z;
                if (v < size) {
                    size = v;
                    best = candidate;
                }
            }
            return best != null ? MaterialColor(best.sharedMaterial) : new Color(0.3f, 0.3f, 0.32f);
        }

        public static Color MaterialColor(Material m) {
            if (m == null) return Color.gray;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            return m.HasProperty("_Color") ? m.color : Color.gray;
        }

        /// <summary>Whether anything but the sensor (and its own body while simulating) is in a box.</summary>
        public static bool Touching(GameObject self, Vector3 center, Vector3 halfSize, Quaternion rotation) {
            var own = Sandbox.Running ? Sandbox.BodyOf(self) : null;
            foreach (var c in Physics.OverlapBox(center, halfSize, rotation, Mask, QueryTriggerInteraction.Ignore)) {
                if (Sandbox.Running) {
                    if (own != null && c.attachedRigidbody == own) continue;
                }
                else if (c.transform.IsChildOf(self.transform)) continue;
                else if (c.GetComponentInParent<SavedObject>() is SavedObject saved && saved.gameObject.IsDeleted()) continue;
                return true;
            }
            return false;
        }

        /// <summary>A small steady wobble, like a real sensor's (seeded per device).</summary>
        public static float Noise(int seed, float amount) =>
            amount * (Mathf.PerlinNoise(seed * 0.137f, Time.time * 3.1f) - 0.5f) * 2f;
    }

    // ---------------------------------------------------------------------- motors

    /// <summary>A V5 motor: what the Brain's motor dashboard shows (and robot code reads).</summary>
    public class MotorDevice : V5Device {
        public readonly MotorDrive motor;
        public double Degrees;          // the shaft's position since power on (or Zero)
        public float Torque, Power, Current;

        public MotorDevice(GameObject part) : base(part, DeviceKind.Motor) {
            motor = part.GetComponent<MotorDrive>();
        }

        public override bool AlwaysSample => true;
        public override string Name => motor != null ? motor.Label : base.Name;
        public override Color Tint => motor != null && motor.HasCartridge ? motor.Color : new Color(0.55f, 0.6f, 0.68f);

        /// <summary>Velocity in rpm at the shaft (positive: forward).</summary>
        public float Rpm => motor != null ? motor.Level * motor.Rpm : 0f;
        /// <summary>-100..100 % of its free speed.</summary>
        public float Percent => motor != null ? Mathf.Clamp(motor.Level * 100f, -100f, 100f) : 0f;
        public float Revolutions => (float)(Degrees / 360.0);
        public float StallTorque => motor != null ? motor.StallTorque : 1f;
        public float FreeRpm => motor != null ? motor.Rpm : 200f;
        /// <summary>"36:1" / "18:1" / "6:1" (the 5.5W: "5.5W").</summary>
        public string Gearing => motor == null || !motor.HasCartridge ? "5.5W" : motor.Rpm == 100 ? "36:1" : motor.Rpm == 600 ? "6:1" : "18:1";

        public override string Reading => Mathf.RoundToInt(Percent) + "%";

        public void Zero() => Degrees = 0;
        public override void PowerOn() => Degrees = 0;

        public override void Sample(float dt) {
            if (motor == null) return;
            Degrees += motor.Level * motor.Rpm * 6.0 * dt;
            // A DC motor: its torque falls in a line from stall to nothing at free speed (VEX's curve); with no
            // command it brakes. The current limit holds it to stall torque.
            var stall = motor.StallTorque;
            Torque = Mathf.Clamp(stall * (motor.Command - motor.Level), -stall, stall);
            if (Mathf.Abs(motor.Command) < 0.001f && Mathf.Abs(motor.Level) < 0.002f) Torque = 0f;
            Power = Mathf.Abs(Torque * Rpm * Mathf.PI / 30f);
            Current = Mathf.Abs(Torque) / stall * (motor.HasCartridge ? 2.5f : 1.8f);
        }
    }

    /// <summary>A device with no readings in the app yet (GPS, radio, the 3-wire expander).</summary>
    public class PlainDevice : V5Device {
        public PlainDevice(GameObject part, DeviceKind kind) : base(part, kind) { }

        public string Note => kind switch {
            DeviceKind.Radio => "Links the brain to the controller (VEXnet / Bluetooth).",
            DeviceKind.Expander => "Adds eight more 3-wire ports on a smart port.",
            DeviceKind.Gps => "Reads the field's code strip for its position. Not simulated yet.",
            _ => "",
        };
    }

    // ---------------------------------------------------------------------- pneumatics

    /// <summary>A solenoid valve on a 3-wire port (Digital Out): High sends air to extend the cylinders it drives, Low
    /// (the default) to retract them. A cylinder no valve's tubing reaches acts as if it had its own.</summary>
    public class SolenoidDevice : V5Device {
        public readonly List<PneumaticCylinder> cylinders = new();
        public PneumaticCylinder own;   // the cylinder standing in for a valve it doesn't have

        public SolenoidDevice(GameObject valve) : base(valve, DeviceKind.Solenoid) { }

        public override string Key => part != null ? base.Key : (own != null ? own.gameObject.GetInstanceID() : 0) + ":cylinder";
        public override string Name => part != null ? base.Name : own != null ? own.Label : "Solenoid";
        public override Color Tint => new(0.98f, 0.73f, 0.23f);

        public bool High => cylinders.Count > 0 && cylinders.Any(Extended);

        public static bool Extended(PneumaticCylinder c) =>
            c != null && (Sandbox.Running && Sandbox.Instance != null ? Sandbox.Instance.Extended(c) && !Sandbox.Instance.IsLoose(c) : c.Extended);

        public static string Position(PneumaticCylinder c) {
            if (c == null) return "";
            if (Sandbox.Running && Sandbox.Instance != null) {
                if (!Sandbox.Instance.Slides(c)) return "Held still";
                if (Sandbox.Instance.IsLoose(c)) return "Loose (no air)";
                return Sandbox.Instance.Extended(c) ? "Extended" : "Retracted";
            }
            if (c.Moving) return "Moving";
            var p = c.Position;
            return p > 0.995f ? "Extended" : p < 0.005f ? "Retracted" : Mathf.RoundToInt(p * 100f) + "% out";
        }

        public override string Reading => cylinders.Count == 0 ? "no cylinder" : Position(cylinders[0]);

        public override (string mode, string value) PortState(int index) => ("Digital Out", High ? "High (1)" : "Low (0)");

        /// <summary>Switches the valve: High extends, Low retracts.</summary>
        public void Set(bool high) {
            foreach (var c in cylinders) {
                if (c == null) continue;
                if (Sandbox.Running && Sandbox.Instance != null) Sandbox.Instance.SetAir(c, high);
                else if (Extended(c) != high || c.Position > 0.005f && c.Position < 0.995f) c.Set(high);
            }
        }
    }
}
