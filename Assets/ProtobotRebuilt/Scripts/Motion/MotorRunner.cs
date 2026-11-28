using System.Collections.Generic;
using System.Linq;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    // V2: turns motors' drive systems in the build mode. A motor runs while one of its keys is held and it's
    // controlled (selected, MotorControls), or from the part menu until stopped. Every shaft geared to it turns at
    // its ratio; motors in the system that aren't running are turned with it. Several running motors in one system
    // share the work (their speeds are averaged). A run (start to stop) is one undo step.
    public class MotorRunner : MonoBehaviour {
        private static MotorRunner host;

        public class Run {
            public DriveSystem system;
            public float speed;       // rpm of the system's first rotor
            public float turned;      // degrees of the first rotor
            public List<GameObject> parts;
            public List<IElement> before;
            public MotorDrive starter;
            // Parts on a joint screw to what turns: they're carried round, turning about the joint as little as
            // they can (Linkage.Trail), each led by its rotor's turn so far.
            public readonly List<(Rotor rotor, Linkage.Body leader, Linkage.Body body, Linkage.Pin pin)> hanging = new();
            // V2: shafts of other systems that turn in bearings on what turns (a sprocket's shaft on a swinging arm):
            // they're carried round with it.
            public readonly List<(Rotor carrier, List<GameObject> parts)> riders = new();
            // V2 (advanced gear kit): what it drives in a straight line, each with how far it's gone and where its
            // parts started (a follower: its cam's reach when the run began).
            public readonly List<(Slider slider, List<(Transform t, Vector3 at)> start, float moved, float reach)> sliders = new();
            // V2 (build 88, string phase 3): strings wound on a drum (or shaft) the system turns: which end.
            public readonly List<(StringLine line, Rotor rotor, int end)> winches = new();
        }

        /// <summary>V2: starts turning a system by hand or by motor (null and why when it can't). The motor runs and
        /// the hand crank both go through Begin / Apply / End.</summary>
        public static Run Begin(DriveSystem system, MotorDrive starter, out string refusal) {
            refusal = null;
            if (system.jammed) {
                refusal = system.jamReason;
                return null;
            }
            if (system.backdriven) {
                refusal = "a worm gear's wheel can't turn its worm (turn the worm's shaft)";
                return null;
            }
            var parts = system.rotors.SelectMany(r => r.parts).Where(p => p != null).Distinct().ToList();
            var grounded = parts.Concat(system.sliders.SelectMany(sl => sl.parts)).FirstOrDefault(Linkage.IsGrounded);
            if (grounded != null) {
                refusal = Interface.PartLabels.Short(grounded) + " on its shaft is grounded";
                return null;
            }
            var run = new Run { system = system, parts = parts, starter = starter };
            FindHanging(run);
            FindRiders(run, CurrentRotors());
            foreach (var slider in system.sliders) {
                var start = slider.parts.Where(p => p != null).Select(p => (p.transform, p.transform.position)).ToList();
                run.sliders.Add((slider, start, 0f, GearKit.CamReach(slider)));
            }
            var moved = parts.Concat(run.hanging.SelectMany(h => h.body.objects)).Concat(run.riders.SelectMany(r => r.parts))
                             .Concat(system.sliders.SelectMany(sl => sl.parts)).Where(p => p != null).Distinct().ToList();
            run.parts = moved;
            run.before = moved.Select(p => (IElement)new ObjectElement(p)).ToList();
            foreach (var (chain, _, _) in system.chains) run.before.Add(new ChainLoop.PhaseElement(chain));
            // V2 (build 88): winches: a string with an end wound on a part that turns winds on or off as it turns.
            foreach (var line in StringLine.All) {
                if (line == null || line.nodes.Count < 2 || line.gameObject.IsDeleted()) continue;
                foreach (var end in new[] { 0, line.nodes.Count - 1 }) {
                    var node = line.nodes[end];
                    if (!node.spool || !node.Round || node.post.part == null) continue;
                    var rotor = system.rotors.FirstOrDefault(r => r.parts.Contains(node.post.part));
                    if (rotor == null) continue;
                    run.winches.Add((line, rotor, end));
                    run.before.Add(new StringLine.StringElement(line));
                    break;
                }
            }
            return run;
        }

        /// <summary>V2: turns the system's first rotor by up to `degrees` (less where a rack or nut reaches the end of
        /// its travel, none the way a ratchet holds); what it turned.</summary>
        public static float Apply(Run run, float degrees) {
            if (Mathf.Abs(degrees) < 1e-6f) return 0f;
            var system = run.system;
            if (system.blocked != 0 && Mathf.Sign(degrees) == system.blocked) return 0f;
            // The straight-line movers' travel limits it.
            var scale = 1f;
            foreach (var (slider, _, moved, _) in run.sliders) {
                if (slider.cam != null || !system.ratio.TryGetValue(slider.rotor, out var k)) continue;
                var step = degrees * k * Mathf.Deg2Rad * slider.inchesPerRadian;
                if (Mathf.Abs(step) < 1e-7f) continue;
                var room = step > 0f ? slider.high - moved : slider.low - moved;
                if (room * step <= 0f) scale = 0f;
                else if (step / room > 1f) scale = Mathf.Min(scale, room / step);
            }
            // V2 (build 88): a winch winds until its string is tight (what it pulls doesn't move in the build: in
            // Simulate it does) and unwinds until it's all off the drum.
            foreach (var (line, rotor, end) in run.winches) {
                var wind = degrees * WindPerDegree(line, rotor, end, system.ratio[rotor], out var path);
                if (Mathf.Abs(wind) < 1e-7f || path == null) continue;
                var room = wind > 0f ? Mathf.Max(0f, line.length - path.tight) : path.wound;
                if (Mathf.Abs(wind) > room) scale = Mathf.Min(scale, room / Mathf.Abs(wind));
            }
            degrees *= scale;
            if (Mathf.Abs(degrees) < 1e-6f) return 0f;
            var winds = run.winches.Select(w => degrees * WindPerDegree(w.line, w.rotor, w.end, system.ratio[w.rotor], out _)).ToList();
            run.turned += degrees;
            // V2: each part's new pose from where it was before this step (parents set before their children), so a
            // part inside another that turns on a different shaft (a universal joint's far yoke) isn't turned twice.
            var moves = new List<(Transform t, Vector3 position, Quaternion rotation, int depth)>();
            foreach (var rotor in system.rotors) {
                var q = Quaternion.AngleAxis(degrees * system.ratio[rotor], rotor.axis);
                var pivot = rotor.start;
                void Turn(GameObject part) {
                    if (part == null) return;
                    var t = part.transform;
                    var depth = 0;
                    for (var up = t.parent; up != null; up = up.parent) depth++;
                    moves.Add((t, pivot + q * (t.position - pivot), q * t.rotation, depth));
                }
                foreach (var part in rotor.parts) Turn(part);
                foreach (var (carrier, riding) in run.riders)
                    if (carrier == rotor)
                        foreach (var part in riding) Turn(part);
            }
            foreach (var (t, position, rotation, _) in moves.OrderBy(m => m.depth))
                if (t != null) t.SetPositionAndRotation(position, rotation);
            foreach (var (chain, rotor, inchesPerRadian) in system.chains)
                if (chain != null) chain.Advance(degrees * system.ratio[rotor] * Mathf.Deg2Rad * inchesPerRadian);
            foreach (var (rotor, leader, body, pin) in run.hanging) {
                var q = Quaternion.AngleAxis(run.turned * system.ratio[rotor], rotor.axis);
                leader.Set(q, rotor.start - q * rotor.start, false);
                Linkage.Trail(body, pin, leader);
            }
            for (var i = 0; i < run.sliders.Count; i++) {
                var (slider, start, moved, reach) = run.sliders[i];
                if (slider.cam != null) moved = GearKit.CamReach(slider) - reach;   // it rides on its cam
                else moved += degrees * system.ratio[slider.rotor] * Mathf.Deg2Rad * slider.inchesPerRadian;
                foreach (var (t, at) in start)
                    if (t != null) t.position = at + slider.axis * moved;
                run.sliders[i] = (slider, start, moved, reach);
            }
            for (var i = 0; i < run.winches.Count; i++) {
                var (line, _, end) = run.winches[i];
                if (line == null || Mathf.Abs(winds[i]) < 1e-7f) continue;
                var r = line.nodes[end].post.radius + line.kind.DrawRadius;
                line.turns = Mathf.Max(0f, line.turns + winds[i] / (2f * Mathf.PI * Mathf.Max(r, 1e-3f)));
                line.length -= winds[i];
                line.Refresh(true);
            }
            return degrees;
        }

        // V2 (build 88): inches of string a winch winds on (+) or off per degree of its system's first rotor. The
        // string leaves the drum at a point that stays put while the drum turns under it; the coil lies behind that
        // point (StringPath.Of), so turning the drum the way the string goes round it pays out at the first point and
        // winds on at the last.
        public static float WindPerDegree(StringLine line, Rotor rotor, int end, float ratio, out StringPath path) {
            path = null;
            if (line == null || rotor == null) return 0f;
            path = StringLine.PathOf(line.nodes, line.kind, line.turns);
            if (!path.Valid || path.ways.Length <= end) return 0f;
            var node = line.nodes[end];
            var axis = node.post.Axis.normalized;
            var way = path.ways[end] != 0 ? path.ways[end] : 1;
            var r = node.post.radius + line.kind.DrawRadius;
            var turn = ratio * Mathf.Sign(Vector3.Dot(rotor.axis, axis)) * Mathf.Deg2Rad;   // the drum about the post's axis
            return -r * turn * (end == 0 ? way : -way);
        }

        /// <summary>V2: one undo step for the run (nothing when it didn't turn).</summary>
        public static void End(Run run) {
            if (run != null) Finish(run);
        }

        // V2: a shaft whose bearings are all on one of the system's turning rotors rides on it.
        private static void FindRiders(Run run, List<Rotor> all) {
            var carrierOf = new Dictionary<GameObject, Rotor>();
            foreach (var rotor in run.system.rotors)
                foreach (var part in rotor.parts)
                    if (part != null) carrierOf[part] = rotor;
            foreach (var rotor in all) {
                if (run.system.rotors.Contains(rotor) || rotor.bearings.Count == 0 || rotor.motors.Count > 0) continue;
                Rotor carrier = null;
                var rides = true;
                foreach (var (part, _) in rotor.bearings) {
                    if (part == null || !carrierOf.TryGetValue(part, out var c) || (carrier != null && c != carrier)) {
                        rides = false;
                        break;
                    }
                    carrier = c;
                }
                if (!rides || carrier == null) continue;
                var parts = rotor.parts.Where(p => p != null && !carrierOf.ContainsKey(p)).ToList();
                if (parts.Count > 0) run.riders.Add((carrier, parts));
            }
        }

        // What hangs on joint screws from a system's turning parts (and isn't also held by the frame).
        private static void FindHanging(Run run) {
            var moving = new HashSet<GameObject>(run.parts);
            var stop = new HashSet<GameObject>(moving);
            foreach (var rotor in run.system.rotors) {
                foreach (var (part, _) in rotor.bearings) stop.Add(part);
                foreach (var motor in rotor.motors) stop.Add(motor.gameObject);
            }
            var taken = new HashSet<GameObject>();
            foreach (var rotor in run.system.rotors)
                foreach (var part in rotor.parts)
                    foreach (var screw in GroupManager.Neighbours(part).Concat(new[] { part }).Distinct()) {
                        if (!Linkage.IsJoint(screw)) continue;
                        var others = GroupManager.Neighbours(screw).Where(n => !moving.Contains(n) && !taken.Contains(n) && !Linkage.Fastens(screw, n)).ToList();
                        if (!moving.Contains(screw)) {
                            // The screw stays with what turns? Only if it's fastened to it; else it hangs too.
                            if (!GroupManager.Neighbours(screw).Any(n => moving.Contains(n) && Linkage.Fastens(screw, n))) others.Add(screw);
                        }
                        if (others.Count == 0) continue;
                        var found = Dynamics.Fastened(others, stop, o => o.GetComponent<Shaft>() != null, out var frame);
                        if (frame || found.Count == 0) continue;
                        var body = new Linkage.Body();
                        foreach (var obj in found) {
                            taken.Add(obj);
                            body.objects.Add(obj);
                            var nested = false;
                            for (var t = obj.transform.parent; t != null && !nested; t = t.parent) nested = found.Contains(t.gameObject);
                            if (!nested) body.start[obj.transform] = (obj.transform.position, obj.transform.rotation);
                        }
                        var renderers = found.Select(o => o.GetComponent<Renderer>()).Where(r => r != null).ToList();
                        body.center = renderers.Count > 0 ? renderers.Aggregate(Vector3.zero, (sum, r) => sum + r.bounds.center) / renderers.Count : screw.transform.position;
                        var pin = new Linkage.Pin { point = screw.transform.position, axis = screw.transform.forward };
                        run.hanging.Add((rotor, new Linkage.Body(), body, pin));
                    }
        }

        private readonly List<Run> runs = new();
        private readonly HashSet<MotorDrive> refused = new();
        private readonly HashSet<MotorDrive> idle = new();   // running with no shaft in them

        public static void Ensure() {
            if (host != null) return;
            host = new GameObject("Motor Runner").AddComponent<MotorRunner>();
        }

        /// <summary>Whether any motor is running (the idle frame rate waits for it).</summary>
        public static bool Active => host != null && host.runs.Count > 0;

        /// <summary>Whether a motor is turning (its own run or driven by another).</summary>
        public static bool Turning(MotorDrive motor) => motor != null && Mathf.Abs(motor.Level) > 0.001f;

        public static void StopAll() {
            foreach (var motor in MotorDrive.Live()) motor.StopToggle();
        }

        private static List<Rotor> CurrentRotors() => GearTrain.Current();

        private void Update() {
            var motors = MotorDrive.Live();
            if (Sandbox.Running) return;  // the sandbox drives them
            var typing = InputEvents.TextInput.IsTyping || InputEvents.TextInput.ShortcutsPaused;
            foreach (var motor in motors) {
                float direction = motor.Toggled;
                if (direction == 0 && !typing && MotorControls.IsControlled(motor)) {
                    if (Controls.Held(Controls.ForwardKey(motor.gameObject))) direction += 1f;
                    if (Controls.Held(Controls.ReverseKey(motor.gameObject))) direction -= 1f;
                }
                motor.Command = direction * Controls.Speed(motor.gameObject) * (Controls.Reversed(motor.gameObject) ? -1f : 1f);
                // V2: run by hand from the Devices panel (< and >).
                if (motor.Toggled == 0 && Mathf.Abs(motor.Manual) > 0.001f) motor.Command = motor.Manual;
                if (Mathf.Abs(motor.Command) < 0.001f) {
                    refused.Remove(motor);
                    idle.Remove(motor);
                }
            }

            // Runs for motors starting up.
            foreach (var motor in motors) {
                if (Mathf.Abs(motor.Command) < 0.001f || refused.Contains(motor) || runs.Any(r => r.system.Motors.Contains(motor))) continue;
                if (motor.gameObject.IsDeleted()) continue;
                var system = GearTrain.SystemOf(motor, CurrentRotors());
                if (system == null) {
                    idle.Add(motor);
                    continue;
                }
                var run = Begin(system, motor, out var why);   // V2: shared with the hand crank
                if (run == null) {
                    refused.Add(motor);
                    Interface.Interface.Say(motor.Label + " can't turn: " + why, false);
                    Audio.Sounds.Play(Audio.Sounds.Error);
                    continue;
                }
                runs.Add(run);

            }

            foreach (var motor in motors) motor.Level = idle.Contains(motor) ? motor.Command : 0f;
            for (var i = runs.Count - 1; i >= 0; i--) {
                var run = runs[i];
                var target = 0f;
                var driving = 0;
                var fastest = 1f;
                foreach (var motor in run.system.Motors) {
                    var ratio = run.system.MotorRatio(motor);
                    if (Mathf.Abs(ratio) < 1e-5f) continue;
                    fastest = Mathf.Max(fastest, motor.Rpm / Mathf.Abs(ratio));
                    if (Mathf.Abs(motor.Command) < 0.001f) continue;
                    target += motor.Command * motor.Rpm / ratio;
                    driving++;
                }
                if (driving > 0) target /= driving;
                // V5 motors get up to speed in a moment.
                run.speed = Mathf.MoveTowards(run.speed, target, fastest / 0.2f * Time.deltaTime);
                var degrees = run.speed * 6f * Time.deltaTime;
                // V2: a rack or nut at the end of its travel, or a ratchet holding, stalls it.
                if (Mathf.Abs(Apply(run, degrees)) < Mathf.Abs(degrees) * 0.999f) run.speed = 0f;
                foreach (var motor in run.system.Motors)
                    motor.Level = run.speed * run.system.MotorRatio(motor) / Mathf.Max(1f, motor.Rpm);
                if (driving == 0 && Mathf.Abs(run.speed) < 0.001f) {
                    Finish(run);
                    runs.RemoveAt(i);
                }
            }
            foreach (var motor in motors) motor.Sound(motor.Level);
        }

        private static void Finish(Run run) {
            var turns = Mathf.Abs(run.turned) / 360f;
            if (turns < 0.001f) return;
            StateSystem.AddElements(run.before);
            var after = run.parts.Where(o => o != null).Select(o => (IElement)new ObjectElement(o)).ToList();
            foreach (var (chain, _, _) in run.system.chains)
                if (chain != null) {
                    chain.Save();
                    after.Add(new ChainLoop.PhaseElement(chain));
                }
            foreach (var (line, _, _) in run.winches)
                if (line != null) {
                    line.Save();
                    after.Add(new StringLine.StringElement(line));
                }
            StateSystem.AddState(new State(after));
        }
    }
}
