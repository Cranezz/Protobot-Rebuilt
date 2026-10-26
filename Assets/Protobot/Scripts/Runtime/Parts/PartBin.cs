using System;
using Protobot.Interaction;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// One of the shelf bins in the workshop. Clicking it opens the part picker on its category.
    /// </summary>
    public class PartBin : MonoBehaviour, IInteractable
    {
        [SerializeField] string binId;
        [SerializeField] string label;

        /// <summary>Raised when any bin is clicked; the part picker listens for it.</summary>
        public static event Action<PartBin> Opened;

        public string BinId => binId;
        public string Label => label;

        public void Configure(string id, string displayLabel)
        {
            binId = id;
            label = displayLabel;
        }

        public void Interact() => Opened?.Invoke(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Opened = null;
    }
}
