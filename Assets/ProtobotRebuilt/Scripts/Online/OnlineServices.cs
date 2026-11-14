using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Protobot.Online {
    /// <summary>V2: Unity Gaming Services for playing online: started once, the player signed in (anonymously for now:
    /// an id kept on this computer; accounts come with the leaderboard), and the name others see.
    /// Needs the project linked to a Unity Cloud project (Edit > Project Settings > Services) with Relay and Lobby on.</summary>
    public static class OnlineServices {
        /// <summary>Why online play can't start (empty when it can).</summary>
        public static string Problem { get; private set; } = "";
        public static bool SignedIn => UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;
        public static string PlayerId => SignedIn ? AuthenticationService.Instance.PlayerId : "";

        private static Task<bool> starting;

        /// <summary>The name shown to other players (kept on this computer).</summary>
        public static string Name {
            get {
                var name = PlayerPrefs.GetString("V2.Online.Name", "");
                if (string.IsNullOrWhiteSpace(name)) {
                    name = "Player " + UnityEngine.Random.Range(1000, 10000);
                    PlayerPrefs.SetString("V2.Online.Name", name);
                }
                return name;
            }
            set {
                value = Clean(value);
                if (value.Length > 0) PlayerPrefs.SetString("V2.Online.Name", value);
            }
        }

        public static string Clean(string name) {
            name = (name ?? "").Trim();
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++) if (char.IsControl(chars[i]) || chars[i] == '<' || chars[i] == '>') chars[i] = ' ';
            name = new string(chars).Trim();
            return name.Length > 24 ? name.Substring(0, 24) : name;
        }

        /// <summary>Starts the services and signs in (once); false with Problem set when it can't.</summary>
        public static Task<bool> Start() {
            if (SignedIn) return Task.FromResult(true);
            if (starting == null || starting.IsCompleted && !starting.Result) starting = StartNow();
            return starting;
        }

        private static async Task<bool> StartNow() {
            Problem = "";
            if (string.IsNullOrEmpty(Application.cloudProjectId)) {
                Problem = Application.isEditor
                    ? "Online play isn't set up in this project yet: link it to a Unity Cloud project (Edit > Project Settings > Services)."
                    : "Online play isn't set up in this version of Protobot.";
                return false;
            }
            try {
                if (UnityServices.State != ServicesInitializationState.Initialized) {
                    var options = new InitializationOptions();
                    // Two copies on one computer (testing, or a second window) need their own sign-in.
                    var profile = Profile();
                    if (!string.IsNullOrEmpty(profile)) options.SetProfile(profile);
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Builds.ActionLog.Write("online", "signed in as " + AuthenticationService.Instance.PlayerId + " (" + Name + ")");
                return true;
            }
            catch (Exception e) {
                Problem = Explain(e);
                Debug.LogWarning("[Online] Couldn't sign in: " + e);
                return false;
            }
        }

        // "-profile <name>" on the command line, or a different one per editor clone (Multiplayer Play Mode).
        private static string Profile() {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-profile") return Sanitize(args[i + 1]);
            if (Application.isEditor && Application.dataPath.Contains("Library/VP/")) return "clone" + Math.Abs(Application.dataPath.GetHashCode() % 1000);
            return "";
        }

        private static string Sanitize(string s) {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s) if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            return sb.Length > 30 ? sb.ToString(0, 30) : sb.ToString();
        }

        /// <summary>A sentence for the player from a services error.</summary>
        public static string Explain(Exception e) {
            var text = e.Message ?? "";
            if (e is RequestFailedException failed && (failed.ErrorCode == CommonErrorCodes.TransportError || failed.ErrorCode == CommonErrorCodes.Timeout))
                return "Couldn't reach the online services: check your internet connection.";
            if (text.Contains("network") || text.Contains("Network") || text.Contains("resolve host")) return "Couldn't reach the online services: check your internet connection.";
            return text.Length > 160 ? text.Substring(0, 160) + "…" : text;
        }
    }
}
