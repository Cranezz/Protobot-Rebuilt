using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Protobot.EditorTools {
    // V2: every player build gets the next build number (Resources/Version/build.txt), and the app's version
    // (PlayerSettings.bundleVersion = Application.version, written into saved builds) says it: "Beta 1.9 build 12".
    // Tools > Protobot > Next Build Number does the same by hand.
    public class BuildNumberStep : IPreprocessBuildWithReport {
        private const string File = "Assets/ProtobotRebuilt/Resources/Version/build.txt";
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Next();

        [MenuItem("Tools/Protobot/Next Build Number")]
        public static void Next() {
            var number = Current() + 1;
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            System.IO.File.WriteAllText(File, number.ToString());
            AssetDatabase.ImportAsset(File, ImportAssetOptions.ForceUpdate);
            PlayerSettings.bundleVersion = Builds.BuildInfo.BundleVersion(number);
            Debug.Log("[Build] Build number " + number + " (" + PlayerSettings.bundleVersion + ")");
        }

        private static int Current() =>
            System.IO.File.Exists(File) && int.TryParse(System.IO.File.ReadAllText(File).Trim(), out var n) ? n : 0;
    }
}
