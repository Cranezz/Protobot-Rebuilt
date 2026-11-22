using System.Text.RegularExpressions;
using UnityEngine;

namespace Protobot.Builds {
    /// <summary>V2: which version this is: Protobot Rebuilt V2, version 2.0, its beta number and a build number that goes
    /// up by one with every Windows build (Editor/BuildNumberStep), so small updates can be told apart in bug reports. The
    /// build number lives in Resources/Version/build.txt. Shown as "v2.0 Beta 1 (build 70)"; the installer's package
    /// version (Velopack, semantic versioning) is "2.0.0-beta.1.70", which sorts after every 1.9.N build.</summary>
    public static class BuildInfo {
        public const string Product = "Protobot Rebuilt V2";
        public const string Version = "2.0";
        public const int Beta = 1;

        private static int? build;

        public static int Build {
            get {
                if (build == null) {
                    // V2: a built app's own version ("2.0 Beta 1 build 70") first: the player build bumps the number in
                    // its preprocess step, after Resources/Version/build.txt was already packed (it showed one less).
                    var version = Application.version ?? "";
                    var at = version.LastIndexOf("build ", System.StringComparison.Ordinal);
                    if (!Application.isEditor && at >= 0 && int.TryParse(version.Substring(at + 6).Trim(), out var fromVersion)) build = fromVersion;
                    else {
                        var text = Resources.Load<TextAsset>("Version/build");
                        build = text != null && int.TryParse(text.text.Trim(), out var n) ? n : 0;
                    }
                }
                return build.Value;
            }
        }

        /// <summary>"v2.0 Beta 1 · Build 70" (the home menu).</summary>
        public static string Display => "v" + Version + " Beta " + Beta + " · Build " + Build;

        /// <summary>"v2.0 Beta 1 (build 70)" (release titles, the update window).</summary>
        public static string Label => Name(Build);
        public static string Name(int number) => "v" + Version + " Beta " + Beta + " (build " + number + ")";

        /// <summary>"Protobot Rebuilt V2, v2.0 Beta 1 build 70 (editor)" (logs and reports).</summary>
        public static string Full => Product + ", v" + Version + " Beta " + Beta + " build " + Build + (Application.isEditor ? " (editor)" : "");

        /// <summary>What a Windows build's version is set to (PlayerSettings.bundleVersion = Application.version).</summary>
        public static string BundleVersion(int number) => Version + " Beta " + Beta + " build " + number;

        /// <summary>The installer's package version: "2.0.0-beta.1.70".</summary>
        public static string PackageVersion(int number) => Version + ".0-beta." + Beta + "." + number;

        /// <summary>A package version as people read it: "2.0.0-beta.1.70" -> "v2.0 Beta 1 (build 70)", "1.9.69" -> "Beta 1.9
        /// (build 69)" (the releases before 2.0).</summary>
        public static string Readable(string package) {
            if (string.IsNullOrEmpty(package)) return "";
            var beta = Regex.Match(package, @"^(\d+)\.(\d+)\.\d+-beta\.(\d+)\.(\d+)$");
            if (beta.Success) return "v" + beta.Groups[1].Value + "." + beta.Groups[2].Value + " Beta " + beta.Groups[3].Value + " (build " + beta.Groups[4].Value + ")";
            var old = Regex.Match(package, @"^(\d+\.\d+)\.(\d+)$");
            if (old.Success) return "Beta " + old.Groups[1].Value + " (build " + old.Groups[2].Value + ")";
            return package;
        }
    }
}
