using System.Collections.Generic;
using System.Linq;
using System.Text;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.EditorTools {
    /// <summary>
    /// V2: odometry on the 6-motor drive, as competitive teams build it: two unpowered 2.75" omni tracking wheels, one
    /// rolling forward (vertical) and one sideways (horizontal), each a compact pod: the V5 Rotation Sensor is the arm,
    /// the wheel sits on a shaft through it, and it pivots on a joint screw through its mounting hole into a 3x3 angle
    /// hung under the cross-members; a rubber band from the sensor's end to a screw further along the angle pulls the
    /// wheel down onto the floor. And a V5 Inertial Sensor flat on the middle cross-member. Load the 6-motor drive first
    /// (at the origin, front +z).
    /// </summary>
    public static class OdomRig {
        private const float WheelRadius = 1.376f;   // OMNI-V2-2.75in
        private const float ArmLength = 1.0f;       // the sensor: its mounting hole (the pivot) to its shaft hole
        private const float SensorHalf = 0.41f;     // half the sensor's thickness
        private const float WheelGap = 0.125f;      // a 1/8" spacer between the sensor and the wheel

        private static GameObject Part(string id, Vector3 position, Quaternion rotation) => PartsManager.GeneratePart(id, position, rotation);

        // Local x and z of a part onto these world directions (orthonormal).
        private static Quaternion FromAxes(Vector3 imageX, Vector3 imageZ) => Quaternion.LookRotation(imageZ, Vector3.Cross(imageZ, imageX));

        public static readonly List<GameObject> pivots = new();

        public static string Build() {
            var log = new StringBuilder();
            pivots.Clear();
            bands.Clear();
            var crosses = Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None).Where(s => s.id == "CCHL-1x2-23")
                .Select(s => s.gameObject).OrderBy(p => p.transform.position.z).ToList();
            if (crosses.Count != 3) return "Load the 6 motor drive first (its three top C-channels weren't found)";
            var rear = crosses[0];
            var middle = crosses[1];
            var front = crosses[2];
            // Vertical wheel (rolls along z): a 9-hole angle along z bolted under the middle and front cross-members,
            // the pod on its left (-x) side, the wheel ahead of the pivot.
            Pod(log, "Vertical tracking", "ANGL-3x3-9", new[] { (middle, new Vector2(0.5f, -1.5f)), (middle, new Vector2(0.5f, -1.0f)), (front, new Vector2(0.5f, 2.5f)) },
                Vector3.left, Vector3.forward, -0.5f);
            // Horizontal wheel (rolls along x): an 8-hole angle along x hung from the rear cross-member's front edge, the
            // pod in front of it (+z), the wheel to the right of the pivot.
            Pod(log, "Horizontal tracking", "ANGL-3x3-8", new[] { (rear, new Vector2(-1.5f, -5.0f)), (rear, new Vector2(2.0f, -5.0f)) },
                Vector3.forward, Vector3.right, -1.0f);

            // The Inertial Sensor flat on the middle cross-member's flanges, its hole over the web's (x 3, z -1), a
            // 1-1/4" screw down through both into a nut under the web.
            var webHole = MotionTestRig.HoleNear(middle, new Vector3(3f, 2.4f, -1f), Vector3.up);
            var webBottom = webHole.transform.position.y - webHole.transform.lossyScale.z / 2f;
            var flangeTop = middle.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).Max();
            var imu = Part("SNSR-Inertial-V5", new Vector3(webHole.transform.position.x, flangeTop + 0.256f, webHole.transform.position.z - 0.5f), Quaternion.identity);
            Physics.SyncTransforms();
            var imuHole = imu.GetComponentsInChildren<HoleCollider>(true).First();
            var imuTop = imuHole.transform.position.y + imuHole.transform.lossyScale.z / 2f;
            MotionTestRig.Screw(new Vector3(webHole.transform.position.x, imuTop, webHole.transform.position.z), Vector3.up, "1.25in");
            MotionTestRig.Nut(new Vector3(webHole.transform.position.x, webBottom, webHole.transform.position.z), Vector3.up);   // (a nut threads toward the head, as MotionTestRig's)
            Controls.SetName(imu, "Inertial");
            log.AppendLine("imu at " + imu.transform.position.ToString("F2"));
            return log.ToString();
        }

        /// <summary>One pod. angleId: its bracket; bolts: the web holes (x, z) its flat leg's inner holes bolt to (the
        /// first places it: its rearmost hole goes there); outward: from the bracket's hanging leg toward the sensor and
        /// wheel (the axle's direction); ahead: pivot to wheel, level; pivotAlong: where along `ahead` the pivot is.</summary>
        private static void Pod(StringBuilder log, string name, string angleId, (GameObject web, Vector2 at)[] bolts, Vector3 outward, Vector3 ahead, float pivotAlong) {
            var up = Vector3.up;
            var fails = 0;
            void Fasten(Vector3 point, Vector3 into, string what) {
                if (!MotionTestRig.Bolt(point, into)) {
                    fails++;
                    log.AppendLine(name + ": couldn't fasten " + what + " at " + point.ToString("F3"));
                }
            }
            HoleCollider WebHole(GameObject part, Vector2 at) => MotionTestRig.HoleNear(part, new Vector3(at.x, 2.4f, at.y), up);
            var first = WebHole(bolts[0].web, bolts[0].at);
            var webBottom = first.transform.position.y - first.transform.lossyScale.z / 2f;
            var webTop = webBottom + first.transform.lossyScale.z;

            // The bracket: hanging leg square to `outward`, the other leg under the web going inboard from the corner;
            // its flat leg's rearmost hole nearest the corner under the first bolt's web hole.
            var angle = Part(angleId, first.transform.position + Vector3.down, Quaternion.LookRotation(outward, up));
            Physics.SyncTransforms();
            var flat = angle.GetComponentsInChildren<HoleCollider>(true).Where(h => Mathf.Abs(Vector3.Dot(h.transform.forward, up)) > 0.98f).ToList();
            var hanging = angle.GetComponentsInChildren<HoleCollider>(true).Where(h => Mathf.Abs(Vector3.Dot(h.transform.forward, outward)) > 0.98f).ToList();
            var legPlane = hanging[0].transform.position;
            var key = flat.OrderBy(h => Mathf.Round(Vector3.Dot(h.transform.position - legPlane, -outward) * 4f)).ThenBy(h => Vector3.Dot(h.transform.position, ahead)).First();
            var keyTop = key.transform.position.y + key.transform.lossyScale.z / 2f;
            angle.transform.position += new Vector3(first.transform.position.x - key.transform.position.x, webBottom - keyTop, first.transform.position.z - key.transform.position.z);
            Physics.SyncTransforms();
            foreach (var (web, at) in bolts) {
                var hole = WebHole(web, at);
                Fasten(new Vector3(hole.transform.position.x, webTop + 0.01f, hole.transform.position.z), Vector3.down, "the bracket");
            }

            // The pivot: the hanging leg's lowest row, at pivotAlong; the arm (the sensor) rises from it to the axle, a
            // wheel's radius up.
            var lowest = hanging.Min(h => h.transform.position.y);
            var low = hanging.Where(h => h.transform.position.y < lowest + 0.05f).ToList();
            var pivotHole = low.OrderBy(h => Mathf.Abs(Vector3.Dot(h.transform.position, ahead) - pivotAlong)).First();
            var depth = pivotHole.transform.lossyScale.z;
            var P = pivotHole.transform.position + outward * (depth / 2f);   // on the leg's outboard face
            var sin = Mathf.Clamp((WheelRadius - P.y) / ArmLength, -0.9f, 0.9f);
            var u = (ahead * Mathf.Sqrt(1f - sin * sin) + up * sin).normalized;
            var A = P + u * ArmLength;   // the axle, on the leg's outboard face

            // The Rotation Sensor flat against the leg: its mounting hole on the pivot, its shaft hole on the axle.
            var sensor = Part("SNSR-Rotation-V5", A + outward * SensorHalf, FromAxes(u, outward));
            // A 2" shaft through it, a 1/8" spacer, the wheel (locked on the shaft by its insert).
            Part("SHFT-Normal-2", A + outward * 1.02f, Quaternion.LookRotation(outward));
            Part("SPCR-Nylon-1/8in", A + outward * (2f * SensorHalf + WheelGap / 2f), Quaternion.LookRotation(outward));
            Part("OMNI-V2-2.75in", A + outward * (2f * SensorHalf + WheelGap + 0.488f), Quaternion.LookRotation(outward));
            Physics.SyncTransforms();
            Controls.SetName(sensor, name);

            // The pivot: a joint screw from inside the leg through it and the sensor's mounting hole.
            Fasten(pivotHole.transform.position - outward * (depth / 2f + 0.01f), outward, "the pivot");
            Physics.SyncTransforms();
            var pivot = Object.FindObjectsByType<Screw>(FindObjectsSortMode.None).Select(s => s.gameObject)
                .Where(s => Vector3.ProjectOnPlane(s.transform.position - pivotHole.transform.position, outward).magnitude < 0.05f)
                .OrderBy(s => (s.transform.position - pivotHole.transform.position).sqrMagnitude).FirstOrDefault();
            if (pivot != null) {
                Linkage.SetJoint(pivot, true);
                pivots.Add(pivot);
            }
            else log.AppendLine(name + ": no pivot screw");

            // The band's anchor: a 3/4" screw through the leg's lowest row about 1.6" past the axle, head inside,
            // sticking out beside the sensor; the band goes from the sensor's end (past its shaft) to it, pulling the
            // wheel's end forward and down.
            var along = Vector3.Dot(A, ahead) + 1.6f;
            var anchorHole = low.OrderBy(h => Mathf.Abs(Vector3.Dot(h.transform.position, ahead) - along)).First();
            var anchor = MotionTestRig.Screw(anchorHole.transform.position - outward * (depth / 2f), -outward, "3/4in");
            MotionTestRig.Nut(anchorHole.transform.position + outward * (depth / 2f), -outward);
            Physics.SyncTransforms();
            var end = A + outward * SensorHalf + u * 0.43f;   // the sensor's end face, past its shaft
            bands.Add((sensor, end, anchor, anchorHole.transform.position + outward * (depth / 2f + SensorHalf), name));
            log.AppendLine(name + ": pivot " + P.ToString("F2") + " axle " + A.ToString("F2") + " (" + (Mathf.Asin(sin) * Mathf.Rad2Deg).ToString("F1") + "° up), " + fails + " fastenings failed");
        }

        private static readonly List<(GameObject post, Vector3 postPoint, GameObject anchor, Vector3 anchorPoint, string name)> bands = new();

        /// <summary>After the screws have connected: the bands (VEX #32; a light preload: harder bands lifted the light
        /// test drive off its wheels) from each sensor's end to its anchor screw.</summary>
        public static string Finish(string kind = "V32", int wraps = 3) {
            var log = new StringBuilder();
            var type = PartsManager.GetPartType("BAND");
            foreach (var (post, postPoint, anchor, anchorPoint, name) in bands) {
                if (post == null || anchor == null) continue;
                ElasticTool.PostLine(anchor, out _, out var axis);
                var posts = new List<BandPost> {
                    new BandPost { part = post, local = post.transform.InverseTransformPoint(postPoint), radius = 0f, what = "Round the sensor's end" },
                    new BandPost { part = anchor, local = anchor.transform.InverseTransformPoint(anchorPoint), axisLocal = anchor.transform.InverseTransformDirection(axis).normalized, radius = 0.082f, what = "anchor" },
                };
                var generator = type.GetComponent<PartGenerator>();
                generator.param1.value = kind;
                var obj = generator.Generate(posts[0].Center, Quaternion.identity);
                var band = obj.GetComponent<ElasticBand>();
                band.Set(posts, 1, wraps, 0f, Vector3.Cross(axis, anchorPoint - postPoint).normalized);
                log.AppendLine(name + " band: " + band.Describe());
            }
            bands.Clear();
            return log.ToString();
        }
    }
}
