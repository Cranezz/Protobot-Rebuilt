using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.EditorTools {
    // V2: test builds for the moving parts (play mode, called from scripts): a plate with three motors geared in a
    // row, and a four-wheel drivetrain (two C-channel rails, a plate across, a motor and wheel at each corner).
    public static class MotionTestRig {
        private static readonly List<GameObject> made = new();

        private static GameObject Part(string id, Vector3 position, Quaternion rotation) {
            var part = PartsManager.GeneratePart(id, position, rotation);
            made.Add(part);
            if (Tag != null) Tags[part] = Tag;
            return part;
        }

        /// <summary>V2 (tutorial videos): when set, every part made is listed in <see cref="Tags"/> under it (the
        /// stages a demo reveals).</summary>
        public static string Tag;
        public static readonly Dictionary<GameObject, string> Tags = new();

        // Local z along forward, local x along xDirection.
        private static Quaternion Frame(Vector3 forward, Vector3 xDirection) => Quaternion.LookRotation(forward, Vector3.Cross(forward, xDirection));

        /// <summary>An 11W motor with its front face on <paramref name="face"/> (where its shaft hole's line meets
        /// it), its shaft coming out along <paramref name="outward"/>, its body toward <paramref name="along"/>.</summary>
        public static GameObject Motor(Vector3 face, Vector3 outward, Vector3 along, string kind = "11W") {
            var rotation = Frame(outward, along);
            var hole = rotation * new Vector3(-0.5f, 0f, 1.338f);
            var origin = face - outward * (1.767f - 1.338f) - hole;
            return Part("MOTR-" + kind, origin, rotation);
        }

        public static GameObject Shaft(Vector3 center, Vector3 axis, int length) => Part("SHFT-Normal-" + length, center, Quaternion.LookRotation(axis));

        /// <summary>A screw with its head on <paramref name="head"/>, pointing out of the part along <paramref name="outward"/>.</summary>
        public static GameObject Screw(Vector3 head, Vector3 outward, string size = "1/2in") => Part("SCRW-" + size, head, Quaternion.LookRotation(outward));

        /// <summary>A nut with its face on <paramref name="face"/>, threading toward <paramref name="into"/>.</summary>
        public static GameObject Nut(Vector3 face, Vector3 into) => Part("NUT-Keps", face, Quaternion.LookRotation(into));

        public static void Clear() {
            foreach (var part in made)
                if (part != null) Object.Destroy(part);
            made.Clear();
        }

        /// <summary>A plate (11 x 11 holes) at <paramref name="origin"/> with three motors under it, their shafts up
        /// through it 1.5" apart, a 36T gear on each above the plate (they mesh in a row).</summary>
        public static List<GameObject> Gears(Vector3 origin, int count = 3) {
            var start = made.Count;
            var up = Vector3.up;
            Part("PLTE-11-11", origin, Quaternion.LookRotation(up, Vector3.forward));
            const float half = 0.023f;
            for (var i = 0; i < count; i++) {
                var x = (i - (count - 1) / 2f) * 1.5f;
                var hole = origin + new Vector3(x, 0f, 0f);
                Motor(hole - up * half, up, Vector3.forward);
                Shaft(hole + up * 0.2f, up, 3);
                Part("GEAR-Normal-36T", hole + up * 0.5f, Quaternion.LookRotation(up));
                // Two screws down through the plate into the motor's threaded holes.
                foreach (var dz in new[] { 0.5f, 1.0f }) Screw(hole + new Vector3(0f, half, dz), up);
            }
            return made.GetRange(start, made.Count - start);
        }

        /// <summary>A plate with shafts up through it, a sprocket on each at the same height (the first driven by a
        /// motor under the plate): positions in plate holes (x, z), for chain tests.</summary>
        public static List<GameObject> Sprockets(Vector3 origin, string sprocket, params Vector2[] at) =>
            Sprockets(origin, at.Select(_ => sprocket).ToArray(), at);

        /// <summary>The sprockets made (in the order given), for chains.</summary>
        public static List<GameObject> SprocketsOf(List<GameObject> parts) => parts.Where(p => Motion.ChainFamily.ForSprocket(p) != null).ToList();

        public static List<GameObject> Sprockets(Vector3 origin, string[] sprockets, Vector2[] at) {
            var start = made.Count;
            var up = Vector3.up;
            Part("PLTE-11-11", origin, Quaternion.LookRotation(up, Vector3.forward));
            const float half = 0.023f;
            for (var i = 0; i < at.Length; i++) {
                var hole = origin + new Vector3(at[i].x, 0f, at[i].y);
                if (i == 0) {
                    Motor(hole - up * half, up, Vector3.forward);
                    foreach (var dz in new[] { 0.5f, 1.0f }) Screw(hole + new Vector3(0f, half, dz), up);
                }
                Shaft(hole + up * 0.2f, up, 3);
                // Its axle (the hole at its middle) up the shaft, its teeth 0.6" above the plate.
                var part = Part(sprockets[i], hole + up * 0.6f, Quaternion.LookRotation(up));
                var (center, axis) = Motion.ChainLoop.SprocketFrame(part);
                part.transform.rotation = Quaternion.FromToRotation(axis, up) * part.transform.rotation;
                (center, _) = Motion.ChainLoop.SprocketFrame(part);
                part.transform.position += hole + up * 0.6f - center;
            }
            return made.GetRange(start, made.Count - start);
        }

        /// <summary>Makes a chain with the chain tool (as clicks would): round these sprockets in order, flipping the
        /// ones listed, of a style ("chain", "tread", "traction").</summary>
        public static Motion.ChainLoop Chain(IList<GameObject> sprockets, string style = "chain", params int[] flip) {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Motion.ChainTool.Begin(sprockets[0]);
            var tool = Object.FindFirstObjectByType<Motion.ChainTool>();
            var click = typeof(Motion.ChainTool).GetMethod("Click", flags);
            for (var i = 1; i < sprockets.Count; i++) click.Invoke(tool, new object[] { sprockets[i] });
            foreach (var i in flip) click.Invoke(tool, new object[] { sprockets[i] });
            typeof(Motion.ChainTool).GetField("style", flags).SetValue(tool, style);
            typeof(Motion.ChainTool).GetMethod("Refresh", flags).Invoke(tool, null);
            var before = Object.FindObjectsByType<Motion.ChainLoop>(FindObjectsSortMode.None);
            typeof(Motion.ChainTool).GetMethod("Make", flags).Invoke(tool, null);
            var made = Object.FindObjectsByType<Motion.ChainLoop>(FindObjectsSortMode.None).FirstOrDefault(c => !before.Contains(c) && c.wraps.Count > 0);
            if (made != null) MadeAdd(made.gameObject);
            return made;
        }

        private static void MadeAdd(GameObject go) => made.Add(go);

        /// <summary>A 50 mm cylinder pinned to a plate at its back (screw and nut through its clevis), its rod's
        /// thread through a second plate with a nut: extending pushes that plate.</summary>
        public static List<GameObject> Cylinder(Vector3 origin) {
            var start = made.Count;
            var cylinder = Part("PNMT-50mm-Normal", origin, Quaternion.identity);
            Part("PLTE-5-5", origin + new Vector3(-0.314f, 0f, -0.30f), Quaternion.identity);
            Screw(origin + new Vector3(-0.314f, 0f, -0.33f), Vector3.back, "3/4in");
            Nut(origin + new Vector3(-0.314f, 0f, 0.25f), Vector3.back);
            var thread = cylinder.GetComponent<Motion.PneumaticCylinder>().Thread.transform.position;
            Part("PLTE-5-5", thread + new Vector3(0.12f, 0f, 0f), Quaternion.LookRotation(Vector3.right));
            Nut(thread + new Vector3(0.17f, 0f, 0f), Vector3.left);
            return made.GetRange(start, made.Count - start);
        }

        /// <summary>Two plates on a joint screw (the second turns about it), the first grounded when asked.
        /// Returns (first plate, second plate, screw); make the screw a joint after the frames that connect it.</summary>
        public static (GameObject a, GameObject b, GameObject screw) Pinned(Vector3 origin) {
            var flat = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            var a = Part("PLTE-5-5", origin, flat);
            var b = Part("PLTE-5-5", origin + new Vector3(2f, 0.046f, 0f), flat);
            var screw = Screw(origin + new Vector3(1f, 0.069f, 0f), Vector3.up);
            Nut(origin + new Vector3(1f, -0.023f, 0f), Vector3.up);
            return (a, b, screw);
        }

        /// <summary>A big plate (25 x 25 holes) with 14 shafts and 20 sprockets stacked at three heights (high strength
        /// at the bottom, Normal in the middle, 6P on top), joined by six chains into one system a single motor turns,
        /// and a small C-channel arm screwed to a shaft collar on one shaft (it turns with it). Returns the sprockets by
        /// chain ("A".."H"), in the order the chains go round them, and the inside (idler) ones' indexes.</summary>
        public static Dictionary<string, (List<GameObject> sprockets, int[] inside)> SprocketPlate(Vector3 origin) {
            var up = Vector3.up;
            Part("PLTE-25-25", origin, Quaternion.LookRotation(up, Vector3.forward));
            const float half = 0.023f;
            float[] level = { 0.6f, 1.25f, 1.9f };
            var shafts = new Dictionary<string, Vector2> {
                ["S0"] = new(-5f, -5f), ["S1"] = new(-1.5f, -5f), ["S2"] = new(-5f, -1.5f), ["S3"] = new(3f, -5f),
                ["S4"] = new(1f, -1.5f), ["S5"] = new(5f, 1f), ["S6"] = new(1.5f, 3f), ["S7"] = new(-2f, 4.5f),
                ["S8"] = new(-4.5f, 1f), ["S9"] = new(-5f, 5f), ["S10"] = new(-2.5f, 1.5f), ["S11"] = new(5f, 5.5f),
                ["S12"] = new(3.5f, -1f), ["S13"] = new(-2.5f, -2.5f),
            };
            Vector3 Hole(string shaft) => origin + new Vector3(shafts[shaft].x, 0f, shafts[shaft].y);
            foreach (var shaft in shafts.Keys) Shaft(Hole(shaft) + up * 1.2f, up, 4);
            Motor(Hole("S0") - up * half, up, Vector3.forward);
            foreach (var dz in new[] { 0.5f, 1.0f }) Screw(Hole("S0") + new Vector3(0f, half, dz), up);
            GameObject Sprocket(string shaft, int height, string id) {
                var at = Hole(shaft) + up * level[height];
                var part = Part(id, at, Quaternion.LookRotation(up));
                var (center, axis) = Motion.ChainLoop.SprocketFrame(part);
                part.transform.rotation = Quaternion.FromToRotation(axis, up) * part.transform.rotation;
                (center, _) = Motion.ChainLoop.SprocketFrame(part);
                part.transform.position += at - center;
                return part;
            }
            var chains = new Dictionary<string, (List<GameObject>, int[])> {
                // High strength at the bottom: the motor's chain, and two more.
                ["A"] = (new List<GameObject> { Sprocket("S0", 0, "SPKT-High Strength-12T"), Sprocket("S1", 0, "SPKT-High Strength-24T"), Sprocket("S2", 0, "SPKT-High Strength-18T") }, new int[0]),
                ["D"] = (new List<GameObject> { Sprocket("S6", 0, "SPKT-High Strength-12T"), Sprocket("S7", 0, "SPKT-High Strength-30T"), Sprocket("S8", 0, "SPKT-High Strength-6T") }, new int[0]),
                ["G"] = (new List<GameObject> { Sprocket("S5", 0, "SPKT-High Strength-18T"), Sprocket("S11", 0, "SPKT-High Strength-24T") }, new int[0]),
                // Normal in the middle.
                ["B"] = (new List<GameObject> { Sprocket("S1", 1, "SPKT-Normal-10T"), Sprocket("S3", 1, "SPKT-Normal-48T"), Sprocket("S4", 1, "SPKT-Normal-24T") }, new int[0]),
                ["E"] = (new List<GameObject> { Sprocket("S7", 1, "SPKT-Normal-15T"), Sprocket("S9", 1, "SPKT-Normal-40T"), Sprocket("S10", 1, "SPKT-Normal-24T") }, new int[0]),
                ["H"] = (new List<GameObject> { Sprocket("S0", 1, "SPKT-Normal-24T"), Sprocket("S13", 1, "SPKT-Normal-15T") }, new int[0]),
                // 6P (low strength) on top, with an idler pressing on one span.
                ["C"] = (new List<GameObject> { Sprocket("S4", 2, "VXSPROCKETS-6P 16T Sprocket"), Sprocket("S12", 2, "VXSPROCKETS-6P 8T Sprocket"), Sprocket("S5", 2, "VXSPROCKETS-6P 40T Sprocket"), Sprocket("S6", 2, "VXSPROCKETS-6P 24T Sprocket") }, new[] { 1 }),
            };
            // The arm: a collar clamped on S13 above the sprockets, a 1x2 C-channel screwed to its set-screw hole, and a
            // collar under it as a stop.
            var axle = Hole("S13");
            var h = axle + up * 2.6f;
            var r = Vector3.right;
            var collarTurn = Quaternion.LookRotation(up, -r);
            Part("CLMP-Normal-Normal", h - collarTurn * new Vector3(0f, 0.007f, 0f), collarTurn);
            Part("CLMP-Normal-Normal", h - up * 0.65f - collarTurn * new Vector3(0f, 0.007f, 0f), collarTurn);
            var channelTurn = Quaternion.LookRotation(-r, up);
            Part("CCHL-1x2-5", h + r * (0.2165f + 0.0555f) + channelTurn * new Vector3(0.75f, 0f, 0f), channelTurn);
            Screw(h + r * (0.2165f + 0.0625f), r, "1/4in");
            return chains;
        }

        /// <summary>
        /// A flat 6-motor drivetrain on 3.25" omnis, 360 rpm (600 rpm motors, 36T driving 60T), built the way teams do
        /// (the VEX Forum's "Catalogue of Drive Gearings", 6m 360 3.25): per side an inner and an outer 1x2 C-channel,
        /// 2" apart, with a gear chain in one line between them: wheel 60T, motor 36T, wheel, motor, wheel, motor
        /// (z = -5 .. 5, 2" apart: 60T + 36T pitch radii). Each wheel spins on a screw joint (its round insert; Linkage.SpinsOn): screw head, outer
        /// channel, keps nut, 1/8" spacer, wheel, 60T gear screwed to the wheel (its round insert doesn't drive it),
        /// 1/8" spacer, keps nut, inner channel, nylock (the 1/8" between wheel and keps on the outside has 0.04" to spare). Each motor sits inside the inner channel, screwed to it; its
        /// shaft goes through the inner channel, spacers, the 36T, spacers and a bearing on the outer
        /// channel. A standoff ties the channels at the back; three C-channels across the top flanges join the
        /// sides. Wheels touch the ground at y = origin.y. Returns the parts; set the motors with FinishSixMotorDrive.
        /// </summary>
        public static List<GameObject> SixMotorDrive(Vector3 origin) {
            var start = made.Count;
            const float A = 5.75f;            // inner channel's web (its origin), from the middle
            const float WebOut = 0.0555f, WebIn = 0.007f;
            const float Gap = 2.05f;          // between the webs (the gear-to-wheel screws end 1/32" short of the outer one)
            const float Axle = 1.625f;        // axles' height: the wheel's radius
            const float H = Axle + 0.25f;     // channels' middle: axles on their lower row of holes
            float[] wheels = { -5f, -1f, 3f };
            float[] motors = { -3f, 1f, 5f };
            var up = Vector3.up;
            foreach (var s in new[] { -1f, 1f }) {
                var o = Vector3.right * s;
                Vector3 At(float x, float y, float z) => origin + new Vector3(x * s, y, z);
                var xi = A + WebOut;                    // inner web's outer face
                var B = xi + Gap + 0.055f;              // outer channel's origin
                Part("CCHL-1x2-25", At(A, H, 0f), Quaternion.LookRotation(o, up));      // flanges inward
                Part("CCHL-1x2-25", At(B, H, 0f), Quaternion.LookRotation(-o, up));     // flanges outward
                var turn = Quaternion.LookRotation(o, up);
                foreach (var z in wheels) {
                    Nut(At(xi, Axle, z), -o);                                            // keps on the inner web
                    Part("SPCR-Nylon-1/8in", At(xi + 0.2035f, Axle, z), turn);
                    var gear = Part("GEAR-High Strength-60T", At(xi + 0.516f, Axle, z), turn);
                    Part("OMNI-V2-3.25in", At(xi + 1.255f, Axle, z), turn);
                    Part("SPCR-Nylon-1/8in", At(xi + 1.8065f, Axle, z), turn);
                    Nut(At(xi + Gap, Axle, z), o);                                       // keps on the outer web
                    Screw(At(B + WebIn, Axle, z), o, "2.50in");                          // head outside the outer web
                    Part("NUT-Lock", At(A - WebIn, Axle, z), Quaternion.LookRotation(o)); // nylock inside the inner web
                    // The gear screwed to the wheel through their matching holes, 1" out from the axle.
                    foreach (var side in new[] { 1f, -1f }) {
                        var offset = gear.transform.rotation * new Vector3(side, 0f, 0f);
                        Screw(At(xi + 0.266f, Axle, z) + offset, -o, "1.75in");
                        Nut(At(xi + 1.744f, Axle, z) + offset, -o);
                    }
                }
                foreach (var z in motors) {
                    Motor(At(A - WebIn, Axle, z), o, Vector3.back);
                    foreach (var dz in new[] { 0.5f, 1.0f }) Screw(At(xi, Axle, z - dz), o);
                    Part("SHFT-Normal-3", At(A + 1.1f, Axle, z), turn);                       // the 36T held by its insert
                    Part("SPCR-Nylon-1/4in", At(xi + 0.133f, Axle, z), turn);
                    Part("GEAR-High Strength-36T", At(xi + 0.516f, Axle, z), turn);
                    Part("SPCR-Nylon-1/2in", At(xi + 1.016f, Axle, z), turn);
                    Part("SPCR-Nylon-1/2in", At(xi + 1.516f, Axle, z), turn);
                    Part("SPCR-Nylon-1/4in", At(xi + 1.891f, Axle, z), turn);
                    // A bearing on the outer web, screwed through it into keps nuts.
                    Part("BRNG-Normal", At(B + WebIn + 0.143f, Axle, z), turn);
                    foreach (var dz in new[] { -0.5f, 0.5f }) {
                        Screw(At(B + WebIn + 0.28f, Axle, z + dz), o, "3/8in");
                        Nut(At(xi + Gap, Axle, z + dz), o);
                    }
                }
                // A standoff between the channels at the back.
                Part("SNDF-2.00in", At(xi + Gap / 2f, Axle, 6f), turn);
                Screw(At(B + WebIn, Axle, 6f), o, "1/4in");
                Screw(At(A - WebIn, Axle, 6f), -o, "1/4in");
            }
            // Across the top flanges, clear of the motors: screws down through both into nuts under the flange.
            const float Top = H + 0.5f;
            foreach (var z in new[] { -5.25f, -1.25f, 2.75f }) {
                Part("CCHL-1x2-23", origin + new Vector3(0f, Top + 0.055f, z), Frame(Vector3.down, Vector3.right));
                foreach (var s in new[] { -1f, 1f })
                    foreach (var dz in new[] { -0.25f, 0.25f }) {
                        var at = origin + new Vector3((A - 0.25f) * s, Top, z + dz);
                        Screw(at + up * (0.0625f + 0.007f), up);
                        Nut(at - up * 0.0625f, up);
                    }
            }
            return made.GetRange(start, made.Count - start);
        }

        /// <summary>
        /// V2 (tutorial 2, the developer's team's drivetrain from their photos): six motors on 3.25" omnis, an inner and an
        /// outer 31-hole 1x2 C-channel per side 2" apart, one gear line between them from back to front: wheel 60T, motor
        /// 36T, wheel, motor, idler 36T (on a screw), motor, wheel (z -5.5, -3.5, -1.5, 0.5, 2, 3.5, 5.5: the two wheels
        /// close together at the back and one at the front, as the photos). Motors 600 rpm: 360 rpm at the wheels.
        /// Standoffs tie each side's rails at both ends; three 23-hole C-channels across the inner rails' top flanges.
        /// Parts are tagged (Tags) by the stage the video shows them in (the video builds the left side, then mirrors it):
        /// rail1 (the left inner rail), lframe (its outer rail and standoffs), w1screw / w1parts / w1wheel / w1bolts (the
        /// back wheel: its axle screw, the nuts, spacers and gear on it, the wheel, the bolts holding the gear to the
        /// wheel), w2parts / w2wheel / w2group (the next wheel: its wheel, and its gear and bolts: pasted with it, never revealed), lwheels (the front wheel), m1motor, lmotors (the left
        /// motors, their gears, the idler), rside (the whole right side: made by the mirror), bars, bar1screw, bar1nut,
        /// barscrews.
        /// </summary>
        public static List<GameObject> TutorialDrive(Vector3 origin) {
            var start = made.Count;
            Tags.Clear();
            const float A = 5.75f, WebOut = 0.0555f, WebIn = 0.007f, Gap = 2.05f, Axle = 1.625f, H = Axle + 0.25f;
            float[] wheels = { -5.5f, -1.5f, 5.5f };
            float[] motors = { -3.5f, 0.5f, 3.5f };
            const float Idler = 2f;
            var up = Vector3.up;
            foreach (var s in new[] { -1f, 1f }) {
                var left = s < 0f;
                var o = Vector3.right * s;
                Vector3 At(float x, float y, float z) => origin + new Vector3(x * s, y, z);
                var xi = A + WebOut;
                var B = xi + Gap + 0.055f;
                var turn = Quaternion.LookRotation(o, up);
                var rest = left ? null : "rside";
                Tag = left ? "rail1" : rest;
                Part("CCHL-1x2-31", At(A, H, 0f), Quaternion.LookRotation(o, up));
                Tag = rest ?? "lframe";
                Part("CCHL-1x2-31", At(B, H, 0f), Quaternion.LookRotation(-o, up));
                foreach (var z in new[] { -7.5f, 7.5f }) {
                    Part("SNDF-2.00in", At(xi + Gap / 2f, Axle, z), turn);
                    Screw(At(B + WebIn, Axle, z), o, "1/4in");
                    Screw(At(A - WebIn, Axle, z), -o, "1/4in");
                }
                foreach (var z in wheels) {
                    // the left wheels in the order the video makes them: w1 (back), w2, then the front one
                    var w = !left ? 0 : z == wheels[0] ? 1 : z == wheels[1] ? 2 : 3;
                    var parts = rest ?? (w == 1 ? "w1parts" : w == 2 ? "w2parts" : "lwheels");
                    Tag = parts;
                    Nut(At(xi, Axle, z), -o);
                    Part("SPCR-Nylon-1/8in", At(xi + 0.2035f, Axle, z), turn);
                    if (w == 2) Tag = "w2group";   // the second wheel is pasted with its gear and bolts
                    var gear = Part("GEAR-High Strength-60T", At(xi + 0.516f, Axle, z), turn);
                    Tag = rest ?? (w == 1 ? "w1wheel" : w == 2 ? "w2wheel" : "lwheels");
                    Part("OMNI-V2-3.25in", At(xi + 1.255f, Axle, z), turn);
                    Tag = parts;
                    Part("SPCR-Nylon-1/8in", At(xi + 1.8065f, Axle, z), turn);
                    Nut(At(xi + Gap, Axle, z), o);
                    Tag = rest ?? (w == 1 ? "w1screw" : parts);
                    Screw(At(B + WebIn, Axle, z), o, "2.50in");
                    Tag = parts;
                    Part("NUT-Lock", At(A - WebIn, Axle, z), Quaternion.LookRotation(o));
                    Tag = rest ?? (w == 1 ? "w1bolts" : w == 2 ? "w2group" : "lwheels");
                    foreach (var side in new[] { 1f, -1f }) {
                        var offset = gear.transform.rotation * new Vector3(side, 0f, 0f);
                        Screw(At(xi + 0.266f, Axle, z) + offset, -o, "1.75in");
                        Nut(At(xi + 1.744f, Axle, z) + offset, -o);
                    }
                }
                foreach (var z in motors) {
                    var first = left && z == motors[0];
                    Tag = rest ?? (first ? "m1motor" : "lmotors");
                    Motor(At(A - WebIn, Axle, z), o, Vector3.back);
                    Tag = rest ?? "lmotors";
                    foreach (var dz in new[] { 0.5f, 1.0f }) Screw(At(xi, Axle, z - dz), o);
                    Part("SHFT-Normal-3", At(A + 1.1f, Axle, z), turn);
                    Part("SPCR-Nylon-1/4in", At(xi + 0.133f, Axle, z), turn);
                    Part("GEAR-High Strength-36T", At(xi + 0.516f, Axle, z), turn);
                    Part("SPCR-Nylon-1/2in", At(xi + 1.016f, Axle, z), turn);
                    Part("SPCR-Nylon-1/2in", At(xi + 1.516f, Axle, z), turn);
                    Part("SPCR-Nylon-1/4in", At(xi + 1.891f, Axle, z), turn);
                    Part("BRNG-Normal", At(B + WebIn + 0.143f, Axle, z), turn);
                    foreach (var dz in new[] { -0.5f, 0.5f }) {
                        Screw(At(B + WebIn + 0.28f, Axle, z + dz), o, "3/8in");
                        Nut(At(xi + Gap, Axle, z + dz), o);
                    }
                }
                // The idler between the middle and front motors: a 36T spinning on a screw, like the wheels.
                Tag = rest ?? "lmotors";
                Nut(At(xi, Axle, Idler), -o);
                Part("SPCR-Nylon-1/8in", At(xi + 0.2035f, Axle, Idler), turn);
                Part("GEAR-High Strength-36T", At(xi + 0.516f, Axle, Idler), turn);
                Part("SPCR-Nylon-1/2in", At(xi + 1.016f, Axle, Idler), turn);
                Part("SPCR-Nylon-1/2in", At(xi + 1.516f, Axle, Idler), turn);
                Part("SPCR-Nylon-1/8in", At(xi + 1.8065f, Axle, Idler), turn);
                Nut(At(xi + Gap, Axle, Idler), o);
                Screw(At(B + WebIn, Axle, Idler), o, "2.50in");
                Part("NUT-Lock", At(A - WebIn, Axle, Idler), Quaternion.LookRotation(o));
            }
            const float Top = H + 0.5f;
            var firstBar = true;
            foreach (var z in new[] { -7.25f, -1.25f, 4.75f }) {
                Tag = "bars";
                Part("CCHL-1x2-23", origin + new Vector3(0f, Top + 0.055f, z), Frame(Vector3.down, Vector3.right));
                foreach (var s in new[] { -1f, 1f })
                    foreach (var dz in new[] { -0.25f, 0.25f }) {
                        Tag = firstBar ? "bar1screw" : "barscrews";
                        var at = origin + new Vector3((A - 0.25f) * s, Top, z + dz);
                        Screw(at + up * (0.0625f + 0.007f), up);
                        Tag = firstBar ? "bar1nut" : "barscrews";
                        firstBar = false;
                        Nut(at - up * 0.0625f, up);
                    }
            }
            Tag = null;
            return made.GetRange(start, made.Count - start);
        }

        /// <summary>
        /// V2 (tutorial 5): what holds the polycarbonate corner pieces of the developer's team's drivetrain (their photos)
        /// over each end of <see cref="TutorialDrive"/>: black 7/8" nylon spacers standing on the rails' top flanges (the
        /// piece clears the cross bars and sits level with the wheels' tops), five a corner, kept off the cross bars, and
        /// a screw down through each (piece, spacer, flange) with a nut under the flange. Tags: spbl (the back left
        /// corner's spacers), sprest (the other corners'), cscrews (every screw and nut). The pieces themselves are
        /// sketched in the video. <paramref name="plateTop"/>: the pieces' top face (where the screw heads sit).
        /// </summary>
        public static List<GameObject> TutorialCorners(float plateTop) {
            var start = made.Count;
            const float RailTop = 2.374f, Spacer = 0.875f, Inner = 5.492f, Outer = 8.168f;
            var spots = new List<(float x, float z)> { (Outer, 7f), (Outer, 5f), (Outer, 3.5f), (Inner, 6.5f), (Inner, 3.5f) };
            const string Id = "VXSPACERS-7/8\" Nylon Spacer (5/16\" OD)";
            // The spacer's hole runs up, its own x along the robot's x (a sketch on its top lines up with the robot).
            var probe = PartsManager.GeneratePart(Id, Vector3.one * 500f, Quaternion.identity);
            var hole = probe.GetComponentsInChildren<HoleCollider>(true).OrderBy(h => h.transform.localPosition.sqrMagnitude).First().transform;
            var axisLocal = hole.localRotation * Vector3.forward;
            var holeLocal = hole.localPosition;
            var rightLocal = Vector3.ProjectOnPlane(Vector3.right, axisLocal).sqrMagnitude > 0.5f ? Vector3.ProjectOnPlane(Vector3.right, axisLocal).normalized : Vector3.ProjectOnPlane(Vector3.forward, axisLocal).normalized;
            Object.DestroyImmediate(probe);
            var turn = Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(rightLocal, axisLocal));
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    foreach (var (x, z) in spots) {
                        var at = new Vector3(x * sx, RailTop, z * sz);
                        Tag = sx < 0f && sz < 0f ? "spbl" : "sprest";
                        var middle = at + Vector3.up * (Spacer / 2f);
                        var spacer = Part(Id, middle - turn * holeLocal, turn);
                        if (spacer.TryGetComponent(out Renderer renderer))
                            foreach (var m in renderer.materials) m.color = new Color(0.06f, 0.06f, 0.065f);
                        Tag = "cscrews";
                        Screw(new Vector3(at.x, plateTop + 0.007f, at.z), Vector3.up, "1.25in");
                        Nut(at - Vector3.up * 0.0625f, Vector3.up);
                    }
            Tag = null;
            return made.GetRange(start, made.Count - start);
        }

        /// <summary>After the screws have connected (a few physics frames): motors get blue (600 rpm) cartridges,
        /// sides and names, the right side reversed, split arcade. (The wheels spin on their screws by themselves:
        /// Linkage.SpinsOn.)</summary>
        public static void FinishSixMotorDrive(List<GameObject> parts) {
            var motors = parts.Where(p => p != null && p.GetComponent<Motion.MotorDrive>() != null).OrderBy(p => p.transform.position.z).ToList();
            foreach (var motor in motors) {
                var left = motor.transform.position.x < 0f;
                var drive = motor.GetComponent<Motion.MotorDrive>();
                drive.SetCartridge(600, false);
                Motion.Controls.SetSide(motor, left ? "L" : "R");
                Motion.Controls.SetReversed(motor, !left);   // forward drives toward +z, the front the game guesses (right side x+)
                var index = motors.Where(m => (m.transform.position.x < 0f) == left).ToList().IndexOf(motor);
                Motion.Controls.SetName(motor, (left ? "Left " : "Right ") + new[] { "front", "middle", "back" }[index]);
            }
            Motion.Controls.Drive = Motion.Controls.DriveMode.SplitArcade;
        }

        // ------------------------------------------------------------------ V2: the Override demo robot (Phase 6)

        /// <summary>The part's hole nearest a point (world), optionally only holes along an axis.</summary>
        public static HoleCollider HoleNear(GameObject part, Vector3 world, Vector3? axis = null) {
            HoleCollider best = null;
            var bestDistance = float.MaxValue;
            foreach (var h in part.GetComponentsInChildren<HoleCollider>(true)) {
                if (axis is Vector3 a && Mathf.Abs(Vector3.Dot(h.transform.forward, a)) < 0.98f) continue;
                var d = (h.transform.position - world).sqrMagnitude;
                if (d < bestDistance) {
                    bestDistance = d;
                    best = h;
                }
            }
            return best;
        }

        /// <summary>Moves a part (already turned) so its hole nearest `hint` (world, before the move) is at `target`.</summary>
        public static GameObject Seat(GameObject part, Vector3 hint, Vector3 target, Vector3? axis = null) {
            var hole = HoleNear(part, hint, axis);
            if (hole != null) part.transform.position += target - hole.transform.position;
            Physics.SyncTransforms();
            return part;
        }

        /// <summary>C's screw and nut through the holes on a line (through `point`, going `into`): the first hole met
        /// going that way and every hole touching it. False when nothing could be fastened there.</summary>
        public static bool Bolt(Vector3 point, Vector3 into) {
            Physics.SyncTransforms();
            HoleCollider first = null;
            var firstAlong = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(point - into * 1f, into, 2f, 1 << HoleCollider.HOLE_COLLISIONS_LAYER, QueryTriggerInteraction.Collide)) {
                if (!hit.collider.TryGetComponent(out HoleCollider hole) || Mathf.Abs(Vector3.Dot(hole.transform.forward, into)) < 0.98f) continue;
                var offset = hole.transform.position - point;
                if (Vector3.ProjectOnPlane(offset, into).magnitude > 0.06f) continue;
                var along = Vector3.Dot(offset, into) - hole.transform.lossyScale.z / 2f;
                if (along < -0.2f || along >= firstAlong) continue;
                firstAlong = along;
                first = hole;
            }
            if (first == null) return false;
            var face = first.transform.position - into * (first.transform.lossyScale.z / 2f);
            face += Vector3.ProjectOnPlane(point - face, into) * 0f;
            return Tools.QuickPlace.FastenAt(face, into, false, first);
        }

        private static List<GameObject> demo;

        /// <summary>
        /// The Override demo robot on the 6-motor drive (load it first, at the origin, front +z): a deck on standoffs over
        /// the top cross-members with 1x5 C-channel rails along its sides and a back wall (a hopper for pieces put in by
        /// hand or a Match Load); a front intake on plates bolted to the rails (a tray 1/2" off the floor on two angles and a
        /// top roller of 3" flex wheels, one 600 rpm motor) that pulls a lying pin or cup in and holds it; the V5 Brain on
        /// the right rail; and a 12" arm at the back on a shaft turned by a 100 rpm motor (stowed upright, it swings back
        /// over the robot to push a Toggle, 13" up the wall). 16.9" x 16.8" x 17.2". FinishDemoRobot after the screws connect.
        /// </summary>
        public static string DemoRobot() {
            var log = new System.Text.StringBuilder();
            var start = made.Count;
            var loaded = PartsManager.FindLoadedObjects();
            var crosses = loaded.Where(p => p.GetComponent<SavedObject>() != null && p.GetComponent<SavedObject>().id == "CCHL-1x2-23")
                .OrderBy(p => p.transform.position.z).ToList();
            if (crosses.Count != 3) return "Load the 6 motor drive first (its three top C-channels weren't found)";
            var up = Vector3.up;
            var fails = 0;
            void Fasten(Vector3 point, Vector3 into, string what) {
                if (!Bolt(point, into)) {
                    fails++;
                    log.AppendLine("couldn't fasten " + what + " at " + point.ToString("F3"));
                }
            }

            // The deck: 1" standoffs on the top C-channels (screws up through their webs), a 21 x 23 plate on top.
            var webHole = HoleNear(crosses[1], new Vector3(5f, 2.4f, -1f), up);
            var webTop = webHole.transform.position.y + webHole.transform.lossyScale.z / 2f;
            var webBottom = webTop - webHole.transform.lossyScale.z;
            var standoffs = new[] { new Vector2(5f, -5f), new Vector2(5f, -1f), new Vector2(5f, 3f), new Vector2(-5f, -5f), new Vector2(-5f, -1f), new Vector2(-5f, 3f) };
            foreach (var s in standoffs) Part("SNDF-1.00in", new Vector3(s.x, webTop + 0.5f, s.y), Quaternion.LookRotation(up));
            var deckY = webTop + 1f + 0.023f;
            var deckTop = deckY + 0.023f;
            var deck = Part("PLTE-21-23", new Vector3(0f, deckY, 0.5f), Quaternion.LookRotation(up, Vector3.forward));
            Physics.SyncTransforms();
            foreach (var s in standoffs) {
                Fasten(new Vector3(s.x, webBottom, s.y), up, "a standoff to the top channel");
                Fasten(new Vector3(s.x, deckTop, s.y), Vector3.down, "the deck to a standoff");
            }

            // The rails: 1x5 C-channels, 30 holes (z -4.75 to 10.25), web outward at the deck's sides, bottom flange on it (flanges in).
            var rails = new Dictionary<float, GameObject>();
            foreach (var side in new[] { 1f, -1f }) {
                var o = Vector3.right * side;
                var rail = Part("CCHL-1x5-30", new Vector3(5f * side, deckTop + 1.25f, 2.75f), Quaternion.LookRotation(o, up));
                Seat(rail, new Vector3(5f * side, deckTop, -4.5f), new Vector3(5f * side, deckTop + 0.0315f, -4.5f), up);
                rails[side] = rail;
                foreach (var z in new[] { -4f, -2f, 0f, 2f, 4f, 6f }) Fasten(new Vector3(5f * side, deckTop + 0.07f, z), Vector3.down, "a rail to the deck");
            }
            // The back wall: a 1x5 C-channel across the back, flanges in.
            var back = Part("CCHL-1x5-19", new Vector3(0f, deckTop + 1.25f, -4.75f), Quaternion.LookRotation(Vector3.back, up));
            Seat(back, new Vector3(0f, deckTop, -4.5f), new Vector3(0f, deckTop + 0.0315f, -4.5f), up);
            foreach (var x in new[] { -4f, -2f, 0f, 2f, 4f }) Fasten(new Vector3(x, deckTop + 0.07f, -4.5f), Vector3.down, "the back wall to the deck");

            // The rails' web: its holes' rows and outside faces (measured).
            var railHole = HoleNear(rails[1f], new Vector3(5.3f, deckTop + 1.25f, 7f), Vector3.right);
            var row = railHole.transform.position.y;              // the middle row
            var webOut = railHole.transform.position.x + railHole.transform.lossyScale.z / 2f;
            var webIn = webOut - railHole.transform.lossyScale.z;
            float Row(float k) => row + 0.5f * k;                 // rows on the 1/2" grid, below and above the middle one

            // The intake (front, z 6.25 to 10.25: with the back wheels at -6.6 the robot is 16.9" deep, so facing into the
            // field it starts clear of the Goals): side plates (8 x 14 holes) flat against the rails' webs outside, a 1x1
            // angle along each one's bottom edge, a tray across them 1/2" off the floor, and a top roller (four 3" flex
            // wheels) over it: driving into a lying pin or cup pushes it over the tray's lip and the roller pulls it in
            // against the drive and holds it (reverse to let it go).
            var plateCenterY = (Row(-8f) + Row(5f)) / 2f;         // (its holes are on the rail's grid)
            foreach (var side in new[] { 1f, -1f }) {
                var plate = Part("PLTE-8-14", new Vector3((webOut + 0.023f) * side, plateCenterY, 8.25f), Quaternion.LookRotation(Vector3.right * side, up));
                Seat(plate, new Vector3((webOut + 0.023f) * side, Row(-1f), 9f), new Vector3((webOut + 0.023f) * side, Row(-1f), 9f), Vector3.right);
                foreach (var (z, k) in new[] { (7f, -2f), (7f, 2f), (10f, -2f), (10f, 2f) })
                    Fasten(new Vector3((webOut + 0.046f) * side, Row(k), z), Vector3.left * side, "an intake plate to a rail");
                // The angle: one leg against the plate's inside at its bottom row, the other leg inward, under the tray.
                var angle = Part("ANGL-1x1-8", new Vector3((webOut - 0.03f) * side, Row(-8f), 8.25f), Quaternion.LookRotation(Vector3.right * side, Vector3.down));
                Seat(angle, new Vector3((webOut - 0.03f) * side, Row(-8f), 7f), new Vector3((webOut - 0.0315f) * side, Row(-8f), 7f), Vector3.right);
                foreach (var z in new[] { 7f, 9.5f })
                    Fasten(new Vector3((webOut + 0.046f) * side, Row(-8f), z), Vector3.left * side, "an angle to an intake plate");
            }
            var plateOut = webOut + 0.046f;
            var legHole = HoleNear(Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None).Where(o => o.id == "ANGL-1x1-8").Select(o => o.gameObject).First(),
                new Vector3(5f, Row(-8f) - 0.265f, 7f), up);
            var legTop = legHole.transform.position.y + legHole.transform.lossyScale.z / 2f;
            Part("PLTE-21-8", new Vector3(0f, legTop + 0.023f, 8.25f), Quaternion.LookRotation(up, Vector3.forward));
            foreach (var x in new[] { 5f, -5f })
                foreach (var z in new[] { 7f, 9.5f })
                    Fasten(new Vector3(x, legTop + 0.047f, z), Vector3.down, "the tray to an angle");
            // The top roller: a shaft through both plates and the rails, four 3" flex wheels, a 600 rpm motor outside the
            // right plate.
            var t1 = new Vector3(0f, Row(-1f), 8.5f);
            Part("SHFT-Normal-12", t1, Quaternion.LookRotation(Vector3.right));
            foreach (var x in new[] { -3f, -1f, 1f, 3f }) Part("FWHL-With Adapters-3.00in", t1 + Vector3.right * x, Quaternion.LookRotation(Vector3.right));
            var intake = Motor(new Vector3(plateOut, t1.y, t1.z), Vector3.left, Vector3.up);
            foreach (var d in new[] { 0.5f, 1f }) Fasten(new Vector3(plateOut - 0.046f - 0.001f, t1.y + d, t1.z), Vector3.right, "the intake motor");

            // The arm: a shaft through both rails' top row at z -4, a 100 rpm motor outside the left rail, a collar
            // clamped on the shaft at the middle and a 12" C-channel screwed to its set-screw hole, standing up.
            var pivot = new Vector3(0f, Row(2f), -4f);
            Part("SHFT-Normal-12", pivot, Quaternion.LookRotation(Vector3.right));
            var armMotor = Motor(new Vector3(-webOut, pivot.y, pivot.z), Vector3.right, Vector3.forward);
            foreach (var d in new[] { 0.5f, 1f }) Fasten(new Vector3(-webIn + 0.001f, pivot.y, pivot.z + d), Vector3.left, "the arm motor");
            var a = Vector3.right;
            var r = Vector3.forward;
            var collarTurn = Quaternion.LookRotation(a, -r);
            Part("CLMP-Normal-Normal", pivot - collarTurn * new Vector3(0f, 0.007f, 0f), collarTurn);
            var armTurn = Quaternion.LookRotation(-r, a);
            var arm = Part("CCHL-1x2-24", pivot + up * 6f, armTurn);
            // Its center-row hole nearest the bottom end goes over the set-screw hole.
            HoleCollider end = null;
            foreach (var h in arm.GetComponentsInChildren<HoleCollider>(true)) {
                var lp = arm.transform.InverseTransformPoint(h.transform.position);
                if (Mathf.Abs(lp.y) > 0.05f || Mathf.Abs(Vector3.Dot(h.transform.forward, r)) < 0.98f) continue;
                if (end == null || h.transform.position.y < end.transform.position.y) end = h;
            }
            if (end != null) arm.transform.position += pivot + r * 0.248f - end.transform.position;
            Physics.SyncTransforms();
            if (!Bolt(pivot + r * (0.2165f + 0.0625f), -r)) {
                // (as the sprocket plate does it: a 1/4" screw from the arm's web into the collar's thread)
                Screw(pivot + r * (0.2165f + 0.0625f), r, "1/4in");
            }

            // The V5 Brain on the right rail's outside at the back (its weight balances the intake), screwed through
            // the rail's web into its four threaded holes.
            var brain = Part("BRAN", new Vector3(webOut + 0.62f, (Row(-1f) + Row(2f)) / 2f, -3f), Quaternion.LookRotation(up, Vector3.right));
            Seat(brain, new Vector3(webOut + 0.12f, Row(-1f), -4.5f), new Vector3(webOut + 0.125f, Row(-1f), -4.5f), Vector3.right);
            foreach (var (k, z) in new[] { (-1f, -4.5f), (2f, -4.5f), (-1f, -1.5f), (2f, -1.5f) })
                Fasten(new Vector3(webIn - 0.001f, Row(k), z), Vector3.right, "the brain");

            demo = made.GetRange(start, made.Count - start);
            demo.AddRange(new[] { intake, armMotor });
            log.Insert(0, "made " + (made.Count - start) + " parts, " + fails + " fastenings failed\n");
            return log.ToString();
        }

        /// <summary>After the screws connected: the chain, the motors' cartridges, names and keys (intake I/K, arm U/J).</summary>
        public static string FinishDemoRobot() {
            var log = new System.Text.StringBuilder();
            var sprockets = Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None).Where(s => s.id == "SPKT-Normal-15T" && s.transform.position.z > 6f)
                .Select(s => s.gameObject).OrderByDescending(s => s.transform.position.z).ToList();
            if (sprockets.Count == 2) {
                var chain = Chain(sprockets);
                log.AppendLine("chain " + (chain != null ? "made" : "NOT made"));
            }
            foreach (var motor in Object.FindObjectsByType<Motion.MotorDrive>(FindObjectsSortMode.None)) {
                var p = motor.transform.position;
                if (p.y < 3f && p.z < 6.5f) continue;   // the drive's own
                var drive = motor;
                if (p.z < 0f) {
                    drive.SetCartridge(100, false);
                    Motion.Controls.SetName(motor.gameObject, "Arm");
                    Motion.Controls.SetHold(motor.gameObject, true);   // stays up (and where it's left)
                    Motion.Controls.SetForwardKey(motor.gameObject, UnityEngine.InputSystem.Key.U);
                    Motion.Controls.SetReverseKey(motor.gameObject, UnityEngine.InputSystem.Key.J);
                }
                else {
                    drive.SetCartridge(600, false);
                    Motion.Controls.SetName(motor.gameObject, "Intake");
                    Motion.Controls.SetForwardKey(motor.gameObject, UnityEngine.InputSystem.Key.I);
                    Motion.Controls.SetReverseKey(motor.gameObject, UnityEngine.InputSystem.Key.K);
                }
                log.AppendLine("motor " + Motion.Controls.GivenName(motor.gameObject) + " at " + p.ToString("F1"));
            }
            return log.ToString();
        }

        /// <summary>A drivetrain: rails along z at x = ±5, a plate across their top flanges, a motor inside each
        /// rail at each end driving a 4" wheel outside it (omni at the front, traction at the back).</summary>
        public static List<GameObject> Drive(Vector3 origin) {
            var start = made.Count;
            foreach (var side in new[] { -1f, 1f }) {
                var outward = Vector3.right * side;
                // A rail: web facing outward, flanges inward, top and bottom.
                var rail = origin + new Vector3(5f * side, 0f, 0f);
                Part("CCHL-1x2-15", rail, Quaternion.LookRotation(outward, Vector3.up));
                var inner = 5f - 0.007f;
                var outer = 5f + 0.0555f;
                foreach (var end in new[] { -1f, 1f }) {
                    var z = 3f * end;
                    var shaftAt = origin + new Vector3(0f, 0.25f, z);
                    Motor(shaftAt + outward * inner, outward, Vector3.back * end);
                    // Screws from outside the web into the motor's threaded holes.
                    foreach (var dz in new[] { 0.5f, 1.0f })
                        Screw(shaftAt + outward * outer + Vector3.back * end * dz, outward);
                    Shaft(shaftAt + outward * 5.25f, outward, 3);
                    var wheel = end > 0 ? "OMNI-V2-4.00in" : "TWHL-V2-4.00in";
                    Part(wheel, shaftAt + outward * 5.75f, Quaternion.LookRotation(outward));
                }
                // Plate to rail: screws down through the plate and top flange into nuts under it.
                foreach (var z in new[] { -1f, 1f }) {
                    var at = origin + new Vector3(4.75f * side, 0.47f, z);
                    Screw(at + Vector3.up * (0.02f + 0.046f), Vector3.up);
                    Nut(at - Vector3.up * 0.02f, Vector3.up);
                }
            }
            Part("PLTE-20-5", origin + new Vector3(0f, 0.47f + 0.02f + 0.023f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
            return made.GetRange(start, made.Count - start);
        }
    
        // ------------------------------------------------------------------ V2: chain tensioners

        /// <summary>A flat plate (holes up) whose two end holes are on <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static GameObject PlateBetween(string id, Vector3 a, Vector3 b) {
            var part = Part(id, a, Quaternion.LookRotation(Vector3.up, Vector3.forward));
            Physics.SyncTransforms();
            var holes = part.GetComponentsInChildren<HoleCollider>(true).Where(h => Mathf.Abs(Vector3.Dot(h.transform.forward, Vector3.up)) > 0.98f).ToList();
            HoleCollider h1 = null, h2 = null;
            var most = -1f;
            foreach (var x in holes)
                foreach (var y in holes) {
                    var d = (x.transform.position - y.transform.position).sqrMagnitude;
                    if (d > most) {
                        most = d;
                        h1 = x;
                        h2 = y;
                    }
                }
            if (h1 == null) return part;
            var have = Vector3.ProjectOnPlane(h2.transform.position - h1.transform.position, Vector3.up);
            var want = Vector3.ProjectOnPlane(b - a, Vector3.up);
            part.transform.RotateAround(h1.transform.position, Vector3.up, Vector3.SignedAngle(have, want, Vector3.up));
            part.transform.position += a - h1.transform.position;
            Physics.SyncTransforms();
            return part;
        }

        /// <summary>A round part (spacer, sprocket) on an upright axle through (x, z): its bottom at <paramref name="bottom"/>
        /// (or, with <paramref name="middle"/>, its teeth's middle there). Returns it; its top is its renderer's top.</summary>
        public static GameObject Stand(string id, Vector3 at, bool middle = false) {
            var part = Part(id, at, Quaternion.identity);
            Physics.SyncTransforms();
            var (_, axis) = Motion.ChainLoop.SprocketFrame(part);
            part.transform.rotation = Quaternion.FromToRotation(axis, Vector3.up) * part.transform.rotation;
            Physics.SyncTransforms();
            var (center, _) = Motion.ChainLoop.SprocketFrame(part);
            var y = middle ? at.y - center.y : at.y - part.GetComponent<Renderer>().bounds.min.y;
            part.transform.position += new Vector3(at.x - center.x, y, at.z - center.z);
            Physics.SyncTransforms();
            return part;
        }

        private static float Top(GameObject part) => part.GetComponent<Renderer>().bounds.max.y;

        // Shaft collars on a hanging shaft: one on top of what holds it, one under the sprocket (a shaft joins the
        // parts between two collars).
        private static void Collars(Vector3 axle, float holderTop, GameObject sprocket) {
            Stand("CLMP-Normal-Normal", new Vector3(axle.x, holderTop, axle.z));
            var below = Stand("CLMP-Normal-Normal", new Vector3(axle.x, 0f, axle.z));
            below.transform.position += Vector3.up * (sprocket.GetComponent<Renderer>().bounds.min.y - Top(below));
            Physics.SyncTransforms();
        }

        private const string Spacer1 = "VXSPACERS-1.00\" Nylon Spacer (3/8\" OD)";
        private const string SpacerHalf = "VXSPACERS-1/2\" Nylon Spacer (3/8\" OD)";
        private const string SpacerQuarter = "VXSPACERS-1/4\" Nylon Spacer (3/8\" OD)";

        public static readonly Dictionary<string, GameObject> TensionerParts = new();

        /// <summary>
        /// Two chains with tensioners, as teams build them, on a 25 x 25 plate (chains 3/4" up, x = -3 and x = +3):
        /// station 1 (left): a motor's 15T drives a 24T on a shaft hanging from a C-channel arm that a second motor (100
        /// rpm, U/J, hold) swings on a shaft collar; the tensioner is a 1/2" spacer on a screw at the end of a 1x6 plate
        /// that turns on a joint screw over a 1" spacer, a #32 band (two wraps) pulling it into the chain. Station 2
        /// (right): 15T to a fixed 24T, its tensioner a 10T sprocket on a shaft through the arm's end. FinishTensioners
        /// after the screws connect.
        /// </summary>
        public static List<GameObject> TensionerRig(Vector3 origin) {
            var start = made.Count;
            TensionerParts.Clear();
            var up = Vector3.up;
            const float half = 0.023f, chainY = 0.75f;
            var plate = Part("PLTE-25-25", origin, Quaternion.LookRotation(up, Vector3.forward));
            TensionerParts["plate"] = plate;
            Vector3 Hole(float x, float z) => origin + new Vector3(x, 0f, z);

            GameObject DriveSprocket(float x, float z, Vector3 along, string id) {
                var hole = Hole(x, z);
                Motor(hole - up * half, up, along);
                foreach (var d in new[] { 0.5f, 1.0f }) Screw(hole + new Vector3(0f, half, 0f) + along * d, up);
                Shaft(hole + up * 0.2f, up, 3);
                return Stand(id, hole + up * chainY, true);
            }

            // The tensioner arm: a 1x6 plate on a 1" spacer, a joint screw down through both and the plate.
            GameObject Arm(string key, Vector3 pivot, Vector3 idler) {
                var column = Stand(Spacer1, pivot + up * half);
                var armY = Top(column) + half;
                var arm = PlateBetween("PLTE-1-6", new Vector3(pivot.x, armY, pivot.z), new Vector3(idler.x, armY, idler.z));
                var screw = Screw(new Vector3(pivot.x, armY + half, pivot.z), up, "1.25in");
                Nut(pivot - up * half, up);
                TensionerParts[key + " arm"] = arm;
                TensionerParts[key + " pivot"] = screw;
                return arm;
            }

            // A band post on the plate: 1" + 1/4" spacers on a screw up from under it.
            GameObject Post(string key, Vector3 at) {
                var low = Stand(Spacer1, at + up * half);
                var high = Stand(SpacerQuarter, new Vector3(at.x, Top(low), at.z));
                Screw(at - up * half, -up, "1.50in");
                Nut(new Vector3(at.x, Top(high), at.z), -up);
                TensionerParts[key + " post"] = high;
                return high;
            }

            // ---- station 1: spacer idler, driven sprocket on a swinging arm
            TensionerParts["1 drive"] = DriveSprocket(-3f, -4.5f, Vector3.left, "SPKT-Normal-15T");
            var p1 = Hole(-2.5f, 3.5f);
            var swingMotor = Motor(p1 - up * half, up, Vector3.forward);
            foreach (var d in new[] { 0.5f, 1.0f }) Screw(p1 + new Vector3(0f, half, d), up);
            Shaft(p1 + up * 0.7f, up, 4);
            TensionerParts["swing motor"] = swingMotor;
            var r = Vector3.left;
            var h = p1 + up * 2f;
            var collarTurn = Quaternion.LookRotation(up, -r);
            Part("CLMP-Normal-Normal", h - collarTurn * new Vector3(0f, 0.007f, 0f), collarTurn);
            var channelTurn = Quaternion.LookRotation(-r, up);
            var channel = Part("CCHL-1x2-8", h + r * (0.2165f + 0.0555f) + channelTurn * new Vector3(0.5f, 0f, 0f), channelTurn);   // a base hole on the set screw
            Screw(h + r * (0.2165f + 0.0625f), r, "1/4in");
            TensionerParts["swing arm"] = channel;
            Physics.SyncTransforms();
            // The driven shaft down through the channel's flange holes 2.5" along it.
            var flange = channel.GetComponentsInChildren<HoleCollider>(true)
                .Where(x => Mathf.Abs(x.transform.forward.y) > 0.98f)
                .OrderBy(x => Mathf.Abs(Vector3.ProjectOnPlane(x.transform.position - p1, up).magnitude - 2.55f)).First();
            var c1 = new Vector3(flange.transform.position.x, origin.y, flange.transform.position.z);
            Shaft(c1 + up * 1.65f, up, 3);
            TensionerParts["1 driven"] = Stand("SPKT-Normal-24T", c1 + up * chainY, true);
            Collars(c1, Top(channel), TensionerParts["1 driven"]);
            // The tensioner.
            // The arm runs along the chain (turning it moves the idler into the chain, not along it); the band goes
            // over the chain to a post on its far side.
            var t1 = Hole(-2f, 0.5f);
            var i1 = t1 + (new Vector3(-0.8f, 0f, -2.5f)).normalized * 2.5f;
            var arm1 = Arm("1", t1, i1);
            var armTop = Top(arm1);
            var armBottom = armTop - 2f * half;
            var idler = Stand(SpacerHalf, new Vector3(i1.x, armBottom, i1.z));
            idler.transform.position += up * (armBottom - Top(idler));   // hanging under the arm
            Physics.SyncTransforms();
            TensionerParts["1 idler"] = idler;
            TensionerParts["1 idler screw"] = Screw(new Vector3(i1.x, armTop, i1.z), up, "3/4in");
            Nut(new Vector3(i1.x, idler.GetComponent<Renderer>().bounds.min.y, i1.z), up);
            Post("1", Hole(-4.5f, -1.5f));

            // ---- station 2: sprocket idler, fixed sprockets
            TensionerParts["2 drive"] = DriveSprocket(3f, -4.5f, Vector3.right, "SPKT-Normal-15T");
            var c2 = Hole(3f, 1f);
            Shaft(c2 + up * 0.2f, up, 3);
            TensionerParts["2 driven"] = Stand("SPKT-Normal-24T", c2 + up * chainY, true);
            var t2 = Hole(2f, 0.5f);
            var i2 = t2 + (new Vector3(0.8f, 0f, -2.5f)).normalized * 2.5f;
            var arm2 = Arm("2", t2, i2);
            var i2Hole = new Vector3(i2.x, origin.y, i2.z);
            Shaft(i2Hole + up * 1.75f, up, 3);
            TensionerParts["2 idler"] = Stand("SPKT-Normal-10T", i2Hole + up * chainY, true);
            Collars(i2Hole, Top(arm2), TensionerParts["2 idler"]);
            // Its band goes round a screw one hole in from the idler.
            var postAt = i2 + (t2 - i2).normalized * 0.5f;
            var top2 = Top(arm2);
            TensionerParts["2 arm post"] = Screw(new Vector3(postAt.x, top2, postAt.z), up, "1/2in");
            Nut(new Vector3(postAt.x, top2 - 2f * half, postAt.z), up);
            Post("2", Hole(4.5f, -1.5f));
            return made.GetRange(start, made.Count - start);
        }

        /// <summary>After TensionerRig's screws have connected: joints, the plate grounded, motors, bands, chains.</summary>
        public static string FinishTensioners() {
            var log = new System.Text.StringBuilder();
            Motion.Linkage.SetGrounded(TensionerParts["plate"], true);
            foreach (var key in new[] { "1 pivot", "2 pivot" }) Motion.Linkage.SetJoint(TensionerParts[key], true);
            foreach (var motor in Object.FindObjectsByType<Motion.MotorDrive>(FindObjectsSortMode.None)) {
                if (motor.gameObject == TensionerParts["swing motor"]) {
                    motor.SetCartridge(100, false);
                    Motion.Controls.SetName(motor.gameObject, "Swing arm");
                    Motion.Controls.SetHold(motor.gameObject, true);
                    Motion.Controls.SetSpeed(motor.gameObject, 0.25f);   // slow enough to swing it through the tensioner's range by key
                    Motion.Controls.SetForwardKey(motor.gameObject, UnityEngine.InputSystem.Key.U);
                    Motion.Controls.SetReverseKey(motor.gameObject, UnityEngine.InputSystem.Key.J);
                }
                else {
                    motor.SetCartridge(200, false);
                    var left = motor.transform.position.x < 0f;
                    Motion.Controls.SetName(motor.gameObject, left ? "Chain 1" : "Chain 2");
                    Motion.Controls.SetForwardKey(motor.gameObject, left ? UnityEngine.InputSystem.Key.I : UnityEngine.InputSystem.Key.O);
                    Motion.Controls.SetReverseKey(motor.gameObject, left ? UnityEngine.InputSystem.Key.K : UnityEngine.InputSystem.Key.L);
                }
            }
            // The bands: #32, two wraps, from the arm's post to the plate's.
            foreach (var station in new[] { "1", "2" }) {
                var armPost = TensionerParts.TryGetValue(station + " arm post", out var p) ? p : TensionerParts[station + " idler screw"];
                var framePost = TensionerParts[station + " post"];
                var posts = new List<Motion.BandPost> {
                    new() { part = armPost, local = armPost.transform.InverseTransformPoint(armPost.transform.position + Vector3.up * 0.06f),
                            axisLocal = armPost.transform.InverseTransformDirection(Vector3.up), radius = 0.15f, what = "screw head" },
                    new() { part = framePost, local = framePost.transform.InverseTransformPoint(framePost.GetComponent<Renderer>().bounds.center),
                            axisLocal = framePost.transform.InverseTransformDirection(Vector3.up), radius = 0.19f, what = "spacer" },
                };
                var type = PartsManager.GetPartType("BAND");
                var generator = type.GetComponent<PartGenerator>();
                generator.param1.value = "V32";
                var obj = generator.Generate(posts[0].Center, Quaternion.identity);
                made.Add(obj);
                obj.GetComponent<Motion.ElasticBand>().Set(posts, 1, 2, 0f, Vector3.up);
                log.AppendLine("band " + station + " " + obj.GetComponent<Motion.ElasticBand>().Describe());
            }
            // The chains: drive, driven, idler (the idler pressing in from outside the loop).
            foreach (var station in new[] { "1", "2" }) {
                var parts = new List<GameObject> { TensionerParts[station + " drive"], TensionerParts[station + " driven"], TensionerParts[station + " idler"] };
                Motion.ChainLoop chain = null;
                foreach (var flip in new[] { new[] { 2 }, new int[0] }) {
                    chain = Chain(parts, "chain", flip);
                    if (chain != null) break;
                }
                // As teams cut it: a link longer than the bare loop, so the idler presses in and the tensioner sits
                // mid-travel (room both ways).
                if (chain != null && chain.fixedLinks > 0) {
                    chain.fixedLinks += 1;
                    chain.Save();
                    chain.Rebuild();
                }
                log.AppendLine("chain " + station + " " + (chain != null ? chain.Path.links + " links, fixed " + chain.fixedLinks + ", tensioner " + (Motion.ChainTensioner.Of(chain) != null) : "NOT made"));
            }
            return log.ToString();
        }
    }
}
