using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9, robot code): the open build's program (Python), saved in the build (BuildData.code). It can be
    /// edited in the user's own editor: <see cref="OpenExternally"/> writes it to a file (with a vex.pyi stub so VS
    /// Code completes the API) and every save there comes back here.
    /// </summary>
    public static class CodeStore {
        public static string Source { get; private set; } = "";
        public static event Action Changed;

        public static string Token => Source ?? "";

        public static string Folder => Path.Combine(Application.persistentDataPath, "Robot Code");
        public static string ExternalPath => Path.Combine(Folder, Scripting.CppTranslator.LooksLikeCpp(Source) ? "main.cpp" : "main.py");   // V2: C++ too
        public static bool Watching { get; private set; }
        // V2: the file VS Code was given. Kept until it's opened again, so a program that turns from Python into C++
        // (or back) in either editor keeps syncing through the same file instead of switching to a stale main.py/.cpp.
        private static string watchedPath;
        // V2: a project (CodeProject): each of its files is written into the folder (src/main.cpp, include/..., static/...)
        // and a save in any of them comes back; null: a single program (watchedPath).
        private static System.Collections.Generic.List<string> projectFiles;
        private static DateTime lastWrite;
        private static float nextPoll;

        /// <summary>A build was opened (or a new one started): its program, or none.</summary>
        public static void Load(string token) {
            RobotProgram.Stop(null);
            Source = token ?? "";
            Watching = false;
            Changed?.Invoke();
        }

        /// <summary>The program was edited (in the panel, or a file saved outside).</summary>
        public static void Set(string text) {
            text ??= "";
            if (text == Source) return;
            Source = text;
            if (Watching) WriteExternal();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ the user's own editor

        public static void OpenExternally() {
            try {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, "vex.pyi"), ApiStub.Text);
                File.WriteAllText(Path.Combine(Folder, "motion.pyi"), ApiStub.Motion);   // V2
                CppHeaders.Write(Folder);   // V2: C++ (PROS, VEXcode C++, LemLib, EZ-Template) for VS Code's IntelliSense
                // V2: a project's files each in their place (after the headers: its own robot-config.h wins).
                var sections = CodeProject.Split(Source);
                if (sections.Count > 0) {
                    projectFiles = sections.Select(s => s.path).ToList();
                    var main = sections.FirstOrDefault(s => Path.GetFileNameWithoutExtension(s.path).Equals("main", StringComparison.OrdinalIgnoreCase) && CodeProject.IsCode(s.path))
                               ?? sections.FirstOrDefault(s => CodeProject.IsCode(s.path)) ?? sections[0];
                    watchedPath = Path.Combine(Folder, main.path);
                }
                else {
                    projectFiles = null;
                    watchedPath = ExternalPath;   // V2: the language the program is in now
                }
                WriteExternal();
                Watching = true;
                var path = watchedPath;
                Watcher.Ensure();   // V2: saves come back with the code window closed too
                var code = FindVsCode();
                if (code != null) {
                    // The folder too, so VS Code finds vex.pyi for completions.
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(code, "\"" + Folder + "\" \"" + path + "\"") { UseShellExecute = false });
                    Interface.Interface.Announce("Opened in VS Code: saving there updates the program here");
                }
                else if (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor) {
                    // V2 (build 79): a Mac without VS Code: its default text editor (TextEdit).
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", "-t \"" + path + "\"") { UseShellExecute = false });
                    Interface.Interface.Announce("Opened in your text editor (VS Code wasn't found): saving there updates the program here");
                }
                else {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = false });
                    Interface.Interface.Announce("Opened in Notepad (VS Code wasn't found): saving there updates the program here");
                }
            }
            catch (Exception e) {
                Debug.LogWarning("[Robot code] couldn't open the editor: " + e.Message);
                Interface.Interface.Say("Couldn't open an editor: " + e.Message, false);
            }
        }

        private static void WriteExternal() {
            if (projectFiles != null) {
                WriteProject();
                return;
            }
            try {
                Directory.CreateDirectory(Folder);
                watchedPath ??= ExternalPath;
                var current = File.Exists(watchedPath) ? File.ReadAllText(watchedPath) : null;
                if (current != Source) File.WriteAllText(watchedPath, Source);
                lastWrite = File.GetLastWriteTimeUtc(watchedPath);
            }
            catch (Exception e) {
                Debug.LogWarning("[Robot code] couldn't write " + watchedPath + ": " + e.Message);
            }
        }

        // V2: each file of the project (a file the program no longer has is left on disk).
        private static void WriteProject() {
            try {
                var sections = CodeProject.Split(Source);
                if (sections.Count == 0) return;   // (turned into a single program here: the files stay as they were)
                projectFiles = sections.Select(s => s.path).ToList();
                var newest = DateTime.MinValue;
                foreach (var s in sections) {
                    var file = Path.Combine(Folder, s.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    var current = File.Exists(file) ? File.ReadAllText(file).Replace("\r\n", "\n") : null;
                    if (current != s.text) File.WriteAllText(file, s.text);
                    var w = File.GetLastWriteTimeUtc(file);
                    if (w > newest) newest = w;
                }
                lastWrite = newest;
            }
            catch (Exception e) {
                Debug.LogWarning("[Robot code] couldn't write the project: " + e.Message);
            }
        }

        // V2: a save in any of the project's files: the project again from all of them.
        private static void PollProject() {
            var newest = DateTime.MinValue;
            foreach (var f in projectFiles) {
                var file = Path.Combine(Folder, f);
                if (File.Exists(file)) {
                    var w = File.GetLastWriteTimeUtc(file);
                    if (w > newest) newest = w;
                }
            }
            if (newest <= lastWrite) return;
            lastWrite = newest;
            var sections = CodeProject.Split(Source);
            var text = CodeProject.Join(sections.Select(s => {
                var file = Path.Combine(Folder, s.path);
                return (s.path, File.Exists(file) ? File.ReadAllText(file).Replace("\r\n", "\n") : s.text);
            }));
            if (text == Source) return;
            Source = text;
            Changed?.Invoke();
        }

        /// <summary>Picks up a save in the outside editor (called every frame by the code panel; checks 4x a second).</summary>
        public static void Poll() {
            if (!Watching || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.25f;
            try {
                if (projectFiles != null) {
                    PollProject();
                    return;
                }
                if (watchedPath == null || !File.Exists(watchedPath)) return;
                var write = File.GetLastWriteTimeUtc(watchedPath);
                if (write == lastWrite) return;
                lastWrite = write;
                var text = File.ReadAllText(watchedPath).Replace("\r\n", "\n");
                if (text == Source) return;
                Source = text;
                Changed?.Invoke();
            }
            catch (IOException) {
                // The editor is still writing: next time.
            }
        }

        /// <summary>V2: checks for saves every frame (Poll limits it to 4x a second) whether or not the code window is open.</summary>
        private class Watcher : MonoBehaviour {
            private static Watcher instance;
            public static void Ensure() {
                if (instance != null) return;
                var go = new GameObject("Robot Code Watcher") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Watcher>();
            }
            private void Update() => Poll();
        }

        private static string FindVsCode() {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[] {
                Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe"),
                Path.Combine(local, "Programs", "VSCodium", "VSCodium.exe"),
                "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code",
                "/usr/bin/code",
                "/usr/local/bin/code", "/opt/homebrew/bin/code",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Visual Studio Code.app", "Contents", "Resources", "app", "bin", "code"),
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
