using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// Everything done to a part after it was taken from a bin (cuts, drilled holes, paint), in the
    /// part's own space, so a save file (or undo) can rebuild it exactly. Cuts and holes are numbered in the
    /// order they were made, because replaying them in another order can give a different shape.
    /// </summary>
    public class PartModifications : MonoBehaviour
    {
        [System.Serializable]
        public struct Cut
        {
            public Vector3 point;
            public Vector3 keepNormal;
            public int step;
        }

        [System.Serializable]
        public struct DrilledHole
        {
            public Vector3 center;
            public Vector3 normal;
            public float diameter;
            public float depth;
            public int step;
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

        /// <summary>Number for the next cut or hole (edits are only ever added).</summary>
        public int NextStep => cuts.Count + holes.Count;

        public static PartModifications On(GameObject part) =>
            part.TryGetComponent(out PartModifications mods) ? mods : part.AddComponent<PartModifications>();

        public void AddCut(Vector3 point, Vector3 keepNormal) =>
            cuts.Add(new Cut { point = point, keepNormal = keepNormal, step = NextStep });

        public void AddHole(Vector3 center, Vector3 normal, float diameter, float depth) =>
            holes.Add(new DrilledHole { center = center, normal = normal, diameter = diameter, depth = depth, step = NextStep });

        /// <summary>Records the part's current paint (after any change).</summary>
        public void RecordPaint(Interaction.HoverTarget hover)
        {
            paints.Clear();
            foreach (var pair in hover.Paints)
                paints.Add(new Paint { material = pair.Key.name, color = pair.Value });
        }
    }
}
