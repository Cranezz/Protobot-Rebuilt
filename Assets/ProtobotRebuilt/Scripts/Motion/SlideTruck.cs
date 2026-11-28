using System.Collections.Generic;
using System.Linq;
using Protobot.Transformations;
using UnityEngine;

namespace Protobot.Motion {
    // V2: VEX's linear slide: the acetal slide truck rides in an outer or inner slide member (a dovetail C channel:
    // widest a little in from its web, narrower at its lips, so the truck is held in by them). From the parts list or the
    // Move tool a truck over a slide member seats in its channel where the mouse is along it (1/16" steps), never past
    // its ends: its widest part at the channel's widest, its mounting face a little proud of the lips (V2 build 88: it
    // was pushed against the web, its sides inside the walls). The original's classic trunks (its linear motion kit)
    // go on the structure rail (V2 build 88: they went on VEX's slide members): the inner trunk in the rail's channel,
    // the outer one wrapped round its outside. A seated truck is held by its member (they move together in the
    // builder) but slides along it in the simulation, with the acetal's light drag, stopping at the member's ends.
    public class SlideTruck : MonoBehaviour {
        public const string TruckId = "VXSLIDEASSEMBLYP-Inner Acetal Slide Truck";

        public const string ClassicInner = "LMTK-Inner Slide Trunk", ClassicOuter = "LMTK-Outer Slide Trunk";
        public static bool IsTruck(string id) => id == TruckId || id == ClassicInner || id == ClassicOuter;
        public static bool IsMember(GameObject part) => IsVexMember(part) || IsRail(part);

        private static string IdOf(GameObject part) => part != null && part.TryGetComponent(out SavedObject saved) ? saved.id : null;

        public static bool IsVexMember(GameObject part) {
            var id = IdOf(part);
            return id != null && (id.EndsWith("-Outer Slide Member") || id.EndsWith("-Inner Slide Member"));
        }

        /// <summary>V2 (build 88): the structure rail the classic trunks ride on (Structure > Rails > Rail; VEX's rail stock
        /// is a closed box, not this channel).</summary>
        public static bool IsRail(GameObject part) {
            var id = IdOf(part);
            return id != null && id.StartsWith("RAIL-");
        }

        /// <summary>Whether a truck of this kind rides on this part: the acetal truck in VEX's slide members, the classic
        /// trunks on the rails.</summary>
        public static bool Fits(GameObject part, string truckId) => truckId == TruckId ? IsVexMember(part) : IsRail(part);

        public GameObject Member { get; private set; }
        private string id => TryGetComponent(out SavedObject saved) ? saved.id : TruckId;
        private Bounds Bounds() => TryGetComponent(out MeshFilter f) && f.sharedMesh != null ? f.sharedMesh.bounds : new Bounds();
        private long seenVersion = -1;

        // ------------------------------------------------------------------ the channel, from the member's model

        /// <summary>A member's cross-section in its own space: its length along x; the way its channel opens (an axis,
        /// y or z, and a sign); its middle across; and, measured along the opening way (t), its web's inside and
        /// outside faces and the depth where the channel is widest inside.</summary>
        private class Frame {
            public int open = 2, across = 1;
            public float sign = -1f, middle, webInner, webOuter, widest;
            public float legacyCenterY, legacyWeb;   // (the seat before build 88, for trucks saved there)

            public Vector3 Point(float x, float t) {
                var p = Vector3.zero;
                p.x = x;
                p[across] = middle;
                p[open] = sign * t;
                return p;
            }

            public Vector3 OpenDir {
                get {
                    var d = Vector3.zero;
                    d[open] = sign;
                    return d;
                }
            }
        }

        private static readonly Dictionary<Mesh, Frame> frames = new();

