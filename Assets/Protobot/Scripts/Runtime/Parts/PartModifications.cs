using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// Everything done to a part after it was taken from a bin (cuts, drilled holes, paint), in the
    /// part's own space, so a save file can rebuild it exactly.
    /// </summary>
    public class PartModifications : MonoBehaviour
    {
        [System.Serializable]
        public struct Cut
        {
            public Vector3 point;
            public Vector3 keepNormal;
        }

        [System.Serializable]
        public struct DrilledHole
        {
            public Vector3 center;
            public Vector3 normal;
            public float diameter;
            public float depth;
        }

        [System.Serializable]
        public struct Paint
        {
            public string material;   // which of the part's materials (a part can have several, e.g. tyre and hub)
            public Color color;
        }

        public List<Cut> cuts = new();
        public List<DrilledHole> holes = new();
        public List<Paint> paints = new();

        public static PartModifications On(GameObject part) =>
            part.TryGetComponent(out PartModifications mods) ? mods : part.AddComponent<PartModifications>();

        /// <summary>Records the part's current paint (after any change).</summary>
        public void RecordPaint(Interaction.HoverTarget hover)
        {
            paints.Clear();
            foreach (var pair in hover.Paints)
                paints.Add(new Paint { material = pair.Key.name, color = pair.Value });
        }
    }
}
