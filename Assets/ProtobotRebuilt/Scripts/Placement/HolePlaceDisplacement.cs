using System.Collections;
using UnityEngine;
using Protobot.Transformations;

namespace Protobot {
    public class HolePlaceDisplacement : PlaceDisplacement {
        [SerializeField] private MouseCast mouseCast;
        private GameObject HoverObj => mouseCast.gameObject;

        [SerializeField] private HoleFace hoverHoleFace;
        [SerializeField] private HoleFace selectedHoleFace;

        public override bool ModifyRotation => true;

        private bool OntoScrew(HoleFace face, Screw screw, out Displacement displacement) {
            displacement = null;
            var center = face.hole.position;
            var axis = face.hole.forward.normalized;
            // Along the hole's axis: every hole of the moving parts that's on it (the hubs of a wheel and its gear).
            float low = float.MaxValue, high = float.MinValue;
            foreach (var part in face.hole.part.GetConnectedObjects(true)) {
                if (part == null || part.GetComponent<SavedObject>() == null) continue;
                foreach (var hole in part.GetComponentsInChildren<HoleCollider>(true)) {
                    if (hole.transform.parent != part.transform) continue;
                    var t = hole.transform;
                    if (Mathf.Abs(Vector3.Dot(t.forward, axis)) < 0.98f) continue;
                    var offset = t.position - center;
                    if (Vector3.ProjectOnPlane(offset, axis).magnitude > 0.06f) continue;
                    var along = Vector3.Dot(offset, axis);
                    var half = t.lossyScale.z / 2f;
                    low = Mathf.Min(low, along - half);
                    high = Mathf.Max(high, along + half);
                }
            }
            if (!(high > low)) return false;
            var head = screw.transform.position;
            var down = -screw.transform.forward;
            var near = Vector3.Dot(mouseCast.hit.point - head, down);
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var used = AxleLine.Used(face.gameObject, head, down, screw.ScrewLength);
            var start = AxleLine.Free(used, near, high - low, 0f, screw.ScrewLength, true, keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed), out var edge);
            if (!start.HasValue) return false;
            // The smallest turn that lines the hole up with the screw, unless it snaps against a neighbour: then the
            // face that was grabbed is the one that goes against it (the part turns round if it has to), as a hole
            // placed on a hole puts the grabbed face on it.
            var sign = Vector3.Dot(axis, down) >= 0f ? 1f : -1f;
            var turn = Quaternion.FromToRotation(axis, down * sign);
            var grabbed = face.direction.sqrMagnitude > 0.5f ? face.direction.normalized : face.transform.forward * -1f;
            if (edge != 0 && Mathf.Sign(Vector3.Dot(turn * grabbed, down)) != edge) {
                var across = Vector3.ProjectOnPlane(screw.transform.up, down);
                if (across.sqrMagnitude < 1e-4f) across = Vector3.ProjectOnPlane(Vector3.up, down);
                if (across.sqrMagnitude < 1e-4f) across = Vector3.ProjectOnPlane(Vector3.right, down);
                turn = Quaternion.AngleAxis(180f, across.normalized) * turn;
                sign = -sign;
            }
            // The hole's middle on the screw, so the stretch the parts take starts at `start`.
            var middle = head + down * (start.Value - Mathf.Min(sign * low, sign * high));
            var faceTransform = face.transform;
            displacement = new Displacement(middle + turn * (faceTransform.position - center), turn * faceTransform.rotation);
            return true;
        }

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            if (HoverObj != null && placementData.objectId.Contains("Hole")) {
                if (HoverObj.CompareTag("Screw")) {
                    Screw screw = HoverObj.GetComponent<Screw>();
                    // V2: the part (and everything moving with it: a gear bolted to a wheel) goes onto the screw by the
                    // hole it's held by, keeping its own turn (the smallest turn that lines the hole up with the screw,
                    // so it never flips), and slides into the free stretch nearest the mouse it fits in, by the length
                    // of all its holes on that line (the hubs: they go flush against the part beside them).
                    if (placementData.TryParse(out GameObjectPlacementData data) && data.GetGameObject().TryGetComponent(out HoleFace face)
                        && face.hole != null && face.hole.part != null && OntoScrew(face, screw, out displacement))
                        return true;
                    displacement = new Displacement(screw.GetLowestPoint(), HoverObj.GetAlignmentRotation(true));
                    return true;
                }

                if (HoverObj.tag.Contains("Hole")) {
                    displacement = new Displacement(hoverHoleFace.position, hoverHoleFace.LookRotation);
                    return true;
                }
            }
            
            displacement = null;
            return false;
        }
    }
}