        private static Frame FrameOf(Mesh mesh) {
            if (frames.TryGetValue(mesh, out var known)) return known;
            var b = mesh.bounds;
            var probe = new GameObject("Slide Probe") { hideFlags = HideFlags.HideAndDontSave };
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            float Depth(Vector3 from, Vector3 dir, float max) => collider.Raycast(new Ray(from, dir), out var h, max) ? h.distance : float.NaN;
            // Sampled at several places along it (a ray down a hole finds nothing, or the far side): the usual depth
            // from each side, and a place where the web is solid.
            var samples = Enumerable.Range(0, 13).Select(i => b.center.x - 0.75f + i * 0.125f).ToList();
            var frame = new Frame();
            var best = -1f;
            var x = b.center.x;
            foreach (var axis in new[] { 1, 2 }) {
                var other = axis == 1 ? 2 : 1;
                foreach (var sign in new[] { 1f, -1f }) {
                    // From outside the open side, the way in: how far it goes before meeting the web.
                    var depths = new List<(float x, float d)>();
                    foreach (var sx in samples) {
                        var from = Vector3.zero;
                        from.x = sx;
                        from[other] = b.center[other];
                        from[axis] = sign > 0f ? b.max[axis] + 1f : b.min[axis] - 1f;
                        var dir = Vector3.zero;
                        dir[axis] = -sign;
                        var d = Depth(from, dir, b.size[axis] + 2f) - 1f;
                        if (!float.IsNaN(d)) depths.Add((sx, d));
                    }
                    if (depths.Count < 3) continue;
                    var sorted = depths.OrderBy(v => v.d).ToList();
                    var median = sorted[sorted.Count / 2];
                    if (median.d > best) {
                        best = median.d;
                        frame.open = axis;
                        frame.across = other;
                        frame.sign = sign;
                        x = median.x;
                    }
                }
            }
            frame.middle = b.center[frame.across];
            var openDir = frame.OpenDir;
            var lo = frame.sign > 0f ? b.min[frame.open] : -b.max[frame.open];   // t of the closed side
            var hi = frame.sign > 0f ? b.max[frame.open] : -b.min[frame.open];   // t of the open side
            var depthIn = Depth(frame.Point(x, hi + 1f), -openDir, hi - lo + 2f);
            frame.webInner = float.IsNaN(depthIn) ? lo : hi + 1f - depthIn;
            var depthOut = Depth(frame.Point(x, lo - 1f), openDir, hi - lo + 2f);
            frame.webOuter = float.IsNaN(depthOut) ? lo : lo - 1f + depthOut;
            // The widest place inside: from the middle out to each wall.
            var acrossDir = Vector3.zero;
            acrossDir[frame.across] = 1f;
            var widestWidth = -1f;
            frame.widest = (frame.webInner + hi) / 2f;
            for (var t = frame.webInner + 0.01f; t < hi; t += 0.005f) {
                var c = frame.Point(x, t);
                var a = Depth(c, acrossDir, 2f);
                var a2 = Depth(c, -acrossDir, 2f);
                if (float.IsNaN(a) || float.IsNaN(a2)) continue;
                if (a + a2 > widestWidth + 1e-4f) {
                    widestWidth = a + a2;
                    frame.widest = t;
                }
            }
            // (the seat before build 88: a z ray from below, the truck on the web)
            frame.legacyCenterY = b.center.y;
            frame.legacyWeb = b.max.z - 0.046f;
            if (collider.Raycast(new Ray(new Vector3(b.center.x, b.center.y, b.min.z - 1f), Vector3.forward), out var hit, b.size.z + 2f)) frame.legacyWeb = hit.point.z;
            Object.DestroyImmediate(probe);
            return frames[mesh] = frame;
        }

        private const float HalfLength = 1f, Clearance = 0.004f;
        // The acetal truck (its length along y, mounting face -z): widest 0.09" from its middle toward its back.
        private const float TruckWidestBack = 0.09f;
        // The classic inner trunk (length along z, mounting face +y): its back face 0.149" below its origin. The outer
        // trunk's pocket (the rail's outside fits in it) has its floor at the trunk's origin.
        private const float InnerBack = 0.149f;
        private static readonly Quaternion TruckTurn = Quaternion.AngleAxis(-90f, Vector3.forward);   // (before build 88)

        /// <summary>Where a truck goes in this member, nearest the point.</summary>
        public static Displacement Seat(GameObject member, Vector3 near, bool free = false, string truckId = TruckId, Bounds? truckBounds = null, bool inside = true) {
            var mesh = member.GetComponent<MeshFilter>().sharedMesh;
            var f = FrameOf(mesh);
            var b = mesh.bounds;
            var x = member.transform.InverseTransformPoint(near).x;
            if (!free) x = Mathf.Round(x * 16f) / 16f;
            // (inside: wholly on the member; else anywhere along its line: a truck slid partly off an end, TruckSlider)
            if (inside) x = Mathf.Clamp(x, b.min.x + HalfLength, b.max.x - HalfLength);
            var open = member.transform.TransformDirection(f.OpenDir);
            var length = member.transform.right;
            if (truckId == TruckId) {
                // Its widest at the channel's widest, its mounting face out of the opening (proud of the lips).
                return new Displacement(member.transform.TransformPoint(f.Point(x, f.widest + TruckWidestBack)), Quaternion.LookRotation(-open, length));
            }
            // The classic trunks: length along the rail, mounting face out of its opening; inner: its back near the web
            // inside the channel; outer: its pocket's floor on the web's outside, wrapped round the rail.
            var t = truckId == ClassicInner ? f.webInner + InnerBack + Clearance : f.webOuter - Clearance;
            return new Displacement(member.transform.TransformPoint(f.Point(x, t)), Quaternion.LookRotation(length, open));
        }

