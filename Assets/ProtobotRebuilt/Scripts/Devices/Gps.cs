using System.Globalization;
using Protobot.Game;
using UnityEngine;

namespace Protobot.Devices {
    // V2: the V5 GPS Sensor (276-7405). The real one is a camera reading the black-and-white Field Code strips on the
    // perimeter walls, plus an inertial sensor. VEX's coordinates: (0, 0) in the field's middle, about ±1.8 m each way,
    // heading 0-359.99 clockwise with 0 at the wall opposite the audience (strip #4's wall), 90 the blue drivers' side:
    // the same as the game's field frame here (+X toward blue, +Y away from the audience). Outside a game the field is
    // a 12' square (±70.2") round the build's origin (+X the build's X, +Y its Z).
    //
    // What's simulated: it must look across the field at a wall more or less level (within 30°); three rays across its
    // view (±15°) go to the wall at its own height, and anything in the way (a game piece, another robot, its own robot)
    // blocks that part of the strip. Seeing the strip: quality 100 and its position within a few hundredths of an inch
    // (VEX: about ±13 mm in a match; noise 0.15"). Closer than 8" to the wall it reads too little of the strip: quality
    // 90 ("found by other means"), position from its inertial sensor. Not seeing it: the inertial sensor carries on
    // (position drifts 3% of the distance driven), quality 90 for half a second, then falls from 80 to 0 over 4 s, and
    // at 0 everything holds still (VEX: "all data is frozen"). Heading comes from the inertial sensor while the strip is
    // out of view and is put right as soon as it's seen.

    /// <summary>V5 GPS Sensor: the sensor's own position (inches, field frame) and the way its camera faces.</summary>
    public class GpsSensor : V5Device {
        public const float FieldHalf = GameField.Half;
        public float X, Y;          // inches, the sensor's middle (robot code adds its origin offset)
        public float Heading;       // degrees clockwise from +Y, the way its camera looks
        public float Quality;       // 0-100
        public float ErrorInches;   // VEX's RMS error estimate
        public float WallDistance;  // inches to the wall it's reading (0 when none)
        public bool Seeing;         // the strip is in view
        public float Pitch, Roll, Rate;
        public Vector3 Accel;
        private readonly InertialSensor imu;
        private float lostFor = -1f;
        private Vector2 drift, lastTrue;
        private float driftAngle;
        private bool started;

        public GpsSensor(GameObject part) : base(part, DeviceKind.Gps) => imu = new InertialSensor(part);

        public override bool FastSample => true;
        public override bool AlwaysSample => true;
        public override string Reading => Quality <= 0f ? "No signal"
            : DevicesFormat.Inches(X) + ", " + DevicesFormat.Inches(Y) + " · " + Heading.ToString("0", CultureInfo.InvariantCulture) + "°";

        public override void PowerOn() {
            imu.PowerOn();
            started = false;
            lostFor = -1f;
            drift = Vector2.zero;
        }

        /// <summary>The field the sensor is on: the game's, or a 12' square round the build's origin.</summary>
        public static Transform Field => SkillsGame.Current != null && SkillsGame.Current.field != null ? SkillsGame.Current.field.transform : null;

        public static Vector3 ToField(Vector3 world) {
            var f = Field;
            return f != null ? f.InverseTransformPoint(world) : world;
        }

        public static Vector3 DirToField(Vector3 world) {
            var f = Field;
            return f != null ? f.InverseTransformDirection(world) : world;
        }

        public static Vector3 FromField(Vector3 local) {
            var f = Field;
            return f != null ? f.TransformPoint(local) : local;
        }

        public static Vector3 DirFromField(Vector3 local) {
            var f = Field;
            return f != null ? f.TransformDirection(local) : local;
        }

        /// <summary>Heading (clockwise from +Y) of a direction in the field frame.</summary>
        public static float HeadingOf(Vector3 fieldDir) => Mathf.Repeat(Mathf.Atan2(fieldDir.x, fieldDir.z) * Mathf.Rad2Deg, 360f);

