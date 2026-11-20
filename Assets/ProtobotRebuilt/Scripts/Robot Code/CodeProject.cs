using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2: a robot code project of several files (a PROS or VEXcode project folder: headers, src/*.cpp, path files)
    /// kept as one text in the build: each file starts with a marker line ("//// file: src/main.cpp", a C++ comment),
    /// so the editor shows it all and a line number is the same in the editor, the program and its errors. Files that
    /// aren't code (LemLib's path files, static/*.txt or the SD card's) are left out of the program (their lines
    /// blank) and read by name (LemLib's ASSET(...) and follow("file.txt")).
    /// </summary>
    public static class CodeProject {
        public const string Marker = "//// file: ";
        public const string PythonMarker = "#### file: ";   // (a Python project: "////" isn't a Python comment)

        /// <summary>The file a marker line starts (null: not a marker).</summary>
        public static string MarkerPath(string line) =>
            line.StartsWith(Marker) ? line.Substring(Marker.Length).Trim() : line.StartsWith(PythonMarker) ? line.Substring(PythonMarker.Length).Trim() : null;

        public sealed class Section {
            public string path;
            public int markerLine;   // 1-based line of the marker
            public string text;      // the file (the lines after the marker)
        }

        private static readonly string[] CodeExtensions = { ".cpp", ".c", ".cc", ".cxx", ".h", ".hpp", ".hh", ".py" };

        public static bool IsCode(string path) => CodeExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

        /// <summary>Has file markers (a project, not a single program).</summary>
        public static bool IsProject(string source) => source != null && (source.StartsWith(Marker) || source.Contains("\n" + Marker) || source.StartsWith(PythonMarker) || source.Contains("\n" + PythonMarker));

        private static string countedText;
        private static int counted;

        /// <summary>How many files a project has (0: a single program); kept for the same text (the code window asks every frame).</summary>
        public static int FileCount(string source) {
            if (ReferenceEquals(source, countedText)) return counted;
            countedText = source;
            counted = 0;
            if (!IsProject(source)) return 0;
            foreach (var line in source.Split('\n'))
                if (MarkerPath(line) != null) counted++;
            return counted;
        }

        public static List<Section> Split(string source) {
            var result = new List<Section>();
            if (!IsProject(source)) return result;
            var lines = source.Replace("\r\n", "\n").Split('\n');
            Section cur = null;
            var body = new StringBuilder();
            for (var i = 0; i < lines.Length; i++) {
                var path = MarkerPath(lines[i]);
                if (path != null) {
                    if (cur != null) {
                        cur.text = Trim(body);
                        result.Add(cur);
                    }
                    cur = new Section { path = path, markerLine = i + 1 };
                    body.Clear();
                    continue;
                }
                if (cur != null) body.Append(lines[i]).Append('\n');
            }
            if (cur != null) {
                cur.text = Trim(body);
                result.Add(cur);
            }
            return result;
        }

        private static string Trim(StringBuilder b) {
            var s = b.ToString();
            return s.EndsWith("\n") ? s.Substring(0, s.Length - 1) : s;
        }

        /// <summary>The program to run: the code files as written, every other file's lines blank (line numbers kept).</summary>
        public static string CodeOnly(string source) {
            if (!IsProject(source)) return source;
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var code = true;
            for (var i = 0; i < lines.Length; i++) {
                var path = MarkerPath(lines[i]);
                if (path != null) {
                    code = IsCode(path);
                    continue;
                }
                if (!code) lines[i] = "";
            }
            return string.Join("\n", lines);
        }

        /// <summary>The project's files that aren't code (path files), by their path.</summary>
        public static Dictionary<string, string> Files(string source) {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Split(source))
                if (!IsCode(s.path)) files[s.path] = s.text;
            return files;
        }

        /// <summary>A file by its name as a program asks for it ("rightPath1.txt", "/usd/path.txt", "static/x.txt"):
        /// its path, else its file name anywhere in the project. Null: not there.</summary>
        public static string Find(Dictionary<string, string> files, string name) {
            if (files == null || string.IsNullOrEmpty(name)) return null;
            var n = name.Replace('\\', '/').Trim();
            if (n.StartsWith("/usd/")) n = n.Substring(5);
            n = n.TrimStart('/');
            if (files.TryGetValue(n, out var text)) return text;
            var file = Path.GetFileName(n);
            foreach (var kv in files)
                if (string.Equals(Path.GetFileName(kv.Key), file, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        /// <summary>"src/main.cpp line 42" for a line of the project (empty for a single program).</summary>
        public static string Where(string source, int line) {
            if (!IsProject(source) || line <= 0) return "";
            Section at = null;
            foreach (var s in Split(source))
                if (s.markerLine < line) at = s;
            return at == null ? "" : at.path + " line " + (line - at.markerLine);
        }

        /// <summary>Joins files into a project text (in the order given).</summary>
        public static string Join(IEnumerable<(string path, string text)> files) {
            var list = files.ToList();
            var marker = list.Any(f => f.path.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) && !list.Any(f => IsCode(f.path) && !f.path.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) ? PythonMarker : Marker;
            var b = new StringBuilder();
            foreach (var (path, text) in list) {
                if (b.Length > 0) b.Append('\n');
                b.Append(marker).Append(path.Replace('\\', '/')).Append('\n');
                b.Append(text.Replace("\r\n", "\n").TrimEnd('\n'));
            }
            return b.ToString();
        }

        /// <summary>The project with a file added (or replaced, same path). A single program becomes a project first: its
        /// code as src/main.cpp (C++) or main.py.</summary>
        public static string WithFile(string source, string path, string text) {
            var files = IsProject(source)
                ? Split(source).Select(s => (s.path, s.text)).ToList()
                : new List<(string, string)> { (Scripting.CppTranslator.LooksLikeCpp(source) ? "src/main.cpp" : "main.py", source ?? "") };
            var i = files.FindIndex(f => string.Equals(f.Item1, path, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) files[i] = (path, text);
            else files.Add((path, text));
            return Join(files);
        }

        // ------------------------------------------------------------------ project folders

        // Library headers a PROS/VEXcode project carries (the simulator has their API built in).
        private static readonly string[] LibraryFolders = { "pros", "okapi", "lemlib", "display", "fmt", "EZ-Template", "liblvgl", "lvgl", "squiggles", "gif-pros" };
        private static readonly string[] LibraryHeaders = { "vex.h", "main.h", "api.h", "v5.h", "v5_vcs.h", "v5_api.h", "v5_apitypes.h", "v5_apiuser.h", "v5_color.h" };
        private static readonly string[] PathExtensions = { ".txt", ".csv", ".json" };

        /// <summary>
        /// Reads a PROS (project.pros) or VEXcode (.v5code) project folder, or any folder with src/ or .cpp/.py files:
        /// the project's own headers (include/, not the libraries'), its source files (robot-config first, main last)
        /// and its path files (static/, usd/ or sd/ folders). Null when there's no code in it.
        /// </summary>
        public static string FromFolder(string dir, out string summary) {
            summary = "";
            var root = Path.GetFullPath(dir);
            string Rel(string f) => Path.GetFullPath(f).Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            bool Skip(string f) {
                var rel = Rel(f);
                var parts = rel.Split('/');
                if (parts.Any(x => x.StartsWith(".") || x == "bin" || x == "build" || x == "firmware" || x == "vex" || x == "node_modules")) return true;
                // Another project in a folder of this one (its own project.pros / .v5code) isn't part of it.
                var d = Path.GetDirectoryName(Path.GetFullPath(f));
                while (d != null && d.Length > root.Length) {
                    if (File.Exists(Path.Combine(d, "project.pros")) || Directory.GetFiles(d, "*.v5code").Length > 0) return true;
                    d = Path.GetDirectoryName(d);
                }
                // A copy a download made beside the file ("main (1).cpp") when the file itself is there.
                var name = Path.GetFileNameWithoutExtension(f);
                var stripped = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(\d+\)$", "");
                return stripped != name && File.Exists(Path.Combine(Path.GetDirectoryName(f), stripped + Path.GetExtension(f)));
            }
            var all = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(f => !Skip(f)).ToList();
            var headers = all.Where(f => {
                var rel = Rel(f);
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".h" && ext != ".hpp" && ext != ".hh") return false;
                var parts = rel.Split('/');
                if (parts.Take(parts.Length - 1).Any(x => LibraryFolders.Contains(x, StringComparer.OrdinalIgnoreCase))) return false;
                return !LibraryHeaders.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
            }).OrderBy(f => Rel(f), StringComparer.OrdinalIgnoreCase).ToList();
            var sources = all.Where(f => {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext == ".cpp" || ext == ".c" || ext == ".cc" || ext == ".cxx" || ext == ".py";
            }).OrderBy(f => Path.GetFileName(f).StartsWith("robot-config", StringComparison.OrdinalIgnoreCase) ? 0 : Path.GetFileNameWithoutExtension(f).Equals("main", StringComparison.OrdinalIgnoreCase) ? 2 : 1)
              .ThenBy(f => Rel(f), StringComparer.OrdinalIgnoreCase).ToList();
            if (sources.Count == 0) return null;
            var paths = all.Where(f => {
                var rel = Rel(f);
                var top = rel.Split('/')[0].ToLowerInvariant();
                return PathExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) && (top == "static" || top == "usd" || top == "sd" || top == "paths")
                       && new FileInfo(f).Length < 2_000_000;
            }).OrderBy(f => Rel(f), StringComparer.OrdinalIgnoreCase).ToList();
            var files = headers.Concat(sources).Concat(paths).Select(f => (Rel(f), File.ReadAllText(f))).ToList();
            var kind = File.Exists(Path.Combine(root, "project.pros")) ? "PROS project" : Directory.GetFiles(root, "*.v5code").Length > 0 ? "VEXcode project" : "project";
            summary = kind + ": " + sources.Count + (sources.Count == 1 ? " source file" : " source files") + (headers.Count > 0 ? ", " + headers.Count + (headers.Count == 1 ? " header" : " headers") : "")
                      + (paths.Count > 0 ? ", " + paths.Count + (paths.Count == 1 ? " path file" : " path files") : "");
            return Join(files);
        }

        // ------------------------------------------------------------------ path files

        /// <summary>A path.jerryio / LemLib path file: "x, y, speed" lines up to "endData" (inches; speed 0-127).</summary>
        public static List<(double x, double y, double speed)> ParsePath(string text) {
            var points = new List<(double, double, double)>();
            if (text == null) return points;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n')) {
                var line = raw.Trim();
                if (line == "endData") break;
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) continue;
                if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) continue;
                var speed = parts.Length > 2 && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : double.NaN;
                points.Add((x, y, speed));
            }
            return points;
        }
    }
}
