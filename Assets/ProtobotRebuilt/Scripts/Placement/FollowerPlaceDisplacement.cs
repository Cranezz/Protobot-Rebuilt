using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Protobot.Transformations;
using UnityEngine;

namespace Protobot {
    // V2: a cam follower into its mounting bracket (the user's ask: the bracket holds the follower). Placing a cam
    // follower (from the parts list or the Move tool) over a Cam Follower Mounting Bracket puts it through the
    // bracket's window, square to the bracket and centred in it, sliding along with the mouse; it keeps its own turn
    // about its length. The gear kit (Motion/GearKit) then lets a cam push it up and down through the bracket.
    public class FollowerPlaceDisplacement : PlaceDisplacement {
        private MouseCast mouseCast;
        public override bool ModifyRotation => true;

        public static void AddTo(Placement placement, List<PlaceDisplacement> list) {
            var displacement = placement.gameObject.AddComponent<FollowerPlaceDisplacement>();
            foreach (var other in list)
                if (other is HolePlaceDisplacement hole)
                    displacement.mouseCast = typeof(HolePlaceDisplacement).GetField("mouseCast", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hole) as MouseCast;
            list.Insert(0, displacement);
        }

        public static bool IsBracket(GameObject part) =>
            part != null && part.TryGetComponent(out SavedObject saved) && saved.id != null && saved.id.Contains("Cam Follower Mounting Bracket");

        /// <summary>The bracket's window: its biggest hole (centre, axis, world).</summary>
        public static bool Window(GameObject bracket, out Vector3 center, out Vector3 axis) {
            center = axis = Vector3.zero;
            var window = bracket.GetComponentsInChildren<HoleCollider>(true).Where(h => h.transform.parent == bracket.transform)
                                .OrderByDescending(h => h.transform.localScale.x * h.transform.localScale.y).FirstOrDefault();
            if (window == null || Mathf.Min(window.transform.localScale.x, window.transform.localScale.y) < 0.4f) return false;
            center = window.transform.position;
            axis = window.transform.forward;
            return true;
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
            if (Motion.GearKit.Kind(id) != Motion.KitKind.Follower || mesh == null) return false;
            var hovered = mouseCast.gameObject.GetComponentInParent<SavedObject>();
            var bracket = hovered != null ? hovered.gameObject : null;
            if (!IsBracket(bracket) || bracket == self || !Window(bracket, out var center, out var axis)) return false;
            var b = mesh.bounds;
            var size = b.size;
            var longLocal = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            var half = Vector3.Dot(size, longLocal) / 2f;
            var current = Placement.Instance != null ? Placement.Instance.transform.rotation : Quaternion.identity;
            var longNow = current * longLocal;
            if (Vector3.Dot(longNow, axis) < 0f) axis = -axis;
            var rotation = Quaternion.FromToRotation(longNow, axis) * current;
            var along = Mathf.Clamp(Vector3.Dot(mouseCast.hit.point - center, axis), -half + 0.3f, half - 0.3f);
            var position = center + axis * along - rotation * b.center;
            displacement = new Displacement(position, rotation);
            return true;
        }
    }
}
