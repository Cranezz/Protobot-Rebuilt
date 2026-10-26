using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// One of the shelf bins in the workshop. Clicking it (handled by the interaction system)
    /// opens the part picker for its category.
    /// </summary>
    public class PartBin : MonoBehaviour
    {
        [SerializeField] string binId;
        [SerializeField] string label;

        public string BinId => binId;
        public string Label => label;

        public void Configure(string id, string displayLabel)
        {
            binId = id;
            label = displayLabel;
        }
    }
}
