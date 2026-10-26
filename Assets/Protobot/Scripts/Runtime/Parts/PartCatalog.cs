using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// Every VEX part the player can spawn, grouped into the bins on the workshop shelf.
    /// Built by Tools > Protobot > Build Part Library from the converted part models.
    /// </summary>
    [CreateAssetMenu(menuName = "Protobot/Part Catalog", fileName = "PartCatalog")]
    public class PartCatalog : ScriptableObject
    {
        public List<PartEntry> parts = new();

        public IEnumerable<PartEntry> InBin(string binId)
        {
            foreach (var part in parts)
                if (part.binId == binId)
                    yield return part;
        }
    }

    [Serializable]
    public class PartEntry
    {
        /// <summary>Stable id used in save files, e.g. "Structure/C Channel/Aluminum 1x2x1/Alu C 1x2x1x35".</summary>
        public string id;
        public string displayName;
        public string binId;
        public GameObject model;

        /// <summary>Real-world bounding size in metres, as measured in Fusion.</summary>
        public Vector3 size;
        public int triangles;

        /// <summary>Full-length metal that the player cuts to size with the saw.</summary>
        public bool cuttable;
    }
}
