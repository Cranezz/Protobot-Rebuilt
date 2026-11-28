using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Motion {
    // V2 (build 88, string phase 2: Docs/String concept.md): string can't stretch. Like a chain (ChainStop), every point
    // of a string that moves relative to its frame (the body most of its points are on, or the world) is held by a joint
    // to that frame: a distance limit from the point to a spot far back along the way that lengthens the string, moved
    // every step so its edge is where the whole path (round its pulleys and posts, as StringPath lays it) would be
    // exactly the string's length; the slack is shared by how fast each point lengthens it. A slack string pulls
    // nothing; a tight one stops its points going further, and PhysX puts the reaction on the frame. A pulley that
    // moves with two strands round it lengthens the string twice as fast as a tied end, so it takes twice the pull:
    // block and tackle comes out of it. The pull (tension) is the joint's force over that rate; past the string's
    // rating for a few steps it breaks.
    public partial class Sandbox {
        private class SimString {
            public StringLine line;
            public Rigidbody frame;   // null: the world
            public readonly List<GameObject> parts = new();
            public readonly List<Vector3> buildCenters = new(), buildAxes = new();
            public readonly List<(Vector3 center, Vector3 axis)> world = new();
            public readonly List<(int node, ConfigurableJoint joint, Vector3 anchorLocal)> moving = new();
            public float target, slack;
            public float tension, peak;   // lbf: now (smoothed a little for the label), the most in this run
            public int over;              // steps in a row past its rating
            public bool broken;
            public float brokeAt;
            // V2 (phase 3, winches): an end wound on a drum (its node, its body), how much is on it (inches), the
            // string's whole length (free + wound), and the angle in the drum's own frame where the string last left it.
            public int spool = -1;
            public Rigidbody spoolBody;
            public ConfigurableJoint spoolJoint;
            public float wound, total, spoolPhi = float.NaN, spoolRadius;
            // V2 (phase 4, releases): an end tied round a pin that slides in a hole of another body (a screw or shaft a
            // piston or motor pulls out): where the loop sits (on that other body), and the pin's length either side.
            public readonly List<(int node, GameObject pin, Rigidbody holder, Vector3 loopLocal, float low, float high)> pins = new();
            public int released = -1;
            // V2 (build 88, open chain): the nodes it runs through (the line's own, or a run of an open chain between its
            // end and the motor's sprocket that drives it: `sub`), and the turns on its drum.
            public List<StringNode> nodes;
            public bool sub;
            public SimString partner;   // the open chain's run on the other side of its drive sprocket
            public float Turns => sub ? 0f : line.TurnsNow;
        }

        private readonly List<SimString> strings = new();
        private const float StringReach = 40f;
        // (the limit acts this far before it's reached, so a winch's stall pull doesn't stretch the string first)
        internal static float StringContact = 0.25f;
        /// <summary>V2 (tests): the walls without where the points are going (the phase 2 model).</summary>
        public static bool StringPredictOff;
        // How fast (in/s for each lbf over the weakest-held point) a point held harder than the rest is let go on.
        internal static float EvenUp = 2.5f;
        internal static float MaxGive = 0.03f;
        internal static bool GiveSpool;   // (tests: the drum's end in the give too; it walked a chain lift at its stop)   // (4 shook a cascade carriage; 0.5 lowered it at a fifth of its speed)
        private const int BreakSteps = 6;   // 1/40 s past the rating (a single step's spike isn't a break)

        private void MakeStrings() {
            strings.Clear();
            foreach (var line in StringLine.All) {
                if (line == null || !line.gameObject.activeInHierarchy || line.gameObject.IsDeleted() || line.nodes.Count < 2) continue;
                line.broken = false;
                line.simTurns = -1f;
                var path = StringLine.PathOf(line.nodes, line.kind, line.turns);
                if (!path.Valid) continue;
                // V2 (build 88): an open chain round a sprocket a motor turns can't slide round it: its teeth carry it.
                // It's two runs, each wound on that sprocket at one end (as a winch drum, with plenty on it), so turning
                // the sprocket pays one out as fast as it takes the other in.
                var grip = line.kind.IsChain ? DrivenSprocket(line) : -1;
                if (grip < 0) {
                    MakeString(line, line.nodes, Mathf.Max(line.length, path.tight), path.wound, false);
                    continue;
                }
                var slack = Mathf.Max(0f, line.length - path.tight);
                SimString first = null;
                for (var run = 0; run < 2; run++) {
                    var from = run == 0 ? 0 : grip;
                    var to = run == 0 ? grip : line.nodes.Count - 1;
                    var nodes = new List<StringNode>();
                    for (var i = from; i <= to; i++) {
                        var copy = line.nodes[i].Copy();
                        copy.spool = i == grip;
                        if (i == grip) copy.side = path.ways.Length > grip && path.ways[grip] != 0 ? path.ways[grip] : 1;
                        nodes.Add(copy);
                    }
                    var part = StringPath.Of(nodes, nodes.Select(n => (n.post.Center, n.post.Axis)).ToList(), 0f, 0f);
                    if (!part.Valid) continue;
                    const float Reservoir = 1000f;   // (inches "on" the sprocket: never runs out)
                    var before = strings.Count;
                    MakeString(line, nodes, part.tight + slack * part.tight / Mathf.Max(path.tight, 1e-3f), Reservoir, true);
                    if (strings.Count == before) continue;
                    if (first == null) first = strings[^1];
                    else (first.partner, strings[^1].partner) = (strings[^1], first);
                }
            }
        }

        // V2 (build 88): an open chain run pulled past its length (its stage held by a hard stop while the motor's
        // sprocket still turned: the walls only stop more, they never pull back) while the run on the sprocket's other
        // side is slack skips teeth: the difference goes round the sprocket. Without it the stretch stayed and held the
        // stage 0.7" up when it came back down.
        internal static float SkipAt = 0.2f;   // (0.03 let a motor at a hard stop slip its chain at 5 lbf; 0.2 holds 8-17)

        private void SkipTeeth() {
            foreach (var st in strings) {
                var other = st.partner;
                if (other == null || st.broken || other.broken || st.slack > -SkipAt || other.slack < SkipAt) continue;
                var d = Mathf.Min(-st.slack, other.slack);
                st.total += d;
                other.total -= d;
                st.slack += d;
                other.slack -= d;
            }
        }

        // The first sprocket in the middle of an open chain that a motor's gear train turns (-1: none).
        private int DrivenSprocket(StringLine line) {
            for (var i = 1; i < line.nodes.Count - 1; i++) {
                var part = line.nodes[i].post.part;
                if (part == null || !line.nodes[i].Round || ChainFamily.ForSprocket(part) == null || !bodyOf.TryGetValue(part, out var body)) continue;
                foreach (var train in trains)
                    if (train.motors.Count > 0 && train.rotors.Any(r => r.rb == body.rb)) return i;
            }
            return -1;
        }

        private void MakeString(StringLine line, List<StringNode> nodeList, float target, float wound, bool sub) {
            {
                var st = new SimString { line = line, nodes = nodeList, sub = sub, target = target };
                // A winch: an end wound on a round post (its drum) turns the string on and off it.
                for (var e = 0; e < 2; e++) {
                    var end = e == 0 ? 0 : nodeList.Count - 1;
                    if (nodeList[end].spool && nodeList[end].Round) st.spool = end;
                }
                st.wound = wound;
                st.total = st.target + st.wound;
                if (st.spool >= 0) st.spoolRadius = nodeList[st.spool].post.radius + line.kind.PathRadius;
                var bodies = new List<Rigidbody>();
                foreach (var node in nodeList) {
                    var part = node.post.part;
                    Rigidbody rb = null;
                    if (part != null && bodyOf.TryGetValue(part, out var body)) rb = body.rb;
                    st.parts.Add(rb != null ? part : null);   // a part that isn't simulated holds its point where it is
                    st.buildCenters.Add(node.post.Center);
                    st.buildAxes.Add(node.post.Axis);
                    bodies.Add(rb);
                }
                // Its frame: the world when a point is fixed there, else the body most points are on (the heavier).
                // (never the drum's body: its rim is what winds the string, so it moves against the frame)
                Rigidbody frame = null;
                if (st.spool >= 0) st.spoolBody = bodies[st.spool];
                if (!bodies.Contains(null)) {
                    // V2 (build 88): a pulley or sprocket counts as the body it turns on (an idler at the top of a chain
                    // lift's tower is the tower's: with the lift's stage taken for the frame, the drive sprocket's stop and
                    // the idler's asked for opposite things and the stage couldn't move)
                    var count = new Dictionary<Rigidbody, int>();
                    for (var i = 0; i < bodies.Count; i++) {
                        var rb = TurnsOn(bodies[i], st.buildCenters[i]) ?? bodies[i];
                        count[rb] = count.TryGetValue(rb, out var c) ? c + 1 : 1;
                    }
                    foreach (var (rb, c) in count) {
                        if (rb == st.spoolBody && count.Count > 1) continue;
                        if (frame == null || c > count[frame] || (c == count[frame] && rb.mass > frame.mass)) frame = rb;
                    }
                    if (frame == st.spoolBody) st.spoolBody = null;   // every point on the drum: nothing to wind against
                }
                st.frame = frame;
                for (var i = 0; i < bodies.Count; i++) {
                    var rb = bodies[i];
                    if (rb == null || rb == frame || i == st.spool) continue;   // (the drum's end: its own joint below)
                    var center = st.buildCenters[i];
                    if (frame != null && OnHingeAxis(rb, frame, center)) continue;   // a pulley turning on the frame: its middle stays
                    var joint = StopJoint(rb, frame, center);
                    st.moving.Add((i, joint, joint.anchor));
                }
                if (st.spoolBody != null) st.spoolJoint = StopJoint(st.spoolBody, frame, st.buildCenters[st.spool]);
                if (st.moving.Count == 0 && st.spoolJoint == null) return;   // every point on one body (or turning on it): it holds nothing
                FindPins(st, bodies);
                strings.Add(st);
                StringStop(st);
            }
        }

        // The body a body turns on (a hinge between them whose axis goes through the point), or null.
        private Rigidbody TurnsOn(Rigidbody body, Vector3 point) {
            if (body == null) return null;
            foreach (var joint in FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) {
                var owner = joint.GetComponent<Rigidbody>();
                Rigidbody other = owner == body ? joint.connectedBody : joint.connectedBody == body ? owner : null;
                if (other == null || other == body) continue;
                if (OnHingeAxis(body, other, point)) return other;
            }
            return null;
        }

        // V2 (phase 4): ends tied round a pin through a hole in another body: the loop stays with that body (the pin
        // slides through it), and comes off when the pin's end passes it.
        private void FindPins(SimString st, List<Rigidbody> bodies) {
            foreach (var end in new[] { 0, st.nodes.Count - 1 }) {
                var node = st.nodes[end];
                var pin = node.post.part;
                var pinBody = bodies[end];
                if (end == st.spool || !node.Round || pin == null || pinBody == null) continue;
                var center = node.post.Center;
                var axis = node.post.Axis.normalized;
                // The pin's length either side of the loop, from its mesh.
                float low = 0f, high = 0f;
                if (pin.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) {
                    var b = filter.sharedMesh.bounds;
                    low = float.MaxValue;
                    high = float.MinValue;
                    for (var k = 0; k < 8; k++) {
                        var corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                        var along = Vector3.Dot(pin.transform.TransformPoint(corner) - center, axis);
                        low = Mathf.Min(low, along);
                        high = Mathf.Max(high, along);
                    }
                }
                if (high - low < 0.05f) continue;
                // V2 (build 88): a hole the pin goes through now (a hole in line with it but past its end, like a
                // tower's behind a standoff, holds nothing)
                Rigidbody holder = null;
                var nearest = 3f;
                foreach (var (part, body) in bodyOf) {
                    if (part == null || body.rb == null || body.rb == pinBody || part.IsDeleted()) continue;
                    foreach (var hole in part.GetComponentsInChildren<HoleCollider>(true)) {
                        var h = hole.transform;
                        if (Vector3.Cross(h.forward, axis).magnitude > 0.1f) continue;
                        var off = h.position - center;
                        var along = Vector3.Dot(off, axis);
                        if ((off - along * axis).magnitude > 0.08f || Mathf.Abs(along) >= nearest) continue;
                        if (along < low - 0.05f || along > high + 0.05f) continue;   // (one at its end counts: a pin set in it)
                        nearest = Mathf.Abs(along);
                        holder = body.rb;
                    }
                }
                if (holder == null) continue;
                st.pins.Add((end, pin, holder, holder.transform.InverseTransformPoint(center), low, high));
            }
        }

        // Whether a pin has slid out of its string's loop.
        private bool PinOut(SimString st, out int node) {
            node = -1;
            foreach (var (i, pin, holder, loopLocal, low, high) in st.pins) {
                if (pin == null || holder == null) continue;
                var center = SimPoint(pin, st.buildCenters[i]);
                var axis = SimDirection(pin, st.buildAxes[i]).normalized;
                var along = Vector3.Dot(holder.transform.TransformPoint(loopLocal) - center, axis);
                var margin = st.line.kind.DrawRadius;
                if (along >= low - margin && along <= high + margin) continue;
                node = i;
                return true;
            }
            return false;
        }

        // A point's stop: a distance limit to the frame, its wall moved every step (StringStop).
        private static ConfigurableJoint StopJoint(Rigidbody rb, Rigidbody frame, Vector3 center) {
            var joint = rb.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = frame;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = rb.transform.InverseTransformPoint(center);
            joint.connectedAnchor = frame != null ? frame.transform.InverseTransformPoint(center) : center;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.linearLimit = new SoftJointLimit { limit = 1000f };
            joint.enableCollision = true;
            joint.enablePreprocessing = false;
            // (no projection: a winch pulling at its stall moved the point back by teleporting the lighter body, which
            // spun a whole light robot round; the limit's contact distance holds it instead)
            joint.projectionMode = JointProjectionMode.None;
            rb.solverIterations = Mathf.Max(rb.solverIterations, 40);
            rb.solverVelocityIterations = Mathf.Max(rb.solverVelocityIterations, 12);
            return joint;
        }

        private float StringLength(SimString st) {
            var path = StringPath.Of(st.nodes, st.world, st.line.kind.PathRadius, st.Turns);
            return path.Valid ? path.tight : float.NaN;
        }

        private void StringStop(SimString st) {
            if (st.broken || st.line == null) return;
            if (st.pins.Count > 0 && PinOut(st, out var off)) {
                Release(st, off);
                return;
            }
            st.world.Clear();
            for (var i = 0; i < st.parts.Count; i++) {
                var part = st.parts[i];
                var axis = st.buildAxes[i];
                st.world.Add((SimPoint(part, st.buildCenters[i]), axis != Vector3.zero ? SimDirection(part, axis) : axis));
            }
            var whole = StringPath.Of(st.nodes, st.world, st.line.kind.PathRadius, st.Turns);
            if (!whole.Valid) return;   // (the walls stay where they were)
            var length = whole.tight;
            // A winch: where the string leaves the drum, and what turning the drum (or the string swinging round it)
            // put on or took off since the last step. The string's whole length stays: the free part is what's left.
            Vector3 leaves = default, toward = default;
            if (st.spool >= 0 && whole.spans.Count > 0) {
                var span = st.spool == 0 ? whole.spans[0] : whole.spans[^1];
                leaves = st.spool == 0 ? span.a : span.b;
                toward = st.spool == 0 ? span.b : span.a;
                if (st.spoolBody != null) {
                    var axis = st.world[st.spool].axis.normalized;
                    var local = st.spoolBody.transform.InverseTransformPoint(leaves) - st.spoolBody.transform.InverseTransformPoint(st.world[st.spool].center);
                    var axisLocal = st.spoolBody.transform.InverseTransformDirection(axis);
                    var u = Vector3.Cross(axisLocal, Mathf.Abs(axisLocal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                    var phi = Mathf.Atan2(Vector3.Dot(local, Vector3.Cross(axisLocal, u)), Vector3.Dot(local, u));
                    if (!float.IsNaN(st.spoolPhi)) {
                        var turned = Mathf.DeltaAngle(st.spoolPhi * Mathf.Rad2Deg, phi * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        var way = whole.ways.Length > st.spool && whole.ways[st.spool] != 0 ? whole.ways[st.spool] : 1;
                        // (the coil lies behind where it leaves: that point moving on round the drum the way it goes
                        // round lengthens the coil at the first point, shortens it at the last: StringPath.Of)
                        st.wound = Mathf.Max(0f, st.wound + st.spoolRadius * turned * (st.spool == 0 ? way : -way));
                    }
                    st.spoolPhi = phi;
                    if (!st.sub) st.line.simTurns = st.spoolRadius > 1e-4f ? st.wound / (2f * Mathf.PI * st.spoolRadius) : 0f;
                }
            }
            st.target = st.total - st.wound;
            st.slack = st.target - length;

            // How fast each moving point lengthens the string (its gradient), and the pull its joint takes now.
            const float H = 0.03f;
            var grads = new Vector3[st.moving.Count];
            var total = 0f;
            var pull = 0f;
            for (var k = 0; k < st.moving.Count; k++) {
                var m = st.moving[k];
                var (c, a) = st.world[m.node];
                var g = Vector3.zero;
                for (var d = 0; d < 3; d++) {
                    var e = Vector3.zero;
                    e[d] = H;
                    st.world[m.node] = (c + e, a);
                    var plus = StringLength(st);
                    st.world[m.node] = (c - e, a);
                    var minus = StringLength(st);
                    g[d] = float.IsNaN(plus) || float.IsNaN(minus) ? 0f : (plus - minus) / (2f * H);
                }
                st.world[m.node] = (c, a);
                grads[k] = g;
                total += g.magnitude;
                if (m.joint != null && g.magnitude > 0.2f) pull = Mathf.Max(pull, m.joint.currentForce.magnitude / g.magnitude);
            }
            // The drum's rim where the string leaves: turning it to wind moves that point away from the string.
            var spoolWay = Vector3.zero;
            if (st.spoolJoint != null && (leaves - toward).sqrMagnitude > 1e-8f) {
                spoolWay = (leaves - toward).normalized;
                st.spoolJoint.anchor = st.spoolBody.transform.InverseTransformPoint(leaves);
                total += 1f;
                // V2 (build 88): wound on a drum or driven by a sprocket, the pull is the drum's end's (what the motor
                // feels); a point pressed against a hard stop reads the stop's push on top of it
                pull = st.spoolJoint.currentForce.magnitude;
            }
            var lbf = pull / (4.448f * Newton);
            if (st.slack > 0.02f) lbf = 0f;   // (a joint's force from the step before the string went slack)
            st.tension = Mathf.Lerp(st.tension, lbf, 0.25f);
            st.peak = Mathf.Max(st.peak, lbf);
            st.over = st.line.kind.Rated && lbf > st.line.kind.strength ? st.over + 1 : 0;
            if (st.over >= BreakSteps) {
                Break(st, lbf);
                return;
            }
            if (total < 1e-4f) return;

            // V2 (build 88): where each point is going: the slack the string will have next step if every point keeps
            // its speed. Each wall is set for that, plus its share of what's left: a point that follows the others (a
            // light carriage pulled down by stage 2 through a cascade) isn't a step behind them, so the one that's
            // driven isn't held back by the string while the other catches up (it was: lowering a cascade lift went at
            // a twentieth of the motor's speed, the motor near its stall).
            var ahead = new float[st.moving.Count];
            var coming = 0f;
            if (!StringPredictOff)
                for (var k = 0; k < st.moving.Count; k++) {
                    var (_, joint, anchorLocal) = st.moving[k];
                    if (joint == null || grads[k].sqrMagnitude < 1e-8f || !joint.TryGetComponent(out Rigidbody rb)) continue;
                    var at = joint.transform.TransformPoint(anchorLocal);
                    var v = rb.GetPointVelocity(at) - (st.frame != null ? st.frame.GetPointVelocity(at) : Vector3.zero);
                    var lengthens = Vector3.Dot(grads[k], v) * Time.fixedDeltaTime;
                    coming += lengthens;
                    ahead[k] = lengthens / grads[k].magnitude;
                }
            var step = (st.slack - coming) / total;   // each moves this far along its way (beyond where it's going) to make the string exactly its length
            // V2 (build 88): a string's pull is the same all along it. A point held much harder than the weakest-held one
            // (stage 2 driven down by its winch against a cascade rope whose carriage needs a pound to follow) is given
            // room along its way, and the others take that up, in proportion to the difference: the walls drift until
            // every point takes the same pull, as the real string's would.
            // (the drum's or drive sprocket's end takes part too, as the last one: a stage stopped by a hard stop while
            // its chain's sprocket still turned took half of each step's stretch it couldn't move, and the chain
            // stretched on at many times the motor's pull)
            // (only a chain run's drive sprocket: it feeds the chain both ways; a winch drum pressed by a stage into its
            // bottom stop took the cascade rope's give and pulled the stage past its stop)
            var n = st.moving.Count + (spoolWay != Vector3.zero && GiveSpool && st.sub ? 1 : 0);
            var give = new float[n + 1];
            if (!StringPredictOff && n > 1) {
                var least = float.MaxValue;
                var pulls = new float[n];
                var gs = new float[n];
                for (var k = 0; k < n; k++) {
                    var joint = k < st.moving.Count ? st.moving[k].joint : st.spoolJoint;
                    gs[k] = k < st.moving.Count ? grads[k].magnitude : 1f;
                    pulls[k] = joint != null && gs[k] > 0.2f ? joint.currentForce.magnitude / gs[k] : float.NaN;
                    if (!float.IsNaN(pulls[k])) least = Mathf.Min(least, pulls[k]);
                }
                if (least < float.MaxValue) {
                    float weighted = 0f, sum = 0f;
                    for (var k = 0; k < n; k++) {
                        if (float.IsNaN(pulls[k]) || gs[k] < 1e-4f) continue;
                        // (only a point held well past the others: the small differences of a string at rest moved the
                        // walls about and the lift walked)
                        var over = pulls[k] - 1.5f * least - 4.448f * Newton;
                        // (at most 0.03" a step: bigger jumps yanked a light pulley's body off its axle)
                        give[k] = over > 0f ? Mathf.Min(MaxGive, EvenUp * over / (4.448f * Newton) * Time.fixedDeltaTime) : 0f;
                        // The others take up only what it really moved into its room last step: a stage held still by a
                        // stop of its own (on the end of its slide, its winch at stall) isn't driven, and taking up its
                        // whole room dragged the carriage down into its bracket until the rope broke.
                        var used = k < st.moving.Count ? Mathf.Clamp(ahead[k], 0f, give[k]) : give[k];
                        weighted += used * gs[k];
                        sum += gs[k];
                    }
                    if (sum > 1e-4f)
                        for (var k = 0; k < n; k++)
                            if (!float.IsNaN(pulls[k])) give[k] -= weighted / sum;
                }
            }
            if (spoolWay != Vector3.zero) {
                var wall = leaves + spoolWay * (step + give[st.moving.Count]);
                var back = wall - spoolWay * StringReach;
                st.spoolJoint.connectedAnchor = st.frame != null ? st.frame.transform.InverseTransformPoint(back) : back;
                st.spoolJoint.linearLimit = new SoftJointLimit { limit = StringReach, contactDistance = StringContact };
            }
            for (var k = 0; k < st.moving.Count; k++) {
                var (_, joint, anchorLocal) = st.moving[k];
                if (joint == null || grads[k].sqrMagnitude < 1e-8f) continue;
                var way = grads[k].normalized;
                var now = joint.transform.TransformPoint(anchorLocal);
                var wall = now + way * (ahead[k] + step + give[k]);
                var back = wall - way * StringReach;
                joint.connectedAnchor = st.frame != null ? st.frame.transform.InverseTransformPoint(back) : back;
                joint.linearLimit = new SoftJointLimit { limit = StringReach, contactDistance = StringContact };
            }
        }

        private void Break(SimString st, float lbf) {
            st.broken = true;
            st.brokeAt = Time.unscaledTime;
            foreach (var m in st.moving) if (m.joint != null) Destroy(m.joint);
            if (st.spoolJoint != null) Destroy(st.spoolJoint);
            st.line.broken = true;
            Audio.Sounds.PlayAt(Audio.Sounds.Air, st.world.Count > 0 ? st.world[0].center : Vector3.zero, 0.5f);
            var name = st.line.kind.shortName;
            if (!name.StartsWith("VEX")) name = name.ToLowerInvariant();
            Interface.Interface.Say("The " + name + " broke: pulled with " + Lbf(lbf) + " lbf, it's rated for " + Lbf(st.line.kind.strength) + " lb", false);
        }

        // V2 (phase 4): the pin it was tied round slid out of its loop: it lets go (no message: that's what it's for).
        private void Release(SimString st, int node) {
            st.broken = true;
            st.released = node;
            st.brokeAt = Time.unscaledTime;
            foreach (var m in st.moving) if (m.joint != null) Destroy(m.joint);
            if (st.spoolJoint != null) Destroy(st.spoolJoint);
            st.line.released = node;
            st.line.broken = true;   // (drawn hanging from its other end: StringLine.LooseEnd)
            Audio.Sounds.PlayAt(Audio.Sounds.Air, node >= 0 && st.world.Count > node ? st.world[node].center : Vector3.zero, 0.3f);
        }

        private static string Lbf(float lbf) => lbf.ToString(lbf < 10f ? "0.#" : "0", CultureInfo.InvariantCulture);

        // The pull on a string, by it: shown while the mouse is on the string, when it's near its rating, and for a
        // moment after it breaks.
        private void DrawStrings(bool begun) {
            var view = UnityEngine.Camera.main;
            if (view == null) return;
            var mouse = (Vector2)MouseInput.Position;
            var labelled = new HashSet<StringLine>();
            foreach (var each in strings) {
                if (each.line == null || !labelled.Add(each.line)) continue;
                var st = each;
                foreach (var other in strings) if (other.line == each.line && other.tension > st.tension) st = other;
                var line = st.line.Line;
                if (line == null || line.Count < 2) continue;
                if (st.released >= 0) continue;   // (let go of its pin: nothing to say)
                var recent = st.broken && Time.unscaledTime - st.brokeAt < 3f;
                if (st.broken && !recent) continue;
                var near = !st.broken && st.line.kind.Rated && st.tension >= 0.75f * st.line.kind.strength;
                if (!near && !recent && !(MouseNearLine(view, line, mouse) && !MouseInput.overUI)) continue;
                if (!begun) Overlay.Begin();
                begun = true;
                var at = st.broken ? line[0] : PointAlong(line, 0.5f);
                if (!Overlay.ToScreen(at, out var screen)) continue;
                var strength = st.line.kind.strength;
                var text = st.broken ? "Broke at " + Lbf(st.peak) + " lbf"
                    : st.slack > 0.05f ? Inch.Format(Mathf.Round(st.slack * 8f) / 8f) + " slack"
                    : Lbf(st.tension) + " lbf" + (st.line.kind.Rated ? " (rated " + Lbf(strength) + ")" : "");
                var color = st.broken || (st.line.kind.Rated && st.tension > strength) ? Theme.Danger : near ? Theme.Warning : Theme.Accent;
                Overlay.Text(screen + new Vector2(0f, 22f), text, color, null, true);
            }
        }

        private static bool MouseNearLine(UnityEngine.Camera view, List<Vector3> line, Vector2 mouse) {
            var limit = 12f * Mathf.Max(1f, Screen.dpi / 96f);
            for (var i = 0; i + 1 < line.Count; i++) {
                var a = view.WorldToScreenPoint(line[i]);
                var b = view.WorldToScreenPoint(line[i + 1]);
                if (a.z <= 0f || b.z <= 0f) continue;
                if (Overlay.DistanceToSegment(mouse, a, b) < limit) return true;
            }
            return false;
        }

        private static Vector3 PointAlong(List<Vector3> line, float t) {
            var total = 0f;
            for (var i = 0; i + 1 < line.Count; i++) total += Vector3.Distance(line[i], line[i + 1]);
            var want = total * t;
            for (var i = 0; i + 1 < line.Count; i++) {
                var d = Vector3.Distance(line[i], line[i + 1]);
                if (want <= d) return Vector3.Lerp(line[i], line[i + 1], d > 1e-6f ? want / d : 0f);
                want -= d;
            }
            return line[^1];
        }

        /// <summary>V2 (tests): each string the simulation holds: its slack (inches), pull now and its most (lbf),
        /// whether it broke and how many of its points move on joints.</summary>
        public static List<(StringLine line, float slack, float tension, float peak, bool broken, int moving)> Strings() {
            var list = new List<(StringLine, float, float, float, bool, int)>();
            if (instance != null) foreach (var st in instance.strings) list.Add((st.line, st.slack, st.tension, st.peak, st.broken, st.moving.Count + (st.spoolJoint != null ? 1 : 0)));
            return list;
        }

        /// <summary>V2 (tests): inches wound on a string's drum now (-1: it has none).</summary>
        /// <summary>V2 (tests): a string's pins (ends tied round a pin that can slide out) and the end that let go.</summary>
        public static (int pins, int released) Pins(StringLine line) {
            if (instance != null) foreach (var st in instance.strings) if (st.line == line) return (st.pins.Count, st.released);
            return (0, -1);
        }

        /// <summary>V2 (tests): each simulated run: its nodes, frame, spool, moving points, wound, target and slack.</summary>
        public static string StringDebug() {
            var sb = new System.Text.StringBuilder();
            if (instance == null) return "";
            foreach (var st in instance.strings) {
                sb.Append(st.line.kind.key + (st.sub ? " run" : "") + " nodes " + st.nodes.Count + " frame " + (st.frame != null ? st.frame.name + " m" + st.frame.mass.ToString("F2") : "world")
                          + " spool " + st.spool + (st.spoolBody != null ? " on " + st.spoolBody.name + " m" + st.spoolBody.mass.ToString("F3") : "") + " side " + (st.spool >= 0 ? st.nodes[st.spool].side : 0)
                          + " moving " + string.Join(",", st.moving.Select(m => m.node + ":" + (m.joint != null ? m.joint.GetComponent<Rigidbody>().mass.ToString("F3") : "-")))
                          + " wound " + st.wound.ToString("F2") + " target " + st.target.ToString("F2") + " slack " + st.slack.ToString("F3"));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static float Wound(StringLine line) {
            if (instance != null) foreach (var st in instance.strings) if (st.line == line) return st.spool >= 0 ? st.wound : -1f;
            return -1f;
        }
    }
}
