using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Motion {
    // V2: the advanced gear kit working (the user's list): bevel gears mesh at right angles, a worm turns its wheel
    // (and a wheel can't turn its worm), a gear drives a rack in a straight line, a lead screw drives its nut along it,
    // a cam pushes its follower, a ratchet's pawl lets its shaft turn one way only. The original's kit parts (MCMS) and
    // VEX-CAD's (VXADVANCEDGEARS) both count. GearTrain links the shafts (bevel, worm); the straight-line movers are
    // Sliders of a DriveSystem, moved by MotorRunner (and the hand crank) in the builder and by the sandbox's trains.
    public enum KitKind { None, Bevel, Worm, WormWheel, LeadScrew, LeadNut, Rack, Cam, Follower, Crank, Ratchet, Pawl, WinchDrum }

    /// <summary>A part moving in a straight line, driven by a shaft: a rack, a lead screw's nut, a cam's follower.</summary>
    public class Slider {
        public KitKind kind;
        public GameObject part;
        public readonly List<GameObject> parts = new();   // it and what's fastened to it
        public Rotor rotor;
        public Vector3 axis;              // the way it moves (world)
        public float inchesPerRadian;     // along axis per radian its rotor turns (rack, nut)
        public float low, high;           // how far it can go from where it is now (low <= 0 <= high)
        public CamProfile cam;            // a follower: where it is comes from the cam's turn
        public Vector3 camPoint;          // the follower's line: through here (world, on the cam's axis), along axis
        public float lateral;             // the line's distance beside the cam's axis
        public float tip;                 // the follower's roller radius
    }

    /// <summary>A cam's outline round its shaft: the farthest out in each degree (in the cam's own space).</summary>
    public class CamProfile {
        public GameObject cam;
        public Vector3 center, axis, u, v;   // the cam's own space: its axis and two directions across it
        public readonly float[] radius = new float[360];

        /// <summary>How far the cam reaches along a world direction (from its axis), on a line `lateral` beside the axis,
        /// for a roller of radius `tip`.</summary>
        public float Reach(Vector3 direction, float lateral, float tip) {
            var t = cam.transform;
            var d = t.InverseTransformDirection(direction).normalized;
            var psi = Mathf.Atan2(Vector3.Dot(d, v), Vector3.Dot(d, u));
            var best = 0f;
            for (var i = 0; i < 360; i++) {
                var r = radius[i];
                if (r <= 0f) continue;
                var phi = i * Mathf.Deg2Rad;
                var along = r * Mathf.Cos(phi - psi);
                var side = r * Mathf.Sin(phi - psi);
                if (along > best && Mathf.Abs(side - lateral) <= tip + 0.08f) best = along;
            }
            return best;
        }
    }

    public static class GearKit {
        // ------------------------------------------------------------------ what a part is

        public static KitKind Kind(GameObject part) {
            if (part == null || !part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id)) return KitKind.None;
            return Kind(saved.id);
        }

        public static KitKind Kind(string id) {
            var u = (id ?? "").ToUpperInvariant();
            if (!(u.StartsWith("MCMS-") || u.StartsWith("VXADVANCEDGEARS-"))) return KitKind.None;
            if (u.Contains("BRACKET")) return KitKind.None;
            if (u.Contains("BEVEL GEAR")) return KitKind.Bevel;
            if (u.Contains("SCREW KIT-WORM GEAR") || u.Contains("LEAD SCREW SEGMENT")) return KitKind.LeadScrew;
            if (u.Contains("WORM NUT") || u.Contains("LEAD SCREW NUT")) return KitKind.LeadNut;
            if (u.Contains("WORM AND WHEEL-WORM") || u.EndsWith("-WORM GEAR")) return KitKind.Worm;
            if (u.Contains("WORM AND WHEEL-GEAR") || u.Contains("WORM WHEEL")) return KitKind.WormWheel;
            if (u.StartsWith("MCMS-RACK") || u.Contains("RACK GEAR")) return KitKind.Rack;
            if (u.Contains("DROP OFF CAM")) return KitKind.Cam;
            if (u.Contains("CAM FOLLOWER")) return KitKind.Follower;
            if (u.Contains("HAND CRANK")) return KitKind.Crank;
            if (u.Contains("RATCHET")) return KitKind.Ratchet;
            if (u.Contains("WINCH PAWL")) return KitKind.Pawl;
            if (u.Contains("WINCH DRUM")) return KitKind.WinchDrum;
            return KitKind.None;
        }

        /// <summary>Kit parts keyed to their shaft (square holes): they turn with it.</summary>
        public static bool TurnsWithShaft(GameObject part) {
            var k = Kind(part);
            return k == KitKind.Bevel || k == KitKind.Worm || k == KitKind.WormWheel || k == KitKind.LeadScrew || k == KitKind.Cam
                   || k == KitKind.Crank || k == KitKind.Ratchet || k == KitKind.WinchDrum;
        }

        /// <summary>Riding on a shaft without holding it (a nut, a follower): not a bearing.</summary>
        public static bool Rides(GameObject part) {
            var k = Kind(part);
            return k == KitKind.LeadNut || k == KitKind.Follower || k == KitKind.Rack;
        }

        /// <summary>A bevel gear's teeth: from its name (VEX-CAD 12T/24T/36T); the original's "16T"/"32T" are the
        /// same size as VEX's 12T and 36T, so by size.</summary>
        public static int BevelTeeth(GameObject part) {
            if (!part.TryGetComponent(out SavedObject saved)) return 0;
            var id = saved.id.ToUpperInvariant();
            if (id.StartsWith("VXADVANCEDGEARS-")) {
                if (id.Contains("12T")) return 12;
                if (id.Contains("24T")) return 24;
                if (id.Contains("36T")) return 36;
            }
            var r = Measure(part).radius;
            return r < 0.45f ? 12 : r < 0.6f ? 24 : 36;
        }

        // ------------------------------------------------------------------ measured once per mesh

        public struct Shape {
            public Vector3 center, axis;   // the middle hole (part's space)
            public float radius;           // outer radius about it
            public float low, high;        // along it
            public Vector3 longAxis;       // the bounds' longest side (part's space)
            public float lead;             // a screw's thread: inches along its axis per radian (signed; 0: none)
        }

        private static readonly Dictionary<Mesh, Shape> shapes = new();

        public static Shape Measure(GameObject part) {
            var filter = part.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return default;
            if (shapes.TryGetValue(mesh, out var known)) return known;
            var b = mesh.bounds;
            var s = new Shape { center = b.center, axis = Vector3.forward };
            HoleCollider middle = null;
            var best = float.MaxValue;
            foreach (var hole in part.GetComponentsInChildren<HoleCollider>(true)) {
                if (hole.transform.parent != part.transform) continue;
                var d = (hole.transform.localPosition - b.center).sqrMagnitude;
                if (d < best) {
                    best = d;
                    middle = hole;
                }
            }
            var size = b.size;
            s.longAxis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            if (middle != null) {
                s.center = middle.transform.localPosition;
                s.axis = (middle.transform.localRotation * Vector3.forward).normalized;
            }
            if (mesh.isReadable) {
                var verts = mesh.vertices;
                float lo = float.MaxValue, hi = float.MinValue, r = 0f;
                foreach (var v in verts) {
                    var d = v - s.center;
                    var a = Vector3.Dot(d, s.axis);
                    lo = Mathf.Min(lo, a);
                    hi = Mathf.Max(hi, a);
                    r = Mathf.Max(r, (d - s.axis * a).magnitude);
                }
                s.low = lo;
                s.high = hi;
                s.radius = r;
                var kind = Kind(part);
                if (kind == KitKind.LeadScrew || kind == KitKind.Worm) s.lead = Helix(verts, s.center, s.axis);
            }
            shapes[mesh] = s;
            return s;
        }

        // A screw's thread: crests along one line on its surface, and a quarter turn round (their shift says which way
        // the thread winds). Inches along the axis per radian of the helix (z = z0 + lead * angle).
        private static float Helix(Vector3[] verts, Vector3 center, Vector3 axis) {
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var w = Vector3.Cross(axis, u);
            List<float> Crests(float at) {
                var points = new List<Vector2>();
                foreach (var v in verts) {
                    var d = v - center;
                    var a = Vector3.Dot(d, axis);
                    var f = d - axis * a;
                    var angle = Mathf.Atan2(Vector3.Dot(f, w), Vector3.Dot(f, u)) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.DeltaAngle(angle, at)) < 4f) points.Add(new Vector2(a, f.magnitude));
                }
                var top = points.Count > 0 ? points.Max(p => p.y) : 0f;
                points.Sort((x, y) => x.x.CompareTo(y.x));
                var crests = new List<float>();
                float sum = 0f;
                var n = 0;
                foreach (var p in points) {
                    if (p.y > top - 0.01f) {
                        sum += p.x;
                        n++;
                    }
                    else if (n > 0) {
                        crests.Add(sum / n);
                        sum = 0f;
                        n = 0;
                    }
                }
                if (n > 0) crests.Add(sum / n);
                return crests;
            }
            var c0 = Crests(0f);
            var c90 = Crests(90f);
            if (c0.Count < 3 || c90.Count < 2) return 0f;
            var gaps = new List<float>();
            for (var i = 1; i < c0.Count; i++) gaps.Add(c0[i] - c0[i - 1]);
            gaps.Sort();
            var pitch = gaps[gaps.Count / 2];
            if (pitch < 0.02f) return 0f;
            // The crest a quarter turn round: the nearest one to a crest at 0, as a shift within half a pitch.
            var mid = c0[c0.Count / 2];
            var shift = c90.OrderBy(c => Mathf.Abs(c - mid)).First() - mid;
            shift -= Mathf.Round(shift / pitch) * pitch;
            var sign = shift < 0f ? -1f : 1f;
            return sign * pitch / (2f * Mathf.PI);
        }

        // ------------------------------------------------------------------ shafts linked (GearTrain.SystemOf)

        /// <summary>Kit gears on a rotor that link it to others: bevels and worms (each with the other rotor's turn per
        /// turn of this one, about its own axis); `backdrive`: this rotor is a worm's wheel driving its worm (it can't).</summary>
        public static IEnumerable<(Rotor other, float ratio, bool backdrive)> Links(Rotor rotor, List<Rotor> all) {
            foreach (var part in rotor.parts) {
                var kind = Kind(part);
                if (kind != KitKind.Bevel && kind != KitKind.Worm && kind != KitKind.WormWheel) continue;
                foreach (var other in all) {
                    if (other == rotor) continue;
                    foreach (var otherPart in other.parts) {
                        var otherKind = Kind(otherPart);
                        if (kind == KitKind.Bevel && otherKind == KitKind.Bevel) {
                            if (BevelRatio(rotor, part, other, otherPart, out var r)) yield return (other, r, false);
                        }
                        else if (kind == KitKind.Worm && otherKind == KitKind.WormWheel) {
                            if (WormRatio(rotor, part, other, otherPart, out var r)) yield return (other, r, false);
                        }
                        else if (kind == KitKind.WormWheel && otherKind == KitKind.Worm) {
                            if (WormRatio(other, otherPart, rotor, part, out var r) && Mathf.Abs(r) > 1e-6f) yield return (other, 1f / r, true);
                        }
                    }
                }
            }
        }

        private static Vector3 WorldCenter(GameObject part, Shape s) => part.transform.TransformPoint(s.center);

        // Two lines' nearest points.
        private static bool Nearest(Vector3 p1, Vector3 d1, Vector3 p2, Vector3 d2, out Vector3 on1, out Vector3 on2) {
            var r = p1 - p2;
            float a = Vector3.Dot(d1, d1), b = Vector3.Dot(d1, d2), c = Vector3.Dot(d2, d2), d = Vector3.Dot(d1, r), e = Vector3.Dot(d2, r);
            var den = a * c - b * b;
            if (Mathf.Abs(den) < 1e-9f) {
                on1 = p1;
                on2 = p2;
                return false;
            }
            var s = (b * e - c * d) / den;
            var t = (a * e - b * d) / den;
            on1 = p1 + d1 * s;
            on2 = p2 + d2 * t;
            return true;
        }

        // Bevels at right angles, their axes meeting, each gear's teeth about the other's radius from where they meet.
        private static bool BevelRatio(Rotor a, GameObject ga, Rotor b, GameObject gb, out float ratio) {
            ratio = 0f;
            if (Mathf.Abs(Vector3.Dot(a.axis, b.axis)) > 0.12f) return false;
            if (!Nearest(a.start, a.axis, b.start, b.axis, out var oa, out var ob) || (oa - ob).magnitude > 0.2f) return false;
            var o = (oa + ob) / 2f;
            var sa = Measure(ga);
            var sb = Measure(gb);
            var ca = WorldCenter(ga, sa);
            var cb = WorldCenter(gb, sb);
            var da = Vector3.Dot(ca - o, a.axis);
            var db = Vector3.Dot(cb - o, b.axis);
            if (Mathf.Abs(Mathf.Abs(da) - sb.radius) > 0.4f || Mathf.Abs(Mathf.Abs(db) - sa.radius) > 0.4f) return false;
            // Where the teeth touch: a's rim toward b, b's rim toward a (the corner of the square o, ca, cb).
            var p = o + a.axis * da + b.axis * db;
            var n = Vector3.Cross(a.axis, b.axis);
            var va = Vector3.Dot(Vector3.Cross(a.axis, p - (o + a.axis * da)), n);
            var vb = Vector3.Dot(Vector3.Cross(b.axis, p - (o + b.axis * db)), n);
            if (Mathf.Abs(va) < 1e-4f || Mathf.Abs(vb) < 1e-4f) return false;
            ratio = Mathf.Sign(va / vb) * BevelTeeth(ga) / (float)BevelTeeth(gb);
            return true;
        }

        // A worm and its wheel: axes square to each other, not meeting, as far apart as the two reach (tooth in thread).
        private static bool WormRatio(Rotor w, GameObject worm, Rotor g, GameObject wheel, out float ratio) {
            ratio = 0f;
            if (Mathf.Abs(Vector3.Dot(w.axis, g.axis)) > 0.12f) return false;
            if (!Nearest(w.start, w.axis, g.start, g.axis, out var qw, out var qg)) return false;
            var sw = Measure(worm);
            var sg = Measure(wheel);
            var d = (qg - qw).magnitude;
            if (d < Mathf.Max(sw.radius, sg.radius) - 0.05f || d > sw.radius + sg.radius + 0.03f) return false;
            // Each on the other's line: the wheel's middle over the worm, the worm in the wheel's plane.
            var cw = WorldCenter(worm, sw);
            var cg = WorldCenter(wheel, sg);
            var wormAxisWorld = worm.transform.TransformDirection(sw.axis).normalized;
            var along = Vector3.Dot(qw - cw, wormAxisWorld);
            if (along < sw.low - 0.05f || along > sw.high + 0.05f) return false;
            if (Mathf.Abs(Vector3.Dot(qg - cg, g.axis)) > Mathf.Max(0.12f, (sg.high - sg.low) / 2f)) return false;
            if (Mathf.Abs(sw.lead) < 1e-5f) return false;
            // The thread carries the tooth in it along the worm's axis: -lead per radian of the worm (its own sense).
            // (the tooth moves -lead x the worm's turn along the worm's own axis; along either way of it that's the same)
            var lead = sw.lead;
            var contact = qw + (qg - qw).normalized * (sw.radius * 0.8f);
            var push = Vector3.Dot(Vector3.Cross(g.axis, contact - qg), w.axis);
            if (Mathf.Abs(push) < 1e-4f) return false;
            ratio = -lead / push;
            return true;
        }

        // ------------------------------------------------------------------ straight-line movers

        /// <summary>The racks, nuts and followers a system drives (its rotors' gears, lead screws and cams).</summary>
        public static List<Slider> Sliders(DriveSystem system) {
            var list = new List<Slider>();
            if (system.rotors.Count == 0) return list;
            var candidates = new List<GameObject>();
            foreach (var saved in Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None)) {
                var k = Kind(saved.gameObject);
                if ((k == KitKind.Rack || k == KitKind.LeadNut || k == KitKind.Follower) && !saved.gameObject.IsDeleted()) candidates.Add(saved.gameObject);
            }
            if (candidates.Count == 0) return list;
            var inSystem = new HashSet<GameObject>(system.rotors.SelectMany(r => r.parts).Where(p => p != null));
            foreach (var part in candidates) {
                if (inSystem.Contains(part)) continue;
                Slider found = null;
                var kind = Kind(part);
                foreach (var rotor in system.rotors) {
                    found = kind == KitKind.Rack ? Rack(rotor, part) : kind == KitKind.LeadNut ? Nut(rotor, part) : Follower(rotor, part);
                    if (found != null) break;
                }
                if (found == null) continue;
                // It and what's screwed to it move; reaching the frame (or the shafts) means it can't.
                var stop = new HashSet<GameObject>(inSystem);
                foreach (var rotor in system.rotors)
                    foreach (var (bearing, _) in rotor.bearings) stop.Add(bearing);
                var moving = Dynamics.Fastened(new[] { part }, stop, o => o.GetComponent<Shaft>() != null, out var frame);
                if (frame) {
                    system.jammed = true;
                    system.jamReason = Interface.PartLabels.Short(part) + " is fastened to the frame, so it can't slide";
                }
                found.parts.AddRange(moving);
                list.Add(found);
            }
            return list;
        }

        // A spur gear on the rotor with the rack's teeth on its pitch circle: the rack slides along its length.
        private static Slider Rack(Rotor rotor, GameObject rack) {
            var rs = Measure(rack);
            var t = rack.transform;
            var length = t.TransformDirection(rs.longAxis).normalized;
            if (Mathf.Abs(Vector3.Dot(length, rotor.axis)) > 0.12f) return null;
            var filter = rack.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return null;
            var verts = filter.sharedMesh.vertices;
            var rackCenter = t.TransformPoint(filter.sharedMesh.bounds.center);
            foreach (var gear in rotor.gears) {
                if (gear.sprocket || gear.teeth <= 0) continue;
                var c = gear.center;
                var toward = Vector3.Cross(rotor.axis, length).normalized;
                if (Vector3.Dot(c - rackCenter, toward) < 0f) toward = -toward;   // from the rack to the gear
                // The rack's tooth tips: its farthest points toward the gear.
                float tips = float.MinValue, lo = float.MaxValue, hi = float.MinValue, thickLo = float.MaxValue, thickHi = float.MinValue;
                foreach (var v in verts) {
                    var w = t.TransformPoint(v) - rackCenter;
                    tips = Mathf.Max(tips, Vector3.Dot(w, toward));
                    var along = Vector3.Dot(w, length);
                    lo = Mathf.Min(lo, along);
                    hi = Mathf.Max(hi, along);
                    var thick = Vector3.Dot(w, rotor.axis);
                    thickLo = Mathf.Min(thickLo, thick);
                    thickHi = Mathf.Max(thickHi, thick);
                }
                var gap = Vector3.Dot(c - rackCenter, toward) - tips;   // gear centre to the tooth tips
                if (Mathf.Abs(gap - (gear.PitchRadius - 1f / 24f)) > 0.08f) continue;
                var plane = Vector3.Dot(c - rackCenter, rotor.axis);
                if (plane < thickLo - 0.2f || plane > thickHi + 0.2f) continue;
                var at = Vector3.Dot(c - rackCenter, length);
                if (at < lo - 0.1f || at > hi + 0.1f) continue;
                var contact = c - toward * gear.PitchRadius;
                var ipr = Vector3.Dot(Vector3.Cross(rotor.axis, contact - c), length);
                // The rack slides until its end reaches the gear (kept a tooth short of it).
                return new Slider {
                    kind = KitKind.Rack, part = rack, rotor = rotor, axis = length, inchesPerRadian = ipr,
                    low = at - hi + 0.1f, high = at - lo - 0.1f,
                };
            }
            return null;
        }

        // A lead screw on the rotor through the nut: the nut goes along it (lead per turn), within the screw.
        private static Slider Nut(Rotor rotor, GameObject nut) {
            var filter = nut.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;
            var ns = Measure(nut);
            var t = nut.transform;
            var bore = t.TransformDirection(ns.longAxis).normalized;
            var center = t.TransformPoint(filter.sharedMesh.bounds.center);
            if (Mathf.Abs(Vector3.Dot(bore, rotor.axis)) < 0.98f) return null;
            if (Vector3.ProjectOnPlane(center - rotor.start, rotor.axis).magnitude > 0.08f) return null;
            var at = Vector3.Dot(center - rotor.start, rotor.axis);
            foreach (var screw in rotor.parts) {
                if (Kind(screw) != KitKind.LeadScrew) continue;
                var ss = Measure(screw);
                if (Mathf.Abs(ss.lead) < 1e-5f) continue;
                var c = Vector3.Dot(WorldCenter(screw, ss) - rotor.start, rotor.axis);
                var screwAxis = screw.transform.TransformDirection(ss.axis).normalized;
                var flip = Mathf.Sign(Vector3.Dot(screwAxis, rotor.axis));
                float lo = c + (flip > 0f ? ss.low : -ss.high), hi = c + (flip > 0f ? ss.high : -ss.low);
                // Lead screw segments butted end to end on one shaft make one long screw.
                foreach (var more in rotor.parts) {
                    if (more == screw || Kind(more) != KitKind.LeadScrew) continue;
                    var ms = Measure(more);
                    var mc = Vector3.Dot(WorldCenter(more, ms) - rotor.start, rotor.axis);
                    var half = (ms.high - ms.low) / 2f;
                    if (mc - half <= hi + 0.05f && mc + half >= lo - 0.05f) {
                        lo = Mathf.Min(lo, mc - half);
                        hi = Mathf.Max(hi, mc + half);
                    }
                }
                if (at < lo - 0.1f || at > hi + 0.1f) continue;
                var half2 = Mathf.Min(0.3f, Vector3.Scale(filter.sharedMesh.bounds.size, Abs(ns.longAxis)).magnitude / 4f);
                // The thread carries the nut: -lead per radian (in the screw's own sense; the same along either way).
                return new Slider {
                    kind = KitKind.LeadNut, part = nut, rotor = rotor, axis = rotor.axis, inchesPerRadian = -ss.lead,
                    low = lo + half2 - at, high = hi - half2 - at,
                };
            }
            return null;
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static readonly Dictionary<Mesh, CamProfile> camShapes = new();

        // A cam on the rotor with the follower's line square to its axis, through the cam's plane, pointing at it.
        private static Slider Follower(Rotor rotor, GameObject follower) {
            var fs = Measure(follower);
            var ft = follower.transform;
            var filter = follower.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;
            var length = ft.TransformDirection(fs.longAxis).normalized;
            if (Mathf.Abs(Vector3.Dot(length, rotor.axis)) > 0.12f) return null;
            var center = ft.TransformPoint(filter.sharedMesh.bounds.center);
            foreach (var cam in rotor.parts) {
                if (Kind(cam) != KitKind.Cam) continue;
                var profile = Profile(cam);
                if (profile == null) continue;
                var cs = Measure(cam);
                var camCenter = WorldCenter(cam, cs);
                var camAxis = cam.transform.TransformDirection(cs.axis).normalized;
                var plane = Vector3.Dot(center - camCenter, camAxis);
                if (plane < cs.low - 0.3f || plane > cs.high + 0.3f) continue;
                // Its line, from the cam's axis toward the follower.
                var onAxis = camCenter + camAxis * plane;
                var out1 = Vector3.Dot(center - onAxis, length) >= 0f ? length : -length;
                var side = Vector3.ProjectOnPlane(center - onAxis, out1);
                var lateral = Vector3.Dot(side, Vector3.Cross(camAxis, out1).normalized);
                if (side.magnitude > cs.radius) continue;
                var halfLength = Vector3.Scale(filter.sharedMesh.bounds.size, Abs(fs.longAxis)).magnitude / 2f;
                var nearEnd = Vector3.Dot(center - onAxis, out1) - halfLength;
                if (nearEnd > cs.radius + 0.6f) continue;
                return new Slider {
                    kind = KitKind.Follower, part = follower, rotor = rotor, axis = out1, cam = profile, camPoint = onAxis,
                    lateral = lateral, tip = 0.15f, low = -10f, high = 10f,
                };
            }
            return null;
        }

        private static CamProfile Profile(GameObject cam) {
            var filter = cam.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return null;
            var mesh = filter.sharedMesh;
            if (camShapes.TryGetValue(mesh, out var known)) return Copy(known, cam);
            var s = Measure(cam);
            var p = new CamProfile { cam = cam, center = s.center, axis = s.axis };
            p.u = Vector3.Cross(s.axis, Mathf.Abs(s.axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            p.v = Vector3.Cross(s.axis, p.u);
            foreach (var vert in mesh.vertices) {
                var d = vert - s.center;
                var f = d - s.axis * Vector3.Dot(d, s.axis);
                var angle = Mathf.Atan2(Vector3.Dot(f, p.v), Vector3.Dot(f, p.u)) * Mathf.Rad2Deg;
                var i = ((int)Mathf.Floor(angle + 360f)) % 360;
                p.radius[i] = Mathf.Max(p.radius[i], f.magnitude);
            }
            camShapes[mesh] = p;
            return Copy(p, cam);
        }

        private static CamProfile Copy(CamProfile p, GameObject cam) {
            var copy = new CamProfile { cam = cam, center = p.center, axis = p.axis, u = p.u, v = p.v };
            System.Array.Copy(p.radius, copy.radius, 360);
            return copy;
        }

        /// <summary>Where a follower's line meets its cam now (inches out from the cam's axis).</summary>
        public static float CamReach(Slider s) => s.cam != null && s.cam.cam != null ? s.cam.Reach(s.axis, s.lateral, s.tip) : 0f;

        // ------------------------------------------------------------------ ratchet

        /// <summary>The way a rotor can't turn (+1: not about +axis, -1: not about -axis, 0: free): a ratchet on it
        /// with a pawl, fastened to the frame, resting on its teeth. The pawl is a strut: teeth moving toward its pivot
        /// jam it; moving away they lift it and slide under.</summary>
        public static int Blocked(Rotor rotor) {
            foreach (var ratchet in rotor.parts) {
                if (Kind(ratchet) != KitKind.Ratchet) continue;
                var rs = Measure(ratchet);
                var center = WorldCenter(ratchet, rs);
                foreach (var saved in Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None)) {
                    var pawl = saved.gameObject;
                    if (Kind(pawl) != KitKind.Pawl || pawl.IsDeleted() || rotor.parts.Contains(pawl)) continue;
                    var ps = Measure(pawl);
                    var pivot = WorldCenter(pawl, ps);
                    var filter = pawl.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                    // Its tip: the point farthest from its pivot.
                    var tip = pivot;
                    var far = 0f;
                    foreach (var v in filter.sharedMesh.vertices) {
                        var w = pawl.transform.TransformPoint(v);
                        var d = (w - pivot).sqrMagnitude;
                        if (d > far) {
                            far = d;
                            tip = w;
                        }
                    }
                    var off = tip - center;
                    var axial = Vector3.Dot(off, rotor.axis);
                    var radial = Vector3.ProjectOnPlane(off, rotor.axis).magnitude;
                    if (Mathf.Abs(axial) > 0.35f || Mathf.Abs(radial - rs.radius) > 0.15f) continue;
                    var surface = Vector3.Cross(rotor.axis, tip - center);   // the teeth's way under the tip, turning +
                    return Vector3.Dot(surface, pivot - tip) > 0f ? 1 : -1;
                }
            }
            return 0;
        }
    }
}
