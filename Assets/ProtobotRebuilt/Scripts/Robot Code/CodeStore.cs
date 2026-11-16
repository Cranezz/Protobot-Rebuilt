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
        public static string ExternalPath => Path.Combine(Folder, "main.py");
        public static bool Watching { get; private set; }
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
                WriteExternal();
                File.WriteAllText(Path.Combine(Folder, "vex.pyi"), ApiStub.Text);
                File.WriteAllText(Path.Combine(Folder, "motion.pyi"), ApiStub.Motion);   // V2
                Watching = true;
                var path = ExternalPath;
                var code = FindVsCode();
                if (code != null) {
                    // The folder too, so VS Code finds vex.pyi for completions.
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(code, "\"" + Folder + "\" \"" + path + "\"") { UseShellExecute = false });
                    Interface.Interface.Announce("Opened in VS Code: saving there updates the program here");
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
            try {
                Directory.CreateDirectory(Folder);
                var current = File.Exists(ExternalPath) ? File.ReadAllText(ExternalPath) : null;
                if (current != Source) File.WriteAllText(ExternalPath, Source);
                lastWrite = File.GetLastWriteTimeUtc(ExternalPath);
            }
            catch (Exception e) {
                Debug.LogWarning("[Robot code] couldn't write " + ExternalPath + ": " + e.Message);
            }
        }

        /// <summary>Picks up a save in the outside editor (called every frame by the code panel; checks 4x a second).</summary>
        public static void Poll() {
            if (!Watching || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.25f;
            try {
                if (!File.Exists(ExternalPath)) return;
                var write = File.GetLastWriteTimeUtc(ExternalPath);
                if (write == lastWrite) return;
                lastWrite = write;
                var text = File.ReadAllText(ExternalPath).Replace("\r\n", "\n");
                if (text == Source) return;
                Source = text;
                Changed?.Invoke();
            }
            catch (IOException) {
                // The editor is still writing: next time.
            }
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
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
