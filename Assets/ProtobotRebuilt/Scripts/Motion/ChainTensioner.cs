using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Motion {
    // V2: a chain tensioner, as teams build them: an idler (a sprocket, or spacers the chain slides round) on an arm
    // that turns on a joint screw, a rubber band pulling the arm so the idler presses into the chain. The chain keeps
    // its own number of links (ChainLoop.fixedLinks), and whenever its sprockets move (an arm the driven sprocket is on
    // swings, a shaft is moved) the tensioner turns about its pivot until the chain is tight again: toward the band's
    // pull to take up slack, back against it when the chain needs more room. In the sandbox the chain limits the arm
    // (Sandbox.MakeTensioners) and the band holds it there.
    public class ChainTensioner {
        public ChainLoop chain;
        public readonly List<int> wraps = new();              // the chain's wraps the arm carries
        public readonly HashSet<GameObject> carried = new();  // everything that turns with the arm
        public readonly List<Transform> roots = new();        // the topmost of those (what's turned)
        public GameObject pivot;                              // the joint screw it turns on
        public GameObject armPart, framePart;                 // a part each side of the pivot (the sandbox's bodies)
        public int pull;                                      // the way the band turns it about the pivot's forward (+1/-1)
        public ElasticBand band;

        public Vector3 Point => pivot.transform.position;
        public Vector3 Axis => pivot.transform.forward;

        private static readonly Dictionary<ChainLoop, (long version, ChainTensioner tensioner)> cache = new();

        /// <summary>The chain's tensioner, or null (kept until the build changes).</summary>
        public static ChainTensioner Of(ChainLoop chain) {
            if (chain == null) return null;
            if (cache.TryGetValue(chain, out var known) && known.version == Dynamics.Version && (known.tensioner == null || known.tensioner.Alive)) return known.tensioner;
            var found = Find(chain.Sprockets(), chain);
            cache[chain] = (Dynamics.Version, found);
            return found;
        }

        private bool Alive => pivot != null && armPart != null && roots.All(r => r != null);

        /// <summary>A tensioner among the parts a chain goes round (each with its wrap), or null.</summary>
        public static ChainTensioner Find(IList<(GameObject part, int wrap)> wrapParts, ChainLoop chain = null) {
            if (wrapParts == null || wrapParts.Count < 3) return null;
            var group = new HashSet<GameObject>();
            foreach (var (part, _) in wrapParts) {
                if (part == null) continue;
                var connected = part.GetConnectedObjects(true);
                if (connected != null) group.UnionWith(connected.Where(o => o != null));
                // A sprocket spinning on a screw is its own group: take what it's on too.
                foreach (var next in GroupManager.Neighbours(part)) {
                    var more = next.GetConnectedObjects(true);
                    if (more != null) group.UnionWith(more.Where(o => o != null));
                }
            }
            if (chain != null) group.Remove(chain.gameObject);
            var mechanism = Linkage.Analyze(group);
            if (mechanism.pins.Count == 0) return null;
            var wrapOf = new Dictionary<GameObject, int>();
            foreach (var (part, wrap) in wrapParts)
                if (part != null) wrapOf[part] = wrap;

            foreach (var (part, wrap) in wrapParts) {
                if (part == null || !mechanism.bodyOf.TryGetValue(part, out var own)) continue;
                var axle = ChainLoop.SprocketFrame(part);
                // The arm is the part's own body, or the body it spins on (a sprocket on a screw through the arm).
                var candidates = new List<Linkage.Body> { own };
                foreach (var pin in mechanism.pins)
                    if ((pin.a == own || pin.b == own) && OnLine(axle.center, pin)) candidates.Add(pin.Other(own));
                foreach (var arm in candidates) {
                    var tensioner = Try(mechanism, arm, wrapParts, wrapOf);
                    if (tensioner == null) continue;
                    tensioner.chain = chain;
                    return tensioner;
                }
            }
            return null;
        }

        private static bool OnLine(Vector3 point, Linkage.Pin pin) => Vector3.ProjectOnPlane(point - pin.point, pin.axis).magnitude < 0.1f;

        private static ChainTensioner Try(Linkage.Mechanism mechanism, Linkage.Body arm, IList<(GameObject part, int wrap)> wrapParts, Dictionary<GameObject, int> wrapOf) {
            if (arm.grounded) return null;
            var pins = mechanism.pins.Where(p => p.a == arm || p.b == arm).ToList();
            Linkage.Pin pivot = null;
            var carriedBodies = new List<Linkage.Body> { arm };
            foreach (var pin in pins) {
                var other = pin.Other(arm);
                // A leaf: a small body on this pin only (an idler sprocket spinning on its screw, a washer) turns with the arm.
                var otherPins = mechanism.pins.Count(p => p.a == other || p.b == other);
                var wrapsThere = other.objects.Where(wrapOf.ContainsKey).ToList();
                var leaf = otherPins == 1 && !other.grounded && other.objects.Count <= 6
                           && wrapsThere.All(w => OnLine(ChainLoop.SprocketFrame(w).center, pin));
                if (leaf) {
                    carriedBodies.Add(other);
                    continue;
                }
                if (pivot != null) return null;   // turns on two things: not a tensioner arm
                pivot = pin;
            }
            if (pivot == null) return null;
            var carried = new HashSet<GameObject>(carriedBodies.SelectMany(b => b.objects).Where(o => o != null && o.GetComponent<ChainLoop>() == null));
            var carriedWraps = wrapParts.Where(w => w.part != null && carried.Contains(w.part)).Select(w => w.wrap).ToList();
            if (carriedWraps.Count == 0 || carriedWraps.Count >= wrapParts.Count) return null;
            // Spinning on the pivot isn't swinging on it.
            foreach (var (part, wrap) in wrapParts)
                if (carriedWraps.Contains(wrap) && OnLine(ChainLoop.SprocketFrame(part).center, pivot)) return null;
            // The joint screw itself.
            var screw = mechanism.bodyOf.Keys.Where(o => o.GetComponent<Screw>() != null && Linkage.IsJoint(o))
                                 .OrderBy(o => (o.transform.position - pivot.point).sqrMagnitude).FirstOrDefault();
            if (screw == null || Vector3.ProjectOnPlane(screw.transform.position - pivot.point, pivot.axis).magnitude > 0.05f) return null;
            // A band pulling the arm: a post on it and one off it, turning it about the pivot.
            ElasticBand pulling = null;
            var torque = 0f;
            foreach (var band in ElasticBand.All) {
                if (band == null || band.gameObject.IsDeleted() || band.posts.Count < 2) continue;
                var n = band.posts.Count;
                if (!band.posts.Any(p => p.part != null && carried.Contains(p.part)) || band.posts.All(p => p.part != null && carried.Contains(p.part))) continue;
                var t = 0f;
                for (var i = 0; i < n; i++) {
                    var post = band.posts[i];
                    if (post.part == null || !carried.Contains(post.part)) continue;
                    var at = post.Center;
                    var prev = band.posts[(i + n - 1) % n];
                    var next = band.posts[(i + 1) % n];
                    var force = Vector3.zero;
                    if (prev.part == null || !carried.Contains(prev.part)) force += (prev.Center - at).normalized;
                    if (next.part == null || !carried.Contains(next.part)) force += (next.Center - at).normalized;
                    t += Vector3.Dot(Vector3.Cross(at - pivot.point, force), screw.transform.forward);
                }
                if (Mathf.Abs(t) > Mathf.Abs(torque)) {
                    torque = t;
                    pulling = band;
                }
            }
            if (pulling == null || Mathf.Abs(torque) < 0.01f) return null;
            var frameBody = pivot.Other(arm);
            var tensioner = new ChainTensioner {
                pivot = screw, pull = torque > 0f ? 1 : -1, band = pulling,
                armPart = Biggest(arm.objects), framePart = Biggest(frameBody.objects)
            };
            tensioner.wraps.AddRange(carriedWraps);
            tensioner.carried.UnionWith(carried);
            foreach (var obj in carried) {
                var nested = false;
                for (var p = obj.transform.parent; p != null; p = p.parent)
                    if (carried.Contains(p.gameObject)) {
                        nested = true;
                        break;
                    }
                if (!nested) tensioner.roots.Add(obj.transform);
            }
            return tensioner.armPart != null && tensioner.framePart != null ? tensioner : null;
        }

        // The body's main part (its sandbox body is found by it): the biggest that isn't a screw, nut or shaft.
        private static GameObject Biggest(IEnumerable<GameObject> objects) {
            GameObject best = null;
            var size = -1f;
            foreach (var o in objects) {
                if (o == null || o.GetComponent<SavedObject>() == null || o.GetComponent<ChainLoop>() != null) continue;
                var id = o.GetComponent<SavedObject>().id ?? "";
                var minor = o.GetComponent<Screw>() != null || id.StartsWith("NUT") || id.StartsWith("SHFT") || id.StartsWith("SPCR") || id.StartsWith("VXSPACERS");
                var s = o.TryGetComponent(out Renderer r) ? r.bounds.size.sqrMagnitude : 0f;
                if (minor) s *= 0.001f;
                if (s > size) {
                    size = s;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>Whether it's being dragged (in the Move/Transform tools' pivot): it isn't turned under the mouse.</summary>
        public bool BeingMoved() => roots.Any(r => r != null && r.GetComponentInParent<Pivot>() != null);

        // ------------------------------------------------------------------ solving

        /// <summary>The chain's length (its links) less the loop's round the wraps with the arm turned by an angle
        /// (degrees about its pivot's forward): positive while the chain is slack there.</summary>
        public static float Slack(ChainLoop chain, IList<ChainWrap> wraps, IList<int> carriedWraps, System.Func<int, Vector3> worldCenter,
                                  Vector3 point, Vector3 axis, float degrees, List<ChainWrap> scratch) {
            scratch.Clear();
            var turn = Quaternion.AngleAxis(degrees, axis);
            for (var i = 0; i < wraps.Count; i++) {
                var w = wraps[i].Copy();
                if (carriedWraps.Contains(i)) {
                    var world = point + turn * (worldCenter(i) - point);
                    w.center = chain.ToFlat(world);
                }
                scratch.Add(w);
            }
            var tight = ChainPath.Tight(scratch);
            return float.IsNaN(tight) ? float.NaN : chain.fixedLinks * chain.family.pitch - tight;
        }

        /// <summary>The turn (degrees) that makes the chain tight: from where it is, toward the band's pull while it's
        /// slack (or the idler is off the chain), against it while it's too tight. When the chain can't be made tight
        /// that way (the tensioner can't reach), the turn that comes closest (false: its slack is in <paramref name="left"/>).</summary>
        public static bool SolveAngle(System.Func<float, float> slackAt, int pull, out float angle) => SolveAngle(slackAt, pull, out angle, out _);

        public static bool SolveAngle(System.Func<float, float> slackAt, int pull, out float angle, out float left) {
            angle = 0f;
            left = float.NaN;
            var f0 = slackAt(0f);
            if (float.IsNaN(f0)) {
                // Where it is the chain can't go round it (a sprocket moved away and left the idler off the chain): from
                // the nearest turn either way where it can.
                for (var k = 1; k <= 360; k++) {
                    foreach (var a in new[] { pull * k * 0.25f, -pull * k * 0.25f }) {
                        if (float.IsNaN(slackAt(a))) continue;
                        var found = SolveFrom(slackAt, pull, a, out angle, out left);
                        return found;
                    }
                }
                return false;
            }
            return SolveFrom(slackAt, pull, 0f, out angle, out left);
        }

        private static bool SolveFrom(System.Func<float, float> slackAtAbsolute, int pull, float start, out float angle, out float left) {
            float slackAt(float a) => slackAtAbsolute(start + a);
            var found = SolveValid(slackAt, pull, out angle, out left);
            angle += start;
            return found;
        }

        private static bool SolveValid(System.Func<float, float> slackAt, int pull, out float angle, out float left) {
            angle = 0f;
            left = float.NaN;
            var f0 = slackAt(0f);
            if (!float.IsNaN(f0) && Mathf.Abs(f0) < 0.0005f) {
                left = f0;
                return true;
            }
            // Slack (or off the chain): the band takes it up; too tight: the chain pushes the arm back.
            var dir = float.IsNaN(f0) || f0 > 0f ? pull : -pull;
            const float Step = 0.5f;
            var lastA = 0f;
            var lastF = f0;
            var bestA = float.NaN;
            var bestF = float.NaN;
            if (!float.IsNaN(f0)) {
                bestA = 0f;
                bestF = f0;
            }
            for (var k = 1; k <= 300; k++) {
                var a = dir * k * Step;
                var f = slackAt(a);
                if (!float.IsNaN(f)) {
                    if (!float.IsNaN(lastF) && Mathf.Sign(f) != Mathf.Sign(lastF)) {
                        float lo = lastA, hi = a, flo = lastF;
                        for (var i = 0; i < 30; i++) {
                            var mid = (lo + hi) / 2f;
                            var fm = slackAt(mid);
                            if (float.IsNaN(fm)) break;
                            if (Mathf.Sign(fm) == Mathf.Sign(flo)) {
                                lo = mid;
                                flo = fm;
                            }
                            else hi = mid;
                        }
                        angle = (lo + hi) / 2f;
                        left = slackAt(angle);
                        return true;
                    }
                    if (float.IsNaN(bestF) || Mathf.Abs(f) < Mathf.Abs(bestF)) {
                        bestA = a;
                        bestF = f;
                    }
                }
                else if (!float.IsNaN(lastF)) break;   // past where the chain can go: no further
                lastA = a;
                lastF = f;
            }
            if (float.IsNaN(bestF)) return false;
            angle = bestA;
            left = bestF;
            return false;
        }

        private static readonly List<ChainWrap> scratch = new();

        /// <summary>Turns the tensioner until its chain is tight (in the build). True when it moved.</summary>
        public static bool Settle(ChainTensioner t, out string problem) {
            problem = null;
            var chain = t.chain;
            if (chain == null || t.pivot == null || chain.fixedLinks <= 0) return false;
            var point = t.Point;
            var axis = t.Axis;
            Vector3 World(int i) => chain.transform.TransformPoint(chain.wraps[i].center);
            float SlackAt(float a) => Slack(chain, chain.wraps, t.wraps, World, point, axis, a, scratch);
            var tight = SolveAngle(SlackAt, t.pull, out var angle, out var left);
            if (!tight) {
                problem = float.IsNaN(left) ? null
                    : left > 0f ? "The tensioner can't take up all the chain's slack (" + Inch.Format(left) + " left): take a link out, or move a sprocket"
                    : "The chain is " + Inch.Format(-left) + " too short for its sprockets where they are: the tensioner can't let out any more";
                if (float.IsNaN(left)) return false;
            }
            if (Mathf.Abs(angle) < 1e-4f) return false;
            foreach (var root in t.roots)
                if (root != null) root.RotateAround(point, axis, angle);
            return true;
        }

        /// <summary>The tensioner for a chain just made (or changed): it keeps the links it has now and settles.</summary>
        public static void Begin(ChainLoop chain) {
            if (chain == null || chain.Path == null || !chain.Path.Valid) return;
            cache.Remove(chain);
            var t = Find(chain.Sprockets(), chain);
            if (t == null) {
                if (chain.fixedLinks > 0) {
                    chain.fixedLinks = 0;
                    chain.Save();
                }
                return;
            }
            chain.fixedLinks = chain.Path.links;
            chain.Save();
        }
    }
}
