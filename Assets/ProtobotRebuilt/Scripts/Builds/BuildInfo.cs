using UnityEngine;

namespace Protobot.Builds {
    /// <summary>V2: which version this is: Protobot Rebuilt V2, its beta number and a build number that goes up by one
    /// with every Windows build (Editor/BuildNumberStep), so small updates can be told apart in bug reports. The build
    /// number lives in Resources/Version/build.txt.</summary>
    public static class BuildInfo {
        public const string Product = "Protobot Rebuilt V2";
        public const string Beta = "1.9";

        private static int? build;

        public static int Build {
            get {
                if (build == null) {
                    // V2: a built app's own version ("Beta 1.9 build 50") first: the player build bumps the number in
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

        /// <summary>"V2 · Beta 1.9 · Build 12" (the home menu).</summary>
        public static string Display => "V2 · Beta " + Beta + " · Build " + Build;

        /// <summary>"Protobot Rebuilt V2, Beta 1.9 build 12 (editor)" (logs and reports).</summary>
        public static string Full => Product + ", Beta " + Beta + " build " + Build + (Application.isEditor ? " (editor)" : "");

        /// <summary>What a Windows build's version is set to (PlayerSettings.bundleVersion = Application.version).</summary>
        public static string BundleVersion(int number) => "Beta " + Beta + " build " + number;
    }
}
