using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Protobot.EditorTools {
    // V2: every player build gets the next build number (Resources/Version/build.txt), and the app's version
    // (PlayerSettings.bundleVersion = Application.version, written into saved builds) says it: "2.0 Beta 1 build 70".
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
            PatchNotes();
        }

        // V2: the home menu's Patch Notes (Interface/PatchNotes) read the release notes (Release/Release 1.9.N.md, newest
        // first) from Resources/Version/PatchNotes.txt, made here.
        private const string Notes = "Assets/ProtobotRebuilt/Resources/Version/PatchNotes.txt";

        [MenuItem("Tools/Protobot/Gather Patch Notes")]
        public static void PatchNotes() {
            if (!Directory.Exists("Release")) return;
            var files = new System.Collections.Generic.List<(int build, string path)>();
            // "Release 1.9.69.md" (before v2.0) and "Release v2.0 Beta 1 (build 70).md"
            foreach (var path in Directory.GetFiles("Release", "Release *.md")) {
                var name = Path.GetFileNameWithoutExtension(path);
                var m = System.Text.RegularExpressions.Regex.Match(name, @"\(build (\d+)\)$|\.(\d+)$");
                if (m.Success && int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out var n)) files.Add((n, path));
            }
            files.Sort((a, b) => b.build.CompareTo(a.build));
            var text = new System.Text.StringBuilder();
            foreach (var (_, path) in files) text.AppendLine(System.IO.File.ReadAllText(path).Trim()).AppendLine();
            System.IO.File.WriteAllText(Notes, text.ToString());
            AssetDatabase.ImportAsset(Notes, ImportAssetOptions.ForceUpdate);
            // V2: the Privacy Policy, Terms and Credits the app shows (Interface/LegalDocs) are the repository's own.
            foreach (var (from, to) in new[] { ("PRIVACY.md", "Privacy"), ("TERMS.md", "Terms"), ("CREDITS.md", "Credits") }) {
                if (!System.IO.File.Exists(from)) continue;
                var target = "Assets/ProtobotRebuilt/Resources/Legal/" + to + ".txt";
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (!System.IO.File.Exists(target) || System.IO.File.ReadAllText(target) != System.IO.File.ReadAllText(from)) {
                    System.IO.File.Copy(from, target, true);
                    AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static int Current() =>
            System.IO.File.Exists(File) && int.TryParse(System.IO.File.ReadAllText(File).Trim(), out var n) ? n : 0;
    }
}
