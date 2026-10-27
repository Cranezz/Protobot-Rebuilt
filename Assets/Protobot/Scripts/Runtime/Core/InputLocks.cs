using UnityEngine;

namespace Protobot.Core
{
    /// <summary>Temporary claims on the mouse that aren't full menus (e.g. the tool wheel uses the mouse to pick).</summary>
    public static class InputLocks
    {
        /// <summary>While true, moving the mouse doesn't turn the view.</summary>
        public static bool LookSuppressed { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LookSuppressed = false;
    }
}
