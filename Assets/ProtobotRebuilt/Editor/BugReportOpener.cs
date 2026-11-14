using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace Protobot.EditorTools {
    // V2: Tools > Protobot > Open Bug Report…: unpacks a bug report zip (Builds/BugReport) into Temp/Bug Reports/<name>,
    // opens its report.md and session.log, and in Play mode loads the build from it (build.pbb: as it was when reported;
    // saved.pbb: its file as last saved) so the steps in session.log can be followed on it.
    public static class BugReportOpener {
        [MenuItem("Tools/Protobot/Open Bug Report…")]
        public static void Open() {
            var zip = EditorUtility.OpenFilePanel("Open a bug report", "", "zip");
            if (string.IsNullOrEmpty(zip)) return;
            var folder = Path.Combine("Temp", "Bug Reports", Path.GetFileNameWithoutExtension(zip));
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            ZipFile.ExtractToDirectory(zip, folder);
            foreach (var name in new[] { "report.md", "session.log" }) {
                var file = Path.Combine(folder, name);
                if (File.Exists(file)) EditorUtility.OpenWithDefaultApp(Path.GetFullPath(file));
            }
            EditorUtility.RevealInFinder(Path.GetFullPath(folder));
            var build = Path.Combine(folder, "build.pbb");
            if (!File.Exists(build)) build = Path.Combine(folder, "saved.pbb");
            if (Application.isPlaying && File.Exists(build)) {
                PartsManager.DestroyLoadedObjects();
                Builds.SceneBuild.GenerateBuild(Builds.BuildFile.Read(Path.GetFullPath(build)));
                Debug.Log("[Bug report] Loaded " + build);
            }
            else Debug.Log("[Bug report] Unpacked to " + folder + (File.Exists(build) ? " (enter Play mode and open it again to load its build)" : ""));
        }
    }
}
