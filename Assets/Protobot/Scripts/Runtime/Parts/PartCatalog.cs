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

        /// <summary>Ready-to-use prefab: the model plus Rigidbody, colliders and grab/hover components.</summary>
        public GameObject prefab;
        public Sprite thumbnail;

        /// <summary>Real-world bounding size in metres, as measured from the part's mesh.</summary>
        public Vector3 size;
        public int triangles;

        /// <summary>Estimated from the mesh volume and the density of its material (aluminium, steel, plastic...).</summary>
        public float massKg;

        /// <summary>Full-length stock (metal, shafts) that the player cuts to size with the saw.</summary>
        public bool cuttable;

        /// <summary>"444.5 × 25.4 × 14.3 mm", largest dimension first.</summary>
        public string SizeLabel => FormatSize(size);

        public string MassLabel => FormatMass(massKg);

        public static string FormatSize(Vector3 size)
        {
            var d = new[] { size.x, size.y, size.z };
            System.Array.Sort(d);
            return $"{d[2] * 1000f:0.#} × {d[1] * 1000f:0.#} × {d[0] * 1000f:0.#} mm";
        }

        public static string FormatMass(float kg) => kg >= 1f ? $"{kg:0.00} kg" : $"{kg * 1000f:0.#} g";
    }
}
