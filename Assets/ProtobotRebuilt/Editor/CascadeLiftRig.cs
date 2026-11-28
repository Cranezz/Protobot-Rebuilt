using System.Collections.Generic;
using System.Linq;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.EditorTools {
    /// <summary>
    /// V2 (build 88): two sample cascade lifts on VEX's linear slides (Sample Builds/Cascade Lift (string).pbb and
    /// (chain).pbb). A test stand: a plate, two 1x5 C-channel towers (x = ±5) on angles, each carrying a 12" outer slide
    /// member (the base stage, x = ±4); a slide truck in it bolted to a second member in front (stage 2, both sides on
    /// their own); a truck in that one bolted to a plate across both sides (the carriage, stage 3). Rubber bands raise
    /// it: base top to stage 2's bottom bracket, stage 2's top bracket to the carriage. What pulls it down, on each side:
    /// "string": a 200 rpm motor's winch drum winds a VEX rope tied to stage 2's bottom bracket, and a second rope from
    /// the base's top, under a post on stage 2's bottom, up to the carriage (a cascade: the carriage moves twice as far);
    /// "chain": a chain loop from stage 2's bottom bracket down round the motor's 10T sprocket, up over a 10T idler at
    /// the top and back down to the bracket (it pulls stage 2 both ways), and a chain from the base's top under a 15T
    /// sprocket on stage 2's bottom (on a screw) up to the carriage (the cascade).
    /// Stage 2's two sides are joined by a crossbar across their tops; a standoff on each tower is a hard stop 4.6" up
    /// (the carriage then stops short of the crossbar; the winch's rope is longer, so a string lift stops there too).
    /// Right side lanes (left mirrored): the carriage's posts at x 4 (in front of the slides), stage 2's posts at x 5
    /// (the chain's ties at 5.5, its cascade sprocket on a shaft at 5), the base's top post at x 6; the motor's shaft
    /// 1.84" up at x 5 (string: the tower's middle) or 5.5 (chain: the idler at the top stays clear of stage 2's top
    /// bracket). Finish after the screws connect.
    /// </summary>
    public static class CascadeLiftRig {
        public static readonly Dictionary<string, GameObject> P = new();
        private static GameObject Make(string id, Vector3 p, Quaternion r) => MotionTestRig.Make(id, p, r);

        private const float Half = 0.023f;
        private const float WebFront = 0.055f, WebBack = -0.008f;   // the towers' web
        private static float Row(int k) => 0.342f + 0.5f * k;      // the towers' hole rows (their first 0.25" up)
        private const string Truck = SlideTruck.TruckId, Member = "VXSLIDEASSEMBLYP-Outer Slide Member";
        private const float TruckHalfDepth = 0.165f, MemberDepth = 0.465f;   // the acetal truck; the outer slide member (web back to lips)
        private const string Sprocket = "SPKT-Normal-15T", SmallSprocket = "SPKT-Normal-10T";

        // A round part (collar, sprocket, spacer, drum) with its middle on `center` and its axis along `axis`.
        private static GameObject OnAxis(string id, Vector3 center, Vector3 axis) {
            var part = Make(id, center, Quaternion.identity);
            Physics.SyncTransforms();
            var (_, a) = ChainLoop.SprocketFrame(part);
            part.transform.rotation = Quaternion.FromToRotation(a, axis) * part.transform.rotation;
            Physics.SyncTransforms();
            var (c, _) = ChainLoop.SprocketFrame(part);
            part.transform.position += center - c;
            Physics.SyncTransforms();
            return part;
        }

        // A standoff standing out of a face (its back on `face`, along `outward`), held by a screw from behind.
        private static GameObject Standoff(string size, Vector3 face, Vector3 outward, float behind) {
            var number = size.Replace("in", "");
            var length = number.Contains("/") ? float.Parse(number.Split('/')[0]) / float.Parse(number.Split('/')[1])
                : float.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
            var s = Make("SNDF-" + size, face + outward * (length / 2f), Quaternion.LookRotation(outward));
            MotionTestRig.Screw(face - outward * behind, -outward, "1/4in");
            return s;
        }

        public static string Build(string mode) {
            var log = new System.Text.StringBuilder();
            P.Clear();
            var up = Vector3.up;
            var fwd = Vector3.forward;
            var fails = 0;
            void Fasten(Vector3 point, Vector3 into, string what) {
                if (MotionTestRig.Bolt(point, into)) return;
                fails++;
                log.AppendLine("couldn't fasten " + what + " at " + point.ToString("F3"));
            }

            P["base"] = Make("PLTE-25-12", new Vector3(0f, Half, 0.946f), Quaternion.LookRotation(up, fwd));
            var memberTurn = Quaternion.LookRotation(-fwd, Vector3.right);
            foreach (var s in new[] { 1f, -1f }) {
                var side = s > 0 ? "R " : "L ";
                // The tower: a 1x5 C-channel standing up, its web facing the front, 0.046" above the plate (its first
                // row on the angle's).
                var tower = Make("CCHL-1x5-32", new Vector3(5f * s, 0.092f + 8f, 0f), Quaternion.AngleAxis(90f, fwd));
                P[side + "tower"] = tower;
                // An angle inside it at the bottom: one leg on the web's back, the other on the plate.
                Make("ANGL-1x1-3", new Vector3(5f * s, Row(0), WebBack - 0.048f), Quaternion.AngleAxis(180f, fwd));
                Physics.SyncTransforms();
                foreach (var dx in new[] { -0.5f, 0.5f }) {
                    Fasten(new Vector3(5f * s + dx, Row(0), WebFront), -fwd, side + "tower to its angle");
                    Fasten(new Vector3(5f * s + dx, 0.046f + 0.063f + 0.001f, -0.304f), -up, side + "angle to the plate");
                }
                // The motor inside the tower, its shaft out through the web's middle row.
                var shaftAt = new Vector3((mode == "chain" ? 5.5f : 5f) * s, Row(3), 0f);   // (the chain lift's drive in the next row out: clear of stage 2's top bracket)
                P[side + "motor"] = MotionTestRig.Motor(new Vector3(shaftAt.x, shaftAt.y, WebBack), fwd, up);
                foreach (var d in new[] { 1, 2 }) MotionTestRig.Screw(new Vector3(shaftAt.x, Row(3 + d), WebFront), fwd, "3/8in");
                P[side + "shaft"] = mode == "chain" ? MotionTestRig.Shaft(new Vector3(shaftAt.x, shaftAt.y, 0.85f), fwd, 3)
                    : MotionTestRig.Shaft(new Vector3(shaftAt.x, shaftAt.y, 1.35f), fwd, 4);

                // The base stage: an outer slide member on the tower's web (bolted at its top, above where the truck goes).
                var basePos = new Vector3(4f * s + 0.821f, Row(6) + 2.741f, WebFront);
                var baseMember = Make(Member, basePos, memberTurn);
                P[side + "base member"] = baseMember;
                Physics.SyncTransforms();
                foreach (var lx in new[] { 7.259f, 7.755f, 8.259f, 8.755f })
                    Fasten(new Vector3(4f * s, basePos.y + lx, WebFront + 0.046f), -fwd, side + "base member to its tower");

                // Stage 2: a truck in the base member at its bottom, a second member bolted to it in front.
                var bottom = basePos.y - 2.993f;
                var seat = SlideTruck.Seat(baseMember, new Vector3(4f * s, bottom + 1f, 0.3f));
                P[side + "truck A"] = Make(Truck, seat.translation.Position, seat.rotation.Orientation);
                // (the truck's mounting face stands a little proud of the base member's lips: stage 2's web on it)
                var truckFront = seat.translation.Position.z + TruckHalfDepth;
                var m2Pos = new Vector3(basePos.x, basePos.y, truckFront + 0.002f);
                var m2 = Make(Member, m2Pos, memberTurn);
                P[side + "stage 2"] = m2;
                var m2Web = m2Pos.z + 0.046f;
                var truckA = P[side + "truck A"].transform.position.z;
                foreach (var lx in new[] { -2.741f, -2.245f, -1.741f, -1.245f }) {
                    MotionTestRig.Screw(new Vector3(4f * s, m2Pos.y + lx, m2Web), fwd, "3/8in");
                    MotionTestRig.Nut(new Vector3(4f * s, m2Pos.y + lx, truckA - 0.039f), fwd);
                }
                Physics.SyncTransforms();
                // The carriage's truck in stage 2, 4.5" up its length.
                seat = SlideTruck.Seat(m2, new Vector3(4f * s, m2Pos.y - 2.993f + 4.5f, 0.7f));
                P[side + "truck B"] = Make(Truck, seat.translation.Position, seat.rotation.Orientation);

                // Stage 2's brackets: 1 x 2 hole plates on its lips, a long screw through to a nut behind its web.
                var lip = m2Pos.z + MemberDepth;
                GameObject Bracket(string key, string id, float centerX, float lowRow, bool crossbar = false) {
                    var b = Make(id, new Vector3(centerX * s, lowRow + 0.25f, lip + 0.005f + Half), Quaternion.identity);
                    P[side + key] = b;
                    foreach (var y in new[] { lowRow, lowRow + 0.5f }) {
                        // (the top one's screws go through the crossbar in front of it too)
                        MotionTestRig.Screw(new Vector3(4f * s, y, lip + 0.005f + (crossbar ? 4f : 2f) * Half), fwd, crossbar ? "3/4in" : "5/8in");
                        MotionTestRig.Nut(new Vector3(4f * s, y, m2Pos.z), fwd);
                    }
                    return b;
                }
                var low = m2Pos.y - 0.741f;    // its bottom bracket's rows (above truck A, below the carriage)
                var high = m2Pos.y + 8.259f;   // its top bracket's
                var front = lip + 0.005f + 2f * Half;   // the brackets' (and the carriage's) front face
                var back = lip + 0.005f;
                Bracket("bottom bracket", mode == "chain" ? "PLTE-4-2" : "PLTE-3-2", mode == "chain" ? 4.75f : 4.5f, low);
                Bracket("top bracket", "PLTE-3-2", 4.5f, high, true);
                P[side + "top post"] = Standoff("1.00in", new Vector3(5f * s, high, front), fwd, 0.046f);

                // The base's top post (the cascade's anchor, band 1's top): a 2" standoff out of the tower's web.
                P[side + "anchor"] = Standoff("2.00in", new Vector3(6f * s, Row(29), WebFront), fwd, 0.063f);
                // A hard stop: stage 2's bottom bracket meets a standoff out of the tower 4.6" up (the carriage, going
                // twice as far, stops 0.4" short of stage 2's top bracket). The winch's rope is longer than that, so a
                // string lift stops here with rope still on the drum (and the rope goes slack).
                Standoff("1.50in", new Vector3(5f * s, Row(21), WebFront), fwd, 0.063f);

                if (mode == "chain") {
                    // The drive: the motor's 15T sprocket, a 15T idler at the top (its shaft in the web, collars each
                    // side of it), ties for the loop on the bottom bracket at x 5.5.
                    P[side + "drive"] = OnAxis(SmallSprocket, new Vector3(shaftAt.x, shaftAt.y, 1.3f), fwd);
                    OnAxis("CLMP-Normal-Normal", new Vector3(shaftAt.x, shaftAt.y, 1.7f), fwd);
                    var idlerAt = new Vector3(5.5f * s, Row(31), 0f);
                    MotionTestRig.Shaft(new Vector3(idlerAt.x, idlerAt.y, 0.85f), fwd, 3);
                    P[side + "idler"] = OnAxis(SmallSprocket, new Vector3(idlerAt.x, idlerAt.y, 1.3f), fwd);
                    foreach (var z in new[] { -0.15f, 1.68f }) OnAxis("CLMP-Normal-Normal", new Vector3(idlerAt.x, idlerAt.y, z), fwd);
                    P[side + "tie low"] = Standoff("1/2in", new Vector3(5.5f * s, low, front), fwd, 0.046f);
                    P[side + "tie high"] = Standoff("1/2in", new Vector3(5.5f * s, low + 0.5f, front), fwd, 0.046f);
                    // The cascade's sprocket: on a 2" shaft through the bracket at x 5, a collar behind the bracket and
                    // one in front of the sprocket (a shaft holds what's between its collars).
                    var axle = new Vector3(5f * s, low, 0f);
                    MotionTestRig.Shaft(new Vector3(axle.x, axle.y, 1.6f), fwd, 2);
                    OnAxis("CLMP-Normal-Normal", new Vector3(axle.x, axle.y, back - 0.13f), fwd);
                    P[side + "cascade sprocket"] = OnAxis(Sprocket, new Vector3(axle.x, axle.y, 1.92f), fwd);
                    OnAxis("CLMP-Normal-Normal", new Vector3(axle.x, axle.y, 2.32f), fwd);
                }
                else {
                    // The winch drum on the motor's shaft (a collar in front), and stage 2's post at x 5.
                    P[side + "drum"] = OnAxis("VXADVANCEDGEARS-Winch Drum", new Vector3(shaftAt.x, shaftAt.y, 1.68f), fwd);   // (its groove 0.43" behind its middle: at z 1.25)
                    OnAxis("CLMP-Normal-Normal", new Vector3(shaftAt.x, shaftAt.y, 2.6f), fwd);
                    P[side + "stage 2 post"] = Standoff("1.50in", new Vector3(5f * s, low, front), fwd, 0.046f);
                }
            }
            // Stage 2's crossbar: a 2 x 19 hole plate across both sides' top brackets (as real lifts brace each stage: the two
            // sides can't get out of step and jam the carriage), above where the carriage can reach.
            var topRow = P["R top bracket"].transform.position.y - 0.25f;
            var crossZ = P["R stage 2"].transform.position.z + MemberDepth + 0.005f + 3f * Half;
            P["crossbar"] = Make("PLTE-19-2", new Vector3(0f, topRow + 0.25f, crossZ), Quaternion.identity);

            // The carriage: a plate across both trucks (3" tall), bolted to them, posts at x ±4 above and below them.
            var carriageY = P["R truck B"].transform.position.y;
            // (on the trucks' mounting faces, which stand proud of stage 2's lips)
            var cBack = P["R truck B"].transform.position.z + TruckHalfDepth + 0.002f;
            var cFront = cBack + 2f * Half;
            P["carriage"] = Make("PLTE-19-6", new Vector3(0f, carriageY, cBack + Half), Quaternion.identity);
            var truckB = P["R truck B"].transform.position.z;
            foreach (var s in new[] { 1f, -1f }) {
                var side = s > 0 ? "R " : "L ";
                foreach (var dy in new[] { -0.75f, -0.25f, 0.25f, 0.75f }) {
                    MotionTestRig.Screw(new Vector3(4f * s, carriageY + dy, cFront), Vector3.forward, "3/8in");
                    MotionTestRig.Nut(new Vector3(4f * s, carriageY + dy, truckB - 0.039f), Vector3.forward);
                }
                P[side + "carriage low"] = Standoff("1.50in", new Vector3(4f * s, carriageY - 1.25f, cFront), Vector3.forward, 0.046f);
                P[side + "carriage high"] = Standoff("1.50in", new Vector3(4f * s, carriageY + 1.25f, cFront), Vector3.forward, 0.046f);
            }
            Physics.SyncTransforms();
            log.Insert(0, "built the " + mode + " lift, " + fails + " fastenings failed\n");
            return log.ToString();
        }

        // ------------------------------------------------------------------ after the screws connect

        private static BandPost PostAt(GameObject part, float z, float radius = -1f) {
            var (center, axis) = (part.GetComponent<Renderer>().bounds.center, Vector3.forward);
            if (ChainFamily.ForSprocket(part) != null || (part.GetComponent<SavedObject>()?.id ?? "").Contains("Winch Drum")) {
                // (the sprocket's pitch circle, the drum's groove: as the String tool takes them, from the side)
                var outward = center.x >= 0f ? Vector3.right : Vector3.left;
                if (StringUi.PostAt(new Ray(center + outward * 3f, -outward), out var post) && post.part == part) return post;
            }
            center.z = z;
            return new BandPost {
                part = part, local = part.transform.InverseTransformPoint(center), axisLocal = part.transform.InverseTransformDirection(axis),
                radius = radius > 0f ? radius : 0.144f, what = "standoff",
            };
        }

        private static StringLine MakeLine(string kindKey, List<StringNode> nodes, float turns, System.Text.StringBuilder log) {
            var kind = StringKind.Of(kindKey);
            var path = StringLine.PathOf(nodes, kind, turns);
            if (!path.Valid) {
                log.AppendLine("path: " + path.problem);
                return null;
            }
            var generator = PartsManager.GetPartType("STRG").GetComponent<PartGenerator>();
            generator.param1.value = kind.key;
            var obj = generator.Generate(nodes[0].post.Center, Quaternion.identity);
            var line = obj.GetComponent<StringLine>();
            line.Set(nodes, path.tight, turns);
            log.AppendLine(kind.shortName + ": " + line.Describe().Replace('\n', ' ') + " ways " + string.Join(",", path.ways));
            return line;
        }

        private static void Band(BandPost a, BandPost b, int bands, System.Text.StringBuilder log) {
            var generator = PartsManager.GetPartType("BAND").GetComponent<PartGenerator>();
            generator.param1.value = "V64";
            var obj = generator.Generate(a.Center, Quaternion.identity);
            var band = obj.GetComponent<ElasticBand>();
            band.Set(new List<BandPost> { a, b }, bands, 1, 0f, Vector3.forward);
            log.AppendLine("band: " + band.Describe().Replace('\n', ' '));
        }

        /// <summary>travel: how far stage 2 can rise before the winch's rope is all off its drum (the carriage twice that).</summary>
        public static string Finish(string mode, int bands = 2, float travel = 5.5f) {
            var log = new System.Text.StringBuilder();
            foreach (var s in new[] { 1f, -1f }) {
                var side = s > 0 ? "R " : "L ";
                var motor = P[side + "motor"].GetComponent<MotorDrive>();
                motor.SetCartridge(200, false);
                Controls.SetName(motor.gameObject, s > 0 ? "Lift right" : "Lift left");
                Controls.SetHold(motor.gameObject, true);
                Controls.SetForwardKey(motor.gameObject, UnityEngine.InputSystem.Key.U);
                Controls.SetReverseKey(motor.gameObject, UnityEngine.InputSystem.Key.J);
                // (both drums wind the same way round; a chain loop raises stage 2 when its drive sprocket turns
                // against the way the loop goes round it: the motor is reversed where that's the forward way, so U
                // raises both sides)
                Controls.SetReversed(motor.gameObject, false);

                var anchor = P[side + "anchor"];
                if (mode == "chain") {
                    var loop = new List<StringNode> {
                        new() { post = PostAt(P[side + "tie low"], 1.3f) },
                        new() { post = PostAt(P[side + "drive"], 1.3f) },
                        new() { post = PostAt(P[side + "idler"], 1.3f) },
                        new() { post = PostAt(P[side + "tie high"], 1.3f) },
                    };
                    var drive = MakeLine("CHAIN", loop, 0f, log);
                    if (drive != null && drive.Path.ways.Length > 1) Controls.SetReversed(motor.gameObject, drive.Path.ways[1] > 0);
                    var sprocket = PostAt(P[side + "cascade sprocket"], 0f);
                    var plane = sprocket.Center.z;
                    var cascade = new List<StringNode> {
                        new() { post = PostAt(anchor, plane) },
                        new() { post = sprocket },
                        new() { post = PostAt(P[side + "carriage high"], plane) },
                    };
                    MakeLine("CHAIN", cascade, 0f, log);
                    Band(PostAt(anchor, 1.5f), PostAt(P[side + "tie low"], 1.5f), bands, log);
                }
                else {
                    var drum = PostAt(P[side + "drum"], 1.25f);
                    var winch = new List<StringNode> { new() { post = PostAt(P[side + "stage 2 post"], drum.Center.z) }, new() { post = drum, spool = true } };
                    var turns = travel / (2f * Mathf.PI * (drum.radius + StringKind.Of("VEX").DrawRadius));
                    MakeLine("VEX", winch, turns, log);
                    var cascade = new List<StringNode> {
                        new() { post = PostAt(anchor, 1.8f) },
                        new() { post = PostAt(P[side + "stage 2 post"], 1.8f) },
                        new() { post = PostAt(P[side + "carriage high"], 1.8f) },
                    };
                    MakeLine("VEX", cascade, 0f, log);
                    Band(PostAt(anchor, 1.5f), PostAt(P[side + "stage 2 post"], 1.5f), bands, log);
                }
                Band(PostAt(P[side + "top post"], 1.4f), PostAt(P[side + "carriage low"], 1.4f), bands, log);
            }
            return log.ToString();
        }
    }
}
