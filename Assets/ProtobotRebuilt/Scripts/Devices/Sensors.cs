using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.Devices {
    // V2: the sensors, worked out from the build (or the simulation): what each real one measures, its range and
    // its quirks (VEX's figures). Where a sensor looks and where its shaft goes are found on its model: a V5 sensor's
    // dark window, else the face away from its mounting holes; a shaft sensor's square hole.

    /// <summary>Where a sensor looks from, in its part's space.</summary>
    public static class SensorFrame {
        private static readonly Dictionary<Mesh, (Vector3 origin, Vector3 dir)> looks = new();
        private static readonly Dictionary<(Mesh, string), Bounds> pieces = new();

        public static (Vector3 origin, Vector3 dir) Look(GameObject part) {
            if (!part.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return (Vector3.zero, Vector3.up);
            var mesh = filter.sharedMesh;
            if (looks.TryGetValue(mesh, out var found)) return found;
            found = FromHoles(part, mesh);   // (the V5 sensors' dark "screen" piece is also their port's inside: not used)
            looks[mesh] = found;
            return found;
        }

        // The dark lens window (V5 distance and optical: their "Black Screen" piece): its middle, facing out.
        private static (Vector3, Vector3)? FromWindow(GameObject part, Mesh mesh) {
            if (!part.TryGetComponent(out MeshRenderer renderer) || !mesh.isReadable) return null;
            var materials = renderer.sharedMaterials;
            var index = System.Array.FindIndex(materials, m => m != null && m.name.Contains("Screen"));
            if (index < 0 || index >= mesh.subMeshCount) return null;
            var v = mesh.vertices;
            var t = mesh.GetTriangles(index);
            var normal = Vector3.zero;
            var center = Vector3.zero;
            var area = 0f;
            for (var i = 0; i < t.Length; i += 3) {
                var c = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                var a = c.magnitude;
                normal += c;
                center += (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f * a;
                area += a;
            }
            if (area < 1e-6f || normal.sqrMagnitude < 1e-8f) return null;
            center /= area;
            var dir = normal.normalized;
            // Unity winds front faces clockwise: the cross product points into the part; check against the middle.
            if (Vector3.Dot(dir, center - mesh.bounds.center) < 0f) dir = -dir;
            dir = Snap(dir);
            // The window's outer face: as far out along the direction as the mesh goes there.
            var far = mesh.bounds.center + Vector3.Scale(mesh.bounds.extents, new Vector3(Mathf.Sign(dir.x), Mathf.Sign(dir.y), Mathf.Sign(dir.z)));
            var origin = center + dir * Mathf.Max(0f, Vector3.Dot(far - center, dir));
            return (origin, dir);
        }

        // The face away from its mounting holes (along their axis), its middle.
        private static (Vector3, Vector3) FromHoles(GameObject part, Mesh mesh) {
            var bounds = mesh.bounds;
            var holes = part.GetComponentsInChildren<HoleCollider>(true);
            var axis = Vector3.up;
            var at = bounds.center;
            if (holes.Length > 0) {
                var h = holes[0].transform;
                axis = Snap(part.transform.InverseTransformDirection(h.forward));
                at = part.transform.InverseTransformPoint(h.position);
            }
            var axisAbs = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            var max = Vector3.Dot(bounds.max, axisAbs);
            var min = Vector3.Dot(bounds.min, axisAbs);
            var here = Vector3.Dot(at, axisAbs);
            var dir = max - here >= here - min ? axisAbs : -axisAbs;
            var face = Vector3.Dot(dir, Vector3.one) > 0f ? max : min;
            var origin = bounds.center - axisAbs * Vector3.Dot(bounds.center, axisAbs) + axisAbs * face;
            return (origin, dir);
        }

        private static Vector3 Snap(Vector3 v) {
            var a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        /// <summary>The box round a piece of the model with this material (a switch's button or lever).</summary>
        public static Bounds Piece(GameObject part, string material) {
            if (!part.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !part.TryGetComponent(out MeshRenderer renderer)) return default;
            var mesh = filter.sharedMesh;
            if (pieces.TryGetValue((mesh, material), out var found)) return found;
            var index = System.Array.FindIndex(renderer.sharedMaterials, m => m != null && m.name.Contains(material));
            found = mesh.bounds;
            if (index >= 0 && index < mesh.subMeshCount && mesh.isReadable) {
                var v = mesh.vertices;
                var t = mesh.GetTriangles(index);
                if (t.Length > 0) {
                    found = new Bounds(v[t[0]], Vector3.zero);
                    foreach (var i in t) found.Encapsulate(v[i]);
                }
            }
            pieces[(mesh, material)] = found;
            return found;
        }
    }

    // ---------------------------------------------------------------------- distance

    /// <summary>V5 Distance Sensor: a narrow laser beam straight out of its window, 20 to 2000 mm (±15 mm under 200 mm,
    /// 5% over), the object's size (small, medium, large) and how fast it comes closer (m/s).</summary>
    public class DistanceSensor : V5Device {
        public const float MaxMm = 2000f, MinMm = 20f, NoObject = 9999f;
        public float Mm = NoObject;
        public bool Found;
        public string Size = "None";
        public float Velocity;   // m/s, positive coming closer
        private float lastMm = -1f;

        public DistanceSensor(GameObject part) : base(part, DeviceKind.Distance) { }

        public float Inches => Found ? Mm / 25.4f : NoObject;
        public override string Reading => Found ? DevicesFormat.Distance(Mm) : "No object";

        public override void Sample(float dt) {
            var (o, d) = SensorFrame.Look(part);
            var dir = Pose.Dir(part, d);
            var origin = Pose.Point(part, o) + dir * 0.02f;
            if (Pose.Cast(origin, dir, MaxMm / 25.4f, part, out var seen)) {
                var mm = (seen.distance + 0.02f) * 25.4f;
                mm += Pose.Noise(part.GetInstanceID(), mm < 200f ? 6f : mm * 0.015f);
                Found = true;
                Mm = Mathf.Max(0f, mm);
                var size = seen.Size;
                var across = Mathf.Max(Vector3.ProjectOnPlane(size, dir).magnitude, 0.01f);
                Size = across < 3f ? "Small" : across < 9f ? "Medium" : "Large";
                if (lastMm >= 0f && dt > 0f) Velocity = Mathf.Lerp(Velocity, (lastMm - Mm) / 1000f / dt, Mathf.Clamp01(dt * 8f));
                lastMm = Mm;
            }
            else {
                Found = false;
                Mm = NoObject;
                Size = "None";
                Velocity = 0f;
                lastMm = -1f;
            }
        }
    }

    /// <summary>The legacy Ultrasonic Range Finder (two 3-wire ports: output, input): a wide sound cone, 1.5" to 115".</summary>
    public class UltrasonicSensor : V5Device {
        public float Inches = -1f;
        public UltrasonicSensor(GameObject part) : base(part, DeviceKind.Ultrasonic) => ports = 2;

        public override string Reading => Inches < 0f ? "No echo" : DevicesFormat.Inches(Inches);

        public override (string mode, string value) PortState(int index) => index == 0 ? ("Digital Out", "Pulse") : ("Digital In", Inches < 0f ? "No echo" : DevicesFormat.Inches(Inches));

        public override void Sample(float dt) {
            var (o, d) = SensorFrame.Look(part);
            var dir = Pose.Dir(part, d);
            var origin = Pose.Point(part, o) + dir * 0.02f;
            var best = float.MaxValue;
            var side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(side, dir);
            foreach (var (a, b) in new[] { (0f, 0f), (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }) {
                var ray = Quaternion.AngleAxis(a, up) * Quaternion.AngleAxis(b, side) * dir;
                if (Pose.Cast(origin, ray, 115f, part, out var seen)) best = Mathf.Min(best, seen.distance);
            }
            Inches = best < 1.5f ? 1.5f : best <= 115f ? best + Pose.Noise(part.GetInstanceID(), 0.15f) : -1f;
        }
    }

    // ---------------------------------------------------------------------- light

    /// <summary>V5 Optical Sensor: an RGB color sensor and an IR proximity sensor with its own white LED. Hue
    /// 0-359.9°, brightness %, proximity 0-255 (closer = more; it sees about 4"), near object.</summary>
    public class OpticalSensor : V5Device {
        public float Hue, Saturation, Brightness, Proximity;
        public bool Near;
        public Color Seen = Color.black;

        public OpticalSensor(GameObject part) : base(part, DeviceKind.Optical) { }

        public int Led {
            get => int.TryParse(Dynamics.Token(part, "led"), out var v) ? Mathf.Clamp(v, 0, 100) : 0;
            set => Dynamics.SetToken(part, "led", value <= 0 ? null : Mathf.Clamp(value, 0, 100).ToString(CultureInfo.InvariantCulture));
        }

        public string ColorName => !Near ? "None" : DevicesFormat.ColorName(Hue, Saturation, Brightness);
        public override string Reading => Near ? Mathf.RoundToInt(Hue) + "° " + ColorName.ToLowerInvariant() : "Brightness " + Mathf.RoundToInt(Brightness) + "%";

        public override void Sample(float dt) {
            var (o, d) = SensorFrame.Look(part);
            var dir = Pose.Dir(part, d);
            var origin = Pose.Point(part, o) + dir * 0.02f;
            var ambient = 0.18f + 0.22f * Mathf.Max(0f, Vector3.Dot(dir, Vector3.up));
            if (Pose.Cast(origin, dir, 4f, part, out var seen)) {
                var color = Pose.ColorAt(seen);
                // The app's colors are about half their real brightness (its light doubles them).
                var real = new Color(Mathf.Clamp01(color.r * 2f), Mathf.Clamp01(color.g * 2f), Mathf.Clamp01(color.b * 2f));
                Color.RGBToHSV(real, out var h, out var s, out var v);
                var reflect = 0.25f + 0.75f * v;
                var near = Mathf.Clamp01(1f - seen.distance / 4f);
                Proximity = Mathf.Clamp(255f * near * near * reflect + Pose.Noise(part.GetInstanceID(), 2f), 0f, 255f);
                Near = Proximity > 70f;
                // Colors need light on them: its LED close up, else what light reaches the surface.
                var lit = Mathf.Clamp01(ambient * 0.6f + Led / 100f * near * 1.4f + 0.25f * near);
                Brightness = Mathf.Clamp(100f * v * lit, 0f, 100f);
                Hue = h * 360f;
                Saturation = s;
                Seen = real * Mathf.Max(0.25f, lit);
                Seen.a = 1f;
            }
            else {
                Proximity = 0f;
                Near = false;
                Brightness = Mathf.Clamp(100f * ambient * 0.5f + Pose.Noise(part.GetInstanceID(), 0.6f), 0f, 100f);
                Seen = new Color(ambient * 0.4f, ambient * 0.4f, ambient * 0.45f, 1f);
            }
        }
    }

    /// <summary>The legacy Light Sensor (analog): a photocell reading the light reaching it, 0 (bright) to 4095 (dark).</summary>
    public class LightSensor : V5Device {
        public int Value = 4095;
        public LightSensor(GameObject part) : base(part, DeviceKind.Light) { }

        public float Percent => 100f * (1f - Value / 4095f);
        public override string Reading => Mathf.RoundToInt(Percent) + "% light";
        public override (string mode, string value) PortState(int index) => ("Analog In", Value + " (" + Mathf.RoundToInt(Value / 40.95f) + "%)");

        public override void Sample(float dt) {
            var (o, d) = SensorFrame.Look(part);
            var dir = Pose.Dir(part, d);
            var origin = Pose.Point(part, o) + dir * 0.02f;
            // Room light: most from above; whatever is in front of it shades it.
            var light = 0.35f + 0.5f * Mathf.Max(0f, Vector3.Dot(dir, Vector3.up));
            if (Pose.Cast(origin, dir, 6f, part, out var seen)) {
                var c = Pose.ColorAt(seen);
                var surface = Mathf.Clamp01(Mathf.Max(c.r, c.g, c.b) * 2f);
                light *= Mathf.Lerp(0.08f + 0.35f * surface, 1f, seen.distance / 6f);
            }
            Value = Mathf.Clamp(Mathf.RoundToInt(4095f * (1f - Mathf.Clamp01(light))) + Mathf.RoundToInt(Pose.Noise(part.GetInstanceID(), 12f)), 0, 4095);
        }
    }

    /// <summary>The Line Tracker (analog): an IR LED and a phototransistor; light surfaces reflect more: 0 (white,
    /// close) to 4095 (dark or nothing within about 2.5"). Best 1/8" to 1/4" from the surface.</summary>
    public class LineTracker : V5Device {
        public int Value = 4095;
        public LineTracker(GameObject part) : base(part, DeviceKind.LineTracker) { }

        public float Reflectivity => 100f * (1f - Value / 4095f);
        public override string Reading => Value.ToString();
        public override (string mode, string value) PortState(int index) => ("Analog In", Value + " (" + Mathf.RoundToInt(Value / 40.95f) + "%)");

        public override void Sample(float dt) {
            var (o, d) = SensorFrame.Look(part);
            var dir = Pose.Dir(part, d);
            var origin = Pose.Point(part, o) + dir * 0.02f;
            var signal = 0f;
            if (Pose.Cast(origin, dir, 2.5f, part, out var seen)) {
                var c = Pose.ColorAt(seen);
                var surface = Mathf.Clamp01((c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) * 2.2f);
                var distance = seen.distance + 0.02f;
                var falloff = distance < 0.125f ? distance / 0.125f * 0.7f + 0.3f : Mathf.Clamp01(1f - (distance - 0.25f) / 2.25f);
                signal = surface * Mathf.Min(1f, falloff);
            }
            Value = Mathf.Clamp(Mathf.RoundToInt(4095f * (1f - 0.92f * signal)) + Mathf.RoundToInt(Pose.Noise(part.GetInstanceID(), 15f)), 0, 4095);
        }
    }

    // ---------------------------------------------------------------------- shafts

    /// <summary>The shaft in a sensor's square hole: V5 Rotation Sensor (angle 0-359.99°, position, velocity; its zero
    /// is kept in the sensor), the Optical Shaft Encoder (two 3-wire ports, 360 ticks a turn, counts from when the
    /// robot is switched on) and the Potentiometer V2 (analog, 333° of travel between its stops).</summary>
    public class ShaftSensor : V5Device {
        public float Angle;        // 0..360 (after zero and reversed)
        public double Position;    // degrees, unwrapped
        public float Rpm;
        public bool HasShaft;
        private float lastRaw;
        private bool started;
        private Vector3 holeLocal, axisLocal, perpLocal;
        private bool measured;
        private GameObject shaft;
        private Vector3 shaftPerp;
        private long shaftVersion = -1;

        public ShaftSensor(GameObject part, DeviceKind kind) : base(part, kind) {
            if (kind == DeviceKind.Encoder) ports = 2;
        }

        public const float PotRange = 333f;
        public override bool FastSample => true;
        public override bool AlwaysSample => true;
        public int Ticks => Mathf.RoundToInt((float)Position);
        public int PotValue => Mathf.RoundToInt(Mathf.Clamp(Angle, 0f, PotRange) / PotRange * 4095f);

        public override string Reading => kind switch {
            DeviceKind.Encoder => Ticks + " ticks",
            DeviceKind.Potentiometer => Angle.ToString("0.0", CultureInfo.InvariantCulture) + "°",
            _ => Angle.ToString("0.00", CultureInfo.InvariantCulture) + "°",
        };

        public override (string mode, string value) PortState(int index) => kind switch {
            DeviceKind.Encoder => ("Digital In", index == 0 ? "Encoder: " + Ticks : "Encoder (B)"),
            DeviceKind.Potentiometer => ("Analog In", PotValue + " (" + Mathf.RoundToInt(PotValue / 40.95f) + "%)"),
            _ => ("", ""),
        };

        /// <summary>The zero the user set, in degrees of the raw angle (kept in the part: the real sensor keeps it).</summary>
        private float ZeroOffset {
            get => float.TryParse(Dynamics.Token(part, "zero"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            set => Dynamics.SetToken(part, "zero", Mathf.Abs(value) < 0.001f ? null : value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private bool Reversed => Controls.Reversed(part);

        /// <summary>V2: the axis it measures about (its shaft hole's), in the build; a reading goes up when the shaft
        /// turns positively about it (Insert devices works out tracking wheels' `reversed` from it).</summary>
        internal Vector3 AxisWorld {
            get {
                if (!measured) Measure();
                return part.transform.TransformDirection(axisLocal).normalized;
            }
        }

        private void Measure() {
            measured = true;
            var holes = part.GetComponentsInChildren<HoleCollider>(true);
            HoleCollider pick = null;
            foreach (var h in holes)
                if (h.holeData != null && h.holeData.shape != null && h.holeData.shape.name.Contains("Cube")) pick = pick == null || h.holeData.size.x < pick.holeData.size.x ? h : pick;
            if (pick == null) pick = holes.OrderBy(h => h.holeData != null ? h.holeData.size.x : 1f).FirstOrDefault();
            if (pick == null) return;
            holeLocal = part.transform.InverseTransformPoint(pick.transform.position);
            axisLocal = part.transform.InverseTransformDirection(pick.transform.forward).normalized;
            perpLocal = Vector3.ProjectOnPlane(part.transform.InverseTransformDirection(pick.transform.right), axisLocal).normalized;
            if (perpLocal.sqrMagnitude < 0.5f) perpLocal = Vector3.ProjectOnPlane(Vector3.up, axisLocal).normalized;
        }

        private void FindShaft() {
            if (shaftVersion == Dynamics.Version) return;
            shaftVersion = Dynamics.Version;
            shaft = null;
            foreach (var rotor in GearTrain.Current())
                if (rotor.shaft != null && rotor.bearings.Any(b => b.part == part)) {
                    shaft = rotor.shaft;
                    break;
                }
            if (shaft == null) return;
            var axisWorld = part.transform.TransformDirection(axisLocal);
            var p = Vector3.ProjectOnPlane(shaft.transform.right, axisWorld);
            if (p.sqrMagnitude < 0.1f) p = Vector3.ProjectOnPlane(shaft.transform.up, axisWorld);
            shaftPerp = shaft.transform.InverseTransformDirection(p.normalized);
        }

        /// <summary>The shaft's turn in the housing about the hole's axis, 0..360 (no zero, not reversed).</summary>
        private bool Raw(out float raw) {
            raw = 0f;
            if (!measured) Measure();
            FindShaft();
            if (shaft == null || shaft.IsDeleted()) return false;
            var axis = Pose.Dir(part, axisLocal);
            var a = Pose.Dir(part, perpLocal);
            var b = Vector3.ProjectOnPlane(Pose.Dir(shaft, shaftPerp), axis);
            if (b.sqrMagnitude < 1e-6f) return false;
            raw = Mathf.Repeat(Vector3.SignedAngle(a, b, axis), 360f);
            return true;
        }

        internal int samples;   // V2: tests count them (t_odom*.cs)

        public override void Sample(float dt) {
            samples++;
            HasShaft = Raw(out var raw);
            if (!HasShaft) {
                Rpm = Mathf.MoveTowards(Rpm, 0f, 600f * dt);
                return;
            }
            var sign = Reversed ? -1f : 1f;
            if (!started) {
                started = true;
                lastRaw = raw;
                SetFromRaw(raw, sign, true);
                return;
            }
            var delta = Mathf.DeltaAngle(lastRaw, raw) * sign;
            lastRaw = raw;
            Position += delta;
            if (dt > 0f) Rpm = Mathf.Lerp(Rpm, delta / dt / 6f, Mathf.Clamp01(dt * 12f));
            SetFromRaw(raw, sign, false);
        }

        private void SetFromRaw(float raw, float sign, bool powerOn) {
            Angle = Mathf.Repeat((raw - ZeroOffset) * sign, 360f);
            if (powerOn) Position = kind == DeviceKind.Rotation ? Angle : 0;
        }

        public override void PowerOn() {
            started = false;
            Rpm = 0f;
        }

        /// <summary>Rotation: makes where it is now 0° (kept in the sensor); encoder: the count starts again.</summary>
        public void SetZero() {
            if (kind == DeviceKind.Rotation && Raw(out var raw)) {
                ZeroOffset = raw;
                Angle = 0f;
            }
            Position = 0;
        }

        public void ToggleReversed() {
            Controls.SetReversed(part, !Reversed);
            started = false;
        }

        public bool IsReversed => Reversed;
    }

    // ---------------------------------------------------------------------- motion

    /// <summary>V5 Inertial Sensor: a 3-axis gyro and accelerometer. Heading 0-359.99° clockwise from where it was
    /// calibrated (at power on), rotation (the same, not wrapped), pitch/roll/yaw, acceleration in g.</summary>
    public class InertialSensor : V5Device {
        public float Heading, Pitch, Roll, Yaw, Rate;
        public double Rotation;
        public Vector3 Accel;   // g, in its own axes (x, y, z)
        private Vector3 trackLocal = Vector3.forward, sideLocal = Vector3.right;
        private Vector3 reference;
        private float lastHeading;
        private bool calibrated;
        private Vector3 lastPoint, lastVelocity;
        private int samples;

        public InertialSensor(GameObject part) : base(part, DeviceKind.Inertial) { }

        public override bool FastSample => true;
        public override bool AlwaysSample => true;
        public override string Reading => Heading.ToString("0.0", CultureInfo.InvariantCulture) + "°";

        public void Calibrate() {
            // Follow its axis that lies flattest (it works mounted any way up).
            var axes = new[] { Vector3.forward, Vector3.right, Vector3.up };
            trackLocal = axes.OrderBy(a => Mathf.Abs(Vector3.Dot(Pose.Dir(part, a), Vector3.up))).First();
            sideLocal = axes.Where(a => a != trackLocal).OrderBy(a => Mathf.Abs(Vector3.Dot(Pose.Dir(part, a), Vector3.up))).First();
            reference = Vector3.ProjectOnPlane(Pose.Dir(part, trackLocal), Vector3.up).normalized;
            Rotation = 0;
            lastHeading = 0f;
            calibrated = true;
            samples = 0;
        }

        public override void PowerOn() => calibrated = false;

        public override void Sample(float dt) {
            if (!calibrated) Calibrate();
            var forward = Pose.Dir(part, trackLocal);
            var flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f) {
                var heading = Vector3.SignedAngle(reference, flat.normalized, Vector3.up);
                var delta = Mathf.DeltaAngle(lastHeading, heading);
                lastHeading = heading;
                Rotation += delta;
                if (dt > 0f) Rate = Mathf.Lerp(Rate, delta / dt, Mathf.Clamp01(dt * 10f));
                Heading = Mathf.Repeat((float)Rotation, 360f);
                Yaw = Mathf.DeltaAngle(0f, Heading);
            }
            Pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(forward, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
            Roll = Mathf.Asin(Mathf.Clamp(Vector3.Dot(Pose.Dir(part, sideLocal), Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;

            // Acceleration (what an accelerometer feels: the push that's moving it, gravity's 1 g held up included).
            var point = Pose.Point(part, Vector3.zero);
            if (dt > 0f && samples > 0) {
                var velocity = (point - lastPoint) / dt;
                var a = samples > 1 ? (velocity - lastVelocity) / dt : Vector3.zero;
                lastVelocity = velocity;
                var felt = (a + Vector3.up * 386.1f) / 386.1f;
                var local = new Vector3(Vector3.Dot(felt, Pose.Dir(part, Vector3.right)), Vector3.Dot(felt, Pose.Dir(part, Vector3.up)), Vector3.Dot(felt, Pose.Dir(part, Vector3.forward)));
                Accel = Vector3.Lerp(Accel, local, Mathf.Clamp01(dt * 15f));
            }
            lastPoint = point;
            samples++;
        }
    }

    /// <summary>The legacy Analog Accelerometer: three 3-wire ports (X, Y, Z), ±2 g (2048 = 0 g).</summary>
    public class Accelerometer : V5Device {
        private readonly InertialSensor inner;
        public Accelerometer(GameObject part) : base(part, DeviceKind.Accelerometer) {
            ports = 3;
            inner = new InertialSensor(part);
        }

        public override bool FastSample => true;
        public Vector3 G => inner.Accel;
        public int Value(int axis) => Mathf.Clamp(2048 + Mathf.RoundToInt(G[axis] / 2f * 2047f), 0, 4095);
        public override string Reading => "X " + G.x.ToString("0.00", CultureInfo.InvariantCulture) + "  Y " + G.y.ToString("0.00", CultureInfo.InvariantCulture) + "  Z " + G.z.ToString("0.00", CultureInfo.InvariantCulture) + " g";
        public override (string mode, string value) PortState(int index) => ("Analog In", Value(index) + " (" + "XYZ"[Mathf.Clamp(index, 0, 2)] + ")");
        public override void Sample(float dt) => inner.Sample(dt);
        public override void PowerOn() => inner.PowerOn();
    }

    /// <summary>The legacy Yaw Rate Gyroscope (analog): turn rate about its own axis, ±1000°/s (2048 = still), and the
    /// heading worked out from it.</summary>
    public class YawGyro : V5Device {
        private readonly InertialSensor inner;
        public YawGyro(GameObject part) : base(part, DeviceKind.Gyro) => inner = new InertialSensor(part);
        public override bool FastSample => true;
        public override bool AlwaysSample => true;
        public float Rate => inner.Rate;
        public float Heading => inner.Heading;
        public int Value => Mathf.Clamp(2048 + Mathf.RoundToInt(Rate / 1000f * 2047f), 0, 4095);
        public override string Reading => Heading.ToString("0.0", CultureInfo.InvariantCulture) + "°";
        public override (string mode, string value) PortState(int index) => ("Analog In", Value + " (" + Mathf.RoundToInt(Rate) + "°/s)");
        public override void Sample(float dt) => inner.Sample(dt);
        public override void PowerOn() => inner.PowerOn();
        public void Calibrate() => inner.Calibrate();
    }

    // ---------------------------------------------------------------------- switches

    /// <summary>Bumper and limit switches (digital in): pressed when something pushes the button (or the lever). The
    /// port reads High (1) released and Low (0) pressed, as the Brain shows it.</summary>
    public class SwitchSensor : V5Device {
        public bool Pressed;
        public SwitchSensor(GameObject part, DeviceKind kind) : base(part, kind) { }

        public override string Reading => Pressed ? "Pressed" : "Released";
        public override (string mode, string value) PortState(int index) => ("Digital In", Pressed ? "Low (0)" : "High (1)");

        public override void Sample(float dt) {
            var piece = SensorFrame.Piece(part, kind == DeviceKind.Limit ? "Silver" : "Red");
            if (piece.size == Vector3.zero) return;
            var center = Pose.Point(part, piece.center);
            Pressed = Pose.Touching(part, center, piece.extents + Vector3.one * 0.03f, Pose.Rotation(part));
        }
    }

    // ---------------------------------------------------------------------- formatting

    public static class DevicesFormat {
        private const string MmKey = "V2.Devices.Millimeters";
        public static bool Millimeters {
            get => PlayerPrefs.GetInt(MmKey, 1) == 1;
            set => PlayerPrefs.SetInt(MmKey, value ? 1 : 0);
        }

        public static string Distance(float mm) => Millimeters ? Mathf.RoundToInt(mm) + " mm" : (mm / 25.4f).ToString("0.0", CultureInfo.InvariantCulture) + " in";
        public static string Inches(float inches) => inches.ToString("0.0", CultureInfo.InvariantCulture) + " in";

        public static string ColorName(float hue, float saturation, float brightness) {
            if (saturation < 0.18f) return brightness > 55f ? "White" : brightness > 15f ? "Gray" : "Black";
            if (hue < 15f || hue >= 340f) return "Red";
            if (hue < 40f) return "Orange";
            if (hue < 70f) return "Yellow";
            if (hue < 160f) return "Green";
            if (hue < 200f) return "Cyan";
            if (hue < 260f) return "Blue";
            if (hue < 300f) return "Purple";
            return "Pink";
        }
    }
}
