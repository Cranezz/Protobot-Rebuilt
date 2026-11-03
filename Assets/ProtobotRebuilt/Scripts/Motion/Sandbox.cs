using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Motion {
    // V2: the simulation sandbox: the build becomes real rigid bodies under gravity (in inches: 386.1 in/s²) and
    // drives. Parts screwed together tight are one body; parts not fastened to anything fall off. A shaft turns in
    // the holes it passes through (a hinge), a joint screw is a hinge, a motor drives its shaft like a V5 motor
    // (its torque falls from stall to nothing at full speed: 2.1/1.05/0.35 N·m with the red/green/blue cartridge,
    // 0.5 N·m for the 5.5W), meshing gears keep their ratio, and a cylinder pushes its rod out with 12 lbf (10 mm
    // bore at 100 psi) and pulls it in with a little less: equal and opposite, so whichever side is lighter moves
    // (a cylinder mounted backwards lifts its body off a heavy robot). Wheels roll; omni wheels' rollers let them
    // slide sideways. Nothing is grounded. The build is shown by copies of its parts: stopping puts it back as it
    // was. Controls: ControlsPanel (drive with W/A/S/D and the arrows, or a gamepad).
    public class Sandbox : MonoBehaviour {
        public const int Layer = 16, GroundLayer = 17;
        public const float Gravity = 386.09f;          // in/s²
        public const float NewtonMeter = 1550.0031f;   // kg·in²/s² (masses are in kg, lengths in inches)
        public const float Newton = 39.3701f;          // kg·in/s²
        private const float CylinderSpeed = 18f;       // in/s, a VEX cylinder's stroke takes about a tenth of a second
        private const float SlideSign = -1f;           // a ConfigurableJoint's linear drive runs the other way round

        private static Sandbox instance;
        public static bool Running => instance != null;
        public static Sandbox Instance => instance;

        public bool Paused { get; private set; }
        public bool Follow { get; set; } = true;

        private class SimBody {
            public Rigidbody rb;
            public readonly List<GameObject> nodes = new();
            public float mass;
            public int colliders;
        }

        // A gear train: shafts (bodies, hinged to their frames) that turn together at their ratios k (of the train's
        // turn), and the motors driving it (k: the motor's own turn per turn of the train).
        private class Train {
            public readonly List<(Rigidbody rb, Rigidbody frame, Vector3 axisLocal, Vector3 pointLocal, float k)> rotors = new();
            public readonly List<(MotorDrive motor, float k)> motors = new();
        }

        private class Slide {
            public PneumaticCylinder cylinder;
            public ConfigurableJoint joint;
            public bool extended;
            public Rigidbody rod, barrel;
        }

        private class Wheel {
            public Rigidbody rb;
            public Collider collider;
            public Vector3 axisLocal;
            public float radius;
        }

        private readonly List<SimBody> bodies = new();
        private readonly Dictionary<GameObject, SimBody> bodyOf = new();
        private readonly List<Train> trains = new();
        private readonly List<Slide> slides = new();
        private readonly List<Wheel> omnis = new();
        private readonly List<Renderer> hidden = new();

        // A chain runs on the frame its sprockets turn in (its links copied there), at the speed its rotor turns.
        private class SimChain {
            public ChainLoop chain;
            public readonly List<Transform> links = new();
            public Rigidbody rotor, frame;
            public Vector3 axisLocal;
            public float inchesPerRadian, phase;
        }

        private readonly List<SimChain> chains = new();
        private readonly List<(Collider collider, Vector3 point, Vector3 normal, float impulse, Rigidbody other)> contacts = new();
        private Transform root;
        private SimBody main;

        // What the sandbox changes, put back when it stops.
        private Vector3 savedGravity;
        private float savedStep;
        private int savedIterations, savedVelocityIterations;
        private float savedMaxAngular, savedBounce;
        private bool[] savedLayers;
        private System.Func<bool> savedBlocked;

        // ------------------------------------------------------------------ starting and stopping

        public static void Toggle() {
            if (Running) Stop();
            else Begin();
        }

        public static void Begin() {
            if (Running) return;
            if (InputEvents.TextInput.ShortcutsPaused) {
                Interface.Interface.Say("Close the open window first", false);
                return;
            }
            MotorRunner.StopAll();
            MotorControls.Release();
            Popup.Close();
            if (Placement.IsPlacing) Placement.Instance.StopPlacing();
            foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None)) manager.ClearCurrent();

            var go = new GameObject("Sandbox");
            instance = go.AddComponent<Sandbox>();
            try {
                instance.Build();
            }
            catch (System.Exception e) {
                Debug.LogException(e);
                instance.Restore();
                Destroy(go);
                instance = null;
                Interface.Interface.Say("The simulation couldn't start: " + e.Message, false);
                return;
            }
            SetBuildInterface(false);
            ControlsPanel.Open();
            Audio.Sounds.Play(Audio.Sounds.Tick);
            var motors = instance.trains.Sum(t => t.motors.Count);
            Interface.Interface.Say("Simulating " + instance.bodies.Count + (instance.bodies.Count == 1 ? " body" : " bodies")
                                    + (motors > 0 ? ", " + motors + (motors == 1 ? " motor" : " motors") : "")
                                    + (instance.slides.Count > 0 ? ", " + instance.slides.Count + (instance.slides.Count == 1 ? " cylinder" : " cylinders") : "")
                                    + ": drive with W/A/S/D and the arrows, Esc or Stop to build again", true);
        }

        public static void Stop() {
            if (!Running) return;
            var sandbox = instance;
            instance = null;
            sandbox.Restore();
            Destroy(sandbox.gameObject);
            SetBuildInterface(true);
            ControlsPanel.CloseSimulation();
            foreach (var motor in MotorDrive.Live()) motor.Sound(0f);
            Interface.Interface.Say("Back to building: the build is as it was", true);
        }

        public static void Reset() {
            if (!Running) return;
            Stop();
            Begin();
        }

        public void SetPaused(bool value) {
            Paused = value;
            Physics.simulationMode = value ? SimulationMode.Script : SimulationMode.FixedUpdate;
            Interface.Interface.Say(value ? "Paused" : "Running", true);
        }

        private static void SetBuildInterface(bool on) {
            if (Interface.Interface.Root == null) return;
            foreach (var name in new[] { "Tool Rail", "Tool Options", "Parts Browser" }) {
                var t = Interface.Interface.Root.Find(name);
                if (t == null) continue;
                var group = t.gameObject.GetOrAdd<CanvasGroup>();
                group.alpha = on ? 1f : 0f;
                group.blocksRaycasts = on;
                group.interactable = on;
            }
        }

        private void Restore() {
            if (savedLayers != null) {
                for (var other = 0; other < 32; other++) {
                    Physics.IgnoreLayerCollision(Layer, other, savedLayers[other]);
                    Physics.IgnoreLayerCollision(GroundLayer, other, savedLayers[32 + other]);
                }
                Physics.gravity = savedGravity;
                Time.fixedDeltaTime = savedStep;
                Physics.defaultSolverIterations = savedIterations;
                Physics.defaultSolverVelocityIterations = savedVelocityIterations;
                Physics.defaultMaxAngularSpeed = savedMaxAngular;
                Physics.bounceThreshold = savedBounce;
                Physics.simulationMode = SimulationMode.FixedUpdate;
                SelectionSystem.SelectionManager.Blocked = savedBlocked;
            }
            foreach (var renderer in hidden)
                if (renderer != null) renderer.enabled = true;
            hidden.Clear();
            if (root != null) Destroy(root.gameObject);
        }

        // ------------------------------------------------------------------ building the bodies

        private Dictionary<GameObject, GameObject> parent;

        private GameObject Find(GameObject o) {
            while (parent[o] != o) o = parent[o] = parent[parent[o]];
            return o;
        }

        private void Union(GameObject a, GameObject b) {
            if (a == null || b == null || !parent.ContainsKey(a) || !parent.ContainsKey(b)) return;
            a = Find(a);
            b = Find(b);
            if (a != b) parent[a] = b;
        }

        private void Build() {
            savedGravity = Physics.gravity;
            savedStep = Time.fixedDeltaTime;
            savedIterations = Physics.defaultSolverIterations;
            savedVelocityIterations = Physics.defaultSolverVelocityIterations;
            savedMaxAngular = Physics.defaultMaxAngularSpeed;
            savedBounce = Physics.bounceThreshold;
            savedLayers = new bool[64];
            for (var other = 0; other < 32; other++) {
                savedLayers[other] = Physics.GetIgnoreLayerCollision(Layer, other);
                savedLayers[32 + other] = Physics.GetIgnoreLayerCollision(GroundLayer, other);
            }
            savedBlocked = SelectionSystem.SelectionManager.Blocked;
            SelectionSystem.SelectionManager.Blocked = () => true;

            Physics.gravity = new Vector3(0f, -Gravity, 0f);
            Time.fixedDeltaTime = 1f / 240f;
            Physics.defaultSolverIterations = 16;
            Physics.defaultSolverVelocityIterations = 6;
            Physics.defaultMaxAngularSpeed = 400f;
            Physics.bounceThreshold = 4f;
            for (var other = 0; other < 32; other++) {
                var sim = other == Layer || other == GroundLayer;
                Physics.IgnoreLayerCollision(Layer, other, !sim);
                Physics.IgnoreLayerCollision(GroundLayer, other, other != Layer);
            }

            root = new GameObject("Simulation").transform;
            var parts = FindObjectsByType<SavedObject>(FindObjectsSortMode.None)
                        .Where(s => s.gameObject.activeInHierarchy && !s.gameObject.IsDeleted() && s.GetComponent<ChainLoop>() == null)
                        .Select(s => s.gameObject).ToList();
            if (parts.Count == 0) throw new System.Exception("there's nothing in the build");
            var cylinders = parts.Select(p => p.GetComponent<PneumaticCylinder>()).Where(c => c != null && c.enabled && c.Rod != null).ToList();
            var nodes = new List<GameObject>(parts);
            nodes.AddRange(cylinders.Select(c => c.Rod.gameObject));
            parent = nodes.ToDictionary(n => n, n => n);
            var rotors = GearTrain.Rotors();

            // Tight screws hold what they go through together; a joint screw only its nut.
            var hinges = new List<(GameObject a, GameObject b, Vector3 point, Vector3 axis, MotorDrive motor)>();
            foreach (var part in parts) {
                if (part.GetComponent<Screw>() == null || part.GetComponent<RodThread>() != null) continue;
                var joint = Linkage.IsJoint(part);
                var threaded = joint ? Linkage.Threaded(part) : null;
                var head = joint ? Linkage.HeadPart(part) : null;
                if (head != null) threaded.Add(head);
                foreach (var next in GroupManager.Neighbours(part)) {
                    if (!parent.ContainsKey(next)) continue;
                    if (joint && !threaded.Contains(next)) hinges.Add((part, next, part.transform.position, part.transform.forward, null));
                    else Union(part, next);
                }
            }
            // A cylinder's rod holds what its thread fastens.
            foreach (var cylinder in cylinders)
                if (cylinder.Thread != null)
                    foreach (var next in GroupManager.Neighbours(cylinder.Thread.gameObject))
                        if (next != cylinder.gameObject) Union(cylinder.Rod.gameObject, next);
            // A shaft holds what's locked on it, turns in the rest, and is driven by motors.
            foreach (var rotor in rotors) {
                foreach (var part in rotor.parts) Union(rotor.shaft, part);
                foreach (var (part, point) in rotor.bearings) hinges.Add((rotor.shaft, part, point, rotor.axis, null));
                foreach (var motor in rotor.motors) hinges.Add((rotor.shaft, motor.gameObject, motor.ShaftPoint, rotor.axis, motor));
            }

            // Bodies.
            var byRoot = new Dictionary<GameObject, SimBody>();
            foreach (var node in nodes) {
                var r = Find(node);
                if (!byRoot.TryGetValue(r, out var body)) {
                    body = new SimBody();
                    byRoot[r] = body;
                    bodies.Add(body);
                }
                body.nodes.Add(node);
                bodyOf[node] = body;
            }
            var floor = 0f;
            foreach (var body in bodies) MakeBody(body, cylinders, ref floor);
            main = bodies.OrderByDescending(b => b.mass).First();

            // Ground: the grid, or the build's lowest point if it's below it.
            var ground = new GameObject("Ground") { layer = GroundLayer };
            ground.transform.SetParent(root, false);
            var box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(4000f, 2f, 4000f);
            box.center = new Vector3(0f, floor - 1f, 0f);
            box.sharedMaterial = Material(1f);

            // Collisions within the robot only where parts don't start out touching (gears in mesh, a wheel on its
            // channel would fight).
            var all = root.GetComponentsInChildren<Collider>().Where(c => c.gameObject.layer == Layer).ToList();
            for (var i = 0; i < all.Count; i++)
                for (var j = i + 1; j < all.Count; j++) {
                    var a = all[i];
                    var b = all[j];
                    if (a.attachedRigidbody == b.attachedRigidbody) continue;
                    var ba = a.bounds;
                    ba.Expand(0.06f);
                    if (!ba.Intersects(b.bounds)) continue;
                    if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out _)
                        || Near(a, b))
                        Physics.IgnoreCollision(a, b, true);
                }

            MakeHinges(hinges);
            MakeTrains(rotors);
            StillChains();
            MakeSlides(cylinders);
        }

        private static bool Near(Collider a, Collider b) {
            var p = b.ClosestPoint(a.bounds.center);
            return (a.ClosestPoint(p) - p).sqrMagnitude < 0.03f * 0.03f;
        }

        private static readonly Dictionary<float, PhysicsMaterial> Materials = new();

        private static PhysicsMaterial Material(float friction) {
            if (Materials.TryGetValue(friction, out var material) && material != null) return material;
            material = new PhysicsMaterial("Sim " + friction) {
                dynamicFriction = friction * 0.9f, staticFriction = friction, bounciness = 0f,
                frictionCombine = friction <= 0f ? PhysicsMaterialCombine.Minimum : PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            Materials[friction] = material;
            return material;
        }

        private enum WheelKind { None, Traction, Omni }

        private static readonly Dictionary<(Mesh, Vector3), Mesh> Hulls = new();
        private static Vector3[] directions;

        // A part's shape for collisions: its farthest points in 120 directions (PhysX keeps a convex shape under 256
        // faces; this stays under it), or for a wheel a smooth 48-sided cylinder, so it rolls round.
        private static Mesh Hull(Mesh mesh, Vector3? wheelAxis) {
            var key = (mesh, wheelAxis ?? Vector3.zero);
            if (Hulls.TryGetValue(key, out var hull) && hull != null) return hull;
            var points = new List<Vector3>();
            var vertices = mesh.vertices;
            if (wheelAxis.HasValue) {
                var axis = wheelAxis.Value.normalized;
                var bounds = mesh.bounds;
                var side = Perpendicular(axis);
                var up = Vector3.Cross(axis, side);
                float radius = 0f, low = float.MaxValue, high = float.MinValue;
                foreach (var v in vertices) {
                    var offset = v - bounds.center;
                    var along = Vector3.Dot(offset, axis);
                    low = Mathf.Min(low, along);
                    high = Mathf.Max(high, along);
                    radius = Mathf.Max(radius, (offset - axis * along).sqrMagnitude);
                }
                radius = Mathf.Sqrt(radius);
                for (var i = 0; i < 48; i++) {
                    var a = i * Mathf.PI * 2f / 48f;
                    var rim = bounds.center + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
                    points.Add(rim + axis * low);
                    points.Add(rim + axis * high);
                }
            }
            else {
                if (directions == null) {
                    const int Count = 120;
                    directions = new Vector3[Count];
                    var golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
                    for (var i = 0; i < Count; i++) {
                        var y = 1f - 2f * (i + 0.5f) / Count;
                        var r = Mathf.Sqrt(1f - y * y);
                        directions[i] = new Vector3(Mathf.Cos(golden * i) * r, y, Mathf.Sin(golden * i) * r);
                    }
                }
                var best = new int[directions.Length];
                var most = new float[directions.Length];
                for (var d = 0; d < directions.Length; d++) most[d] = float.MinValue;
                for (var v = 0; v < vertices.Length; v++)
                    for (var d = 0; d < directions.Length; d++) {
                        var dot = Vector3.Dot(vertices[v], directions[d]);
                        if (dot > most[d]) {
                            most[d] = dot;
                            best[d] = v;
                        }
                    }
                foreach (var index in best.Distinct()) points.Add(vertices[index]);
            }
            var triangles = new List<int>();
            for (var i = 0; i + 2 < points.Count; i++) triangles.AddRange(new[] { 0, i + 1, i + 2 });
            hull = new Mesh { name = mesh.name + " (hull)" };
            hull.SetVertices(points);
            hull.SetTriangles(triangles, 0);
            hull.RecalculateBounds();
            Hulls[key] = hull;
            return hull;
        }

        // A wheel's axle in its own space: the hole nearest its middle.
        private static Vector3 WheelAxis(GameObject wheel) {
            var center = wheel.TryGetComponent(out Renderer r) ? r.bounds.center : wheel.transform.position;
            var hole = wheel.GetComponentsInChildren<HoleCollider>(true).OrderBy(h => (h.transform.position - center).sqrMagnitude).FirstOrDefault();
            var axis = hole != null ? wheel.transform.InverseTransformDirection(hole.transform.forward) : Vector3.forward;
            // Round to the part's own axes (wheels are modelled square on).
            var a = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return a.x >= a.y && a.x >= a.z ? Vector3.right : a.y >= a.z ? Vector3.up : Vector3.forward;
        }

        private static WheelKind KindOf(GameObject part) {
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id)) return WheelKind.None;
            var id = saved.id.ToUpperInvariant();
            if (id.StartsWith("OMNI-") || (id.Contains("OMNI") && !id.Contains("LOCKED"))) return WheelKind.Omni;
            if (id.StartsWith("TWHL-") || id.StartsWith("FWHL-") || id.StartsWith("MECN") || id.Contains("WHEEL") || id.Contains("TIRE")) return WheelKind.Traction;
            return WheelKind.None;
        }

        private static bool Hardware(GameObject part) {
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id)) return false;
            return saved.id.StartsWith("SCRW") || saved.id.StartsWith("NUT") || saved.id.StartsWith("RBMP");
        }

        private float NodeMass(GameObject node, List<PneumaticCylinder> cylinders) {
            var cylinder = cylinders.FirstOrDefault(c => c.Rod != null && c.Rod.gameObject == node);
            if (cylinder != null) return CylinderMass(cylinder) * 0.25f;
            var grams = node.TryGetComponent(out Parts_List.PartName name) ? name.weightInGrams : 0f;
            if (grams <= 0f && node.TryGetComponent(out Renderer r)) {
                var s = r.bounds.size;
                grams = Mathf.Clamp(s.x * s.y * s.z * 16.387f * 0.4f, 1f, 2000f);  // a guess: 0.4 g/cm³ of its box
            }
            if (node.TryGetComponent(out PneumaticCylinder own)) grams = CylinderMass(own) * 1000f * 0.75f;
            return Mathf.Max(grams, 0.5f) / 1000f;
        }

        private static float CylinderMass(PneumaticCylinder c) => c.TryGetComponent(out Parts_List.PartName name) && name.weightInGrams > 0f ? name.weightInGrams / 1000f : 0.08f;

        private IEnumerable<MeshRenderer> RenderersOf(GameObject node, List<PneumaticCylinder> cylinders) {
            var rodOf = node.GetComponent<PneumaticCylinder>();
            foreach (var renderer in node.GetComponentsInChildren<MeshRenderer>()) {
                if (!renderer.enabled || renderer.GetComponent<MeshFilter>() == null) continue;
                if (renderer.gameObject.layer == HoleCollider.HOLE_COLLISIONS_LAYER || renderer.GetComponent<HoleFace>() != null) continue;
                if (renderer.GetComponent<RodThread>() != null) continue;   // the thread's highlight
                if (rodOf != null && rodOf.Rod != null && renderer.transform.IsChildOf(rodOf.Rod)) continue;
                var owner = renderer.GetComponentInParent<SavedObject>();
                if (owner != null && owner.gameObject != node && node.GetComponent<SavedObject>() != null) continue;
                yield return renderer;
            }
        }

        private void MakeBody(SimBody body, List<PneumaticCylinder> cylinders, ref float floor) {
            var center = Vector3.zero;
            foreach (var node in body.nodes) {
                var m = NodeMass(node, cylinders);
                body.mass += m;
                var c = node.TryGetComponent(out Renderer r) ? r.bounds.center : node.transform.position;
                center += c * m;
            }
            center /= Mathf.Max(body.mass, 1e-6f);
            var go = new GameObject("Body") { layer = Layer };
            go.transform.SetParent(root, false);
            go.transform.position = center;
            var rb = go.AddComponent<Rigidbody>();
            body.rb = rb;

            foreach (var node in body.nodes) {
                foreach (var renderer in RenderersOf(node, cylinders)) {
                    var proxy = new GameObject(renderer.name);
                    proxy.transform.SetParent(go.transform, true);
                    proxy.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    proxy.transform.localScale = renderer.transform.lossyScale;
                    proxy.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    var copy = proxy.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = renderer.sharedMaterials;
                    copy.shadowCastingMode = renderer.shadowCastingMode;
                    copy.receiveShadows = renderer.receiveShadows;
                    copy.renderingLayerMask = renderer.renderingLayerMask & ~OutlineFeature.Layer;
                    renderer.enabled = false;
                    hidden.Add(renderer);
                    floor = Mathf.Min(floor, renderer.bounds.min.y);
                }
                // Its shape for collisions (not screws and nuts: they're inside what they hold).
                if (Hardware(node) || !node.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                var size = node.TryGetComponent(out Renderer bounds) ? bounds.bounds.size : Vector3.one;
                if (Mathf.Max(size.x, size.y, size.z) < 0.25f) continue;
                var shape = new GameObject("Collider") { layer = Layer };
                shape.transform.SetParent(go.transform, true);
                shape.transform.SetPositionAndRotation(node.transform.position, node.transform.rotation);
                shape.transform.localScale = node.transform.lossyScale;
                var kind = KindOf(node);
                var collider = shape.AddComponent<MeshCollider>();
                collider.sharedMesh = Hull(filter.sharedMesh, kind != WheelKind.None ? WheelAxis(node) : (Vector3?)null);
                collider.convex = true;
                collider.contactOffset = 0.02f;
                collider.sharedMaterial = kind == WheelKind.Omni ? Material(0f) : kind == WheelKind.Traction ? Material(1f) : Material(0.35f);
                body.colliders++;
                if (kind == WheelKind.Omni) omnis.Add(new Wheel { rb = rb, collider = collider, radius = Mathf.Max(size.x, size.y, size.z) / 2f });
            }

            rb.mass = Mathf.Max(body.mass, 0.005f);
            rb.centerOfMass = Vector3.zero;
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.05f;
            rb.maxAngularVelocity = 400f;
            rb.solverIterations = 20;
            rb.solverVelocityIterations = 8;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (body.colliders == 0) {
                rb.inertiaTensor = Vector3.one * rb.mass * 0.05f;
                rb.inertiaTensorRotation = Quaternion.identity;
            }
            else {
                // Very light, thin bodies (a shaft) get a little more inertia, for the joints' sake.
                var tensor = rb.inertiaTensor;
                var floorInertia = rb.mass * 0.02f;
                rb.inertiaTensor = new Vector3(Mathf.Max(tensor.x, floorInertia), Mathf.Max(tensor.y, floorInertia), Mathf.Max(tensor.z, floorInertia));
            }
            if (body.colliders > 0 && omnis.Any(w => w.rb == rb)) go.AddComponent<ContactCollector>().owner = this;
        }

        private void MakeHinges(List<(GameObject a, GameObject b, Vector3 point, Vector3 axis, MotorDrive motor)> hinges) {
            var pairs = new Dictionary<(SimBody, SimBody), List<(Vector3 point, Vector3 axis, MotorDrive motor)>>();
            foreach (var (a, b, point, axis, motor) in hinges) {
                if (!bodyOf.TryGetValue(a, out var ba) || !bodyOf.TryGetValue(b, out var bb) || ba == bb) continue;
                var key = (ba, bb);
                if (!pairs.ContainsKey(key) && pairs.ContainsKey((bb, ba))) key = (bb, ba);
                if (!pairs.TryGetValue(key, out var list)) pairs[key] = list = new List<(Vector3, Vector3, MotorDrive)>();
                list.Add((point, key.Item1 == ba ? axis : axis, motor));
            }
            foreach (var pair in pairs) {
                var (owner, other) = pair.Key;
                var list = pair.Value;
                var axis = list[0].axis.normalized;
                var collinear = list.All(h => Mathf.Abs(Vector3.Dot(h.axis.normalized, axis)) > 0.995f && Vector3.ProjectOnPlane(h.point - list[0].point, axis).magnitude < 0.06f);
                var point = list.Aggregate(Vector3.zero, (s, h) => s + h.point) / list.Count;
                var joint = owner.rb.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = other.rb;
                joint.autoConfigureConnectedAnchor = true;
                joint.anchor = owner.rb.transform.InverseTransformPoint(point);
                joint.axis = owner.rb.transform.InverseTransformDirection(axis);
                joint.secondaryAxis = owner.rb.transform.InverseTransformDirection(Perpendicular(axis));
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = collinear ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
                joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Locked;
                joint.enableCollision = false;
                joint.enablePreprocessing = false;
            }
        }

        private static float FreeSpeed(MotorDrive motor) => motor.Rpm * Mathf.PI * 2f / 60f;

        private static Vector3 Perpendicular(Vector3 axis) => Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;

        private Rigidbody HingedTo(Rigidbody body) {
            foreach (var joint in body.GetComponents<ConfigurableJoint>())
                if (joint.angularXMotion == ConfigurableJointMotion.Free) return joint.connectedBody;
            foreach (var other in bodies)
                foreach (var joint in other.rb.GetComponents<ConfigurableJoint>())
                    if (joint.connectedBody == body && joint.angularXMotion == ConfigurableJointMotion.Free) return other.rb;
            return null;
        }

        // Each drive system (shafts geared together, and their motors) is one train; a shaft held still by its frame
        // (fastened to what it turns in) isn't in one.
        private void MakeTrains(List<Rotor> rotors) {
            var done = new HashSet<Rotor>();
            foreach (var first in rotors) {
                if (done.Contains(first)) continue;
                var system = GearTrain.SystemOf(first, rotors);
                foreach (var rotor in system.rotors) done.Add(rotor);
                foreach (var (chain, rotor, inchesPerRadian) in system.chains)
                    if (bodyOf.TryGetValue(rotor.shaft, out var rotorBody))
                        MakeChain(chain, rotorBody.rb, HingedTo(rotorBody.rb), rotor.axis, system.jammed ? 0f : inchesPerRadian);
                if (system.jammed || (system.rotors.Count < 2 && first.motors.Count == 0)) continue;
                var train = new Train();
                var seen = new HashSet<Rigidbody>();
                foreach (var rotor in system.rotors) {
                    if (!bodyOf.TryGetValue(rotor.shaft, out var body) || !seen.Add(body.rb)) continue;
                    if (!body.rb.GetComponents<ConfigurableJoint>().Any(j => j.angularXMotion == ConfigurableJointMotion.Free)
                        && !bodies.Any(b => b.rb.GetComponents<ConfigurableJoint>().Any(j => j.connectedBody == body.rb && j.angularXMotion == ConfigurableJointMotion.Free)))
                        continue;   // not hinged to anything: the train can't turn it
                    train.rotors.Add((body.rb, HingedTo(body.rb), body.rb.transform.InverseTransformDirection(rotor.axis), body.rb.transform.InverseTransformPoint(rotor.start), system.ratio[rotor]));
                }
                foreach (var motor in system.Motors)
                    if (bodyOf.TryGetValue(motor.gameObject, out var motorBody) && !seen.Contains(motorBody.rb))
                        train.motors.Add((motor, system.MotorRatio(motor)));
                if (train.rotors.Count > 0) trains.Add(train);
            }
        }

        private void MakeChain(ChainLoop chain, Rigidbody rotor, Rigidbody frame, Vector3 axis, float inchesPerRadian) {
            if (chain == null || chains.Any(c => c.chain == chain)) return;
            var holder = new GameObject("Chain").transform;
            holder.SetParent(frame != null ? frame.transform : root, false);
            holder.SetPositionAndRotation(chain.transform.position, chain.transform.rotation);
            var sim = new SimChain { chain = chain, rotor = rotor, frame = frame, axisLocal = rotor.transform.InverseTransformDirection(axis), inchesPerRadian = inchesPerRadian, phase = chain.phase };
            foreach (var link in chain.Links) {
                if (link == null) continue;
                var copy = new GameObject(link.name);
                copy.transform.SetParent(holder, false);
                copy.AddComponent<MeshFilter>().sharedMesh = link.GetComponent<MeshFilter>().sharedMesh;
                var renderer = link.GetComponent<MeshRenderer>();
                var proxy = copy.AddComponent<MeshRenderer>();
                proxy.sharedMaterials = renderer.sharedMaterials;
                proxy.renderingLayerMask = renderer.renderingLayerMask & ~OutlineFeature.Layer;
                renderer.enabled = false;
                hidden.Add(renderer);
                sim.links.Add(copy.transform);
            }
            chain.Place(sim.links, sim.phase);
            chains.Add(sim);
        }

        // Chains that no shaft turns (round idlers only) still go with what holds their first sprocket.
        private void StillChains() {
            foreach (var chain in ChainLoop.All) {
                if (chain == null || chain.gameObject.IsDeleted() || chains.Any(c => c.chain == chain)) continue;
                var first = chain.Sprockets().Select(s => s.part).FirstOrDefault(p => bodyOf.ContainsKey(p));
                var body = first != null ? bodyOf[first].rb : null;
                MakeChain(chain, body != null ? body : null, body, chain.Normal, 0f);
            }
        }

        private void RunChains(float dt) {
            foreach (var sim in chains) {
                if (sim.rotor == null || Mathf.Abs(sim.inchesPerRadian) < 1e-6f) continue;
                var axis = sim.rotor.transform.TransformDirection(sim.axisLocal);
                var w = Vector3.Dot(sim.rotor.angularVelocity - (sim.frame != null ? sim.frame.angularVelocity : Vector3.zero), axis);
                sim.phase += w * sim.inchesPerRadian * dt;
                sim.chain.Place(sim.links, sim.phase);
            }
        }

        private void MakeSlides(List<PneumaticCylinder> cylinders) {
            foreach (var cylinder in cylinders) {
                if (!bodyOf.TryGetValue(cylinder.gameObject, out var barrel) || !bodyOf.TryGetValue(cylinder.Rod.gameObject, out var rod)) continue;
                if (barrel == rod) continue;   // what's on the rod is fastened to the cylinder's frame too: it can't move
                var dir = cylinder.ExtendDirection;
                var tip = cylinder.RodTip;
                var joint = rod.rb.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = barrel.rb;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = rod.rb.transform.InverseTransformPoint(tip);
                joint.connectedAnchor = barrel.rb.transform.InverseTransformPoint(tip - dir * ((cylinder.Position - 0.5f) * cylinder.Stroke));
                joint.axis = rod.rb.transform.InverseTransformDirection(dir);
                joint.secondaryAxis = rod.rb.transform.InverseTransformDirection(Perpendicular(dir));
                joint.xMotion = ConfigurableJointMotion.Limited;
                joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Locked;
                joint.linearLimit = new SoftJointLimit { limit = cylinder.Stroke / 2f };
                joint.enableCollision = false;
                joint.enablePreprocessing = false;
                var slide = new Slide { cylinder = cylinder, joint = joint, rod = rod.rb, barrel = barrel.rb, extended = cylinder.Extended };
                slides.Add(slide);
                SetSlide(slide);
            }
        }

        private static void SetSlide(Slide slide) {
            var force = (slide.extended ? PneumaticCylinder.ExtendForceNewtons : PneumaticCylinder.RetractForceNewtons) * Newton;
            slide.joint.xDrive = new JointDrive { positionSpring = 0f, positionDamper = force / CylinderSpeed, maximumForce = force };
            slide.joint.targetVelocity = new Vector3(SlideSign * (slide.extended ? CylinderSpeed : -CylinderSpeed), 0f, 0f);
        }

        // ------------------------------------------------------------------ driving

        /// <summary>For tests run from scripts (stepped frames get no keys).</summary>
        public static System.Func<(Vector2, Vector2)> TestSticks;

        /// <summary>The sticks: from a gamepad, else W/A/S/D (left) and the arrows (right).</summary>
        public static (Vector2 left, Vector2 right) Sticks() {
            if (TestSticks != null) return TestSticks();
            Vector2 left = default, right = default;
            var typing = InputEvents.TextInput.IsTyping;
            var k = Keyboard.current;
            if (k != null && !typing) {
                left = new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
                right = new Vector2((k.rightArrowKey.isPressed ? 1 : 0) - (k.leftArrowKey.isPressed ? 1 : 0), (k.upArrowKey.isPressed ? 1 : 0) - (k.downArrowKey.isPressed ? 1 : 0));
            }
            var pad = Gamepad.current;
            if (pad != null) {
                var l = pad.leftStick.ReadValue();
                var r = pad.rightStick.ReadValue();
                if (l.sqrMagnitude > 0.01f) left = l;
                if (r.sqrMagnitude > 0.01f) right = r;
            }
            return (left, right);
        }

        /// <summary>Left and right side power, -1..1, for the drive mode.</summary>
        public static (float left, float right) DrivePower() {
            var (l, r) = Sticks();
            float left, right;
            switch (Controls.Drive) {
                case Controls.DriveMode.Tank:
                    left = l.y;
                    right = r.y;
                    break;
                case Controls.DriveMode.Arcade:
                    left = l.y + l.x;
                    right = l.y - l.x;
                    break;
                default:
                    left = l.y + r.x;
                    right = l.y - r.x;
                    break;
            }
            var most = Mathf.Max(1f, Mathf.Abs(left), Mathf.Abs(right));
            return (left / most, right / most);
        }

        private void Update() {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !InputEvents.TextInput.IsTyping && !MotionUi.Capturing && !Popup.IsOpen) {
                Stop();
                return;
            }
            var typing = InputEvents.TextInput.IsTyping || InputEvents.TextInput.ShortcutsPaused;
            var (left, right) = DrivePower();
            foreach (var train in trains)
                foreach (var (motor, _) in train.motors) {
                    var side = Controls.Side(motor.gameObject);
                    float direction;
                    if (side == "L") direction = left;
                    else if (side == "R") direction = right;
                    else {
                        direction = 0f;
                        if (!typing && Controls.Held(Controls.ForwardKey(motor.gameObject))) direction += 1f;
                        if (!typing && Controls.Held(Controls.ReverseKey(motor.gameObject))) direction -= 1f;
                    }
                    motor.Command = direction * Controls.Speed(motor.gameObject) * (Controls.Reversed(motor.gameObject) ? -1f : 1f);
                }
            foreach (var slide in slides)
                if (!typing && Controls.Pressed(Controls.CylinderKey(slide.cylinder.gameObject))) {
                    slide.extended = !slide.extended;
                    SetSlide(slide);
                    Audio.Sounds.PlayAt(Audio.Sounds.Air, slide.rod.position, 0.7f);
                }
            foreach (var train in trains)
                foreach (var (motor, _) in train.motors) motor.Sound(Paused ? 0f : motor.Level);
            if (Paused && Keyboard.current != null && Keyboard.current.periodKey.wasPressedThisFrame) Physics.Simulate(Time.fixedDeltaTime);
        }

        private void LateUpdate() {
            if (!Follow || main == null || main.rb == null || PivotCamera.Main == null) return;
            var target = main.rb.worldCenterOfMass;
            var focus = PivotCamera.Main.transform;
            focus.position = Vector3.Lerp(focus.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        private void FixedUpdate() {
            foreach (var train in trains) Turn(train);
            RunChains(Time.fixedDeltaTime);
            foreach (var slide in slides) slide.rod.WakeUp();
            Omnis();
        }

        // A train turns as one: its speed (of its own turn) from its shafts' turning (what the last step did to them:
        // loads, the ground on wheels), then the motors' torque for a step, implicitly (a DC motor: stall torque down
        // to nothing at free speed, so its torque is A - B * speed), and every shaft is set to its ratio of that.
        // Meshing gears keep their ratio this way, and a motor that isn't driven brakes (V5 motors short their windings).
        private static void Turn(Train train) {
            var dt = Time.fixedDeltaTime;
            float inertia = 0f, momentum = 0f;
            var now = new float[train.rotors.Count];
            var spin = new float[train.rotors.Count];
            for (var i = 0; i < train.rotors.Count; i++) {
                var (rb, frame, axisLocal, pointLocal, k) = train.rotors[i];
                var n = rb.transform.TransformDirection(axisLocal);
                now[i] = Vector3.Dot(rb.angularVelocity - (frame != null ? frame.angularVelocity : Vector3.zero), n);
                spin[i] = Inertia(rb, n, rb.transform.TransformPoint(pointLocal));
                inertia += k * k * spin[i];
                momentum += k * spin[i] * now[i];
            }
            if (inertia <= 0f) return;
            var speed = momentum / inertia;
            float a = 0f, b = 0f, most = 0f;
            foreach (var (motor, k) in train.motors) {
                var stall = motor.StallTorque * NewtonMeter;
                a += k * stall * motor.Command;
                b += k * k * stall / FreeSpeed(motor);
                most += Mathf.Abs(k) * stall;
            }
            var next = (inertia * speed + a * dt) / (inertia + b * dt);
            // The V5 motor's current limit: no more than its stall torque.
            var torque = (next - speed) * inertia / dt;
            if (Mathf.Abs(torque) > most && most > 0f) next = speed + Mathf.Sign(torque) * most * dt / inertia;
            for (var i = 0; i < train.rotors.Count; i++) {
                var (rb, frame, axisLocal, pointLocal, k) = train.rotors[i];
                var change = k * next - now[i];
                if (Mathf.Abs(change) < 1e-6f) continue;
                var n = rb.transform.TransformDirection(axisLocal);
                Spin(rb, n, change, rb.transform.TransformPoint(pointLocal));
                if (frame != null) frame.AddTorque(-n * (change * spin[i]), ForceMode.Impulse);
                rb.WakeUp();
            }
            foreach (var (motor, k) in train.motors) motor.Level = k * next / FreeSpeed(motor);
        }

        private static float Inertia(Rigidbody body, Vector3 axis, Vector3 point) {
            var local = Quaternion.Inverse(body.rotation * body.inertiaTensorRotation) * axis;
            var tensor = body.inertiaTensor;
            var off = Vector3.ProjectOnPlane(body.worldCenterOfMass - point, axis);
            return Mathf.Max(local.x * local.x * tensor.x + local.y * local.y * tensor.y + local.z * local.z * tensor.z + body.mass * off.sqrMagnitude, 1e-7f);
        }

        private static void Spin(Rigidbody body, Vector3 axis, float change, Vector3 point) {
            var w = axis * change;
            body.angularVelocity += w;
            body.linearVelocity += Vector3.Cross(w, body.worldCenterOfMass - point);
        }

        // Omni wheels: no friction across them (the rollers), the tire's grip along the way they roll.
        private void Omnis() {
            if (contacts.Count == 0) return;
            var total = bodies.Sum(b => b.mass);
            var touching = Mathf.Max(1, contacts.Select(c => c.collider).Distinct().Count());
            foreach (var (collider, point, normalIn, impulse, other) in contacts) {
                var wheel = omnis.FirstOrDefault(w => w.collider == collider);
                if (wheel == null || wheel.rb == null) continue;
                var rb = wheel.rb;
                var axis = WheelAxis(wheel);
                var normal = normalIn;
                if (Vector3.Dot(normal, collider.bounds.center - point) < 0f) normal = -normal;
                var along = Vector3.Cross(axis, normal);
                if (along.sqrMagnitude < 1e-6f) continue;
                along.Normalize();
                var velocity = rb.GetPointVelocity(point) - (other != null ? other.GetPointVelocity(point) : Vector3.zero);
                var slip = Vector3.Dot(velocity, along);
                var spinMass = Inertia(rb, axis, collider.bounds.center) / Mathf.Max(wheel.radius * wheel.radius, 0.01f);
                var mass = 0.5f * Mathf.Min(spinMass, total / touching);
                var most = 1.0f * impulse;
                var j = Mathf.Clamp(-slip * mass, -most, most);
                rb.AddForceAtPosition(along * j, point, ForceMode.Impulse);
                if (other != null) other.AddForceAtPosition(-along * j, point, ForceMode.Impulse);
            }
            contacts.Clear();
        }

        private Vector3 WheelAxis(Wheel wheel) {
            if (wheel.axisLocal == Vector3.zero) {
                // The wheel's axle: the drive or hinge axis of its body.
                foreach (var joint in wheel.rb.GetComponents<ConfigurableJoint>())
                    if (joint.angularXMotion == ConfigurableJointMotion.Free) {
                        wheel.axisLocal = joint.axis.normalized;
                        break;
                    }
                if (wheel.axisLocal == Vector3.zero)
                    foreach (var other in bodies)
                        foreach (var joint in other.rb.GetComponents<ConfigurableJoint>())
                            if (joint.connectedBody == wheel.rb && joint.angularXMotion == ConfigurableJointMotion.Free)
                                wheel.axisLocal = wheel.rb.transform.InverseTransformDirection(joint.transform.TransformDirection(joint.axis)).normalized;
                if (wheel.axisLocal == Vector3.zero) wheel.axisLocal = Vector3.right;
            }
            return wheel.rb.transform.TransformDirection(wheel.axisLocal);
        }

        private class ContactCollector : MonoBehaviour {
            public Sandbox owner;
            private readonly List<ContactPoint> buffer = new();

            private void OnCollisionStay(Collision collision) {
                collision.GetContacts(buffer);
                foreach (var contact in buffer)
                    if (owner.omnis.Any(w => w.collider == contact.thisCollider))
                        owner.contacts.Add((contact.thisCollider, contact.point, contact.normal, contact.impulse.magnitude, collision.rigidbody));
            }
        }

        // ------------------------------------------------------------------ for the controls panel

        public int BodyCount => bodies.Count;
        public bool Drives(MotorDrive motor) => trains.Any(t => t.motors.Any(m => m.motor == motor));
        public bool Slides(PneumaticCylinder cylinder) => slides.Any(s => s.cylinder == cylinder);
        public bool Extended(PneumaticCylinder cylinder) => slides.FirstOrDefault(s => s.cylinder == cylinder)?.extended ?? cylinder.Extended;
    }
}
