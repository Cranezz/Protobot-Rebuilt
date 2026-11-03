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
            return part;
        }

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
    }
}
