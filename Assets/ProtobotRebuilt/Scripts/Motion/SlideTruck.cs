using System.Collections.Generic;
using System.Linq;
using Protobot.Transformations;
using UnityEngine;

namespace Protobot.Motion {
    // V2: VEX's linear slide: the acetal slide truck rides inside an outer or inner slide member (a C channel). From
    // the parts list or the Move tool a truck over a slide member seats in its channel (its back on the member's web,
    // centred between its lips) where the mouse is along it (1/16" steps), never past its ends. A seated truck is
    // held by its member (they move together in the builder) but slides along it in the simulation, with the acetal's
    // light drag, stopping at the member's ends.
    public class SlideTruck : MonoBehaviour {
        public const string TruckId = "VXSLIDEASSEMBLYP-Inner Acetal Slide Truck";

        public static bool IsTruck(string id) => id == TruckId;
        public static bool IsMember(GameObject part) =>
            part != null && part.TryGetComponent(out SavedObject saved) && saved.id != null
            && (saved.id.EndsWith("-Outer Slide Member") || saved.id.EndsWith("-Inner Slide Member"));

        public GameObject Member { get; private set; }
        private long seenVersion = -1;

        private static readonly Dictionary<Mesh, (float centerY, float webInner)> channels = new();

        private static (float centerY, float webInner) Channel(Mesh mesh) {
            if (channels.TryGetValue(mesh, out var known)) return known;
            var b = mesh.bounds;
            var probe = new GameObject("Slide Probe") { hideFlags = HideFlags.HideAndDontSave };
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            var web = b.max.z - 0.046f;
            var p = new Vector3(b.center.x, b.center.y, b.min.z - 1f);
            if (collider.Raycast(new Ray(p, Vector3.forward), out var hit, b.size.z + 2f)) web = hit.point.z;
            Object.DestroyImmediate(probe);
            return channels[mesh] = (b.center.y, web);
        }

        private const float HalfLength = 1f, HalfDepth = 0.165f, Clearance = 0.004f;
        private static readonly Quaternion TruckTurn = Quaternion.AngleAxis(-90f, Vector3.forward);   // its length along the member's

        /// <summary>Where a truck goes in this member, nearest the point.</summary>
        public static Displacement Seat(GameObject member, Vector3 near, bool free = false) {
            var mesh = member.GetComponent<MeshFilter>().sharedMesh;
            var (centerY, web) = Channel(mesh);
            var b = mesh.bounds;
            var x = member.transform.InverseTransformPoint(near).x;
            if (!free) x = Mathf.Round(x * 16f) / 16f;
            x = Mathf.Clamp(x, b.min.x + HalfLength, b.max.x - HalfLength);
            var local = new Vector3(x, centerY, web - HalfDepth - Clearance);
            return new Displacement(member.transform.TransformPoint(local), member.transform.rotation * TruckTurn);
        }

        /// <summary>The member's length line (world): a point, the way along it, and how far each way the truck can go.</summary>
        public static (Vector3 point, Vector3 along, float low, float high) Track(GameObject member) {
            var b = member.GetComponent<MeshFilter>().sharedMesh.bounds;
            return (member.transform.TransformPoint(new Vector3(0f, b.center.y, b.center.z)), member.transform.right, b.min.x + HalfLength, b.max.x - HalfLength);
        }

        private void LateUpdate() {
            if (seenVersion == Dynamics.Version) return;
            seenVersion = Dynamics.Version;
            Check();
        }

        /// <summary>Finds the member it's seated in (or none) and links them.</summary>
        public void Check() {
            GameObject found = null;
            foreach (var obj in PartsManager.FindLoadedObjects()) {
                if (obj == null || obj == gameObject || obj.IsDeleted() || !IsMember(obj)) continue;
                var seat = Seat(obj, transform.position, true);
                if ((seat.translation.Position - transform.position).sqrMagnitude < 0.03f * 0.03f
                    && Quaternion.Angle(seat.rotation.Orientation, transform.rotation) < 3f) {
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

    // Placing a truck: over a slide member it seats in its channel.
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
            if (placementData is PartPlacementData part && part.partGenerator != null) id = part.partGenerator.GetId();
            else if (placementData.TryParse(out GameObjectPlacementData moving) && moving.GetGameObject() != null && moving.GetGameObject().TryGetComponent(out SavedObject saved)) {
                self = moving.GetGameObject();
                id = saved.id;
            }
            if (!SlideTruck.IsTruck(id)) return false;
            var hovered = mouseCast.gameObject.GetComponentInParent<SavedObject>();
            if (hovered == null || hovered.gameObject == self || !SlideTruck.IsMember(hovered.gameObject)) return false;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            displacement = SlideTruck.Seat(hovered.gameObject, mouseCast.hit.point, keyboard != null && keyboard.shiftKey.isPressed);
            return true;
        }
    }
}
