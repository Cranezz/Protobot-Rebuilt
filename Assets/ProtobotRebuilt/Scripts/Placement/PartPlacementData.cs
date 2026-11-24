using UnityEngine;

namespace Protobot {
    public class PartPlacementData : PlacementData {
        public override Transform placementTransform { get; set; }

        public PartType partType { get; }
        public PartGenerator partGenerator { get; }
        public override string objectId => partType.id;

        public PartPlacementData(PartGenerator partGenerator, PartType partType, Transform placementTransform) {
            this.partType = partType;
            this.partGenerator = partGenerator;
            this.placementTransform = placementTransform;
        }

        /// <summary>V2: what the placing ghost shows instead of the part's own mesh (a pasted group: the part and what's
        /// screwed to it, in the part's frame). A new mesh each call (Placement frees the last one).</summary>
        public System.Func<Mesh> display;

        public override Mesh GetDisplayMesh() => display != null ? display() : partGenerator.GetMesh().CombineSubmeshes();
    }
}