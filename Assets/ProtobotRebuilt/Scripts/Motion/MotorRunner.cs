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

        private class Run {
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
                if (system.jammed) {
                    refused.Add(motor);
                    Interface.Interface.Say(motor.Label + " can't turn: " + system.jamReason, false);
                    Audio.Sounds.Play(Audio.Sounds.Error);
                    continue;
                }
                var parts = system.rotors.SelectMany(r => r.parts).Where(p => p != null).Distinct().ToList();
                var grounded = parts.FirstOrDefault(Linkage.IsGrounded);
                if (grounded != null) {
                    refused.Add(motor);
                    Interface.Interface.Say(motor.Label + " can't turn: " + Interface.PartLabels.Short(grounded) + " on its shaft is grounded", false);
                    Audio.Sounds.Play(Audio.Sounds.Error);
                    continue;
                }
                var run = new Run { system = system, parts = parts, starter = motor };
                FindHanging(run);
                FindRiders(run, CurrentRotors());   // V2
                var moved = parts.Concat(run.hanging.SelectMany(h => h.body.objects)).Concat(run.riders.SelectMany(r => r.parts)).Distinct().ToList();
                run.parts = moved;
                run.before = moved.Select(p => (IElement)new ObjectElement(p)).ToList();
                foreach (var (chain, _, _) in system.chains) run.before.Add(new ChainLoop.PhaseElement(chain));
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
                run.turned += degrees;
                foreach (var rotor in run.system.rotors) {
                    var turn = degrees * run.system.ratio[rotor];
                    var pivot = rotor.start;
                    foreach (var part in rotor.parts)
                        if (part != null) part.transform.RotateAround(pivot, rotor.axis, turn);
                    // The rotor's line stays where it is; its start point turns about its own axis (no change).
                    foreach (var (carrier, riding) in run.riders)
                        if (carrier == rotor)
                            foreach (var part in riding)
                                if (part != null) part.transform.RotateAround(pivot, rotor.axis, turn);
                }
                // Chains run with the sprockets that turn them.
                foreach (var (chain, rotor, inchesPerRadian) in run.system.chains)
                    if (chain != null) chain.Advance(degrees * run.system.ratio[rotor] * Mathf.Deg2Rad * inchesPerRadian);
                foreach (var (rotor, leader, body, pin) in run.hanging) {
                    var q = Quaternion.AngleAxis(run.turned * run.system.ratio[rotor], rotor.axis);
                    leader.Set(q, rotor.start - q * rotor.start, false);
                    Linkage.Trail(body, pin, leader);
                }
                foreach (var motor in run.system.Motors)
                    motor.Level = run.speed * run.system.MotorRatio(motor) / Mathf.Max(1f, motor.Rpm);
                if (driving == 0 && Mathf.Abs(run.speed) < 0.001f) {
                    Finish(run);
                    runs.RemoveAt(i);
                }
            }
            foreach (var motor in motors) motor.Sound(motor.Level);
        }

        private void Finish(Run run) {
            var turns = Mathf.Abs(run.turned) / 360f;
            if (turns < 0.001f) return;
            StateSystem.AddElements(run.before);
            var after = run.parts.Where(o => o != null).Select(o => (IElement)new ObjectElement(o)).ToList();
            foreach (var (chain, _, _) in run.system.chains)
                if (chain != null) {
                    chain.Save();
                    after.Add(new ChainLoop.PhaseElement(chain));
                }
            StateSystem.AddState(new State(after));
        }
    }
}