        /// <summary>Where the camera is and the way it looks (world).</summary>
        public (Vector3 origin, Vector3 look) Lens() {
            var (o, d) = SensorFrame.Look(part);
            return (Pose.Point(part, o), Pose.Dir(part, d));
        }

        public override void Sample(float dt) {
            imu.Sample(dt);
            Pitch = imu.Pitch;
            Roll = imu.Roll;
            Rate = imu.Rate;
            Accel = imu.Accel;
            var (origin, look) = Lens();
            var p = ToField(origin);
            var l = DirToField(look);
            var truth = new Vector2(p.x, p.z);
            var trueHeading = HeadingOf(l);
            var flat = new Vector3(l.x, 0f, l.z);
            var level = flat.magnitude > 0.5f;   // within 30° of level
            var seen = 0;
            WallDistance = 0f;
            if (level && Mathf.Abs(p.x) < FieldHalf && Mathf.Abs(p.z) < FieldHalf) {
                flat.Normalize();
                for (var i = -1; i <= 1; i++) {
                    var dir = Quaternion.AngleAxis(i * 15f, Vector3.up) * flat;
                    var wall = ToWall(p, dir);
                    if (i == 0) WallDistance = wall;
                    var worldDir = DirFromField(dir).normalized;
                    var from = origin + look * 0.05f;
                    if (!Pose.Cast(from, worldDir, Mathf.Max(0.1f, wall - 1f), part, out var hit) || hit.floor) seen++;
                }
            }
            var seed = part.GetInstanceID();
            Seeing = seen > 0 && WallDistance >= 8f;
            // (a jump of a foot in one reading isn't driving: the field was placed round the robot, or a run started)
            if (!started || (truth - lastTrue).sqrMagnitude > 144f) {
                lastTrue = truth;
                drift = Vector2.zero;
                started = true;
            }
            if (Seeing) {
                lostFor = -1f;
                drift = Vector2.zero;
                Quality = 100f;
                ErrorInches = seen == 3 ? 0.4f : 0.8f;
                X = truth.x + Pose.Noise(seed, 0.15f);
                Y = truth.y + Pose.Noise(seed + 7, 0.15f);
                Heading = Mathf.Repeat(trueHeading + Pose.Noise(seed + 13, 0.2f), 360f);
            }
            else {
                if (lostFor < 0f) {
                    lostFor = 0f;
                    driftAngle = Mathf.Repeat(seed * 47.3f + Time.time * 91f, 360f);
                }
                lostFor += dt;
                var near = seen > 0;   // too close to the wall: some strip, not enough
                Quality = near ? 90f : lostFor < 0.5f ? 90f : Mathf.Max(0f, 80f - (lostFor - 0.5f) * 20f);
                if (Quality > 0f) {
                    var moved = (truth - lastTrue).magnitude;
                    var a = driftAngle * Mathf.Deg2Rad;
                    drift += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * moved * (near ? 0.01f : 0.03f);
                    X = truth.x + drift.x;
                    Y = truth.y + drift.y;
                    Heading = Mathf.Repeat(trueHeading, 360f);
                    ErrorInches = 0.8f + drift.magnitude;
                }
            }
            lastTrue = truth;
        }

        /// <summary>Distance from a field point along a level field direction to the inner wall.</summary>
        public static float ToWall(Vector3 p, Vector3 dir) {
            var t = float.MaxValue;
            if (Mathf.Abs(dir.x) > 1e-5f) t = Mathf.Min(t, ((dir.x > 0f ? FieldHalf : -FieldHalf) - p.x) / dir.x);
            if (Mathf.Abs(dir.z) > 1e-5f) t = Mathf.Min(t, ((dir.z > 0f ? FieldHalf : -FieldHalf) - p.z) / dir.z);
            return Mathf.Max(0f, t);
        }
    }
}
