using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Protobot.Transformations;

namespace Protobot {
    public class StandoffPlaceDisplacement : PlaceDisplacement {
        [SerializeField] private MouseCast mouseCast;
        private GameObject HoverObj => mouseCast.gameObject;
        [SerializeField] private HoleFace currentHoleFace;
        public override bool ModifyRotation => true;

        private List<string> AllowedIds = new List<string> {
            "SNDF",
            "SPCR",
            "PSPC",
            "BRNG",
            "WSHR",
        };

        public bool IsIdAllowed(PlacementData placementData) { 
            for (int i = 0; i < AllowedIds.Count; i++) {
                if (placementData.objectId.Contains(AllowedIds[i])) {
                    return true;
                }
            }
            return false;
        }

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            if (placementData.TryGetPartData(out PartData partData)) {
                if (HoverObj != null && IsIdAllowed(placementData)) {
                    if (currentHoleFace.direction != Vector3.zero && currentHoleFace.gameObject.activeInHierarchy) {
                        var offset = currentHoleFace.direction * (partData.PrimaryHoleDepth / 2);
                        displacement = new Displacement(currentHoleFace.position + offset, currentHoleFace.LookRotation);
                        return true;
                    }
                    else if (HoverObj.CompareTag("Screw") && HoverObj.TryGetComponent(out Motion.RubberLinkPart link)) {
                        // V2: onto the rubber link's stud nearer the mouse, against the rubber, lined up with that
                        // stud (a bent link's far stud is turned).
                        var (center, outward, high) = link.Seat(mouseCast.hit.point, partData.PrimaryHoleDepth);
                        var way = high ? outward : -outward;
                        displacement = new Displacement(center, Quaternion.FromToRotation(HoverObj.transform.forward, way) * HoverObj.GetAlignmentRotation());
                        return true;
                    }
                    else if (HoverObj.CompareTag("Screw")) {
                        Screw screw = HoverObj.GetComponent<Screw>();
                        var offset = HoverObj.transform.forward * (partData.PrimaryHoleDepth / 2);
                        // V2: the free stretch of the screw nearest the mouse it fits in, not only past the end.
                        displacement = new Displacement(screw.FreeSpot(mouseCast.hit.point, partData.PrimaryHoleDepth, placementData.objectId.Contains("SNDF")) - offset, HoverObj.GetAlignmentRotation());
                        return true;
                    }
                }
            }

            displacement = null;
            return false;
        }
    }
}