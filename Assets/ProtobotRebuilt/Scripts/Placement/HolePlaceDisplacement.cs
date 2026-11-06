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

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            if (HoverObj != null && placementData.objectId.Contains("Hole")) {
                if (HoverObj.CompareTag("Screw")) {
                    Screw screw = HoverObj.GetComponent<Screw>();
                    // V2: the free stretch of the screw nearest the mouse the part fits in (between two channels,
                    // say), not only past the last part on it.
                    var spot = screw.GetLowestPoint();
                    if (placementData.TryParse(out GameObjectPlacementData data) && data.GetGameObject().TryGetComponent(out HoleFace face)
                        && face.hole != null && face.hole.part != null && face.hole.part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) {
                        var part = face.hole.part.transform;
                        var axis = part.InverseTransformDirection(face.LookRotation * Vector3.forward).normalized;
                        var size = filter.sharedMesh.bounds.size;
                        var thickness = (Mathf.Abs(axis.x) * size.x + Mathf.Abs(axis.y) * size.y + Mathf.Abs(axis.z) * size.z) * part.lossyScale.x;
                        // The hole's face sits inside a part thicker than its hole (a wheel's hub).
                        var inset = Mathf.Max(0f, (thickness - face.hole.depth) / 2f);
                        spot = screw.FreeSpot(mouseCast.hit.point, thickness) - HoverObj.transform.forward * inset;
                    }
                    displacement = new Displacement(spot, HoverObj.GetAlignmentRotation(true));
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