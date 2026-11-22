using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Protobot.Builds {
    // V2: a build (.pbb) or a replay (.pbr) given to Protobot when it starts (double-clicked in Explorer: the launcher
    // registers both file types, see Tools/Launcher) opens straight away, without the intro.
    public class OpenWith : MonoBehaviour {
        /// <summary>The .pbb or .pbr file on the command line, if any.</summary>
        public static string File {
            get {
                var args = Environment.GetCommandLineArgs();
                for (var i = 1; i < args.Length; i++) {
                    var a = args[i];
                    if (args[i - 1].StartsWith("-")) continue;   // an option's value (-open, -benchmark-build...)
                    if ((a.EndsWith(".pbb", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".pbr", StringComparison.OrdinalIgnoreCase)) && System.IO.File.Exists(a))
                        return a;
                }
                return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (!Application.isPlaying || File == null) return;
            new GameObject("Open With").AddComponent<OpenWith>();
        }

        private IEnumerator Start() {
            yield return new WaitForSecondsRealtime(1.5f);   // the menus and part types are made in the first frames
            var path = File;
            Online.OnlineLaunch.CloseLaunchWindow();
            if (path.EndsWith(".pbb", StringComparison.OrdinalIgnoreCase)) {
                var manager = FindAnyObjectByType<BuildsManager>();
                var data = BuildsManager.ParsePath(path);
                if (manager != null && data != null) manager.AttemptLoad(data, path);
            }
            else Game.Replays.OpenPath(path);
            Destroy(gameObject);
        }
    }
}
