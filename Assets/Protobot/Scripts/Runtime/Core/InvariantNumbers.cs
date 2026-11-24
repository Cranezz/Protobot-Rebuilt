using System.Globalization;
using System.Threading;
using UnityEngine;

namespace Protobot.Core {
    /// <summary>
    /// V2: Protobot reads and writes numbers with a dot ("0.0033", part ids like SHFT-…-2.5, saved settings), but .NET
    /// parses with the PC's own format: on a PC set to a comma-decimal language (German, French, Turkish, Russian...)
    /// "0.0033" read as 33, so the camera's pan/zoom ran ~10,000x too fast and a 2.5" shaft became 25". The app runs in
    /// the invariant culture on every thread; Units formats what the user sees (inches as fractions) itself.
    /// </summary>
    public static class InvariantNumbers {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Apply() {
            var c = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = c;
            CultureInfo.DefaultThreadCurrentUICulture = c;
            Thread.CurrentThread.CurrentCulture = c;
            Thread.CurrentThread.CurrentUICulture = c;
        }
    }
}
