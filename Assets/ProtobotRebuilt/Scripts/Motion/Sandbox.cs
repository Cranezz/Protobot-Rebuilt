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

        // V2 game mode (Scripts/Game): a game puts the robot on a field. It sets these before Begin.
        public const int ElementLayer = 18, FieldLayer = 19;
        private static readonly int[] SimLayers = { Layer, GroundLayer, ElementLayer, FieldLayer };
        /// <summary>The floor's height (the field's tiles) instead of the build's lowest point.</summary>
        public static float? FloorHeight;
        /// <summary>Adds the field's colliders and game elements to the simulation (under its root, so they go with it).</summary>
        public static System.Action<Transform> BuildEnvironment;
        /// <summary>A game runs the simulation: it handles Esc and the messages, and has its own window.</summary>
        public static bool Hosted;
        /// <summary>Off before a match starts and after it ends: motors and cylinders take no commands.</summary>
        public static bool DriveEnabled = true;

        // V2 online matches (Scripts/Online/OnlineMatch): several robots in one simulation, each driven by its player.
        /// <summary>A robot's controls for this frame: drive power per side and its keys.</summary>
        public struct DriveInput {
            public float left, right;
            public System.Func<Key, bool> held, pressed;
        }
        /// <summary>Which robot a part belongs to (null: one robot).</summary>
        public static System.Func<GameObject, int> RobotOf;
        /// <summary>A robot's controls; null (or a null answer) is this computer's keys and gamepad.</summary>
        public static System.Func<int, DriveInput?> InputOf;
        /// <summary>The robot the camera follows when there are several.</summary>
        public static int LocalRobot;
        /// <summary>Another computer runs the simulation: this one only shows where everything is (OnlineMatch sets it).</summary>
        public static bool Puppet;

        /// <summary>The simulation's bodies: the nodes each holds (parts, rods, leaves) and its rigid body.</summary>
        public IEnumerable<(IReadOnlyList<GameObject> nodes, Rigidbody rb)> Bodies() =>
            bodies.Where(b => b.rb != null).Select(b => ((IReadOnlyList<GameObject>)b.nodes, b.rb));

        // Which simulation layers touch: the robot everything, the ground the robot and game elements, game elements
        // everything, the field's fixed parts the robot and game elements.
        private static bool Touches(int a, int b) {
            if (a > b) (a, b) = (b, a);
            if (a == Layer) return b == Layer || b == GroundLayer || b == ElementLayer || b == FieldLayer;
            if (a == GroundLayer) return b == ElementLayer;
            if (a == ElementLayer) return b == ElementLayer || b == FieldLayer;
            return false;
        }
        public static Sandbox Instance => instance;

        /// <summary>V2: where a point of a build part is now: carried by its body in the simulation (the build's own
        /// parts stay where they were), else the point itself. Labels and markers use it to follow the robot.</summary>
        public static Vector3 SimPoint(GameObject part, Vector3 world) {
            if (instance == null || part == null || !instance.bodyOf.TryGetValue(part, out var body) || body.rb == null) return world;
            var now = Matrix4x4.TRS(body.rb.position, body.rb.rotation, Vector3.one);
            return now.MultiplyPoint3x4(body.startInverse.MultiplyPoint3x4(world));
        }

        /// <summary>V2: whether a part is one of the simulation's bodies (or rides on one).</summary>
        public static bool Simulated(GameObject part) => instance != null && part != null && instance.bodyOf.TryGetValue(part, out var body) && body.rb != null;

        public static Vector3 SimDirection(GameObject part, Vector3 direction) {
            if (instance == null || part == null || !instance.bodyOf.TryGetValue(part, out var body) || body.rb == null) return direction;
            return body.rb.rotation * (body.startInverse.rotation * direction);
        }

        public bool Paused { get; private set; }
        // V2: the camera: free, following the robot, or tracking a part or game piece clicked in the view.
        public enum CameraMode { Free, Robot, Part }
        public CameraMode View { get; private set; } = CameraMode.Robot;
        private Rigidbody tracked;
        private bool picking;
        private Vector3? lastTarget;
        public bool Follow { get => View == CameraMode.Robot; set => SetCamera(value ? CameraMode.Robot : CameraMode.Free); }

        public void SetCamera(CameraMode mode) {
            picking = mode == CameraMode.Part;
            if (mode != CameraMode.Part) tracked = null;
            View = mode == CameraMode.Part ? CameraMode.Free : mode;   // tracking starts once a part is clicked
            lastTarget = null;
            Interface.Interface.Say(mode == CameraMode.Free ? "The camera stays put: orbit, pan and zoom freely"
                : mode == CameraMode.Robot ? "The camera follows the robot" : "Click a part or a game piece to keep the camera on it", true);
        }

        // Picking the part to track: a click on the robot (layer 16) or a game piece (18).
        private void PickTracked() {
            if (!picking || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame || MouseInput.overUI) return;
            var view = UnityEngine.Camera.main;
            if (view == null) return;
            if (Physics.Raycast(view.ScreenPointToRay(MouseInput.Position), out var hit, 5000f, (1 << Layer) | (1 << 18), QueryTriggerInteraction.Ignore)
                && hit.rigidbody != null) {
                tracked = hit.rigidbody;
                View = CameraMode.Part;
                picking = false;
                lastTarget = null;
                Interface.Interface.Say("The camera tracks " + (hit.collider.gameObject.layer == 18 ? "that game piece" : "that part of the robot") + "; pick Free to let go", true);
            }
            else Interface.Interface.Say("Nothing to track there: click a part of the robot or a game piece", false);
        }

        private class SimBody {
            public Rigidbody rb;
            public readonly List<GameObject> nodes = new();
            public float mass;
            public int colliders;
            public Matrix4x4 startInverse = Matrix4x4.identity;   // V2: the body's pose when the run began, inverted
        }

        // A gear train: shafts (bodies, hinged to their frames) that turn together at their ratios k (of the train's
        // turn), and the motors driving it (k: the motor's own turn per turn of the train).
        private class Train {
            public readonly List<(Rigidbody rb, Rigidbody frame, Vector3 axisLocal, Vector3 pointLocal, float k)> rotors = new();
            public readonly List<(MotorDrive motor, float k)> motors = new();
            // V2: its shafts' hinges (with each one's ratio), whose drives hold a stopped train (GearboxFriction).
            public readonly List<(ConfigurableJoint joint, float k)> hinges = new();
            public float holding = -1f;   // the torque the hinges' drives hold now
        }

        private class Slide {
            public PneumaticCylinder cylinder;
            public ConfigurableJoint joint;
            public bool extended;
            public bool loose;   // V2: no air: the rod slides freely within its stroke
            public PneumaticCylinder.AirPorts ports;
            public Rigidbody rod, barrel;
            public float changedAt = -10f;   // V2: when its key last changed it (its state is shown on it for a moment)
            public float psi = AirSupply.MaxPsi;   // V2: the pressure it pushes with (its last fill from the tanks)
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
        private readonly HashSet<Rigidbody> driven = new();   // V2: bodies in a gear train
        private readonly List<Renderer> hidden = new();
        private readonly Dictionary<Renderer, Renderer> sourceOf = new();   // V2: a copy -> the build renderer it draws (replays)

        // A chain runs on the frame its sprockets turn in (its links copied there), at the speed its rotor turns.
        private class SimChain {
            public ChainLoop chain;
            public readonly List<Transform> links = new();
            public Rigidbody rotor, frame;
            public Vector3 axisLocal;
            public float inchesPerRadian, phase;
            public Transform holder;    // V2: the chain's plane (rides on frame)
            public SimTension tension;  // V2: its tensioner, if it has one
            public SimStop stop;        // V2: its length stops sprockets that move apart (not on its frame)
        }

        private readonly List<SimChain> chains = new();

        // V2: a chain tensioner (ChainTensioner): the arm swings on its joint screw, pulled by its band; the chain stops
        // it where the loop round the sprockets (where they are now) comes to the chain's own length. That stop is a
        // joint the chain moves every step: a distance limit from a point on the arm (a carried wrap's middle) to a
        // point on the frame 150° back round the pivot from where that point is when the chain is tight, so the arm is
        // free to swing away from the chain and stopped (by PhysX, with the reaction on the frame) going into it.
        private class SimTension {
            public SimChain sim;
            public ChainTensioner tensioner;
            public Rigidbody arm, frame;
            public ConfigurableJoint joint;
            public Vector3 pivotInFrame, axisInFrame, pointInArm;
            public float reach;
            public readonly List<GameObject> parts = new();
            public readonly List<Vector3> buildCenters = new();
            public readonly List<ChainWrap> wraps = new(), scratch = new();
            public readonly List<Vector3> world = new();
            public ChainPath path;
            public int pathFrame = -1;
            public float slack;   // inches the chain has over the loop (tests read it)
        }

        private readonly List<SimTension> tensions = new();

        private const float TensionWindow = 150f;

        private void MakeTensioners() {
            foreach (var sim in chains) {
                var chain = sim.chain;
                if (chain == null || chain.fixedLinks <= 0) continue;
                var t = ChainTensioner.Of(chain);
                if (t == null || !bodyOf.TryGetValue(t.armPart, out var armBody) || !bodyOf.TryGetValue(t.framePart, out var frameBody)) continue;
                if (armBody.rb == null || frameBody.rb == null || armBody == frameBody || sim.holder == null) continue;
                var st = new SimTension { sim = sim, tensioner = t, arm = armBody.rb, frame = frameBody.rb };
                for (var i = 0; i < chain.wraps.Count; i++) {
                    var part = chain.PartOf(i);
                    if (part == null || !bodyOf.ContainsKey(part)) {
                        st = null;
                        break;
                    }
                    st.parts.Add(part);
                    st.buildCenters.Add(chain.transform.TransformPoint(chain.wraps[i].center));
                    st.wraps.Add(chain.wraps[i].Copy());
                    st.world.Add(Vector3.zero);
                }
                if (st == null) continue;
                // The sim's bodies start where the build's parts are.
                var axis = t.Axis.normalized * t.pull;   // the way the band pulls it, right-handed
                st.pivotInFrame = st.frame.transform.InverseTransformPoint(t.Point);
                st.axisInFrame = st.frame.transform.InverseTransformDirection(axis);
                var p = st.buildCenters[t.wraps[0]];
                st.pointInArm = st.arm.transform.InverseTransformPoint(p);
                st.reach = Vector3.ProjectOnPlane(p - t.Point, axis).magnitude;
                if (st.reach < 0.1f) continue;
                var joint = st.arm.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = st.frame;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = st.pointInArm;
                joint.connectedAnchor = st.frame.transform.InverseTransformPoint(p);
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                joint.linearLimit = new SoftJointLimit { limit = 2f * st.reach * Mathf.Sin(TensionWindow / 2f * Mathf.Deg2Rad) };
                joint.enableCollision = false;
                joint.enablePreprocessing = false;
                // A tensioner arm is a few grams that a band pulls with a pound or more: the solver needs more passes
                // to hold it on its stop, and anything past it is put back (projection).
                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = 0.003f;
                joint.projectionAngle = 1f;
                foreach (var rb in new[] { st.arm }.Concat(bodies.Where(b => b.rb != null && b.rb != st.frame && b.rb.GetComponents<Joint>().Any(j => j.connectedBody == st.arm)).Select(b => b.rb))) {
                    rb.solverIterations = Mathf.Max(rb.solverIterations, 60);
                    rb.solverVelocityIterations = Mathf.Max(rb.solverVelocityIterations, 20);
                }
                st.joint = joint;
                sim.tension = st;
                tensions.Add(st);
                Tension(st);
            }
        }

        // Where the chain stops the arm now: the turn (toward the band's pull) that makes the loop the chain's length.
        private void Tension(SimTension st) {
            if (st.joint == null || st.arm == null || st.frame == null) return;
            var chain = st.sim.chain;
            var plane = st.sim.holder;
            var pivot = st.frame.transform.TransformPoint(st.pivotInFrame);
            var axis = st.frame.transform.TransformDirection(st.axisInFrame).normalized;
            for (var i = 0; i < st.parts.Count; i++) {
                st.world[i] = SimPoint(st.parts[i], st.buildCenters[i]);
                var local = plane.InverseTransformPoint(st.world[i]);
                st.wraps[i].center = new Vector2(local.x, local.y);
            }
            var carried = st.tensioner.wraps;
            var target = chain.fixedLinks * chain.family.pitch;
            float SlackAt(float degrees) {
                st.scratch.Clear();
                var turn = Quaternion.AngleAxis(degrees, axis);
                for (var i = 0; i < st.wraps.Count; i++) {
                    var w = st.wraps[i].Copy();
                    if (carried.Contains(i)) {
                        var local = plane.InverseTransformPoint(pivot + turn * (st.world[i] - pivot));
                        w.center = new Vector2(local.x, local.y);
                    }
                    st.scratch.Add(w);
                }
                var tight = ChainPath.Tight(st.scratch);
                return float.IsNaN(tight) ? float.NaN : target - tight;
            }
            var f0 = SlackAt(0f);
            st.slack = f0;
            var angle = float.NaN;
            if (!float.IsNaN(f0)) {
                const float H = 0.25f;
                var fp = SlackAt(H);
                var fm = SlackAt(-H);
                var slope = (fp - fm) / (2f * H);
                if (!float.IsNaN(slope) && Mathf.Abs(slope) > 1e-5f) {
                    angle = Mathf.Clamp(-f0 / slope, -30f, 30f);
                    var fa = SlackAt(angle);
                    if (!float.IsNaN(fa)) angle = Mathf.Clamp(angle - fa / slope, -30f, 30f);
                }
            }
            if (float.IsNaN(angle) || float.IsNaN(SlackAt(angle))) {
                // Newton couldn't find it (the idler is off the chain, or the chain is too short for where its sprockets
                // went: a real one would stop them; here nothing stops them): the arm stops where the chain comes
                // closest to its length, at the end of its travel. Nowhere at all: the band alone.
                ChainTensioner.SolveAngle(SlackAt, 1, out angle, out var left);
                if (float.IsNaN(left)) {
                    st.joint.linearLimit = new SoftJointLimit { limit = 1000f };
                    return;
                }
            }
            var point = st.arm.transform.TransformPoint(st.pointInArm);
            var stop = pivot + Quaternion.AngleAxis(angle, axis) * (point - pivot);
            var behind = pivot + Quaternion.AngleAxis(-TensionWindow, axis) * (stop - pivot);
            st.joint.connectedAnchor = st.frame.transform.InverseTransformPoint(behind);
            st.joint.linearLimit = new SoftJointLimit { limit = 2f * st.reach * Mathf.Sin(TensionWindow / 2f * Mathf.Deg2Rad) };
        }

        // V2 (build 79): a chain can't stretch. A sprocket whose middle moves relative to the chain's frame (on an arm, a
        // slide, anything not hinged straight to the frame) is held by its chain: a distance limit from its middle to a
        // point far back on the frame (40") along the way that lengthens the loop, moved every step to where the loop
        // would be exactly the chain's length (Newton, the slack shared by the moving sprockets), so it's a wall it can
        // come back from but not pass, and PhysX puts the reaction on the frame. (Tensioned chains have their own.)
        private class SimStop {
            public SimChain sim;
            public Rigidbody frame;
            public readonly List<GameObject> parts = new();
            public readonly List<Vector3> buildCenters = new();
            public readonly List<ChainWrap> wraps = new();
            public readonly List<(int wrap, ConfigurableJoint joint, Vector3 anchorLocal)> moving = new();
            public float target, slack;
            public int nan;   // steps the loop couldn't be worked out (tests)
            public ChainPath path;
            public int pathFrame = -1;
        }

        private readonly List<SimStop> stops = new();
        private const float StopReach = 40f;

        private void MakeChainStops() {
            foreach (var sim in chains) {
                var chain = sim.chain;
                if (chain == null || sim.tension != null || sim.frame == null || sim.holder == null || chain.Path == null || !chain.Path.Valid) continue;
                var st = new SimStop { sim = sim, frame = sim.frame };
                var ok = true;
                for (var i = 0; i < chain.wraps.Count; i++) {
                    var part = chain.PartOf(i);
                    if (part == null || !bodyOf.TryGetValue(part, out var body) || body.rb == null) { ok = false; break; }
                    var center = chain.transform.TransformPoint(chain.wraps[i].center);
                    st.parts.Add(part);
                    st.buildCenters.Add(center);
                    st.wraps.Add(chain.wraps[i].Copy());
                    var rb = body.rb;
                    if (rb == sim.frame || OnHingeAxis(rb, sim.frame, center)) continue;   // its middle stays put on the frame
                    var joint = rb.gameObject.AddComponent<ConfigurableJoint>();
                    joint.connectedBody = sim.frame;
                    joint.autoConfigureConnectedAnchor = false;
                    joint.anchor = rb.transform.InverseTransformPoint(center);
                    joint.connectedAnchor = sim.frame.transform.InverseTransformPoint(center);
                    joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
                    joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                    joint.linearLimit = new SoftJointLimit { limit = 1000f };
                    joint.enableCollision = true;
                    joint.enablePreprocessing = false;
                    // A motor pushing every step stretched it 0.2" (the solver gives a little each step): projection puts
                    // the sprocket back on its wall when it's past it (as the tensioners do).
                    joint.projectionMode = JointProjectionMode.PositionAndRotation;
                    joint.projectionDistance = 0.01f;
                    joint.projectionAngle = 180f;
                    st.moving.Add((i, joint, joint.anchor));
                    rb.solverIterations = Mathf.Max(rb.solverIterations, 40);   // a stiff chain: it stretched 0.1" at a motor's stall
                    rb.solverVelocityIterations = Mathf.Max(rb.solverVelocityIterations, 12);
                }
                if (!ok || st.moving.Count == 0) {
                    foreach (var m in st.moving) if (m.joint != null) Destroy(m.joint);
                    continue;
                }
                // Its length: its links, or the loop as built if that's longer (a chain made a touch tight).
                st.target = Mathf.Max(chain.Path.links * chain.family.pitch, ChainPath.Tight(st.wraps));
                sim.stop = st;
                stops.Add(st);
                ChainStop(st);
            }
        }

        // Whether `point` stays put relative to `frame` as `body` moves: it's on the line of a hinge joining them (a
        // sprocket on a shaft turning in the frame), not off it (an idler on an arm that swings on the frame).
        private bool OnHingeAxis(Rigidbody body, Rigidbody frame, Vector3 point) {
            foreach (var (owner, other) in new[] { (body, frame), (frame, body) })
                foreach (var joint in owner.GetComponents<ConfigurableJoint>()) {
                    if (joint.connectedBody != other || joint.angularXMotion != ConfigurableJointMotion.Free) continue;
                    var axis = joint.transform.TransformDirection(joint.axis).normalized;
                    var anchor = joint.transform.TransformPoint(joint.anchor);
                    if (Vector3.ProjectOnPlane(point - anchor, axis).magnitude < 0.05f) return true;
                }
            return false;
        }

        private void ChainStop(SimStop st) {
            if (st.frame == null) return;
            var plane = st.sim.holder;
            for (var i = 0; i < st.parts.Count; i++) {
                var local = plane.InverseTransformPoint(SimPoint(st.parts[i], st.buildCenters[i]));
                st.wraps[i].center = new Vector2(local.x, local.y);
            }
            var length = ChainPath.Tight(st.wraps);
            // The loop can't be worked out here (an idler pushed across the line between the others): the walls stay
            // where they were last (letting go let a motor swing its arm 25° on, the chain 0.85" too long).
            if (float.IsNaN(length)) { st.nan++; return; }
            st.slack = st.target - length;
            // Each moving middle's way of lengthening the loop (in the chain's plane) and how fast.
            const float H = 0.05f;
            var grads = new List<Vector2>();
            var total = 0f;
            foreach (var m in st.moving) {
                var c = st.wraps[m.wrap].center;
                float At(Vector2 at) {
                    st.wraps[m.wrap].center = at;
                    var l = ChainPath.Tight(st.wraps);
                    st.wraps[m.wrap].center = c;
                    return l;
                }
                var g = new Vector2(At(c + Vector2.right * H) - At(c - Vector2.right * H), At(c + Vector2.up * H) - At(c - Vector2.up * H)) / (2f * H);
                if (float.IsNaN(g.x) || float.IsNaN(g.y)) g = Vector2.zero;
                grads.Add(g);
                total += g.magnitude;
            }
            if (total < 1e-4f) return;
            var step = st.slack / total;   // each moves this far along its way to make the loop the chain's length
            for (var k = 0; k < st.moving.Count; k++) {
                var (_, joint, anchorLocal) = st.moving[k];
                if (joint == null || grads[k].sqrMagnitude < 1e-8f) continue;
                var way = plane.TransformDirection(new Vector3(grads[k].x, grads[k].y, 0f)).normalized;
                var now = joint.transform.TransformPoint(anchorLocal);
                var wall = now + way * step;
                joint.connectedAnchor = st.frame.transform.InverseTransformPoint(wall - way * StopReach);
                joint.linearLimit = new SoftJointLimit { limit = StopReach };
            }
        }

        /// <summary>V2 (tests): a chain stop's wraps now and what ChainPath.Build says of them.</summary>
        public static string ChainStopDetail(int index) {
            if (instance == null || index >= instance.stops.Count) return "none";
            var st = instance.stops[index];
            var path = ChainPath.Build(st.wraps, st.sim.chain.family.pitch, Vector2.down, st.sim.chain.Path.links);
            var who = "frame " + st.frame.name + " parts " + string.Join(",", st.parts.Select(p => p.name + "@" + (instance.bodyOf.TryGetValue(p, out var b) ? b.rb.name + (instance.HingedTo(b.rb) != null ? "~" + instance.HingedTo(b.rb).name : "") : "?"))) + " moving " + string.Join(",", st.moving.Select(m => m.wrap)) + " || ";
            return who + string.Join(" / ", st.wraps.Select(w => w.center.ToString("F3") + " r" + w.radius.ToString("F3") + " way" + w.way)) + " | tight " + ChainPath.Tight(st.wraps) + " | " + (path.Valid ? "valid" : path.problem);
        }

        /// <summary>V2 (tests): each chain held by its length: its slack now (inches) and how many sprockets move.</summary>
        public static List<(ChainLoop chain, float slack, int moving)> ChainStops() {
            var list = new List<(ChainLoop, float, int)>();
            if (instance != null) foreach (var st in instance.stops) list.Add((st.sim.chain, st.slack, st.moving.Count * 100000 + st.nan));
            return list;
        }

        /// <summary>V2 (tests): each tensioned chain's slack (inches of chain over the loop round its sprockets now)
        /// and its arm's turn about the pivot from the start (degrees).</summary>
        public static List<(ChainLoop chain, float slack, float turned)> Tensions() {
            var list = new List<(ChainLoop, float, float)>();
            if (instance == null) return list;
            foreach (var st in instance.tensions) {
                if (st.arm == null || st.frame == null) continue;
                var rel = Quaternion.Inverse(st.frame.rotation) * st.arm.rotation;
                var startRel = Quaternion.Inverse(instance.StartRotation(st.frame)) * instance.StartRotation(st.arm);
                (rel * Quaternion.Inverse(startRel)).ToAngleAxis(out var deg, out var ax);
                if (deg > 180f) deg -= 360f;
                list.Add((st.sim.chain, st.slack, deg * Mathf.Sign(Vector3.Dot(ax, st.axisInFrame))));
            }
            return list;
        }

        private Quaternion StartRotation(Rigidbody rb) {
            foreach (var body in bodies)
                if (body.rb == rb) return body.startInverse.inverse.rotation;
            return rb.rotation;
        }
        private readonly List<(Collider collider, Vector3 point, Vector3 normal, float impulse, Rigidbody other)> contacts = new();
        private Transform root;
        private SimBody main;

        // What the sandbox changes, put back when it stops.
        private Vector3 savedGravity;
        private float savedStep;
        private int savedIterations, savedVelocityIterations;
        private float savedMaxAngular, savedBounce, savedMaxDelta;
        private readonly List<Collider> pausedTriggers = new();
        private bool[] savedLayers;
        private System.Func<bool> savedBlocked;

        // ------------------------------------------------------------------ starting and stopping

        public static void Toggle() {
            if (Hosted) {   // V2: a game (or an online match) runs it: its own buttons stop it
                Interface.Interface.Say("The game is running the simulation: use its own buttons (Leave match, End run)", false);
                return;
            }
            if (Running) Stop();
            else Begin();
        }

        public static void Begin() {
            if (Running) return;
            if (InputEvents.TextInput.ShortcutsPaused && RobotOf == null) {   // (an online match closes them itself)
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
            if (Hosted) return;
            Devices.DevicesPanel.Open();   // V2: the Brain's devices (and the simulation's buttons)
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
            RobotCode.RobotProgram.Stop(null);   // V2 (Phase 9)
            instance = null;
            sandbox.Restore();
            Destroy(sandbox.gameObject);
            // V2: a game (or a replay) keeps the build tools hidden: only plain simulating brings them back.
            if (!Game.SkillsGame.Active && !Game.ReplayViewer.Active) SetBuildInterface(true);
            Devices.DevicesPanel.SimulationEnded();
            foreach (var motor in MotorDrive.Live()) motor.Manual = 0f;   // V2
            foreach (var motor in MotorDrive.Live()) motor.Silence();
            FlexGrip.Clear();
            AirSupply.End();   // V2
            DriveEnabled = true;
            if (!Hosted) Interface.Interface.Say("Back to building: the build is as it was", true);
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

        public static void SetBuildInterface(bool on) {
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
                for (var i = SimLayers.Length - 1; i >= 0; i--)
                    for (var other = 0; other < 32; other++)
                        Physics.IgnoreLayerCollision(SimLayers[i], other, savedLayers[i * 32 + other]);
                Physics.gravity = savedGravity;
                Time.fixedDeltaTime = savedStep;
                Physics.defaultSolverIterations = savedIterations;
                Physics.defaultSolverVelocityIterations = savedVelocityIterations;
                Physics.defaultMaxAngularSpeed = savedMaxAngular;
                Physics.bounceThreshold = savedBounce;
                Physics.simulationMode = SimulationMode.FixedUpdate;
                SelectionSystem.SelectionManager.Blocked = savedBlocked;
                Time.maximumDeltaTime = savedMaxDelta;
                foreach (var trigger in pausedTriggers)
                    if (trigger != null) trigger.enabled = true;
                pausedTriggers.Clear();
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
            savedMaxDelta = Time.maximumDeltaTime;
            savedLayers = new bool[SimLayers.Length * 32];
            for (var i = 0; i < SimLayers.Length; i++)
                for (var other = 0; other < 32; other++)
                    savedLayers[i * 32 + other] = Physics.GetIgnoreLayerCollision(SimLayers[i], other);
            savedBlocked = SelectionSystem.SelectionManager.Blocked;
            SelectionSystem.SelectionManager.Blocked = () => true;

            Physics.gravity = new Vector3(0f, -Gravity, 0f);
            Time.fixedDeltaTime = 1f / 240f;
            Physics.defaultSolverIterations = 16;
            Physics.defaultSolverVelocityIterations = 6;
            Physics.defaultMaxAngularSpeed = 400f;
            Physics.bounceThreshold = 4f;
            // V2: a slow frame runs at most 12 steps (the simulation slows down instead of freezing on a weak PC),
            // and the screws' hole-finding triggers rest: nothing in the build changes while it runs, and their
            // OnTriggerStay ran for every screw and hole on every step (half the simulation's time on a big robot).
            // Switching a collider off sends no exit, so every screw keeps its holes.
            Time.maximumDeltaTime = 0.05f;
            foreach (var detector in FindObjectsByType<HoleDetector>(FindObjectsSortMode.None))
                foreach (var trigger in detector.GetComponents<Collider>())
                    if (trigger.enabled && trigger.isTrigger) {
                        trigger.enabled = false;
                        pausedTriggers.Add(trigger);
                    }
            foreach (var layer in SimLayers)
                for (var other = 0; other < 32; other++)
                    Physics.IgnoreLayerCollision(layer, other, !Touches(layer, other));

            root = new GameObject("Simulation").transform;
            var parts = FindObjectsByType<SavedObject>(FindObjectsSortMode.None)
                        .Where(s => s.gameObject.activeInHierarchy && !s.gameObject.IsDeleted() && s.GetComponent<ChainLoop>() == null && s.GetComponent<ElasticBand>() == null && s.GetComponent<PneumaticTube>() == null)
                        .Select(s => s.gameObject).ToList();
            if (parts.Count == 0) throw new System.Exception("there's nothing in the build");
            var cylinders = parts.Select(p => p.GetComponent<PneumaticCylinder>()).Where(c => c != null && c.enabled && c.Rod != null).ToList();
            var nodes = new List<GameObject>(parts);
            nodes.AddRange(cylinders.Select(c => c.Rod.gameObject));
            // V2: a hinge's top leaf is a body of its own, turning on the pin.
            var hingeParts = parts.Select(p => p.GetComponent<HingePart>()).Where(h => h != null && h.enabled && h.Leaf != null).ToList();
            nodes.AddRange(hingeParts.Select(h => h.Leaf.gameObject));
            // V2: a universal joint's far yoke too (joined at the cross: MakeUJoints).
            ujoints = parts.Select(p => p.GetComponent<UniversalJoint>()).Where(u => u != null && u.enabled && u.Yoke != null).ToList();
            nodes.AddRange(ujoints.Select(u => u.Yoke.gameObject));
            parent = nodes.ToDictionary(n => n, n => n);
            var rotors = GearTrain.Rotors();

            // Tight screws hold what they go through together; a joint screw only its nut.
            var hinges = new List<(GameObject a, GameObject b, Vector3 point, Vector3 axis, MotorDrive motor)>();
            flexes.Clear();
            foreach (var part in parts) {
                if (part.GetComponent<Screw>() == null || part.GetComponent<RodThread>() != null) continue;
                if (RubberLink(part, out var farSide)) {   // V2: rigid with its first stud's side, bends to the other
                    foreach (var next in GroupManager.Neighbours(part)) {
                        if (!parent.ContainsKey(next)) continue;
                        if (farSide.Contains(next)) {
                            // V2: at the rubber's middle, along it there (bent or straight).
                            var (rubberStart, rubberLength) = Tools.PartBend.LinkRubber(part);
                            var (middle, turn) = Tools.PartBend.CenterAt(part, rubberStart + rubberLength / 2f, Tools.PartBend.SavedBends(part));
                            flexes.Add((part, next, Tools.PartBend.ToWorld(part, middle), part.transform.rotation * (turn * -Tools.PartBend.AlongLocal(part))));
                        }
                        else Union(part, next);
                    }
                    continue;
                }
                var joint = Linkage.IsJoint(part);
                var threaded = joint ? Linkage.Threaded(part) : null;
                var head = joint ? Linkage.HeadPart(part) : null;
                if (head != null) threaded.Add(head);
                foreach (var next in GroupManager.Neighbours(part)) {
                    if (!parent.ContainsKey(next)) continue;
                    if ((joint && !threaded.Contains(next)) || Linkage.SpinsOn(part, next)) hinges.Add((part, next, part.transform.position, part.transform.forward, null));
                    else Union(part, next);
                }
            }
            foreach (var hingePart in hingeParts) hinges.Add((hingePart.gameObject, hingePart.Leaf.gameObject, hingePart.PinPoint, hingePart.PinAxis, null));
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
            // V2: a part held by nothing but one shaft (a spacer or washer between a bearing and a gear) rides on it.
            // On its own hinge it weighed 5 g with almost no inertia: the gears and collars beside it spun it up to
            // the speed limit and it rattled everything round it.
            var alone = nodes.GroupBy(Find).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
            var onShafts = new Dictionary<GameObject, int>();
            foreach (var rotor in rotors)
                foreach (var (part, _) in rotor.bearings)
                    if (alone.Contains(part)) onShafts[part] = onShafts.TryGetValue(part, out var n) ? n + 1 : 1;
            var screwed = new HashSet<GameObject>(hinges.Where(h => h.motor == null && h.a.GetComponent<Screw>() != null).SelectMany(h => new[] { h.a, h.b }));
            foreach (var rotor in rotors)
                foreach (var (part, _) in rotor.bearings)
                    if (onShafts.TryGetValue(part, out var count) && count == 1 && !screwed.Contains(part) && part.GetComponent<MotorDrive>() == null)
                        Union(rotor.shaft, part);

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
            bogies.Clear();
            bogieWheels.Clear();
            foreach (var body in bodies) MakeBody(body, cylinders, ref floor);
            MakeBogieWheels();   // V2
            foreach (var body in bodies)
                if (body.rb != null) body.startInverse = Matrix4x4.TRS(body.rb.position, body.rb.rotation, Vector3.one).inverse;
            main = bodies.OrderByDescending(b => b.mass).First();
            if (RobotOf != null)   // V2: several robots: the camera follows this computer's
                main = bodies.Where(b => b.nodes.Any(n => RobotOf(n) == LocalRobot)).OrderByDescending(b => b.mass).FirstOrDefault() ?? main;

            // Ground: the grid, or the build's lowest point if it's below it.
            var ground = new GameObject("Ground") { layer = GroundLayer };
            ground.transform.SetParent(root, false);
            var box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(4000f, 2f, 4000f);
            box.center = new Vector3(0f, (FloorHeight ?? floor) - 1f, 0f);
            box.sharedMaterial = Material(1f);
            BuildEnvironment?.Invoke(root);   // V2: a game's field and game elements
            if (Puppet) Physics.simulationMode = SimulationMode.Script;   // V2: positions come from the host

            // Collisions within the robot only where parts don't start out touching (gears in mesh, a wheel on its
            // channel would fight).
            var all = root.GetComponentsInChildren<Collider>().Where(c => c.gameObject.layer == Layer).ToList();
            for (var i = 0; i < all.Count; i++)
                for (var j = i + 1; j < all.Count; j++) {
                    var a = all[i];
                    var b = all[j];
                    if (a.attachedRigidbody == b.attachedRigidbody) continue;
                    var ba = a.bounds;
                    ba.Expand(NearGap * 2f);
                    if (!ba.Intersects(b.bounds)) continue;
                    if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out _)
                        || Near(a, b))
                        Physics.IgnoreCollision(a, b, true);
                }

            MakeHinges(hinges);
            MakeFlexes();   // V2
            MakeUJoints();   // V2
            MakeSlideTrucks();   // V2
            MakeElastics();   // V2
            MakeTrains(rotors);
            driven.Clear();   // V2: the bodies a gear train turns (Omnis: a free wheel's grip is worked out apart)
            foreach (var train in trains) foreach (var rotor in train.rotors) driven.Add(rotor.rb);
            StillChains();
            MakeTensioners();   // V2
            MakeChainStops();   // V2
            AirSupply.Begin(parts);   // V2: the robot's tanks (before the slides take their first pressure)
            MakeSlides(cylinders);
        }

        private const float NearGap = 0.1f;

        private static bool Near(Collider a, Collider b) {
            var p = b.ClosestPoint(a.bounds.center);
            // V2: 0.1" (was 0.03"): a turning gear's hull sways a few hundredths as it turns, and against a channel or
            // bearing 0.09" away it struck it on every tooth.
            return (a.ClosestPoint(p) - p).sqrMagnitude < NearGap * NearGap;
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

        private enum WheelKind { None, Traction, Omni, Flex }

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

        // V2: a wheel's radius (its hull's rim from the axle; the world box was bigger for a wheel at an angle).
        private static float RimRadius(Mesh hull, Vector3 axis, float scale) {
            var most = 0f;
            var center = hull.bounds.center;
            foreach (var v in hull.vertices) most = Mathf.Max(most, Vector3.ProjectOnPlane(v - center, axis.normalized).sqrMagnitude);
            return Mathf.Sqrt(most) * scale;
        }

        private static WheelKind KindOf(GameObject part) {
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id)) return WheelKind.None;
            var id = saved.id.ToUpperInvariant();
            if (id.StartsWith("OMNI-") || (id.Contains("OMNI") && !id.Contains("LOCKED"))) return WheelKind.Omni;
            if (id.StartsWith("FWHL-") || id.Contains("FLEX")) return WheelKind.Flex;   // V2: soft grip (FlexGrip)
            if (id.StartsWith("TWHL-") || id.StartsWith("FWHL-") || id.StartsWith("MECN") || id.Contains("WHEEL") || id.Contains("TIRE")) return WheelKind.Traction;
            return WheelKind.None;
        }

        // ------------------------------------------------------------------ V2: bogie wheels
        // A bogie wheel is one model: a bracket and green wheels on steel pins. In the simulation the bracket is part
        // of the body it's screwed to, and each wheel (the halves on one pin) is a body of its own hinged to it on
        // its pin, so a bogie rolls instead of sliding. The pieces are cut from the model once and shared.
        private class BogieShape {
            public Mesh frame;       // the model with its wheels' sub-mesh left empty (materials stay in order)
            public Mesh frameHull;   // only the bracket's (and pins') points, for its collision shape
            public int wheelSub;
            public readonly List<(Mesh mesh, Vector3 axis)> wheels = new();
            // V2: the Double's rocker: the arm its two wheels turn on, pivoting on the middle pin.
            public Mesh rocker;
            public int rockerSub;
            public Vector3 pivot, pivotAxis;   // model space
        }

        // V2: groups of triangles joined by shared corners (corners at the same place count as one).
        private static List<List<int>> Pieces(Vector3[] vertices, int[] triangles) {
            var weld = new Dictionary<Vector3Int, int>();
            var point = new int[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) {
                var k = Vector3Int.RoundToInt(vertices[i] * 2000f);
                if (!weld.TryGetValue(k, out var w)) weld[k] = w = weld.Count;
                point[i] = w;
            }
            var parent = new int[weld.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            for (var i = 0; i + 2 < triangles.Length; i += 3) {
                var a = Find(point[triangles[i]]);
                parent[Find(point[triangles[i + 1]])] = a;
                parent[Find(point[triangles[i + 2]])] = a;
            }
            var pieces = new Dictionary<int, List<int>>();
            for (var i = 0; i + 2 < triangles.Length; i += 3) {
                var r = Find(point[triangles[i]]);
                if (!pieces.TryGetValue(r, out var list)) pieces[r] = list = new List<int>();
                list.Add(triangles[i]);
                list.Add(triangles[i + 1]);
                list.Add(triangles[i + 2]);
            }
            return pieces.Values.ToList();
        }

        private static Bounds BoundsOf(Vector3[] vertices, List<int> triangles) {
            var b = new Bounds(vertices[triangles[0]], Vector3.zero);
            foreach (var i in triangles) b.Encapsulate(vertices[i]);
            return b;
        }

        private static int AxisIndex(Vector3 axis) => Mathf.Abs(axis.x) > 0.5f ? 0 : Mathf.Abs(axis.y) > 0.5f ? 1 : 2;

        // V2: a bogie whose wheels sit on an arm that pivots on a middle pin (the Double): that arm is the plastic piece
        // nearest the wheels' middle (with another plastic piece, the bracket, besides it), the pin the steel piece
        // longest along their axle.
        private static void FindRocker(Mesh mesh, Vector3[] vertices, BogieShape shape, List<int> rest) {
            if (shape.wheels.Count < 2) return;
            var axis = shape.wheels[0].axis;
            var middle = shape.wheels.Aggregate(Vector3.zero, (a, w) => a + w.mesh.bounds.center) / shape.wheels.Count;
            var found = false;
            var bestSub = 0;
            List<int> bestTris = null;
            var bestDistance = float.MaxValue;
            var plastic = 0;
            for (var sub = 0; sub < mesh.subMeshCount; sub++) {
                if (sub == shape.wheelSub) continue;
                foreach (var piece in Pieces(vertices, mesh.GetTriangles(sub))) {
                    if (piece.Count < 180) continue;
                    var b = BoundsOf(vertices, piece);
                    if (b.size[AxisIndex(axis)] < 0.3f || Vector3.ProjectOnPlane(b.size, axis).magnitude < 0.5f) continue;   // (washers, pins)
                    plastic++;
                    var d = Vector3.ProjectOnPlane(b.center - middle, axis).magnitude;
                    if (d < bestDistance) {
                        found = true;
                        bestDistance = d;
                        bestSub = sub;
                        bestTris = piece;
                    }
                }
            }
            if (!found || plastic < 2) return;   // one piece: the wheels turn on the bracket itself
            var pinFound = false;
            var pinCenter = Vector3.zero;
            var pinLength = 0f;
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
                foreach (var piece in Pieces(vertices, mesh.GetTriangles(sub))) {
                    var b = BoundsOf(vertices, piece);
                    var along = b.size[AxisIndex(axis)];
                    var across = Vector3.ProjectOnPlane(b.size, axis).magnitude;
                    if (along > 0.3f && across < 0.3f && along > pinLength) {
                        pinFound = true;
                        pinCenter = b.center;
                        pinLength = along;
                    }
                }
            if (!pinFound) return;
            shape.rocker = Piece(vertices, bestTris, mesh.name + " (bogie rocker)", mesh);
            shape.rockerSub = bestSub;
            shape.pivot = pinCenter;
            shape.pivotAxis = axis;
            // The bracket without the arm.
            var arm = new HashSet<(int, int, int)>();
            for (var i = 0; i + 2 < bestTris.Count; i += 3) arm.Add((bestTris[i], bestTris[i + 1], bestTris[i + 2]));
            var keep = new List<int>();
            var subTris = mesh.GetTriangles(bestSub);
            for (var i = 0; i + 2 < subTris.Length; i += 3)
                if (!arm.Contains((subTris[i], subTris[i + 1], subTris[i + 2]))) keep.AddRange(new[] { subTris[i], subTris[i + 1], subTris[i + 2] });
            shape.frame.SetTriangles(keep, bestSub);
            var restKeep = new List<int>();
            for (var i = 0; i + 2 < rest.Count; i += 3)
                if (!arm.Contains((rest[i], rest[i + 1], rest[i + 2]))) restKeep.AddRange(new[] { rest[i], rest[i + 1], rest[i + 2] });
            shape.frameHull = Piece(vertices, restKeep, mesh.name + " (bogie bracket)");
        }

        private static readonly Dictionary<Mesh, BogieShape> BogieShapes = new();
        private readonly List<(GameObject node, Rigidbody frame)> bogies = new();

        private static BogieShape BogieOf(GameObject node) {
            if (!node.TryGetComponent(out SavedObject saved) || saved.id == null || !saved.id.Contains("Bogie Wheel")) return null;
            var filter = node.GetComponent<MeshFilter>();
            var renderer = node.GetComponent<MeshRenderer>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || renderer == null || !mesh.isReadable) return null;
            if (BogieShapes.TryGetValue(mesh, out var known)) return known;
            BogieShape shape = null;
            var materials = renderer.sharedMaterials;
            var wheelSub = -1;
            for (var i = 0; i < mesh.subMeshCount && i < materials.Length; i++)
                if (materials[i] != null && materials[i].name.Contains("Green")) wheelSub = i;
            if (wheelSub >= 0) {
                shape = new BogieShape { wheelSub = wheelSub };
                var vertices = mesh.vertices;
                shape.frame = Object.Instantiate(mesh);
                shape.frame.name = mesh.name + " (bogie frame)";
                shape.frame.SetTriangles(System.Array.Empty<int>(), wheelSub);
                var rest = new List<int>();
                for (var i = 0; i < mesh.subMeshCount; i++)
                    if (i != wheelSub) rest.AddRange(mesh.GetTriangles(i));
                shape.frameHull = Piece(vertices, rest, mesh.name + " (bogie bracket)");
                // The wheels: the wheel sub-mesh's connected pieces, grouped by the line they turn about (each piece's
                // thin way is its axle).
                var triangles = mesh.GetTriangles(wheelSub);
                var weld = new Dictionary<Vector3Int, int>();
                var point = new int[vertices.Length];
                for (var i = 0; i < vertices.Length; i++) {
                    var k = Vector3Int.RoundToInt(vertices[i] * 2000f);
                    if (!weld.TryGetValue(k, out var w)) weld[k] = w = weld.Count;
                    point[i] = w;
                }
                var parent = new int[weld.Count];
                for (var i = 0; i < parent.Length; i++) parent[i] = i;
                int Find(int x) {
                    while (parent[x] != x) x = parent[x] = parent[parent[x]];
                    return x;
                }
                for (var i = 0; i < triangles.Length; i += 3) {
                    var a = Find(point[triangles[i]]);
                    parent[Find(point[triangles[i + 1]])] = a;
                    parent[Find(point[triangles[i + 2]])] = a;
                }
                var pieces = new Dictionary<int, List<int>>();
                for (var i = 0; i < triangles.Length; i += 3) {
                    var r = Find(point[triangles[i]]);
                    if (!pieces.TryGetValue(r, out var list)) pieces[r] = list = new List<int>();
                    list.Add(triangles[i]);
                    list.Add(triangles[i + 1]);
                    list.Add(triangles[i + 2]);
                }
                var groups = new Dictionary<string, (Vector3 axis, List<int> triangles)>();
                foreach (var piece in pieces.Values) {
                    if (piece.Count < 60) continue;
                    var b = new Bounds(vertices[piece[0]], Vector3.zero);
                    foreach (var i in piece) b.Encapsulate(vertices[i]);
                    var axis = b.size.x <= b.size.y && b.size.x <= b.size.z ? Vector3.right : b.size.y <= b.size.z ? Vector3.up : Vector3.forward;
                    var line = b.center - axis * Vector3.Dot(b.center, axis);
                    var key = axis + "|" + Mathf.Round(line.x * 20f) + "," + Mathf.Round(line.y * 20f) + "," + Mathf.Round(line.z * 20f);
                    if (!groups.TryGetValue(key, out var group)) groups[key] = group = (axis, new List<int>());
                    group.triangles.AddRange(piece);
                }
                foreach (var group in groups.Values) shape.wheels.Add((Piece(vertices, group.triangles, mesh.name + " (bogie wheel)", mesh), group.axis));
                if (shape.wheels.Count == 0) shape = null;
                else FindRocker(mesh, vertices, shape, rest);   // V2
            }
            BogieShapes[mesh] = shape;
            return shape;
        }

        // A mesh of only some triangles of another (its own vertices); with source, normals and uv too.
        private static Mesh Piece(Vector3[] vertices, List<int> triangles, string name, Mesh source = null) {
            var map = new Dictionary<int, int>();
            var points = new List<Vector3>();
            var normals = source != null ? source.normals : null;
            var uvs = source != null ? source.uv : null;
            var newNormals = new List<Vector3>();
            var newUvs = new List<Vector2>();
            var indices = new List<int>(triangles.Count);
            foreach (var i in triangles) {
                if (!map.TryGetValue(i, out var j)) {
                    map[i] = j = points.Count;
                    points.Add(vertices[i]);
                    if (normals != null && normals.Length == vertices.Length) newNormals.Add(normals[i]);
                    if (uvs != null && uvs.Length == vertices.Length) newUvs.Add(uvs[i]);
                }
                indices.Add(j);
            }
            var mesh = new Mesh { name = name, indexFormat = points.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(points);
            if (newNormals.Count == points.Count) mesh.SetNormals(newNormals);
            if (newUvs.Count == points.Count) mesh.SetUVs(0, newUvs);
            mesh.SetTriangles(indices, 0);
            if (newNormals.Count != points.Count) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void MakeBogieWheels() {
            foreach (var (node, frame) in bogies) {
                var shape = BogieOf(node);
                if (shape == null || frame == null) continue;
                // The wheels' color as the part shows it now (painted or not).
                var own = node.GetComponent<MeshRenderer>().sharedMaterials;
                var material = shape.wheelSub < own.Length ? own[shape.wheelSub] : null;
                var holder = frame;
                var holderColliders = new List<Collider>(frame.GetComponentsInChildren<Collider>());
                if (shape.rocker != null) {
                    // V2: the Double's arm, a body pivoting on its middle pin (about 25° each way before the bracket
                    // stops it); the wheels turn on it, so the bogie keeps both wheels on uneven ground.
                    var arm = new GameObject("Bogie Rocker") { layer = Layer };
                    arm.transform.SetParent(root, false);
                    arm.transform.SetPositionAndRotation(node.transform.position, node.transform.rotation);
                    var armView = new GameObject("Rocker");
                    armView.transform.SetParent(arm.transform, false);
                    armView.transform.localScale = node.transform.lossyScale;
                    armView.AddComponent<MeshFilter>().sharedMesh = shape.rocker;
                    armView.AddComponent<MeshRenderer>().sharedMaterial = shape.rockerSub < own.Length ? own[shape.rockerSub] : null;
                    var armShape = new GameObject("Collider") { layer = Layer };
                    armShape.transform.SetParent(arm.transform, false);
                    armShape.transform.localScale = node.transform.lossyScale;
                    var armCollider = armShape.AddComponent<MeshCollider>();
                    armCollider.sharedMesh = Hull(shape.rocker, null);
                    armCollider.convex = true;
                    armCollider.contactOffset = 0.02f;
                    armCollider.sharedMaterial = Material(0.35f);
                    foreach (var other in holderColliders) Physics.IgnoreCollision(armCollider, other, true);
                    var armBody = arm.AddComponent<Rigidbody>();
                    armBody.mass = 0.02f;
                    armBody.angularDamping = 0.5f;
                    armBody.solverIterations = 20;
                    armBody.interpolation = RigidbodyInterpolation.Interpolate;
                    var pivot = arm.AddComponent<ConfigurableJoint>();
                    pivot.connectedBody = frame;
                    pivot.autoConfigureConnectedAnchor = true;
                    pivot.anchor = Vector3.Scale(shape.pivot, node.transform.lossyScale);
                    pivot.axis = shape.pivotAxis;
                    pivot.secondaryAxis = Perpendicular(shape.pivotAxis);
                    pivot.xMotion = pivot.yMotion = pivot.zMotion = ConfigurableJointMotion.Locked;
                    pivot.angularXMotion = ConfigurableJointMotion.Limited;
                    pivot.lowAngularXLimit = new SoftJointLimit { limit = -25f };
                    pivot.highAngularXLimit = new SoftJointLimit { limit = 25f };
                    pivot.angularYMotion = pivot.angularZMotion = ConfigurableJointMotion.Locked;
                    pivot.enableCollision = false;
                    pivot.enablePreprocessing = false;
                    holder = armBody;
                    holderColliders.Add(armCollider);
                }
                foreach (var (mesh, axis) in shape.wheels) {
                    var go = new GameObject("Bogie Wheel") { layer = Layer };
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(node.transform.position, node.transform.rotation);
                    var view = new GameObject("Wheel");
                    view.transform.SetParent(go.transform, false);
                    view.transform.localScale = node.transform.lossyScale;
                    view.AddComponent<MeshFilter>().sharedMesh = mesh;
                    view.AddComponent<MeshRenderer>().sharedMaterial = material;
                    var shapeObject = new GameObject("Collider") { layer = Layer };
                    shapeObject.transform.SetParent(go.transform, false);
                    shapeObject.transform.localScale = node.transform.lossyScale;
                    var collider = shapeObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = Hull(mesh, axis);
                    collider.convex = true;
                    collider.contactOffset = 0.02f;
                    collider.sharedMaterial = Material(0.8f);
                    foreach (var other in holderColliders) Physics.IgnoreCollision(collider, other, true);
                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = 0.012f;   // the two plastic halves, about 12 g
                    rb.angularDamping = 0.5f;   // plastic on a steel pin
                    rb.maxAngularVelocity = 400f;
                    rb.solverIterations = 20;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    var joint = go.AddComponent<ConfigurableJoint>();
                    joint.connectedBody = holder;   // V2: the bracket, or the Double's rocker
                    joint.autoConfigureConnectedAnchor = true;
                    joint.anchor = Vector3.Scale(mesh.bounds.center, node.transform.lossyScale);
                    joint.axis = axis;
                    joint.secondaryAxis = Perpendicular(axis);
                    joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                    joint.angularXMotion = ConfigurableJointMotion.Free;
                    joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Locked;
                    joint.enableCollision = false;
                    joint.enablePreprocessing = false;
                    bogieWheels.Add((rb, holder, joint, axis));
                }
            }
        }

        private readonly List<(Rigidbody wheel, Rigidbody frame, ConfigurableJoint joint, Vector3 axisLocal)> bogieWheels = new();

        // Plastic turning on a steel pin: friction about 0.25 at the pin's radius (0.08") against the load the pin
        // carries, so a bogie rolls freely but doesn't roll on for ever (a lone one kept the speed its drop gave it).
        private void BogieFriction() {
            const float PinFriction = 0.25f, PinRadius = 0.08f;
            foreach (var (wheel, frame, joint, axisLocal) in bogieWheels) {
                if (wheel == null || frame == null || wheel.isKinematic) continue;
                var axis = wheel.transform.TransformDirection(axisLocal);
                var spin = Vector3.Dot(wheel.angularVelocity - frame.angularVelocity, axis);
                if (Mathf.Abs(spin) < 1e-4f) continue;
                var inertia = Inertia(wheel, axis, wheel.worldCenterOfMass);
                var most = inertia * Mathf.Abs(spin) / Time.fixedDeltaTime;   // never more than stops it this step
                var torque = Mathf.Min(PinFriction * joint.currentForce.magnitude * PinRadius, most) * Mathf.Sign(spin);
                wheel.AddTorque(-axis * torque);
                frame.AddTorque(axis * torque);
            }
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
            var hingeOf = node.GetComponent<HingePart>();   // V2: its top leaf is its own body
            var ujOf = node.GetComponent<UniversalJoint>();
            foreach (var renderer in node.GetComponentsInChildren<MeshRenderer>()) {
                if (!renderer.enabled || renderer.GetComponent<MeshFilter>() == null) continue;
                if (renderer.gameObject.layer == HoleCollider.HOLE_COLLISIONS_LAYER || renderer.GetComponent<HoleFace>() != null) continue;
                if (renderer.GetComponent<RodThread>() != null) continue;   // the thread's highlight
                if (rodOf != null && rodOf.Rod != null && renderer.transform.IsChildOf(rodOf.Rod)) continue;
                if (hingeOf != null && hingeOf.Leaf != null && renderer.transform.IsChildOf(hingeOf.Leaf)) continue;
                if (ujOf != null && ujOf.Yoke != null && renderer.transform.IsChildOf(ujOf.Yoke)) continue;
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
                    var bogie = renderer.gameObject == node ? BogieOf(node) : null;   // V2: its wheels turn on their own
                    proxy.AddComponent<MeshFilter>().sharedMesh = bogie != null ? bogie.frame : renderer.GetComponent<MeshFilter>().sharedMesh;
                    var copy = proxy.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = renderer.sharedMaterials;
                    copy.shadowCastingMode = renderer.shadowCastingMode;
                    copy.receiveShadows = renderer.receiveShadows;
                    copy.renderingLayerMask = renderer.renderingLayerMask & ~OutlineFeature.Layer;
                    sourceOf[copy] = renderer;   // V2
                    renderer.enabled = false;
                    hidden.Add(renderer);
                    floor = Mathf.Min(floor, renderer.bounds.min.y);
                }
                // Its shape for collisions (not screws and nuts: they're inside what they hold).
                // V2: a screw or nut on its own (holding nothing) gets one, or it fell through the floor forever.
                if ((Hardware(node) && body.nodes.Count > 1) || !node.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                var size = node.TryGetComponent(out Renderer bounds) ? bounds.bounds.size : Vector3.one;
                if (Mathf.Max(size.x, size.y, size.z) < 0.25f) continue;
                var shape = new GameObject("Collider") { layer = Layer };
                shape.transform.SetParent(go.transform, true);
                shape.transform.SetPositionAndRotation(node.transform.position, node.transform.rotation);
                shape.transform.localScale = node.transform.lossyScale;
                var kind = KindOf(node);
                var collider = shape.AddComponent<MeshCollider>();
                collider.sharedMesh = Hull(filter.sharedMesh, kind != WheelKind.None ? WheelAxis(node) : (Vector3?)null);
                if (BogieOf(node) is BogieShape bogieShape) {   // V2: the bracket's own shape; its wheels are bodies
                    collider.sharedMesh = Hull(bogieShape.frameHull, null);
                    kind = WheelKind.None;
                    bogies.Add((node, rb));
                }
                collider.convex = true;
                collider.contactOffset = 0.02f;
                collider.sharedMaterial = kind == WheelKind.Omni ? Material(0f) : kind == WheelKind.Traction ? Material(1f)
                    : kind == WheelKind.Flex ? Material(1.2f) : Material(0.35f);
                if (kind == WheelKind.Flex) FlexGrip.Register(collider);
                body.colliders++;
                if (kind == WheelKind.Omni) omnis.Add(new Wheel { rb = rb, collider = collider, radius = RimRadius(collider.sharedMesh, WheelAxis(node), shape.transform.lossyScale.x) });
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

        // V2: the rubber link (VEX's flexible link: a rubber body with a stud each end). What its far stud holds hangs
        // on a sprung joint at the rubber's middle: it bends up to 45° (twists 20°) and springs back. About 700
        // lbf-free units: 1 lb on a 1" lever bends it about 15°.
        private readonly List<(GameObject link, GameObject other, Vector3 point, Vector3 axis)> flexes = new();

        private static bool RubberLink(GameObject part, out HashSet<GameObject> farSide) {
            farSide = null;
            if (!part.TryGetComponent(out SavedObject saved) || saved.id != "VXHARDWARE-Rubber Link" || !part.TryGetComponent(out Screw screw) || screw.holeDetector == null) return false;
            farSide = new HashSet<GameObject>();
            var tip = part.transform.position;
            var along = -part.transform.forward;
            // V2: by which stud it's nearer (a bent link's far stud is turned).
            part.TryGetComponent(out RubberLinkPart linkPart);
            foreach (var hole in screw.holeDetector.GetOrderedHoles())
                if (hole != null && hole.holeData != null && hole.holeData.part != null
                    && (linkPart != null ? linkPart.OnFarStud(hole.holeData.position) : Vector3.Dot(hole.holeData.position - tip, along) > screw.ScrewLength / 2f))
                    farSide.Add(hole.holeData.part);
            return true;
        }

        private List<UniversalJoint> ujoints = new();

        // V2: rubber bands and latex tubing: each span of the loop pulls the posts at its ends toward each other with
        // the band's tension at its stretch now (the loop's length changes as its posts' middles move apart or
        // together; the wraps round the posts stay). PhysX spring joints do the pulling (their springs are solved
        // with the rest, so a light part on a strong band stays steady); their stiffness is set every step so the
        // pull is the band's: spring = tension / span.
        private class BandPull {
            public ElasticBand band;
            public readonly List<(Rigidbody rb, Vector3 local)> posts = new();
            public readonly List<(SpringJoint joint, int a, int b)> spans = new();
            public float startLength, startPolygon;
        }

        private readonly List<BandPull> bandPulls = new();

        private void MakeElastics() {
            bandPulls.Clear();
            foreach (var band in ElasticBand.All) {
                if (band == null || band.gameObject.IsDeleted() || band.Shape == null || !band.Shape.Valid || band.posts.Count < 2) continue;
                var pull = new BandPull { band = band, startLength = band.Shape.length };
                foreach (var post in band.posts) {
                    Rigidbody rb = null;
                    if (post.part != null && bodyOf.TryGetValue(post.part, out var body)) rb = body.rb;
                    var center = post.Center;
                    pull.posts.Add((rb, rb != null ? rb.transform.InverseTransformPoint(center) : center));
                }
                var n = pull.posts.Count;
                for (var i = 0; i < n; i++) {
                    var a = pull.posts[i];
                    var b = pull.posts[(i + 1) % n];
                    if (a.rb == null || b.rb == null || a.rb == b.rb) continue;
                    var joint = a.rb.gameObject.AddComponent<SpringJoint>();
                    joint.connectedBody = b.rb;
                    joint.autoConfigureConnectedAnchor = false;
                    joint.anchor = a.local;
                    joint.connectedAnchor = b.local;
                    joint.minDistance = 0f;
                    joint.maxDistance = 0f;
                    joint.tolerance = 0.001f;
                    joint.spring = 0f;
                    joint.damper = 0f;
                    joint.enableCollision = true;
                    pull.spans.Add((joint, i, (i + 1) % n));
                }
                if (pull.spans.Count == 0) continue;
                pull.startPolygon = Polygon(pull);
                bandPulls.Add(pull);
            }
            PullBands();
        }

        private static Vector3 PostAt((Rigidbody rb, Vector3 local) post) => post.rb != null ? post.rb.transform.TransformPoint(post.local) : post.local;

        private static float Polygon(BandPull pull) {
            var total = 0f;
            for (var i = 0; i < pull.posts.Count; i++) total += Vector3.Distance(PostAt(pull.posts[i]), PostAt(pull.posts[(i + 1) % pull.posts.Count]));
            return total;
        }

        private void PullBands() {
            const float LbfToSim = 4.448f * Newton;
            foreach (var pull in bandPulls) {
                if (pull.band == null) continue;
                var length = Mathf.Max(0.01f, pull.startLength + Polygon(pull) - pull.startPolygon);
                var tension = pull.band.SpanPull(length) * LbfToSim;
                // Its stiffness here, for a little damping along each span.
                var stiff = (pull.band.SpanPull(length + 0.01f) * LbfToSim - tension) / 0.01f;
                foreach (var (joint, a, b) in pull.spans) {
                    if (joint == null) continue;
                    var span = Mathf.Max(0.05f, Vector3.Distance(PostAt(pull.posts[a]), PostAt(pull.posts[b])));
                    joint.spring = tension / span;
                    joint.damper = Mathf.Max(0f, stiff) * 0.01f;
                }
            }
        }

        // V2: a slide truck slides along its member (free, with the acetal's light drag), between the member's ends.
        private void MakeSlideTrucks() {
            foreach (var truck in FindObjectsByType<SlideTruck>(FindObjectsSortMode.None)) {
                if (truck.Member == null || !bodyOf.TryGetValue(truck.gameObject, out var a) || !bodyOf.TryGetValue(truck.Member, out var b) || a == b) continue;
                var (point, along, low, high) = SlideTruck.Track(truck.Member);
                var x = Vector3.Dot(truck.transform.position - point, along);
                var middle = point + along * ((low + high) / 2f);
                var joint = a.rb.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = b.rb;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = a.rb.transform.InverseTransformPoint(truck.transform.position);
                // The limit is symmetric round the joint's rest place: the middle of the track.
                joint.connectedAnchor = b.rb.transform.InverseTransformPoint(truck.transform.position - along * (x - (low + high) / 2f));
                joint.axis = a.rb.transform.InverseTransformDirection(along);
                joint.secondaryAxis = a.rb.transform.InverseTransformDirection(Perpendicular(along));
                joint.xMotion = ConfigurableJointMotion.Limited;
                joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Locked;
                joint.linearLimit = new SoftJointLimit { limit = Mathf.Max(0.01f, (high - low) / 2f) };
                joint.xDrive = new JointDrive { positionSpring = 0f, positionDamper = 0.3f * Newton, maximumForce = 0.5f * Newton };
                joint.targetVelocity = Vector3.zero;
                joint.enableCollision = false;
                joint.enablePreprocessing = false;
            }
        }

        // V2: a universal joint: its yokes free to bend about the cross's two pins (45° each way), never to twist.
        private void MakeUJoints() {
            foreach (var uj in ujoints) {
                if (!bodyOf.TryGetValue(uj.gameObject, out var a) || !bodyOf.TryGetValue(uj.Yoke.gameObject, out var b) || a == b) continue;
                var joint = a.rb.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = b.rb;
                joint.autoConfigureConnectedAnchor = true;
                joint.anchor = a.rb.transform.InverseTransformPoint(uj.Cross);
                joint.axis = a.rb.transform.InverseTransformDirection(uj.PinA);
                joint.secondaryAxis = a.rb.transform.InverseTransformDirection(uj.PinB);
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = ConfigurableJointMotion.Limited;
                joint.angularYMotion = ConfigurableJointMotion.Limited;
                joint.angularZMotion = ConfigurableJointMotion.Locked;
                joint.lowAngularXLimit = new SoftJointLimit { limit = -UniversalJoint.MaxBend };
                joint.highAngularXLimit = new SoftJointLimit { limit = UniversalJoint.MaxBend };
                joint.angularYLimit = new SoftJointLimit { limit = UniversalJoint.MaxBend };
                joint.enableCollision = false;
                joint.enablePreprocessing = false;
            }
        }

        private void MakeFlexes() {
            foreach (var (link, other, point, axis) in flexes) {
                if (!bodyOf.TryGetValue(link, out var a) || !bodyOf.TryGetValue(other, out var b) || a == b) continue;
                var joint = a.rb.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = b.rb;
                joint.autoConfigureConnectedAnchor = true;
                joint.anchor = a.rb.transform.InverseTransformPoint(point);
                joint.axis = a.rb.transform.InverseTransformDirection(axis);
                joint.secondaryAxis = a.rb.transform.InverseTransformDirection(Perpendicular(axis));
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
                joint.lowAngularXLimit = new SoftJointLimit { limit = -20f };
                joint.highAngularXLimit = new SoftJointLimit { limit = 20f };
                joint.angularYLimit = new SoftJointLimit { limit = 45f };
                joint.angularZLimit = new SoftJointLimit { limit = 45f };
                joint.rotationDriveMode = RotationDriveMode.Slerp;
                joint.slerpDrive = new JointDrive { positionSpring = 700f, positionDamper = 3f, maximumForce = float.MaxValue };
                joint.enableCollision = false;
                joint.enablePreprocessing = false;
            }
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
                // V2: the axle in each body as built (Roll keeps a free wheel square to it)
                if (collinear) hingeAxle[(owner.rb, other.rb)] = (Quaternion.Inverse(owner.rb.rotation) * axis, Quaternion.Inverse(other.rb.rotation) * axis);
            }
        }

        private readonly Dictionary<(Rigidbody, Rigidbody), (Vector3 inA, Vector3 inB)> hingeAxle = new();

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
                    // V2: its hinges, for the stopped motors' hold (Hold)
                    foreach (var hinge in body.rb.GetComponents<ConfigurableJoint>())
                        if (hinge.angularXMotion == ConfigurableJointMotion.Free) train.hinges.Add((hinge, system.ratio[rotor]));
                    foreach (var holder in bodies)
                        foreach (var hinge in holder.rb.GetComponents<ConfigurableJoint>())
                            if (hinge.connectedBody == body.rb && hinge.angularXMotion == ConfigurableJointMotion.Free) train.hinges.Add((hinge, system.ratio[rotor]));
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
            var sim = new SimChain { chain = chain, rotor = rotor, frame = frame, axisLocal = rotor != null ? rotor.transform.InverseTransformDirection(axis) : axis, inchesPerRadian = inchesPerRadian, phase = chain.phase, holder = holder };
            foreach (var link in chain.Links) {
                if (link == null) continue;
                var copy = new GameObject(link.name);
                copy.transform.SetParent(holder, false);
                copy.AddComponent<MeshFilter>().sharedMesh = link.GetComponent<MeshFilter>().sharedMesh;
                var renderer = link.GetComponent<MeshRenderer>();
                var proxy = copy.AddComponent<MeshRenderer>();
                proxy.sharedMaterials = renderer.sharedMaterials;
                proxy.renderingLayerMask = renderer.renderingLayerMask & ~OutlineFeature.Layer;
                sourceOf[proxy] = renderer;   // V2
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
                var driven = sim.rotor != null && Mathf.Abs(sim.inchesPerRadian) >= 1e-6f;
                if (driven) {
                    var axis = sim.rotor.transform.TransformDirection(sim.axisLocal);
                    var w = Vector3.Dot(sim.rotor.angularVelocity - (sim.frame != null ? sim.frame.angularVelocity : Vector3.zero), axis);
                    sim.phase += w * sim.inchesPerRadian * dt;
                }
                if (sim.tension != null) {
                    // V2: round its sprockets where they are now (the tensioner swings), once a frame.
                    var st = sim.tension;
                    if (st.pathFrame != Time.frameCount) {
                        st.pathFrame = Time.frameCount;
                        var path = ChainPath.Build(st.wraps, sim.chain.family.pitch, Vector2.down, sim.chain.fixedLinks);
                        if (path.Valid) st.path = path;
                    }
                    sim.chain.Place(sim.links, sim.phase, st.path ?? sim.chain.Path);
                }
                else if (sim.stop != null) {
                    // V2: round its sprockets where they are now (one rides on an arm), once a frame.
                    var st = sim.stop;
                    if (st.pathFrame != Time.frameCount) {
                        st.pathFrame = Time.frameCount;
                        var path = ChainPath.Build(st.wraps, sim.chain.family.pitch, Vector2.down, sim.chain.Path.links);
                        if (path.Valid) st.path = path;
                    }
                    sim.chain.Place(sim.links, sim.phase, st.path ?? sim.chain.Path);
                }
                else if (driven) sim.chain.Place(sim.links, sim.phase);
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
                // V2: with air to one end only, the valve starts off: no air, the rod loose.
                var slide = new Slide { cylinder = cylinder, joint = joint, rod = rod.rb, barrel = barrel.rb, extended = cylinder.Extended,
                                        ports = cylinder.Ports, loose = cylinder.Ports != PneumaticCylinder.AirPorts.Both,
                                        psi = AirSupply.Of(cylinder)?.Working ?? AirSupply.MaxPsi };   // V2: charged before the start
                slides.Add(slide);
                SetSlide(slide);
            }
        }

        private static void SetSlide(Slide slide) {
            if (slide.loose) {
                // Both ports open: only the seals' drag (about 1 N) holds the rod.
                slide.joint.xDrive = new JointDrive { positionSpring = 0f, positionDamper = 0.5f * Newton / CylinderSpeed, maximumForce = 1f * Newton };
                slide.joint.targetVelocity = Vector3.zero;
                return;
            }
            // V2: force with the pressure it was filled at (12 lbf at 100 psi), and slower at low pressure (flow).
            var share = Mathf.Clamp01(slide.psi / PneumaticCylinder.Psi);
            var force = (slide.extended ? PneumaticCylinder.ExtendForceNewtons : PneumaticCylinder.RetractForceNewtons) * Newton * share;
            var speed = CylinderSpeed * Mathf.Sqrt(share);
            if (force <= 0.001f) {
                slide.joint.xDrive = new JointDrive { positionSpring = 0f, positionDamper = 0.5f * Newton / CylinderSpeed, maximumForce = 1f * Newton };
                slide.joint.targetVelocity = Vector3.zero;
                return;
            }
            slide.joint.xDrive = new JointDrive { positionSpring = 0f, positionDamper = force / speed, maximumForce = force };
            slide.joint.targetVelocity = new Vector3(SlideSign * (slide.extended ? speed : -speed), 0f, 0f);
        }

        // ------------------------------------------------------------------ driving

        /// <summary>For tests run from scripts (stepped frames get no keys).</summary>
        public static System.Func<(Vector2, Vector2)> TestSticks;

        /// <summary>The sticks: from a gamepad, else W/A/S/D (left) and the arrows (right).</summary>
        public static (Vector2 left, Vector2 right) Sticks() {
            if (TestSticks != null) return TestSticks();
            // V2 (Phase 7): the robot's V5 controller (its keys and gamepad controls: Edit > Controller…).
            var left = new Vector2(Devices.V5Controller.Axis(Devices.V5Axis.Axis4), Devices.V5Controller.Axis(Devices.V5Axis.Axis3));
            var right = new Vector2(Devices.V5Controller.Axis(Devices.V5Axis.Axis1), Devices.V5Controller.Axis(Devices.V5Axis.Axis2));
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
            if (instance != this) return;   // V2: a stray copy saved in the scene (Main.unity) does nothing
            if (!Hosted && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !InputEvents.TextInput.IsTyping && !MotionUi.Capturing && !Popup.IsOpen) {
                Stop();
                return;
            }
            if (Puppet) {   // V2: another computer simulates; motors only make their sound
                foreach (var train in trains)
                    foreach (var (motor, _) in train.motors) if (motor != null) motor.Silence();
                return;
            }
            var typing = InputEvents.TextInput.IsTyping || InputEvents.TextInput.ShortcutsPaused || !DriveEnabled;
            var (left, right) = DriveEnabled ? DrivePower() : (0f, 0f);
            // V2: a robot driven from another computer (an online match) takes its player's controls.
            DriveInput? InputFor(GameObject part) => InputOf?.Invoke(RobotOf?.Invoke(part) ?? 0);
            foreach (var train in trains)
                foreach (var (motor, _) in train.motors) {
                    if (motor == null) continue;   // V2: a build part deleted while simulating
                    if (RobotCode.RobotProgram.Owns(motor)) continue;   // V2: the robot's program drives it
                    var side = Controls.Side(motor.gameObject);
                    var remote = InputFor(motor.gameObject);
                    float direction;
                    if (remote is DriveInput r) {
                        direction = 0f;
                        if (!DriveEnabled) { }
                        else if (side == "L") direction = r.left;
                        else if (side == "R") direction = r.right;
                        else {
                            if (r.held(Controls.ForwardKey(motor.gameObject))) direction += 1f;
                            if (r.held(Controls.ReverseKey(motor.gameObject))) direction -= 1f;
                        }
                    }
                    else if (side == "L") direction = left;
                    else if (side == "R") direction = right;
                    else {
                        direction = 0f;
                        if (!typing && Controls.Held(Controls.ForwardKey(motor.gameObject))) direction += 1f;
                        if (!typing && Controls.Held(Controls.ReverseKey(motor.gameObject))) direction -= 1f;
                    }
                    motor.Command = direction * Controls.Speed(motor.gameObject) * (Controls.Reversed(motor.gameObject) ? -1f : 1f);
                    // V2: run by hand from the Devices panel (< and >, as on the Brain's motor screen).
                    if (Mathf.Abs(motor.Manual) > 0.001f && remote == null) motor.Command = motor.Manual;
                }
            // V2: one key per cylinder. Air to both ends: out, in. Air to one end: that way (stiff), then loose.
            foreach (var slide in slides) {
                var key = Controls.CylinderKey(slide.cylinder.gameObject);
                var remote = InputFor(slide.cylinder.gameObject);
                var pressed = remote is DriveInput r ? DriveEnabled && r.pressed(key) : !typing && Controls.Pressed(key);
                if (!pressed) continue;
                if (slide.ports == PneumaticCylinder.AirPorts.Both) {
                    slide.extended = !slide.extended;
                    slide.loose = false;
                }
                else if (slide.loose) {
                    slide.extended = slide.ports == PneumaticCylinder.AirPorts.Back;
                    slide.loose = false;
                }
                else slide.loose = true;
                if (!slide.loose) slide.psi = AirSupply.Move(slide.cylinder, slide.extended);   // V2: air from the tanks
                SetSlide(slide);
                slide.changedAt = Time.unscaledTime;
                Audio.Sounds.PlayAt(Audio.Sounds.Air, slide.rod.position, slide.loose ? 0.4f : 0.7f);
            }
            foreach (var train in trains)
                foreach (var (motor, _) in train.motors) { if (Paused) motor.Silence(); else motor.Sound(motor.Level); }
            if (Paused && Keyboard.current != null && Tools.Keybinds.Pressed("StepFrame")) StepOnce();
        }

        private void LateUpdate() {
            if (instance != this) return;   // V2
            DrawSlideStates();   // V2
            PickTracked();
            if (PivotCamera.Main == null) return;
            Vector3? target = View == CameraMode.Robot && main != null && main.rb != null ? main.rb.worldCenterOfMass
                        : View == CameraMode.Part && tracked != null ? tracked.worldCenterOfMass : (Vector3?)null;
            if (target == null) {
                lastTarget = null;
                return;
            }
            // V2: the focus moves with the target (and the pan target with it), so the user's own orbit, pan and
            // zoom keep working while it follows; the first frame glides onto the target.
            if (lastTarget == null) PivotCamera.Main.MoveFocusPosition(target.Value);
            else PivotCamera.Main.ShiftFocus(target.Value - lastTarget.Value);
            lastTarget = target;
        }

        private void FixedUpdate() {
            if (instance != this) return;   // V2 (it ran the robot program a second time each step)
            if (Paused) return;   // V2: paused, nothing moves (the robot program's time and the motors ran on)
            Advance();
        }

        /// <summary>V2: one physics step while paused (the "." key, the code window's Step): the robot program,
        /// the gear trains and the physics, as a running step does them.</summary>
        public void StepOnce() {
            if (!Paused) return;
            Advance();
            Physics.Simulate(Time.fixedDeltaTime);
        }

        internal static int steps;   // V2: physics steps run (tests compare sensor sampling with it)

        private void Advance() {
            steps++;
            if (!Puppet) RobotCode.RobotProgram.Step(Time.fixedDeltaTime);   // V2 (Phase 9): robot code, before the trains turn
            foreach (var train in trains)
                if (!train.rotors.Any(r => r.rb != null && r.rb.isKinematic)) Turn(train);   // V2: (another computer drives that robot)
            RunChains(Time.fixedDeltaTime);
            foreach (var slide in slides) slide.rod.WakeUp();
            Omnis();
            BogieFriction();   // V2
            PullBands();   // V2
            foreach (var st in tensions) Tension(st);   // V2: chain tensioners
            foreach (var st in stops) ChainStop(st);    // V2: chains don't stretch
            HoldStill();   // V2
        }

        // V2: static friction for a robot at rest. Rubber on foam tiles holds a stopped robot dead still; here the
        // contacts' tiny leftover slip crept it (about 0.0015" a second sideways and 0.003 degrees a second round after a
        // stop), and its tracking wheels counted it. When none of its motors is driven and every one of its bodies has
        // been nearly still for a tenth of a second, their sliding along the floor and turning about the vertical are
        // taken away each step; a command, a push or a hit faster than that lets go at once.
        private const float StillSpeed = 0.15f;                 // in/s
        private const float StillTurn = 0.3f * Mathf.Deg2Rad;  // rad/s about the vertical
        private const int StillSteps = 24;                      // 0.1 s at 240 steps a second
        private int stillFor;
        internal static bool HoldStillOff;                      // tests compare with and without

        /// <summary>V2: the robot is being held still (HoldStill): its free tracking wheels don't roll.</summary>
        private bool HeldStill => !HoldStillOff && stillFor >= StillSteps;

        private bool IsFreeRoller(Rigidbody rb) {
            if (driven.Contains(rb)) return false;
            foreach (var wheel in omnis)
                if (wheel.rb == rb) return true;
            return false;
        }

        private void HoldStill() {
            if (HoldStillOff) return;
            foreach (var train in trains)
                foreach (var (motor, _) in train.motors)
                    if (motor != null && Mathf.Abs(motor.Command) > 0.001f) {
                        stillFor = 0;
                        return;
                    }
            foreach (var body in bodies) {
                var rb = body.rb;
                if (rb == null || rb.isKinematic) continue;
                var v = rb.linearVelocity;
                // (a free tracking wheel's own spin shows a little turn about the vertical: only its travel counts)
                var turn = IsFreeRoller(rb) ? 0f : rb.angularVelocity.y;
                if (new Vector2(v.x, v.z).magnitude > StillSpeed || Mathf.Abs(turn) > StillTurn) {
                    stillFor = 0;
                    return;
                }
            }
            if (++stillFor < StillSteps) return;
            foreach (var body in bodies) {
                var rb = body.rb;
                if (rb == null || rb.isKinematic) continue;
                var v = rb.linearVelocity;
                rb.linearVelocity = new Vector3(0f, v.y, 0f);
                var w = rb.angularVelocity;
                rb.angularVelocity = new Vector3(w.x, 0f, w.z);
            }
        }

        // A train turns as one: its speed (of its own turn) from its shafts' turning (what the last step did to them:
        // loads, the ground on wheels), then the motors' torque for a step, implicitly (a DC motor: stall torque down
        // to nothing at free speed, so its torque is A - B * speed), and every shaft is set to its ratio of that.
        // Meshing gears keep their ratio this way, and a motor that isn't driven brakes (V5 motors short their windings).
        private const float CoastDrag = 0.1f;
        // V2: a stopped V5 motor's static friction, as a share of its stall torque (back-driving one by hand takes a
        // firm push through its gears; this keeps a robot still on a level field, a hard shove still moves it).
        internal static float GearboxFriction = 0.05f;
        // V2: below this share of its free speed a braking motor's pull fades to the gearbox's hold (tests set it).
        internal static float BrakeBand = 0.03f;

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
                if (motor.Coasting && Mathf.Abs(motor.Command) < 0.001f) {   // V2 (robot code): coast: no braking
                    // ...but a V5 motor's gearbox still drags when it's turned by hand (about a tenth of the brake's
                    // pull): without it an all-omni robot rolled on freely after a move and slowly spun round.
                    b += k * k * stall / FreeSpeed(motor) * CoastDrag;
                    most += Mathf.Abs(k) * stall;
                    continue;
                }
                a += k * stall * motor.Command;
                b += k * k * stall / FreeSpeed(motor);
                most += Mathf.Abs(k) * stall;
            }
            // V2: motors that aren't driven hold against a steady push, as a stopped V5 motor's gearbox does (static
            // friction, GearboxFriction of its stall torque; hold mode up to the stall torque): the shafts' hinges
            // get a drive to stand still, capped at that torque, which the physics solves with the contacts. The brake
            // alone only slows a turning train, so a small steady push (the 48-sided wheels settling, a slope) turned
            // it slowly: a robot standing still crept ~0.07"/s.
            var stopped = train.motors.Count > 0 && train.motors.All(m => Mathf.Abs(m.motor.Command) < 0.001f);
            var hold = stopped && train.motors.Any(m => Controls.Hold(m.motor.gameObject));
            // V2: brake mode stops the motor actively (up to its full torque while it still turns, fading to the
            // gearbox's static hold as it stops): a brake that pulled only in proportion to the speed (shorted windings
            // alone) faded as the robot slowed, and a robot let go of mid-turn kept turning for 2 s (8-25° more).
            var coasting = train.motors.All(m => m.motor.Coasting);
            var turning = 0f;
            foreach (var (motor, k) in train.motors) turning = Mathf.Max(turning, Mathf.Abs(k * speed) / Mathf.Max(FreeSpeed(motor), 1e-3f));
            var brake = coasting ? GearboxFriction : Mathf.Clamp(turning / BrakeBand, GearboxFriction, 1f);
            Hold(train, stopped ? (hold ? most : most * brake) : 0f);
            var next = (inertia * speed + a * dt) / (inertia + b * dt);
            // V2: hold mode (Controls.Hold): not driven, its motors stop the train where it is (up to their stall torque).
            if (hold) next = 0f;
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

        // V2: the train's hinges hold up to this torque at the motors (each shares it by its ratio).
        private static void Hold(Train train, float torque) {
            if (Mathf.Abs(torque - train.holding) < 1e-3f * Mathf.Max(1f, torque)) return;
            train.holding = torque;
            var sum = train.hinges.Sum(h => h.k * h.k);
            if (sum <= 0f) return;
            foreach (var (joint, k) in train.hinges) {
                if (joint == null) continue;
                joint.targetAngularVelocity = Vector3.zero;
                joint.angularXDrive = new JointDrive { positionSpring = 0f, positionDamper = torque > 0f ? 1e7f : 0f, maximumForce = torque * Mathf.Abs(k) / sum };
            }
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
        // V2: the rollers' resistance across the wheel, as a share of the load. Nobody publishes a measured value for
        // VEX omnis; on foam field tiles a small rubber roller sinks in (rolling resistance ~ sqrt(sinkage / diameter):
        // ~0.03" into a 0.45" roller is ~0.25) and turns on a plain pin. At 0.05 a robot on all omnis slid like a puck
        // (80" after a full-speed arc, faster than its top speed sideways); at 0.25 it drifts out ~25° in a full-speed
        // arc and stops within ~3", as all-omni robots do (pushed sideways easily, drift in hard turns: EZ-Template
        // recommends a sideways tracking wheel for them). Tests set it (t_slide.cs).
        internal static float RollerResistance = 0.25f;

        private readonly Dictionary<Collider, int> pointsOn = new();
        // V2: what each wheel's grip held last step (along its roll, across it) and what it can hold (its load).
        // Grip only took away the speed a step had left, so a steady push (the 48-sided wheels settling onto their
        // flats, a slope) moved the robot a little every step: standing still it crept ~0.1"/s. Holding on to the
        // push (an integral, like static friction) stops it. Only while the wheel stands still on the ground
        // (StaticSpeed): rolling or sliding it lets go (held through a drive, a 24" move came out 0.6" short).
        private readonly Dictionary<Collider, Vector2> held = new(), holding = new();
        private readonly Dictionary<Collider, float> loadOn = new();
        private readonly HashSet<Collider> slidingAlong = new(), slidingAcross = new();
        internal static float HoldGain = 0.3f;   // tests set it (t_creep3.cs)
        private const float StaticSpeed = 0.3f;   // in/s

        private void Omnis() {
            rolled.Clear();
            if (contacts.Count == 0) { held.Clear(); FreeRollers(); return; }
            var total = bodies.Sum(b => b.mass);
            var touching = Mathf.Max(1, contacts.Select(c => c.collider).Distinct().Count());
            // V2: a wheel touching at several points shares its correction between them (each took half the slip:
            // two or three points over-corrected and the wheel jittered).
            pointsOn.Clear();
            foreach (var c in contacts) pointsOn[c.collider] = pointsOn.TryGetValue(c.collider, out var n) ? n + 1 : 1;
            loadOn.Clear();
            foreach (var c in contacts) loadOn[c.collider] = (loadOn.TryGetValue(c.collider, out var l) ? l : 0f) + c.impulse;
            holding.Clear();
            slidingAlong.Clear();
            slidingAcross.Clear();
            foreach (var (collider, point, normalIn, impulse, other) in contacts) {
                var wheel = omnis.FirstOrDefault(w => w.collider == collider);
                if (wheel == null || wheel.rb == null || wheel.rb.isKinematic) continue;
                var rb = wheel.rb;
                var axis = WheelAxis(wheel);
                var normal = normalIn;
                if (Vector3.Dot(normal, collider.bounds.center - point) < 0f) normal = -normal;
                var along = Vector3.Cross(axis, normal);
                if (along.sqrMagnitude < 1e-6f) continue;
                along.Normalize();
                var velocity = rb.GetPointVelocity(point) - (other != null ? other.GetPointVelocity(point) : Vector3.zero);
                var slip = Vector3.Dot(velocity, along);
                // V2: the slip is taken up against the robot's weight on this wheel (still at most what friction
                // allows). It was limited by the wheel's own spin inertia, which is tiny next to the gear train and
                // motors it's geared to: under full power the omnis spun in place (a 24" move went 21.3").
                var mass = 0.5f * total / touching / pointsOn[collider];
                // V2: a wheel nothing drives (a tracking wheel) rolls with the floor through its hinge's drive (Roll),
                // inside the physics step; the grip impulse below only handles wheels a gear train turns.
                // (a free wheel rubbing another part of the robot isn't rolling on anything: rolled against it, it was
                // held still and a tracking wheel stopped counting)
                if (!driven.Contains(rb) && other != null && other.gameObject.layer == Layer) continue;
                var free = !driven.Contains(rb) && Roll(rb, wheel.radius, point, axis, along, other, impulse);
                var most = 1.0f * impulse;
                held.TryGetValue(collider, out var hold);
                holding.TryGetValue(collider, out var next);
                var share = 1f / pointsOn[collider];
                var j = Mathf.Clamp(-slip * mass + hold.x * share, -most, most);
                next.x += hold.x * share - slip * mass * HoldGain;
                // (a rolling wheel's contact doesn't slip either: the wheel itself moving lets go too)
                var moving = (rb.linearVelocity - (other != null ? other.linearVelocity : Vector3.zero)).magnitude > StaticSpeed;
                if (moving || Mathf.Abs(slip) > StaticSpeed) slidingAlong.Add(collider);
                if (!free) {
                    rb.AddForceAtPosition(along * j, point, ForceMode.Impulse);
                    if (other != null) other.AddForceAtPosition(-along * j, point, ForceMode.Impulse);
                }
                else {
                    // V2: a tracking wheel's rollers resist at its own scale and don't hold: the robot-sized correction
                    // and static hold (made for the drive wheels) shook the light pod and nudged a still robot along
                    // (0.07" and 0.2° in 10 s).
                    mass = rb.mass / pointsOn[collider];
                    hold = Vector2.zero;
                    next = Vector2.zero;
                }
                // V2: the rollers' own rolling resistance across the wheel (RollerResistance of the load): without it a
                // robot on all omnis kept sliding sideways forever after a turn.
                var across = Vector3.ProjectOnPlane(axis, normal);
                if (across.sqrMagnitude < 1e-6f) { holding[collider] = next; continue; }
                across.Normalize();
                var drift = Vector3.Dot(velocity, across);
                // V2: shared between the wheel's contact points like the grip (each point took the whole wheel's
                // share: four points over-corrected 4x, the sideways speed flipped every step and the clamp turned that
                // into a slow drift: a robot standing still crept ~0.1"/s).
                var roll = Mathf.Clamp(-drift * mass + hold.y * share, -RollerResistance * impulse, RollerResistance * impulse);
                next.y += hold.y * share - drift * mass * HoldGain;
                if (moving || Mathf.Abs(drift) > StaticSpeed) slidingAcross.Add(collider);
                holding[collider] = next;
                rb.AddForceAtPosition(across * roll, point, ForceMode.Impulse);
                if (other != null) other.AddForceAtPosition(-across * roll, point, ForceMode.Impulse);
            }
            // A wheel holds at most what its load allows (sliding lets go); a wheel off the ground holds nothing.
            held.Clear();
            foreach (var (collider, h) in holding) {
                var load = loadOn[collider];
                held[collider] = new Vector2(slidingAlong.Contains(collider) ? 0f : Mathf.Clamp(h.x, -load, load),
                    slidingAcross.Contains(collider) ? 0f : Mathf.Clamp(h.y, -RollerResistance * load, RollerResistance * load));
            }
            contacts.Clear();
            FreeRollers();
        }

        // V2: unpowered omni wheels (tracking wheels) roll without slipping, as a sprung tracking wheel does on foam
        // tiles: each step the wheel is given the spin that makes its contact stand still on the floor (from how the
        // arm it turns in moves there), held by its axle's drive. The grip impulse the driven wheels get was undone by
        // the 48-sided shape bumping over the floor and by the light arm swinging (the wheel turned 3-7% short).
        private readonly Dictionary<Rigidbody, (ConfigurableJoint joint, bool owner)> freeHinges = new();
        private readonly HashSet<Rigidbody> rolled = new();

        private (ConfigurableJoint joint, bool owner) FreeHinge(Rigidbody wheel) {
            if (freeHinges.TryGetValue(wheel, out var found)) return found;
            found = (null, false);
            foreach (var j in wheel.GetComponents<ConfigurableJoint>())
                if (j.angularXMotion == ConfigurableJointMotion.Free) found = (j, true);
            if (found.joint == null)
                foreach (var b in bodies)
                    foreach (var j in b.rb.GetComponents<ConfigurableJoint>())
                        if (j.connectedBody == wheel && j.angularXMotion == ConfigurableJointMotion.Free) found = (j, false);
            freeHinges[wheel] = found;
            return found;
        }

        private bool Roll(Rigidbody wheel, float radius, Vector3 point, Vector3 axis, Vector3 along, Rigidbody ground, float impulse) {
            var (joint, owner) = FreeHinge(wheel);
            if (joint == null) return false;
            if (!rolled.Add(wheel)) return true;   // (once a step, from its first contact)
            var frame = owner ? joint.connectedBody : joint.GetComponent<Rigidbody>();
            if (frame == null) return true;
            var axisWorld = joint.transform.TransformDirection(joint.axis).normalized;
            var lever = Vector3.Dot(Vector3.Cross(axisWorld, point - wheel.worldCenterOfMass), along);
            if (Mathf.Abs(lever) < 0.05f) return true;
            // (the wheel's own radius: the contact point PhysX gives lies a little outside the wheel, and a lever
            // measured to it made the wheel turn 3% short)
            lever = Mathf.Sign(lever) * radius;
            // Heavy about its axle (its own spin only): the floor's bumps under the 48-sided shape can't change its
            // spin within a step. It turns with its arm about the other axes as before.
            if (heavy.Add(wheel)) {
                var local = Quaternion.Inverse(wheel.rotation * wheel.inertiaTensorRotation) * axisWorld;
                var spinInertia = Vector3.Dot(Vector3.Scale(local, local), wheel.inertiaTensor);
                var other = Mathf.Max(wheel.inertiaTensor.x, wheel.inertiaTensor.y, wheel.inertiaTensor.z);
                wheel.inertiaTensorRotation = Quaternion.FromToRotation(Vector3.right, Quaternion.Inverse(wheel.rotation) * axisWorld);
                wheel.inertiaTensor = new Vector3(Mathf.Max(spinInertia, 1e-4f) * 1000f, other, other);
                // (as built when the hinge was made: at its first contact the wheel had already leaned)
                if (hingeAxle.TryGetValue((wheel, frame), out var wf)) axleIn[wheel] = (wf.inB, wf.inA);
                else if (hingeAxle.TryGetValue((frame, wheel), out var fw)) axleIn[wheel] = (fw.inA, fw.inB);
                else axleIn[wheel] = (Quaternion.Inverse(frame.rotation) * axisWorld, Quaternion.Inverse(wheel.rotation) * axisWorld);
                // (a light pod holding a cantilevered wheel is a hard case for the solver: more iterations keep the
                // bearing square)
                if (frame.mass < 0.25f) {
                    wheel.solverIterations = frame.solverIterations = 60;
                    wheel.solverVelocityIterations = frame.solverVelocityIterations = 20;
                }
            }
            // V2: the wheel stays square in its bearing. A cantilevered wheel pressed on the floor twisted its bearing's
            // joint ~2 degrees (the wheel leaned and toed in its Rotation sensor), and setting its turn from the pod's
            // every step left that lean in: here its axle is turned back onto the pod's each step, and it spins about it.
            // Only a wheel hung on a light pod (a tracking wheel in its sensor): drive wheels on the chassis are left to
            // their joints (straightening them against the chassis tore a robot apart).
            var straighten = Vector3.zero;
            if (!StraightenOff && frame.mass < 0.25f && !driven.Contains(wheel) && axleIn.TryGetValue(wheel, out var ax)) {
                var podAxle = (frame.rotation * ax.inFrame).normalized;
                var wheelAxle = (wheel.rotation * ax.inWheel).normalized;
                if (Vector3.Dot(podAxle, axisWorld) < 0f) podAxle = -podAxle;
                if (Vector3.Dot(wheelAxle, podAxle) < 0f) wheelAxle = -wheelAxle;
                var cross = Vector3.Cross(wheelAxle, podAxle);
                var sin = cross.magnitude;
                // the whole lean back each step; more than 8 degrees isn't a lean
                if (sin > 1e-6f && sin < 0.14f) {
                    straighten = cross / sin * (Mathf.Asin(sin) / Time.fixedDeltaTime);
                    axisWorld = podAxle;
                }
            }
            // How far it should have turned in its frame: what its axle moved along the floor since the last step (a
            // rolling wheel turns that far, over its radius) less what its frame (the pod) turned about the axle.
            // Measured from where things actually went: reading the pod's speed at the start of each step summed to
            // 3.5% short of the distance, and the pod's jiggles under the band, turned into spin, made a still robot's
            // tracking sensors count ~0.5° a second. Back-and-forth jiggles cancel here.
            var axle = wheel.worldCenterOfMass;
            var turned = TurnIn(wheel, frame, axisWorld);
            if (!rolledTurn.TryGetValue(wheel, out var should) || freeSince.Contains(wheel) || !rollFrom.TryGetValue(wheel, out var from)) {
                should = turned;   // (first contact, or back on the floor: from here)
                from = (axle, frame.rotation);
            }
            freeSince.Remove(wheel);
            if (HeldStill) {
                // V2: a robot held still: a real tracking wheel doesn't turn (here its pod settling on the band turned it
                // about 0.015 degrees a second). It's held at the turn it has (pulled back if the contacts turn it).
                rolledTurn[wheel] = should;
                rollFrom[wheel] = (axle, frame.rotation);
                var back = (float)((should - turned) / Time.fixedDeltaTime);
                wheel.angularVelocity = frame.angularVelocity + axisWorld * back + straighten;
                return true;
            }
            var moved = axle - from.axle;
            var rolledTurns = -Vector3.Dot(moved - Vector3.Dot(moved, axisWorld) * axisWorld, along) / lever;
            var dq = frame.rotation * Quaternion.Inverse(from.rot);
            var frameTurn = 2f * Mathf.Atan2(dq.x * axisWorld.x + dq.y * axisWorld.y + dq.z * axisWorld.z, dq.w);
            if (frameTurn > Mathf.PI) frameTurn -= 2f * Mathf.PI;
            if (frameTurn < -Mathf.PI) frameTurn += 2f * Mathf.PI;
            should += rolledTurns - frameTurn;
            rolledTurn[wheel] = should;
            rollFrom[wheel] = (axle, frame.rotation);
            // It turns that difference during this step (no force passes: a free wheel's own inertia is too small to
            // push a robot).
            var rel = (float)((should - turned) / Time.fixedDeltaTime);
            if (Mathf.Abs(rel * radius) <= 400f) wheel.angularVelocity = frame.angularVelocity + axisWorld * rel + straighten;
            return true;
        }

        private readonly Dictionary<Rigidbody, (Vector3 axle, Quaternion rot)> rollFrom = new();

        private readonly HashSet<Rigidbody> heavy = new();
        private readonly Dictionary<Rigidbody, (Vector3 inFrame, Vector3 inWheel)> axleIn = new();
        internal static bool StraightenOff;   // tests: leave free wheels to their joints   // a free wheel's axle in its pod and in itself
        private readonly Dictionary<Rigidbody, double> rolledTurn = new();
        private readonly Dictionary<Rigidbody, (double total, float last)> turnIn = new();
        private readonly HashSet<Rigidbody> freeSince = new();

        // A wheel's turn in its frame about its axle, radians, unwrapped.
        private double TurnIn(Rigidbody wheel, Rigidbody frame, Vector3 axisWorld) {
            var q = Quaternion.Inverse(frame.rotation) * wheel.rotation;
            var axis = Quaternion.Inverse(frame.rotation) * axisWorld;
            var twist = 2f * Mathf.Atan2(q.x * axis.x + q.y * axis.y + q.z * axis.z, q.w);
            if (!turnIn.TryGetValue(wheel, out var t)) t = (twist, twist);
            var step = Mathf.DeltaAngle(t.last * Mathf.Rad2Deg, twist * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            t = (t.total + step, twist);
            turnIn[wheel] = t;
            return t.total;
        }


        // A sprung wheel pressed onto foam tiles doesn't leave them, but the light wheel's 48-sided shape bumps off the
        // floor between contacts at speed (~300 bumps a second at 60 in/s): within 1/32" of the floor it still rolls
        // Only clear of the floor does it spin free.
        internal static int rollingSteps, nearSteps, freeSteps;   // tests read them (t_odom*.cs)

        private void FreeRollers() {
            foreach (var wheel in omnis) {
                var rb = wheel.rb;
                if (rb == null || rb.isKinematic || driven.Contains(rb)) continue;
                var (joint, _) = FreeHinge(rb);
                if (joint == null) continue;
                // (a sleeping body reports no contacts and isn't driven: a tracking wheel asleep at the start missed the
                // first 7" the robot drove)
                if (rb.IsSleeping()) rb.WakeUp();
                if (rolled.Contains(rb)) {
                    rollingSteps++;
                    continue;
                }
                var axis = WheelAxis(wheel);
                var center = rb.worldCenterOfMass;
                var down = Vector3.ProjectOnPlane(Vector3.down, axis).normalized;
                if (down.sqrMagnitude > 0.5f && Physics.Raycast(center, down, out var hit, wheel.radius + 0.04f, (1 << GroundLayer) | (1 << FieldLayer), QueryTriggerInteraction.Ignore)
                    && hit.distance > wheel.radius - 0.1f) {
                    var along = Vector3.Cross(axis, hit.normal);
                    if (along.sqrMagnitude > 1e-6f) {
                        Roll(rb, wheel.radius, hit.point, axis, along.normalized, hit.rigidbody, 0f);
                        nearSteps++;
                        continue;
                    }
                }
                freeSteps++;
                // Off the floor it spins down in its bearings (heavy, it kept turning for ever).
                freeSince.Add(rb);
                var frameBody = joint.connectedBody == rb ? joint.GetComponent<Rigidbody>() : joint.connectedBody;
                if (frameBody != null) {
                    var axisFree = joint.transform.TransformDirection(joint.axis).normalized;
                    var rel = Vector3.Dot(rb.angularVelocity - frameBody.angularVelocity, axisFree);
                    rb.angularVelocity -= axisFree * rel * 0.05f;
                }
            }
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
        /// <summary>What the simulation made (removed when it stops): a game adds its pieces here.</summary>
        public Transform Root => root;
        public bool Drives(MotorDrive motor) => trains.Any(t => t.motors.Any(m => m.motor == motor));
        public bool Slides(PneumaticCylinder cylinder) => slides.Any(s => s.cylinder == cylinder);

        /// <summary>V2: the cylinder's air ports changed while running (the Controls window): its valve starts again
        /// as the new ports start (both: holding where it was set; one end: loose).</summary>
        public void SetPorts(PneumaticCylinder cylinder) {
            foreach (var slide in slides) {
                if (slide.cylinder != cylinder) continue;
                slide.ports = cylinder.Ports;
                slide.loose = slide.ports != PneumaticCylinder.AirPorts.Both;
                slide.changedAt = Time.unscaledTime;
                SetSlide(slide);
            }
        }

        // V2: what a cylinder's air is doing, shown on it for 1.5 s after its key changes it (with one end of air
        // a loose rod looks just like one held out, so pressing the key seemed to do nothing).
        private void DrawSlideStates() {
            var any = false;
            foreach (var slide in slides) {
                if (Time.unscaledTime - slide.changedAt > 1.5f || slide.rod == null) continue;
                if (!any) Interface.Overlay.Begin();
                any = true;
                if (!Interface.Overlay.ToScreen(slide.rod.worldCenterOfMass, out var at)) continue;
                var text = slide.loose ? "Loose (no air)" : (slide.extended ? "Air: out" : "Air: in") + (slide.psi < AirSupply.MaxPsi - 0.5f ? " · " + Mathf.RoundToInt(slide.psi) + " psi" : "");
                Interface.Overlay.Text(at + new Vector2(0f, 28f), text, slide.loose ? Interface.Theme.TextDim : Interface.Theme.Accent, null, true);
            }
        }
        /// <summary>V2: the rigid body a build part rides on in the simulation, or null.</summary>
        /// <summary>V2: the build renderer a simulation copy draws (replays record which part it is), or null.</summary>
        public static Renderer SourceOf(Renderer copy) => instance != null && copy != null && instance.sourceOf.TryGetValue(copy, out var source) ? source : null;

        public static Rigidbody BodyOf(GameObject part) => instance != null && part != null && instance.bodyOf.TryGetValue(part, out var body) ? body.rb : null;

        /// <summary>V2: a solenoid switched (the Devices panel, robot code): air out (extend) or in.</summary>
        public void SetAir(PneumaticCylinder cylinder, bool extend) {
            foreach (var slide in slides) {
                if (slide.cylinder != cylinder) continue;
                if (slide.ports == PneumaticCylinder.AirPorts.Both) {
                    if (slide.extended == extend && !slide.loose) continue;
                    slide.extended = extend;
                    slide.loose = false;
                }
                else {
                    // Air to one end only: that end's way when switched on, loose when off.
                    var way = slide.ports == PneumaticCylinder.AirPorts.Back;
                    var on = extend == way;
                    if (slide.extended == way && slide.loose == !on) continue;
                    slide.extended = way;
                    slide.loose = !on;
                }
                if (!slide.loose) slide.psi = AirSupply.Move(slide.cylinder, slide.extended);   // V2: air from the tanks
                SetSlide(slide);
                slide.changedAt = Time.unscaledTime;
                Audio.Sounds.PlayAt(Audio.Sounds.Air, slide.rod.position, slide.loose ? 0.4f : 0.7f);
            }
        }

        public bool IsLoose(PneumaticCylinder cylinder) => slides.FirstOrDefault(s => s.cylinder == cylinder)?.loose ?? cylinder.Ports != PneumaticCylinder.AirPorts.Both;
        public bool Extended(PneumaticCylinder cylinder) => slides.FirstOrDefault(s => s.cylinder == cylinder)?.extended ?? cylinder.Extended;
    }
}
