using System.Reflection;
using UnityEngine;
using Protobot.Transformations;

namespace Protobot {
    // V2: the Move tool holding a pneumatic cylinder by its rod's thread (click the thread): over a hole (a nut's,
    // a part's) the cylinder goes where its thread goes into the hole, as a screw would; elsewhere it follows the
    // mouse as any part does.
    public class ThreadPlaceDisplacement : PlaceDisplacement {
        public static Motion.RodThread Grabbed;

        /// <summary>A thread clicked in the Move tool (the Move tool's manager only runs once something is selected).</summary>
        public static Motion.RodThread Clicked;
        public static int ClickedFrame;

        public static void WatchClicks(Placement placement) {
            if (Interface.ToolRail.Current != "Move" || placement.placing) return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || MouseInput.overUI || Camera.main == null) return;
            if (Physics.Raycast(Camera.main.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"))
                && hit.collider.TryGetComponent(out Motion.RodThread thread) && thread.cylinder != null) {
                Clicked = thread;
                ClickedFrame = Time.frameCount;
            }
        }
        private HoleFace face;
        private Displacement last;
        private int lastFrame = -10;

        public override bool ModifyRotation => true;

        public static void AddTo(Placement placement, System.Collections.Generic.List<PlaceDisplacement> list) {
            var displacement = placement.gameObject.AddComponent<ThreadPlaceDisplacement>();
            foreach (var other in list)
                if (other is ScrewPlaceDisplacement screw)
                    displacement.face = typeof(ScrewPlaceDisplacement).GetField("currentHoleFace", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(screw) as HoleFace;
            list.Insert(0, displacement);
        }

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            displacement = null;
            if (Grabbed == null || Grabbed.cylinder == null) return false;
            if (!placementData.TryParse(out GameObjectPlacementData data) || data.GetGameObject() != Grabbed.cylinder) return false;
            if (!HoleUnderMouse(Grabbed.cylinder, out var facePosition, out var faceDirection)) {
                // The click that puts it there can change what's under the mouse for a frame: keep where it was going.
                if (last != null && Time.frameCount - lastFrame <= 3) {
                    displacement = last;
                    return true;
                }
                return false;
            }
            var cylinder = Grabbed.cylinder.transform;
            // The thread's pose in the cylinder's space, and the pose it should have: in the hole, like a screw (its
            // head end on the face, pointing out of it), turned as little as it can about the hole.
            var localRotation = Quaternion.Inverse(cylinder.rotation) * Grabbed.transform.rotation;
            var localPosition = cylinder.InverseTransformPoint(Grabbed.transform.position);
            var threadUp = Grabbed.transform.up;
            var up = Vector3.ProjectOnPlane(threadUp, faceDirection);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.up, faceDirection);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.right, faceDirection);
            var rotation = Quaternion.LookRotation(faceDirection, up) * Quaternion.Inverse(localRotation);
            var position = facePosition - rotation * Vector3.Scale(localPosition, cylinder.lossyScale);
            displacement = last = new Displacement(position, rotation);
            lastFrame = Time.frameCount;
            return true;
        }

        // The hole under the mouse (not one of the cylinder's own): its face toward the camera and the way out of it.
        private bool HoleUnderMouse(GameObject cylinder, out Vector3 position, out Vector3 direction) {
            position = direction = Vector3.zero;
            if (face != null && face.gameObject.activeInHierarchy && face.direction != Vector3.zero) {
                position = face.position;
                direction = face.LookRotation * Vector3.forward;
                return true;
            }
            var camera = Camera.main;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            var hits = Physics.RaycastAll(ray, Mathf.Infinity, HoleCollider.HOLE_COLLISIONS_MASK, QueryTriggerInteraction.Collide);
            HoleCollider best = null;
            var nearest = float.MaxValue;
            foreach (var hit in hits) {
                if (!hit.collider.TryGetComponent(out HoleCollider hole) || hole.transform.IsChildOf(cylinder.transform)) continue;
                if (hole.transform.parent != null && hole.transform.parent.gameObject.IsDeleted()) continue;
                if (hit.distance < nearest) {
                    nearest = hit.distance;
                    best = hole;
                }
            }
            // Not right on a hole: the nearest hole of the part under the mouse, close to where it points.
            if (best == null && Physics.Raycast(ray, out var onPart, Mathf.Infinity, LayerMask.GetMask("Default"))) {
                var part = onPart.collider.GetComponentInParent<SavedObject>();
                if (part != null && part.gameObject != cylinder && !part.transform.IsChildOf(cylinder.transform)) {
                    nearest = 0.35f;
                    foreach (var hole in part.GetComponentsInChildren<HoleCollider>()) {
                        var distance = Vector3.Distance(hole.transform.position, onPart.point);
                        if (distance < nearest) {
                            nearest = distance;
                            best = hole;
                        }
                    }
                }
            }
            if (best == null) return false;
            var axis = best.transform.forward;
            var side = Mathf.Sign(Vector3.Dot(camera.transform.position - best.transform.position, axis));
            if (side == 0f) side = 1f;
            direction = axis * side;
            position = best.transform.position + direction * (best.transform.lossyScale.z / 2f);
            return true;
        }
    }
}
