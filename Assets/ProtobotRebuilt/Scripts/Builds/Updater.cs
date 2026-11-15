using System;
using System.Threading.Tasks;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using Velopack;
using Velopack.Sources;

namespace Protobot.Builds {
    // V2: updates (Velopack). The app is installed by Setup.exe from the GitHub releases page; at start it looks for a
    // newer release there and offers it: Update now downloads only what changed (a delta when it can), then restarts
    // into the new version; Later asks again next start. Help > Check for updates looks on demand. A copy that wasn't
    // installed (unzipped, or the editor) can't update itself: it says so and links the releases page.
    public static class Updater {
        public const string RepoUrl = "https://github.com/Cranezz/Protobot-Rebuilt";
        public const string ReleasesUrl = RepoUrl + "/releases/latest";

        /// <summary>The version Velopack releases carry: 1.9.&lt;build&gt; (semantic versioning).</summary>
        public static string ReleaseVersion => BuildInfo.Beta + "." + BuildInfo.Build;

        private static bool checking;
        private static volatile int progress = -1;

        // Velopack's install/update/uninstall hooks run the app with special arguments: it handles them and quits
        // before anything else starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hooks() {
            if (Application.isEditor) return;
            try { VelopackApp.Build().Run(); }
            catch (Exception e) { Debug.LogWarning("V2: Velopack start-up: " + e.Message); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AtStart() {
            if (Application.isEditor) return;
            var host = new GameObject("Updater").AddComponent<Runner>();
            UnityEngine.Object.DontDestroyOnLoad(host.gameObject);
        }

        private class Runner : MonoBehaviour {
            private float at;

            private void Start() => at = Time.realtimeSinceStartup + 6f;   // after the home menu is up

            private void Update() {
                if (at > 0f && Time.realtimeSinceStartup > at) {
                    at = -1f;
                    _ = Check(false);
                }
            }
        }

        private static UpdateManager Manager() => new(new GithubSource(RepoUrl, null, false));

        /// <summary>Looks for a newer release; `manual`: also says when there's none (Help > Check for updates).</summary>
        public static async Task Check(bool manual) {
            if (checking) return;
            checking = true;
            try {
                if (Application.isEditor) {
                    if (manual) Interface.Interface.Say("Updates work in the installed app (not the editor)", false);
                    return;
                }
                var manager = Manager();
                if (!manager.IsInstalled) {
                    if (manual) NotInstalled();
                    return;
                }
                var info = await manager.CheckForUpdatesAsync();
                if (info == null) {
                    if (manual) Interface.Interface.Announce("Protobot is up to date (" + BuildInfo.Display + ")");
                    return;
                }
                Offer(manager, info);
            }
            catch (Exception e) {
                Debug.LogWarning("V2: update check failed: " + e.Message);
                if (manual) Interface.Interface.Say("Couldn't check for updates: " + e.Message, false);
            }
            finally {
                checking = false;
            }
        }

        private static void NotInstalled() {
            var dialog = Dialog.Open("Updates", new Vector2(460f, 220f));
            var text = Ui.Text(dialog.Body, "Text", "This copy of Protobot wasn't installed with Setup, so it can't update itself. Get the latest version (and Setup, which keeps it up to date) from the releases page.", Theme.FontBody, Theme.TextDim);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.Size(-1, 80f);
            Ui.Button(dialog.Footer, "Open", "Open the releases page", () => {
                Application.OpenURL(ReleasesUrl);
                dialog.Close();
            }, true, 6f).Size(-1, 34f);
            Ui.Button(dialog.Footer, "Close", "Close", dialog.Close, false, 6f).Size(-1, 34f);
        }

        private static void Offer(UpdateManager manager, UpdateInfo info) {
            var version = info.TargetFullRelease.Version.ToString();
            var dialog = Dialog.Open("Update available", new Vector2(480f, 250f));
            var text = Ui.Text(dialog.Body, "Text", "Protobot " + version + " is out (you have " + ReleaseVersion + "). Update now: it downloads what changed and restarts into the new version. Save your build first.", Theme.FontBody, Theme.TextDim);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.Size(-1, 70f);
            var status = Ui.Text(dialog.Body, "Status", "", Theme.FontSmall, Theme.Accent);
            status.Size(-1, 22f);
            UnityEngine.UI.Button now = null;
            now = Ui.Button(dialog.Footer, "Update", "Update now", async () => {
                now.interactable = false;
                progress = 0;
                var watcher = status.gameObject.AddComponent<ProgressText>();
                watcher.label = status;
                try {
                    await manager.DownloadUpdatesAsync(info, p => progress = p);
                    status.text = "Restarting…";
                    manager.ApplyUpdatesAndRestart(info);
                }
                catch (Exception e) {
                    Debug.LogWarning("V2: update failed: " + e.Message);
                    status.color = Theme.Danger;
                    status.text = "The update failed: " + e.Message;
                    progress = -1;
                    now.interactable = true;
                }
            }, true, 6f);
            now.Size(-1, 34f);
            Ui.Button(dialog.Footer, "Later", "Later", dialog.Close, false, 6f).Size(-1, 34f);
        }

        // The download's progress (reported from another thread) shown on the main thread.
        private class ProgressText : MonoBehaviour {
            public TextMeshProUGUI label;

            private void Update() {
                if (label != null && progress >= 0) label.text = "Downloading… " + progress + "%";
            }
        }
    }
}
