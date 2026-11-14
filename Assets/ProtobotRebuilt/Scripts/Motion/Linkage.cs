using System.Collections.Generic;
using System.Linq;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    // V2: joints and grounded parts in the build, like SolidWorks' mates. A screw fastens what it goes through
    // (as before) unless it's made a joint (right-click): then the parts on it turn about it. The screw stays with
    // the part it's threaded into (its nut). Parts screwed together tight are one body; bodies on a joint are
    // linked by a pin (the screw's axis). Moving one body drags the others along: each turns about its pin as
    // little as it can (the way of least resistance). A grounded part (right-click > Ground) doesn't move at all;
    // a body pinned to it can't be moved, only turned about its joint (a move becomes the turn that gets closest).
    // Grounding and joints are kept in the parts' saved state ("ground:1", "joint:1"); the sandbox ignores grounding.
    public static class Linkage {
        public static bool IsJoint(GameObject screw) => screw != null && screw.GetComponent<Screw>() != null && Dynamics.Token(screw, "joint") == "1";
        public static bool IsGrounded(GameObject part) => part != null && Dynamics.Token(part, "ground") == "1";

        /// <summary>The parts a screw is threaded into (nuts, threaded holes).</summary>
        public static List<GameObject> Threaded(GameObject screw) {
            if (screw == null || !screw.TryGetComponent(out Screw s) || s.holeDetector == null) return new List<GameObject>();
            return s.holeDetector.holes.Where(h => h != null && s.holeDetector.IsTargetHole(h) && h.holeData != null && h.holeData.part != null)
                    .Select(h => h.holeData.part).Distinct().ToList();
        }

        /// <summary>A joint screw held only by a nut stays with the part under its head (the screw and nut alone
        /// would be a loose pin between the parts); null when it's threaded into a part (it stays with that).</summary>
        public static GameObject HeadPart(GameObject screw) {
            if (screw == null || !screw.TryGetComponent(out Screw s) || s.holeDetector == null) return null;
            var threaded = Threaded(screw);
            if (threaded.Any(t => !(t.TryGetComponent(out SavedObject saved) && saved.id != null && saved.id.StartsWith("NUT")))) return null;
            foreach (var hole in s.holeDetector.GetOrderedHoles())
                if (hole != null && hole.holeData != null && hole.holeData.part != null && !threaded.Contains(hole.holeData.part))
                    return hole.holeData.part;
            return null;
        }

        /// <summary>Whether fastening goes from <paramref name="a"/> to its neighbour <paramref name="b"/> (not across a joint).</summary>
        public static bool Fastens(GameObject a, GameObject b) {
            // A chain doesn't fasten its sprockets together: it turns them.
            if (a.GetComponent<ChainLoop>() != null || b.GetComponent<ChainLoop>() != null) return false;
            if (SpinsOn(a, b) || SpinsOn(b, a)) return false;
            if (IsJoint(a)) return Threaded(a).Contains(b) || HeadPart(a) == b;
            if (IsJoint(b)) return Threaded(b).Contains(a) || HeadPart(b) == a;
            return true;
        }

        // V2: a screw through a wheel's, gear's or sprocket's middle hole is its axle: the round insert (bearing
        // insert) there lets it spin, as teams build screw joints. The screw stays tight with everything else it goes
        // through (channels, nuts, spacers); what's screwed to the wheel through its other holes (a gear) turns with
        // it. No joint setting is needed. Worked out per pair, kept until the build changes.
        private static readonly Dictionary<(int, int), bool> spins = new();
        private static long spinsVersion = -1;

        /// <summary>Whether <paramref name="part"/> spins on <paramref name="screw"/> (the screw goes through its
        /// middle hole, along its axle).</summary>
        public static bool SpinsOn(GameObject screw, GameObject part) {
            if (screw == null || part == null || screw.GetComponent<Screw>() == null || part.GetComponent<Screw>() != null) return false;
            if (spinsVersion != Dynamics.Version) {
                spins.Clear();
                spinsVersion = Dynamics.Version;
            }
            var key = (screw.GetInstanceID(), part.GetInstanceID());
            if (spins.TryGetValue(key, out var known)) return known;
            var result = false;
            if (GearTrain.TurnsWithShaft(part) && part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) {
                var middle = part.transform.TransformPoint(filter.sharedMesh.bounds.center);
                HoleCollider axle = null;
                var best = float.MaxValue;
                foreach (var hole in part.GetComponentsInChildren<HoleCollider>(true)) {
                    if (hole.transform.parent != part.transform) continue;
                    var d = (hole.transform.position - middle).sqrMagnitude;
                    if (d < best) {
                        best = d;
                        axle = hole;
                    }
                }
                if (axle != null) {
                    var t = screw.transform;
                    var parallel = Mathf.Abs(Vector3.Dot(axle.transform.forward, t.forward)) > 0.98f;
                    var off = Vector3.ProjectOnPlane(axle.transform.position - t.position, t.forward).magnitude;
                    result = parallel && off < 0.06f;
                }
            }
            spins[key] = result;
            return result;
        }

        // ------------------------------------------------------------------ changing them (one undo step each)

        public static void SetJoint(GameObject screw, bool on) {
            if (screw == null || IsJoint(screw) == on) return;
            var before = new TokenElement(screw, "joint");
            Dynamics.SetToken(screw, "joint", on ? "1" : null);
            StateSystem.AddElement(before);
            StateSystem.AddState(new TokenElement(screw, "joint"));
            var parts = GroupManager.Neighbours(screw).Count(n => n.GetComponent<SavedObject>() != null);
            Interface.Interface.Say(on ? "The screw is a joint: the parts on it turn about it" + (parts < 2 ? " (once it goes through two parts into a nut)" : "")
                                       : "The screw is tight again: it fastens what it goes through", true);
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }

        public static void SetGrounded(GameObject part, bool on) {
            if (part == null || IsGrounded(part) == on) return;
            var before = new TokenElement(part, "ground");
            Dynamics.SetToken(part, "ground", on ? "1" : null);
            StateSystem.AddElement(before);
            StateSystem.AddState(new TokenElement(part, "ground"));
            Interface.Interface.Say(on ? Interface.PartLabels.Short(part) + " is grounded: it stays put (what's on a joint to it can only turn)"
                                       : Interface.PartLabels.Short(part) + " isn't grounded any more", true);
            Audio.Sounds.Play(Audio.Sounds.Tick);
            RefreshGrounded();
        }

        /// <summary>The faint red outline on every grounded part.</summary>
        public static void RefreshGrounded() {
            var grounded = Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None).Where(s => !s.gameObject.IsDeleted() && IsGrounded(s.gameObject)).Select(s => s.gameObject).ToList();
            if (grounded.Count == 0) Highlights.Clear("grounded");
            else Highlights.Set("grounded", grounded, Highlights.Grounded);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init() {
            StateSystem.OnChangeState += () => {
                if (!Sandbox.Running) RefreshGrounded();
            };
        }

        /// <summary>A saved-state token, for undo (joint, ground).</summary>
        private class TokenElement : IElement {
            private readonly GameObject part;
            private readonly string key, value;

            public TokenElement(GameObject part, string key) {
                this.part = part;
                this.key = key;
                value = Dynamics.Token(part, key);
            }

            public void Load() {
                if (part == null) return;
                Dynamics.SetToken(part, key, value);
                RefreshGrounded();
            }
        }

        // ------------------------------------------------------------------ bodies and pins

        public class Body {
            public readonly List<GameObject> objects = new();
            public bool grounded;
            public Vector3 center;                                        // at the start
            public Quaternion turn = Quaternion.identity;                 // from the start
            public Vector3 shift;
            public readonly Dictionary<Transform, (Vector3 position, Quaternion rotation)> start = new();

            public Vector3 Map(Vector3 startPoint) => turn * startPoint + shift;

            public void Set(Quaternion newTurn, Vector3 newShift, bool apply = true) {
                turn = newTurn;
                shift = newShift;
                if (!apply) return;
                foreach (var pair in start)
                    if (pair.Key != null) pair.Key.SetPositionAndRotation(turn * pair.Value.position + shift, turn * pair.Value.rotation);
            }

            /// <summary>Turns the body about a line (in the world, now).</summary>
            public void TurnAbout(Vector3 point, Vector3 axis, float degrees, bool apply = true) {
                var q = Quaternion.AngleAxis(degrees, axis);
                Set(q * turn, q * (shift - point) + point, apply);
            }
        }

        public class Pin {
            public Body a, b;
            public Vector3 point, axis;   // at the start
            public Body Other(Body body) => body == a ? b : a;
        }

        public class Mechanism {
            public readonly List<Body> bodies = new();
            public readonly List<Pin> pins = new();
            public readonly Dictionary<GameObject, Body> bodyOf = new();
            public bool HasLinks => pins.Count > 0 || bodies.Any(b => b.grounded);
        }

        /// <summary>The bodies (tight together) and pins (joints) among some objects.</summary>
        public static Mechanism Analyze(IEnumerable<GameObject> objects) {
            // Parts and what connects them (a cylinder's rod thread isn't a part of its own); a selected hole's face moves with its part.
            var set = new HashSet<GameObject>(objects.Where(o => o != null && (o.GetComponent<SavedObject>() != null || o.GetComponent<ConnectingPart>() != null)));
            var parent = set.ToDictionary(o => o, o => o);
            GameObject Find(GameObject o) {
                while (parent[o] != o) o = parent[o] = parent[parent[o]];
                return o;
            }
            void Union(GameObject x, GameObject y) {
                if (!parent.ContainsKey(x) || !parent.ContainsKey(y)) return;
                x = Find(x);
                y = Find(y);
                if (x != y) parent[x] = y;
            }
            var pinned = new List<(GameObject screw, GameObject part)>();
            foreach (var obj in set) {
                if (obj.GetComponent<ConnectingPart>() == null) continue;
                var joint = IsJoint(obj);
                var threaded = joint ? Threaded(obj) : null;
                var head = joint ? HeadPart(obj) : null;
                if (head != null) threaded.Add(head);
                foreach (var next in GroupManager.Neighbours(obj)) {
                    if (!set.Contains(next)) continue;
                    if ((joint && !threaded.Contains(next)) || SpinsOn(obj, next)) pinned.Add((obj, next));
                    else Union(obj, next);
                }
            }
            // V2: what a cylinder's rod thread holds (a nut, a collar, a bracket) is fastened to the cylinder: the thread
            // itself isn't in the list (it stands for its cylinder), so this was missed and the parts on the rod were
            // left behind when the cylinder moved (outlined with it, but not moved).
            // V2: a hinge's top leaf (a child of the hinge, not a part): what's screwed to it is on the hinge.
            foreach (var obj in set.ToList())
                if (obj.TryGetComponent(out HingePart hingePart) && hingePart.Leaf != null) {
                    foreach (var next in GroupManager.Neighbours(hingePart.Leaf.gameObject))
                        if (set.Contains(next) && next != obj) Union(obj, next);
                }
                else if (obj.TryGetComponent(out UniversalJoint uj) && uj.Yoke != null) {
                    foreach (var next in GroupManager.Neighbours(uj.Yoke.gameObject))
                        if (set.Contains(next) && next != obj) Union(obj, next);
                }
            // V2: a slide truck is held by the slide member it's in.
            foreach (var obj in set.ToList())
                if (obj.TryGetComponent(out SlideTruck truck) && truck.Member != null && set.Contains(truck.Member)) Union(obj, truck.Member);
            foreach (var obj in set.ToList())
                if (obj.TryGetComponent(out PneumaticCylinder cylinder) && cylinder.Thread != null) {
                    foreach (var next in GroupManager.Neighbours(cylinder.Thread.gameObject))
                        if (set.Contains(next) && next != obj) Union(obj, next);
                }
            // A child of something moved (a cylinder's rod thread) goes with it.
            foreach (var obj in set)
                for (var t = obj.transform.parent; t != null; t = t.parent)
                    if (set.Contains(t.gameObject)) {
                        Union(obj, t.gameObject);
                        break;
                    }

            // Two pins between the same bodies that aren't on one line hold them tight.
            for (var changed = true; changed;) {
                changed = false;
                var byPair = new Dictionary<(GameObject, GameObject), (Vector3 point, Vector3 axis)>();
                foreach (var (screw, part) in pinned) {
                    GameObject x = Find(screw), y = Find(part);
                    if (x == y) continue;
                    var key = x.GetInstanceID() < y.GetInstanceID() ? (x, y) : (y, x);
                    var point = screw.transform.position;
                    var axis = screw.transform.forward;
                    if (byPair.TryGetValue(key, out var have)) {
                        var collinear = Mathf.Abs(Vector3.Dot(have.axis, axis)) > 0.995f && Vector3.ProjectOnPlane(point - have.point, have.axis).magnitude < 0.05f;
                        if (!collinear) {
                            Union(x, y);
                            changed = true;
                            break;
                        }
                    }
                    else byPair[key] = (point, axis);
                }
            }

            var mechanism = new Mechanism();
            var roots = new Dictionary<GameObject, Body>();
            foreach (var obj in set) {
                var root = Find(obj);
                if (!roots.TryGetValue(root, out var body)) {
                    body = new Body();
                    roots[root] = body;
                    mechanism.bodies.Add(body);
                }
                body.objects.Add(obj);
                mechanism.bodyOf[obj] = body;
                if (IsGrounded(obj)) body.grounded = true;
            }
            foreach (var body in mechanism.bodies) {
                var sum = Vector3.zero;
                var count = 0;
                foreach (var obj in body.objects) {
                    if (obj.TryGetComponent(out Renderer r)) {
                        sum += r.bounds.center;
                        count++;
                    }
                    // Only the topmost of the body's objects is moved (children move with it).
                    var p = obj.transform.parent;
                    var nested = false;
                    for (; p != null; p = p.parent)
                        if (body.objects.Contains(p.gameObject)) { nested = true; break; }
                    if (!nested) body.start[obj.transform] = (obj.transform.position, obj.transform.rotation);
                }
                body.center = count > 0 ? sum / count : body.objects[0].transform.position;
            }
            var seen = new HashSet<(Body, Body)>();
            foreach (var (screw, part) in pinned) {
                Body a = mechanism.bodyOf[screw], b = mechanism.bodyOf[part];
                if (a == b || seen.Contains((a, b)) || seen.Contains((b, a))) continue;
                seen.Add((a, b));
                mechanism.pins.Add(new Pin { a = a, b = b, point = screw.transform.position, axis = screw.transform.forward });
            }
            return mechanism;
        }

        // ------------------------------------------------------------------ following

        /// <summary>Moves <paramref name="body"/> so it stays on <paramref name="pin"/> where <paramref name="leader"/>
        /// has taken it, turning about the pin as little as it can.</summary>
        public static void Trail(Body body, Pin pin, Body leader) {
            var target = leader.Map(pin.point);
            var targetAxis = leader.turn * pin.axis;
            var axisNow = body.turn * pin.axis;
            var before = body.Map(body.center);
            var turn = Quaternion.FromToRotation(axisNow, targetAxis) * body.turn;
            var shift = target - turn * pin.point;
            var after = turn * body.center + shift;
            var u = Vector3.ProjectOnPlane(after - target, targetAxis);
            var v = Vector3.ProjectOnPlane(before - target, targetAxis);
            if (u.sqrMagnitude > 1e-6f && v.sqrMagnitude > 1e-6f) {
                var twist = Quaternion.AngleAxis(Vector3.SignedAngle(u, v, targetAxis), targetAxis);
                turn = twist * turn;
                shift = target - turn * pin.point;
            }
            body.Set(turn, shift);
        }

        /// <summary>Brings every body linked to the moved ones along (grounded and <paramref name="still"/> ones stay).</summary>
        public static void Follow(Mechanism mechanism, IEnumerable<Body> moved, ICollection<Body> still = null) {
            var done = new HashSet<Body>(moved);
            var queue = new Queue<Body>(done);
            while (queue.Count > 0) {
                var leader = queue.Dequeue();
                foreach (var pin in mechanism.pins) {
                    if (pin.a != leader && pin.b != leader) continue;
                    var body = pin.Other(leader);
                    if (done.Contains(body) || body.grounded || (still != null && still.Contains(body))) continue;
                    Trail(body, pin, leader);
                    done.Add(body);
                    queue.Enqueue(body);
                }
            }
        }

        // ------------------------------------------------------------------ moving with the Move and Transform tools

        public enum MoveKind { Any, Translate, Rotate }

        /// <summary>A move of one body of a mechanism (made when a move starts; MovementManager asks it what's allowed).</summary>
        public class Plan {
            public Mechanism mechanism;
            public Body moving;
            public string refusal;
            public bool constrained;
            private Transform mover;
            private Vector3 moverStartPosition;
            private Quaternion moverStartRotation;
            private List<(Body body, Pin pin)> chain;   // from the moving body to a grounded one
            private readonly HashSet<Body> solved = new();
            private bool saidOnlyTurns;

            /// <summary>The joint it turns about when it's on one to a grounded part (where the gizmo goes).</summary>
            public Vector3? FirstPin => constrained && chain.Count > 0 ? chain[0].pin.point : null;

            public List<GameObject> PivotObjects(List<GameObject> all) =>
                all.Where(o => o != null && (!mechanism.bodyOf.TryGetValue(o, out var b) || b == moving)).ToList();

            public Plan(Transform mover, Mechanism mechanism, Body moving) {
                this.mover = mover;
                this.mechanism = mechanism;
                this.moving = moving;
                moverStartPosition = mover.position;
                moverStartRotation = mover.rotation;
                chain = PathToGround();
                constrained = chain != null;
            }

            private List<(Body, Pin)> PathToGround() {
                var from = new Dictionary<Body, (Body, Pin)>();
                var queue = new Queue<Body>();
                queue.Enqueue(moving);
                from[moving] = (null, null);
                while (queue.Count > 0) {
                    var body = queue.Dequeue();
                    if (body.grounded && body != moving) {
                        var path = new List<(Body, Pin)>();
                        for (var b = body; from[b].Item1 != null; b = from[b].Item1) path.Insert(0, (from[b].Item1, from[b].Item2));
                        return path;
                    }
                    foreach (var pin in mechanism.pins) {
                        if (pin.a != body && pin.b != body) continue;
                        var next = pin.Other(body);
                        if (from.ContainsKey(next)) continue;
                        from[next] = (body, pin);
                        queue.Enqueue(next);
                    }
                }
                return null;
            }

            /// <summary>Where the moving object may go instead of where it was asked to (or null: nowhere).</summary>
            public Transformations.Displacement Constrain(Transformations.Displacement wanted, MoveKind kind) {
                if (!constrained) return wanted;
                var position = wanted.translation.Position;
                var rotation = wanted.rotation.Orientation;
                // The chain's bodies (moving one first) turn about their pins to get the moving one there.
                solved.Clear();
                foreach (var (body, _) in chain) solved.Add(body);
                if (kind == MoveKind.Rotate) {
                    var delta = rotation * Quaternion.Inverse(moverStartRotation);
                    delta.ToAngleAxis(out var angle, out var axis);
                    if (angle > 180f) angle -= 360f;
                    var pin = chain[0].pin;
                    var pinAxis = chain[0].pin.Other(chain[0].body).turn * pin.axis;
                    if (Mathf.Abs(angle) > 0.01f && Mathf.Abs(Vector3.Dot(axis.normalized, pinAxis)) < 0.98f) {
                        if (!saidOnlyTurns) Interface.Interface.Say("It's on a joint to a grounded part: it only turns about the joint", false);
                        saidOnlyTurns = true;
                        return new Transformations.Displacement(mover.position, mover.rotation);
                    }
                    var signed = angle * Mathf.Sign(Vector3.Dot(axis, pinAxis));
                    var holder = pin.Other(moving);
                    var start = holder.Map(pin.point);
                    moving.Set(Quaternion.identity, Vector3.zero, false);
                    moving.TurnAbout(start, pinAxis, signed, false);
                }
                else {
                    // A move: the turn about the joints that gets the part's point (the mover) closest to where it was dragged.
                    var goal = position;
                    for (var iteration = 0; iteration < 12; iteration++)
                        for (var j = 0; j < chain.Count; j++) {
                            var (_, pin) = chain[j];
                            var holder = j + 1 < chain.Count ? chain[j + 1].body : pin.Other(chain[j].body);
                            var point = holder.Map(pin.point);
                            var axis = holder.turn * pin.axis;
                            var x = moving.Map(moverStartPosition);
                            var u = Vector3.ProjectOnPlane(x - point, axis);
                            var v = Vector3.ProjectOnPlane(goal - point, axis);
                            if (u.sqrMagnitude < 1e-6f || v.sqrMagnitude < 1e-6f) continue;
                            var angle = Vector3.SignedAngle(u, v, axis);
                            if (Mathf.Abs(angle) < 0.01f) continue;
                            // The moving body is carried by the pivot: only its numbers change here.
                            for (var k = 0; k <= j; k++) chain[k].body.TurnAbout(point, axis, angle, chain[k].body != moving);
                        }
                }
                // The mover goes where the moving body went (the pivot puts it there); the rest of the chain is set.
                var moverPosition = moving.Map(moverStartPosition);
                var moverRotation = moving.turn * moverStartRotation;
                return new Transformations.Displacement(moverPosition, moverRotation);
            }

            /// <summary>After the moving body moved (the pivot): the others follow.</summary>
            public void Follow() {
                if (mover == null) return;
                // The moving body's move from the start, read off the mover.
                var turn = mover.rotation * Quaternion.Inverse(moverStartRotation);
                var shift = mover.position - turn * moverStartPosition;
                moving.turn = turn;
                moving.shift = shift;
                foreach (var body in solved)
                    if (body != moving) body.Set(body.turn, body.shift);
                var moved = new List<Body> { moving };
                moved.AddRange(solved);
                Linkage.Follow(mechanism, moved);
            }
        }

        /// <summary>The joint a part turns about when it's on one to a grounded part, or null.</summary>
        public static Vector3? GroundPin(GameObject part) {
            if (part == null || part.GetComponent<SavedObject>() == null) return null;
            var group = part.GetConnectedObjects(true);
            if (group == null || group.Count == 0) return null;
            var mechanism = Analyze(group);
            if (!mechanism.HasLinks || !mechanism.bodyOf.TryGetValue(part, out var body) || body.grounded) return null;
            return new Plan(part.transform, mechanism, body).FirstPin;
        }

        /// <summary>A plan for moving <paramref name="mover"/> (and what it carries), or null when nothing in it is
        /// on a joint or grounded (it moves as before).</summary>
        public static Plan MakePlan(GameObject mover, List<GameObject> group) {
            if (mover == null || group == null || group.Count == 0) return null;
            var mechanism = Analyze(group);
            if (!mechanism.HasLinks) return null;
            // A selected hole moves the part it's on; several selected parts move together (grounded ones refuse).
            var anchor = mover;
            if (mover.TryGetComponent(out HoleFace face) && face.hole != null && face.hole.part != null) anchor = face.hole.part;
            if (!mechanism.bodyOf.TryGetValue(anchor, out var moving)) {
                if (mover.TryGetComponent(out Pivot pivot)) {
                    var grounded = pivot.objects.FirstOrDefault(o => mechanism.bodyOf.TryGetValue(o, out var b) && b.grounded);
                    if (grounded != null)
                        return new Plan(mover.transform, mechanism, mechanism.bodyOf[grounded]) { refusal = Interface.PartLabels.Short(grounded) + " is grounded: right-click it and Unground it to move it" };
                }
                return null;
            }
            var plan = new Plan(mover.transform, mechanism, moving);
            if (moving.grounded) {
                var part = moving.objects.FirstOrDefault(IsGrounded);
                plan.refusal = (part != null ? Interface.PartLabels.Short(part) : "It") + " is grounded: right-click it and Unground it to move it";
            }
            return plan;
        }
    }
}
