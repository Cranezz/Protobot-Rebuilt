using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Motion {
    // V2: a V5 motor. The shaft in its square hole turns, with everything locked onto it and screwed to that, and
    // through meshing gears every shaft geared to it (GearTrain: a drive system; MotorRunner turns it). Speeds and
    // torques are VEX's: the 11W motor 100, 200 or 600 rpm with the red, green or blue cartridge (2.1, 1.05 or
    // 0.35 N·m at stall), shown in the window around its shaft as the real one does; the 5.5W motor 200 rpm,
    // 0.5 N·m. Its keys, speed, name and direction are its Controls; the cartridge is kept in the part's saved
    // state ("cartridge:600").
    public class MotorDrive : MonoBehaviour {
        public static readonly List<MotorDrive> All = new();
        public static readonly int[] Cartridges = { 100, 200, 600 };

        public string Kind { get; private set; }
        public int Rpm { get; private set; } = 200;
        public bool HasCartridge => Kind == "11W";
        public HoleCollider ShaftHole { get; private set; }

        /// <summary>The way the shaft turns when the motor runs forward (right hand about this axis).</summary>
        public Vector3 Forward => ShaftHole != null ? ShaftHole.transform.forward : transform.forward;
        public Vector3 ShaftPoint => ShaftHole != null ? ShaftHole.transform.position : transform.position;

        /// <summary>Stall torque at the shaft in N·m (VEX's figures).</summary>
        public float StallTorque => HasCartridge ? 2.1f * 100f / Rpm : 0.5f;

        /// <summary>Run from the part menu: 1 forward, -1 reverse, 0 not (keys run it while it's controlled).</summary>
        public int Toggled { get; private set; }

        /// <summary>This frame's command, -1..1 of full speed (set by the runner; the speed setting and reversed are in it).</summary>
        public float Command { get; set; }

        /// <summary>How fast the shaft turns now, share of full speed (for its sound and the labels).</summary>
        public float Level { get; set; }

        private AudioSource sound;
        private Material cartridge;

        public static Color CartridgeColor(int rpm) => rpm == 100 ? new Color(0.78f, 0.12f, 0.12f) : rpm == 600 ? new Color(0.12f, 0.38f, 0.86f) : new Color(0.16f, 0.62f, 0.22f);
        public static string CartridgeName(int rpm) => rpm == 100 ? "red" : rpm == 600 ? "blue" : "green";
        public Color Color => CartridgeColor(Rpm);

        /// <summary>V2: the brain port it's wired to (a smart cable from it to the brain), else 1, 2... in the order
        /// they were added, skipping the numbers wired motors have.</summary>
        public int Number {
            get {
                var port = Port;
                if (port != null) return port.number;
                var live = Live();
                var used = new HashSet<int>(live.Select(m => m.Port).Where(p => p != null).Select(p => p.number));
                var n = 0;
                foreach (var m in live) {
                    if (m.Port != null) continue;
                    do n++;
                    while (used.Contains(n));
                    if (m == this) return n;
                }
                return n + 1;
            }
        }

        /// <summary>V2: the brain's smart port it's wired to, or null.</summary>
        public WirePort Port {
            get {
                var port = WirePorts.BrainPortOf(gameObject);
                return port != null && port.kind == WireKind.Smart ? port : null;
            }
        }

        /// <summary>Its given name, or "Motor 1".</summary>
        public string Label => Controls.GivenName(gameObject) ?? "Motor " + Number;

        public static List<MotorDrive> Live() => All.Where(m => m != null && !m.gameObject.IsDeleted()).ToList();

        private void Awake() {
            All.Add(this);
            var id = TryGetComponent(out SavedObject saved) ? saved.id : "";
            var parts = id.Split('-');
            Kind = parts.Length > 1 ? parts[1] : "11W";
            ShaftHole = FindShaftHole();
            if (HasCartridge) ColorWindow();
            MotorRunner.Ensure();
        }

        private void Start() {
            var token = Dynamics.Token(gameObject, "cartridge");
            if (HasCartridge && int.TryParse(token, out var rpm) && Cartridges.Contains(rpm)) SetCartridge(rpm, false);
            else if (HasCartridge) SetCartridge(200, false);
        }

        private void OnDestroy() => All.Remove(this);

        private HoleCollider FindShaftHole() {
            if (TryGetComponent(out Motor motor)) {
                var field = typeof(Motor).GetField("shaftHole", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(motor) is HoleCollider hole && hole != null) return hole;
            }
            // Otherwise the square hole nearest the middle.
            return GetComponentsInChildren<HoleCollider>(true).OrderBy(h => (h.transform.position - GetComponent<Renderer>().bounds.center).sqrMagnitude).FirstOrDefault();
        }

        public void SetCartridge(int rpm, bool say = true) {
            if (!HasCartridge) {
                if (say) Interface.Interface.Say("The 5.5W motor has no cartridges: it always turns at 200 rpm", false);
                return;
            }
            Rpm = rpm;
            Dynamics.SetToken(gameObject, "cartridge", rpm.ToString());
            // V2: the material the window shows now (loading a build's colors swaps the materials for copies).
            if (cartridge != null && TryGetComponent(out MeshRenderer shown) && shown.sharedMaterials.Length > 1 && shown.sharedMaterials[1] != null)
                cartridge = shown.sharedMaterials[1];
            if (cartridge != null) {
                var color = CartridgeColor(rpm);
                cartridge.color = color;
                if (cartridge.HasProperty("_BaseColor")) cartridge.SetColor("_BaseColor", color);
            }
            if (say) {
                Interface.Interface.Say(Label + ": " + CartridgeName(rpm) + " cartridge, " + rpm + " rpm", true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }
        }

        /// <summary>Runs forward (1) or in reverse (-1) until stopped (0); the same direction again stops it.</summary>
        public void Run(int direction) {
            if (direction == Toggled) direction = 0;
            Toggled = direction;
        }

        public void StopToggle() => Toggled = 0;

        // ------------------------------------------------------------------ sound

        /// <summary>The motor's whine at a level (share of full speed).</summary>
        public void Sound(float level) {
            level = Mathf.Abs(level);
            if (level < 0.001f) {
                if (sound != null && sound.isPlaying) sound.Stop();
                return;
            }
            if (sound == null) {
                sound = gameObject.AddComponent<AudioSource>();
                sound.clip = Audio.Sounds.MotorLoop;
                sound.loop = true;
                sound.playOnAwake = false;
                sound.spatialBlend = 0.6f;
                sound.dopplerLevel = 0f;
                sound.minDistance = 6f;
                sound.maxDistance = 200f;
            }
            if (!sound.isPlaying) sound.Play();
            // The motor inside always runs at 3600 rpm; the cartridges' gears whine a little differently, and the
            // small motor is higher.
            var pitch = Kind == "11W" ? (Rpm == 100 ? 0.94f : Rpm == 600 ? 1.1f : 1f) : 1.22f;
            sound.pitch = pitch * (0.35f + 0.65f * Mathf.Min(level, 1.2f));
            sound.volume = 0.32f * Mathf.Sqrt(Mathf.Min(level, 1f));
        }

        // ------------------------------------------------------------------ the cartridge window

        private static Mesh windowed;
        private static int windowedFor;

        // The original's motor model is one black piece. The VEX-CAD model has the cartridge as its own colored
        // piece; lined up on the original, the faces of the original that lie on the cartridge (what shows in the
        // arc-shaped window) become a second piece in the cartridge's color.
        private void ColorWindow() {
            if (!TryGetComponent(out MeshFilter filter) || !TryGetComponent(out MeshRenderer renderer) || filter.sharedMesh == null) return;
            var mesh = filter.sharedMesh;
            if (mesh == windowed && windowed != null) {
                // A copy of a motor: its own cartridge color.
                var copied = renderer.sharedMaterials;
                if (copied.Length > 1) {
                    cartridge = new Material(copied[1]) { name = "Cartridge" };
                    renderer.sharedMaterials = new[] { copied[0], cartridge };
                }
                return;
            }
            if (windowed == null || windowedFor != mesh.GetInstanceID()) {
                windowed = SplitWindow(mesh);
                windowedFor = mesh.GetInstanceID();
            }
            if (windowed == null) return;
            filter.sharedMesh = windowed;
            if (TryGetComponent(out MeshCollider collider)) collider.sharedMesh = windowed;
            var materials = renderer.sharedMaterials;
            cartridge = new Material(materials[0]) { name = "Cartridge" };
            renderer.sharedMaterials = new[] { materials[0], cartridge };
        }

        private static Mesh SplitWindow(Mesh motor) {
            var family = Resources.Load<GameObject>("Part Prefabs/VEX/V5 Electronics");
            var vex = family != null ? family.transform.Find("11W Motor") : null;
            if (vex == null || !motor.isReadable) return null;
            var vexMesh = vex.GetComponent<MeshFilter>().sharedMesh;
            var materials = vex.GetComponent<MeshRenderer>().sharedMaterials;
            var green = System.Array.FindIndex(materials, m => m != null && m.name.Contains("Green"));
            if (green < 0 || green >= vexMesh.subMeshCount) return null;

            var (turn, shift, error) = MeshAlign.Find(vexMesh.vertices, motor.vertices);
            if (error > 0.08f) {
                Debug.LogWarning("V2: the VEX motor model doesn't line up with the motor (" + error + "\"): no cartridge color");
                return null;
            }
            var vexVertices = vexMesh.vertices;
            var greenTriangles = vexMesh.GetTriangles(green);
            var grid = new Dictionary<Vector3Int, List<(Vector3, Vector3, Vector3)>>();
            const float Cell = 0.1f;
            for (var i = 0; i < greenTriangles.Length; i += 3) {
                var a = turn * vexVertices[greenTriangles[i]] + shift;
                var b = turn * vexVertices[greenTriangles[i + 1]] + shift;
                var c = turn * vexVertices[greenTriangles[i + 2]] + shift;
                var min = Vector3Int.FloorToInt(Vector3.Min(a, Vector3.Min(b, c)) / Cell - Vector3.one * 0.3f);
                var max = Vector3Int.FloorToInt(Vector3.Max(a, Vector3.Max(b, c)) / Cell + Vector3.one * 0.3f);
                for (var x = min.x; x <= max.x; x++)
                    for (var y = min.y; y <= max.y; y++)
                        for (var z = min.z; z <= max.z; z++) {
                            var key = new Vector3Int(x, y, z);
                            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<(Vector3, Vector3, Vector3)>();
                            list.Add((a, b, c));
                        }
            }

            bool OnCartridge(Vector3 point) {
                if (!grid.TryGetValue(Vector3Int.FloorToInt(point / Cell), out var near)) return false;
                foreach (var (a, b, c) in near)
                    if ((ClosestOnTriangle(point, a, b, c) - point).sqrMagnitude < 0.03f * 0.03f) return true;
                return false;
            }
            var vertices = motor.vertices;
            var triangles = motor.triangles;
            var rest = new List<int>();
            var window = new List<int>();
            for (var i = 0; i < triangles.Length; i += 3) {
                var p0 = vertices[triangles[i]];
                var p1 = vertices[triangles[i + 1]];
                var p2 = vertices[triangles[i + 2]];
                // V2: the whole triangle on the cartridge (its corners and middle), not only its middle: a big housing
                // triangle reaching into the window showed the color past it.
                var onCartridge = OnCartridge((p0 + p1 + p2) / 3f) && OnCartridge(p0) && OnCartridge(p1) && OnCartridge(p2);
                (onCartridge ? window : rest).Add(triangles[i]);
                (onCartridge ? window : rest).Add(triangles[i + 1]);
                (onCartridge ? window : rest).Add(triangles[i + 2]);
            }
            if (window.Count == 0) return null;
            var split = Object.Instantiate(motor);
            split.name = motor.name + " (cartridge)";
            split.subMeshCount = 2;
            split.SetTriangles(rest, 0);
            split.SetTriangles(window, 1);
            return split;
        }

        // Ericson's closest point on a triangle.
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            var bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            var cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            var va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
            var denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }
}
