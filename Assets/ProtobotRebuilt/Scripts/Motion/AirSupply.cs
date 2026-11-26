using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Motion {
    /// <summary>
    /// V2 (build 79): the robot's air. VEX robots carry their air in tanks charged before a match (no compressor on the
    /// robot): V5RC allows two tanks, charged to 100 psi at most (R25), and every time a cylinder moves, the end it moves
    /// toward fills from the tanks and the other end's air goes out. So the pressure drops a little with every move, and a
    /// cylinder pushes with that pressure times its bore's area: 12 lbf at 100 psi, half at 50.
    /// <para>Each robot's tanks (TANK- parts: the V5 tank holds 200 mL, the legacy 1 1/2" x 4" reservoir about 90 mL)
    /// are one supply for all its cylinders. A legacy pressure regulator caps what reaches the cylinders (steadier force,
    /// more moves). The fill pressure is kept on the tanks ("psi:"), the regulator's setting on it ("reg:"). A robot with
    /// no tank has unlimited air at 100 psi (as before tanks counted), and says so.</para>
    /// </summary>
    public static class AirSupply {
        public const float MaxPsi = 100f;            // V5RC R25: charged to 100 psi at most
        public const float V5TankMl = 200f, LegacyTankMl = 90f;
        private const float DeadMl = 0.6f;           // the fittings and a short tube to each cylinder end

        public class Supply {
            public int robot;
            public readonly List<GameObject> tanks = new();
            public GameObject regulator;
            public float volumeMl, psi, fillPsi, regulatorPsi = MaxPsi;
            public int moves;
            public bool Unlimited => tanks.Count == 0;
            /// <summary>What reaches the cylinders now (gauge psi).</summary>
            public float Working => Unlimited ? MaxPsi : Mathf.Min(psi, regulatorPsi);
        }

        private static readonly List<Supply> supplies = new();

        /// <summary>The supplies while the sandbox runs (one per robot), else empty.</summary>
        public static IReadOnlyList<Supply> All => supplies;

        public static bool IsTank(GameObject part) => Id(part).StartsWith("TANK-");
        public static bool IsRegulator(GameObject part) => Id(part).Contains("Pressure Regulator");

        public static float TankMl(GameObject tank) => Id(tank).Contains("Legacy") ? LegacyTankMl : V5TankMl;

        /// <summary>The fill pressure set on a build's tanks (gauge psi), 100 unless changed.</summary>
        public static float FillPsi(IEnumerable<GameObject> tanks) {
            foreach (var t in tanks)
                if (float.TryParse(Dynamics.Token(t, "psi"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return Mathf.Clamp(v, 5f, MaxPsi);
            return MaxPsi;
        }

        public static float RegulatorPsi(GameObject regulator) =>
            regulator != null && float.TryParse(Dynamics.Token(regulator, "reg"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? Mathf.Clamp(v, 5f, MaxPsi) : MaxPsi;

        /// <summary>The build's tanks and regulator (outside the sandbox, for the Devices window).</summary>
        public static (List<GameObject> tanks, GameObject regulator) InBuild() {
            var tanks = new List<GameObject>();
            GameObject regulator = null;
            foreach (var saved in Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None)) {
                var go = saved.gameObject;
                if (!go.activeInHierarchy || go.IsDeleted()) continue;
                if (IsTank(go)) tanks.Add(go);
                else if (regulator == null && IsRegulator(go)) regulator = go;
            }
            return (tanks, regulator);
        }

        /// <summary>Air one move of a cylinder takes from the tanks (mL at the pressure it fills to), by the end it fills.</summary>
        public static float ChamberMl(PneumaticCylinder c, bool extend) {
            var strokeMm = c.Stroke * 25.4f;
            var bore = Mathf.PI * Mathf.Pow(PneumaticCylinder.BoreMm / 2f, 2f);
            var rod = Mathf.PI * Mathf.Pow(PneumaticCylinder.RodMm / 2f, 2f);
            return (extend ? bore : bore - rod) * strokeMm / 1000f + DeadMl;
        }

        /// <summary>Roughly how many moves (out or in) the build's tanks give before the cylinders are down to half force.</summary>
        public static int MovesToHalf(float volumeMl, float fillPsi, float regulatorPsi, IEnumerable<PneumaticCylinder> cylinders) {
            var list = cylinders.Where(c => c != null).ToList();
            if (volumeMl <= 0f || list.Count == 0) return 0;
            var per = list.Average(c => (ChamberMl(c, true) + (c.SingleActing ? 0f : ChamberMl(c, false))) / (c.SingleActing ? 1f : 2f));
            var psi = fillPsi;
            var half = Mathf.Min(fillPsi, regulatorPsi) / 2f;
            var moves = 0;
            while (Mathf.Min(psi, regulatorPsi) > half && moves < 10000) {
                psi = Take(psi, regulatorPsi, volumeMl, per);
                moves++;
            }
            return moves;
        }

        // The tanks after filling a chamber of `ml` from 0 psi: to the regulator's pressure if it's lower (the tanks lose
        // that much air), else the tanks and the chamber end up equal (Boyle: gauge pressures work, atmosphere cancels).
        private static float Take(float psi, float regulatorPsi, float volumeMl, float ml) =>
            psi > regulatorPsi ? Mathf.Max(0f, psi - regulatorPsi * ml / volumeMl) : psi * volumeMl / (volumeMl + ml);

        // ------------------------------------------------------------------ the sandbox

        /// <summary>Called as the sandbox starts: one supply per robot, at its fill pressure.</summary>
        public static void Begin(IEnumerable<GameObject> parts) {
            supplies.Clear();
            foreach (var part in parts) {
                var tank = IsTank(part);
                if (!tank && !IsRegulator(part)) continue;
                var supply = For(RobotOf(part), true);
                if (tank) {
                    supply.tanks.Add(part);
                    supply.volumeMl += TankMl(part);
                }
                else if (supply.regulator == null) supply.regulator = part;
            }
            foreach (var s in supplies) {
                s.fillPsi = s.psi = FillPsi(s.tanks);
                s.regulatorPsi = RegulatorPsi(s.regulator);
            }
        }

        public static void End() => supplies.Clear();

        /// <summary>A cylinder moves (its valve switched): the end it moves toward fills from its robot's tanks. Returns
        /// the pressure it pushes with (gauge psi).</summary>
        public static float Move(PneumaticCylinder c, bool extend) {
            var s = For(RobotOf(c.gameObject), false);
            if (s == null || s.Unlimited) return MaxPsi;
            if (c.SingleActing && !extend) return s.Working;   // the spring brings it back: no air
            s.psi = Take(s.psi, s.regulatorPsi, s.volumeMl, ChamberMl(c, extend));
            s.moves++;
            return s.Working;   // the chamber ends at the tanks' pressure, or the regulator's
        }

        /// <summary>The supply a cylinder's air comes from (null outside the sandbox).</summary>
        public static Supply Of(PneumaticCylinder c) => c == null ? null : For(RobotOf(c.gameObject), false);

        private static Supply For(int robot, bool make) {
            foreach (var s in supplies) if (s.robot == robot) return s;
            if (!make) return supplies.Count == 0 ? null : new Supply { robot = robot };   // a robot with no tank: unlimited
            var made = new Supply { robot = robot };
            supplies.Add(made);
            return made;
        }

        private static int RobotOf(GameObject part) => Sandbox.RobotOf != null && part != null ? Sandbox.RobotOf(part) : 0;

        private static string Id(GameObject part) => part != null && part.TryGetComponent(out SavedObject saved) ? saved.id ?? "" : "";
    }
}