        // Where a truck was seated before build 88 (VEX's slide members only), so builds saved then keep their slides.
        private static Displacement LegacySeat(GameObject member, Vector3 near, string truckId, Bounds truckBounds) {
            var mesh = member.GetComponent<MeshFilter>().sharedMesh;
            var f = FrameOf(mesh);
            var b = mesh.bounds;
            var x = Mathf.Clamp(member.transform.InverseTransformPoint(near).x, b.min.x + HalfLength, b.max.x - HalfLength);
            if (truckId == TruckId)
                return new Displacement(member.transform.TransformPoint(new Vector3(x, f.legacyCenterY, f.legacyWeb - 0.165f - Clearance)), member.transform.rotation * TruckTurn);
            var turn = member.transform.rotation * Quaternion.LookRotation(Vector3.right, Vector3.forward);
            var middle = truckId == ClassicInner
                ? new Vector3(x, f.legacyCenterY, f.legacyWeb - truckBounds.extents.y - Clearance)
                : new Vector3(x, f.legacyCenterY, b.center.z);
            return new Displacement(member.transform.TransformPoint(middle) - turn * truckBounds.center, turn);
        }

        /// <summary>The member's length line (world): a point, the way along it, and how far each way the truck can go.</summary>
        public static (Vector3 point, Vector3 along, float low, float high) Track(GameObject member) {
            var b = member.GetComponent<MeshFilter>().sharedMesh.bounds;
            return (member.transform.TransformPoint(new Vector3(0f, b.center.y, b.center.z)), member.transform.right, b.min.x + HalfLength, b.max.x - HalfLength);
        }

        private void LateUpdate() {
            // (one not held looks again every half second or so: a build just opened, an undo, a truck slid back on
            // can settle a frame after the version changed)
            if (seenVersion == Dynamics.Version && (Member != null || (Time.frameCount + GetInstanceID()) % 30 != 0)) return;
            seenVersion = Dynamics.Version;
            Check();
        }

        private static bool At(Displacement seat, Transform t) =>
            (seat.translation.Position - t.position).sqrMagnitude < 0.03f * 0.03f && Quaternion.Angle(seat.rotation.Orientation, t.rotation) < 3f;

        // Whether a truck at this point is still partly on the member (slid off an end only when it's wholly off).
        private static bool OnMember(GameObject member, Vector3 at) {
            var b = member.GetComponent<MeshFilter>().sharedMesh.bounds;
            var x = member.transform.InverseTransformPoint(at).x;
            return x > b.min.x - HalfLength + 0.02f && x < b.max.x + HalfLength - 0.02f;
        }

        /// <summary>Finds the member it's seated in (or none) and links them. A truck slid partly off an end
        /// (TruckSlider) is still held; wholly off, it isn't.</summary>
        public void Check() {
            GameObject found = null;
            var truckId = id;
            foreach (var obj in PartsManager.FindLoadedObjects()) {
                if (obj == null || obj == gameObject || obj.IsDeleted() || !IsMember(obj)) continue;
                if ((Fits(obj, truckId) && OnMember(obj, transform.position) && At(Seat(obj, transform.position, true, truckId, Bounds(), false), transform))
                    || (IsVexMember(obj) && At(LegacySeat(obj, transform.position, truckId, Bounds()), transform))) {
                    found = obj;
                    break;
                }
            }
            if (found == Member) return;
            if (Member != null) GroupManager.Unlink(gameObject, Member);
            Member = found;
            if (Member != null) GroupManager.Link(gameObject, Member);
        }

        private void OnDestroy() {
            if (Member != null) GroupManager.Unlink(gameObject, Member);
        }
    }

    // Placing a truck: over a member it rides on (a slide member, a rail) it seats in or round its channel.
    public class SlidePlaceDisplacement : PlaceDisplacement {
        private MouseCast mouseCast;
        public override bool ModifyRotation => true;

        public static void AddTo(Placement placement, List<PlaceDisplacement> list) {
            var displacement = placement.gameObject.AddComponent<SlidePlaceDisplacement>();
            foreach (var other in list)
                if (other is HolePlaceDisplacement hole)
                    displacement.mouseCast = typeof(HolePlaceDisplacement).GetField("mouseCast", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(hole) as MouseCast;
            list.Insert(0, displacement);
        }

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            displacement = null;
            if (mouseCast == null || mouseCast.gameObject == null) return false;
            string id = null;
            GameObject self = null;
            Mesh mesh = null;
            if (placementData is PartPlacementData part && part.partGenerator != null) {
                id = part.partGenerator.GetId();
                mesh = part.partGenerator.GetMesh();
            }
            else if (placementData.TryParse(out GameObjectPlacementData moving) && moving.GetGameObject() != null && moving.GetGameObject().TryGetComponent(out SavedObject saved)) {
                self = moving.GetGameObject();
                id = saved.id;
                mesh = self.GetComponent<MeshFilter>()?.sharedMesh;
            }
            if (!SlideTruck.IsTruck(id)) return false;
            var hovered = mouseCast.gameObject.GetComponentInParent<SavedObject>();
            if (hovered == null || hovered.gameObject == self || !SlideTruck.Fits(hovered.gameObject, id)) return false;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            displacement = SlideTruck.Seat(hovered.gameObject, mouseCast.hit.point, keyboard != null && keyboard.shiftKey.isPressed, id, mesh != null ? mesh.bounds : (Bounds?)null);
            return true;
        }
    }
}
