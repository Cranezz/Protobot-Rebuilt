using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Velopack;
using Velopack.Sources;

namespace Protobot.Builds {
    // V2: updates (Velopack). Protobot is installed by Setup.exe from the GitHub releases page; its main exe is a small
    // launcher (Tools/Launcher) that runs Velopack's install/update hooks itself and starts this app. At start the app
    // looks for a newer release and offers it ("Update available": the version, what's new, the download size);
    // Update now shows the updating screen (a progress bar with the megabytes), then hands the update to Velopack's
    // Update.exe and closes; Update.exe installs it and starts the new version. Help > Check for updates looks on
    // demand. A copy that wasn't installed (unzipped, or the editor) can't update itself: it links the releases page.
    public static class Updater {
        public const string RepoUrl = "https://github.com/Cranezz/Protobot-Rebuilt";
        public const string ReleasesUrl = RepoUrl + "/releases/latest";

        /// <summary>The version Velopack releases carry: 1.9.&lt;build&gt; (semantic versioning).</summary>
        public static string ReleaseVersion => BuildInfo.Beta + "." + BuildInfo.Build;

        private static bool checking;

        // Velopack's hooks normally start the launcher, not this app; if one ever starts the app itself, it just quits
        // (the hook has nothing to set up here).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hooks() {
            if (Application.isEditor) return;
            if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("--veloapp-", StringComparison.OrdinalIgnoreCase))) {
                Debug.Log("[Updater] Velopack hook start: quitting");
                Quit();
                return;
            }
            try {
                // Not on its own at start: an update is only applied when the player says so.
                VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
                Debug.Log("[Updater] Velopack ready");
            }
            catch (Exception e) { Debug.LogWarning("V2: Velopack start-up: " + e.Message); }
        }

        // Quits now; if Unity is slow to let go, the process is ended (Update.exe waits for it to be gone).
        private static void Quit() {
            Application.Quit(0);
            new Thread(() => {
                Thread.Sleep(3000);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }) { IsBackground = true }.Start();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AtStart() {
            if (Application.isEditor) return;
            var host = new GameObject("Updater").AddComponent<Runner>();
            UnityEngine.Object.DontDestroyOnLoad(host.gameObject);
        }

        private class Runner : MonoBehaviour {
            private float at;

            private void Start() => at = Time.realtimeSinceStartup + 6f;   // after the intro and home menu

            private void Update() {
                if (at > 0f && Time.realtimeSinceStartup > at) {
                    at = -1f;
                    _ = Check(false);
                }
            }
        }

        // Testing: "-update-from <folder>" reads releases from a folder instead of GitHub; "-update-now" takes an update
        // without asking.
        private static string Arg(string name) {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static bool UpdateNow => Environment.GetCommandLineArgs().Contains("-update-now");

        private static UpdateManager Manager() {
            var folder = Arg("-update-from");
            return folder != null ? new UpdateManager(new SimpleFileSource(new System.IO.DirectoryInfo(folder))) : new UpdateManager(new GithubSource(RepoUrl, null, false));
        }

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
                    Debug.Log("[Updater] not an installed copy: no update check");
                    if (manual) NotInstalled();
                    return;
                }
                var info = await manager.CheckForUpdatesAsync();
                Debug.Log("[Updater] installed " + manager.CurrentVersion + ", newest " + (info != null ? info.TargetFullRelease.Version.ToString() : "the same"));
                if (info == null) {
                    if (manual) Interface.Interface.Announce("Protobot is up to date (" + BuildInfo.Display + ")");
                    return;
                }
                if (UpdateNow) UpdateScreen.Run(manager, info);
                else Offer(manager, info);
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

        /// <summary>What the download is: the deltas from this version when there are any, else the full package.</summary>
        private static long DownloadBytes(UpdateInfo info) {
            var deltas = info.DeltasToTarget;
            return deltas != null && deltas.Length > 0 ? deltas.Sum(d => d.Size) : info.TargetFullRelease.Size;
        }

        public static string Megabytes(long bytes) => bytes < 1024 * 1024 ? Mathf.Max(1, Mathf.RoundToInt(bytes / 1024f)) + " KB" : (bytes / 1048576f).ToString("0.0") + " MB";

        // "**Bold:** text" and "- item" become plain lines for the window.
        private static string Plain(string markdown) {
            if (string.IsNullOrWhiteSpace(markdown)) return "";
            var lines = markdown.Replace("\r", "").Split('\n')
                .Where(l => !l.StartsWith("#"))
                .Select(l => l.Replace("**", "").Replace("`", "").TrimEnd())
                .Select(l => l.StartsWith("- ") ? "•  " + l.Substring(2) : l.StartsWith("  ") ? "    " + l.Trim() : l);
            var text = string.Join("\n", lines).Trim();
            return text.Length > 900 ? text.Substring(0, 900).TrimEnd() + "…" : text;
        }

        private static void Offer(UpdateManager manager, UpdateInfo info) {
            var version = info.TargetFullRelease.Version.ToString();
            var dialog = Dialog.Open("Update available", new Vector2(560f, 470f));
            var head = Ui.Text(dialog.Body, "Head", "Protobot " + version + " is ready", Theme.FontTitle + 4f, Theme.Text, Theme.Medium);
            head.Size(-1, 34f);
            var sub = Ui.Text(dialog.Body, "Sub", "You have " + ReleaseVersion + ".  Download: " + Megabytes(DownloadBytes(info)) + ".  Protobot restarts when it's done: save your build first.",
                Theme.FontSmall, Theme.TextDim);
            sub.textWrappingMode = TextWrappingModes.Normal;
            sub.Size(-1, 40f);
            Dialog.Heading(dialog.Body, "What's new").Size(-1, 24f);
            var notes = Ui.Text(dialog.Body, "Notes", Plain(info.TargetFullRelease.NotesMarkdown), Theme.FontSmall, Theme.Text);
            notes.textWrappingMode = TextWrappingModes.Normal;
            notes.overflowMode = TextOverflowModes.Ellipsis;
            notes.Size(-1, 250f);
            Ui.Button(dialog.Footer, "Update", "Update now", () => {
                dialog.Close();
                UpdateScreen.Run(manager, info);
            }, true, 6f).Size(-1, 34f);
            Ui.Button(dialog.Footer, "Later", "Later", dialog.Close, false, 6f).Size(-1, 34f);
        }

        // The updating screen: over everything, the logo, the version, a progress bar with the megabytes, then
        // "Installing" while Update.exe takes over. Cancel stops the download.
        private class UpdateScreen : MonoBehaviour {
            private static volatile int progress;
            private UpdateManager manager;
            private UpdateInfo info;
            private CancellationTokenSource cancel;
            private Image fill;
            private TextMeshProUGUI status, amount;
            private Button cancelButton;
            private long bytes;
            private bool installing;
            private float shown;

            public static void Run(UpdateManager manager, UpdateInfo info) {
                var go = new GameObject("Updating");
                DontDestroyOnLoad(go);
                var screen = go.AddComponent<UpdateScreen>();
                screen.manager = manager;
                screen.info = info;
                screen.Build();
                screen.Download();
            }

            private void Build() {
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 600;
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                gameObject.AddComponent<GraphicRaycaster>();
                var root = (RectTransform)transform;
                Ui.Image(root, "Back", new Color(0.04f, 0.05f, 0.07f, 1f), 0f, true).rectTransform.Fill();
                var blurred = Online.Backgrounds.Blurred();
                if (blurred != null) {
                    var picture = Ui.Rect("Picture", root).gameObject.AddComponent<RawImage>();
                    picture.texture = blurred;
                    picture.raycastTarget = false;
                    picture.color = new Color(1f, 1f, 1f, 0.35f);
                    var fit = picture.gameObject.AddComponent<AspectRatioFitter>();
                    fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    fit.aspectRatio = (float)blurred.width / blurred.height;
                    picture.gameObject.AddComponent<Online.BackgroundDrift>();
                }
                var card = Ui.Panel(root, "Card", new Color(0.07f, 0.08f, 0.1f, 0.85f), 16f).rectTransform;
                card.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 300f));
                var word = Resources.Load<Texture2D>("Intro/Wordmark");
                var gear = Resources.Load<Texture2D>("Intro/Gear");
                if (gear != null) {
                    var g = Ui.Rect("Gear", card).gameObject.AddComponent<RawImage>();
                    g.texture = gear;
                    g.raycastTarget = false;
                    g.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-150f, -62f), new Vector2(64f * gear.width / gear.height, 64f));
                    g.gameObject.AddComponent<Spin>();
                }
                if (word != null) {
                    var w = Ui.Rect("Word", card).gameObject.AddComponent<RawImage>();
                    w.texture = word;
                    w.raycastTarget = false;
                    w.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(40f, -62f), new Vector2(46f * word.width / word.height, 46f));
                }
                var title = Ui.Text(card, "Title", "Updating to " + info.TargetFullRelease.Version, Theme.FontTitle + 2f, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
                title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -122f), new Vector2(560f, 30f));
                var track = Ui.Image(card, "Track", Theme.Field, 6f);
                track.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(520f, 14f));
                fill = Ui.Image(track.transform, "Fill", Theme.Accent, 6f);
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = new Vector2(0f, 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                status = Ui.Text(card, "Status", "Downloading…", Theme.FontBody, Theme.TextDim);
                status.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-60f, -202f), new Vector2(400f, 26f));
                amount = Ui.Text(card, "Amount", "", Theme.FontBody, Theme.TextDim, null, TextAlignmentOptions.MidlineRight);
                amount.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(160f, -202f), new Vector2(200f, 26f));
                cancelButton = Ui.Button(card, "Cancel", "Cancel", () => cancel?.Cancel(), false, 6f, Theme.FontSmall);
                ((RectTransform)cancelButton.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(120f, 32f));
                bytes = DownloadBytes(info);
            }

            private async void Download() {
                progress = 0;
                cancel = new CancellationTokenSource();
                try {
                    await manager.DownloadUpdatesAsync(info, p => progress = p, cancel.Token);
                    progress = 100;
                    Install();
                }
                catch (Exception e) {
                    if (this == null) return;
                    var cancelled = e is OperationCanceledException || cancel.IsCancellationRequested;
                    Debug.LogWarning("V2: update download: " + e.Message);
                    if (cancelled) {
                        Destroy(gameObject);
                        return;
                    }
                    status.text = "The download failed: " + e.Message;
                    status.color = Theme.Danger;
                    cancelButton.GetComponentInChildren<TextMeshProUGUI>().text = "Close";
                    cancelButton.onClick.RemoveAllListeners();
                    cancelButton.onClick.AddListener(() => Destroy(gameObject));
                }
            }

            // Update.exe waits for this app to close, installs the new version and starts it.
            private void Install() {
                installing = true;
                status.text = "Installing… Protobot will restart by itself";
                amount.text = "";
                cancelButton.gameObject.SetActive(false);
                try {
                    manager.WaitExitThenApplyUpdates(info.TargetFullRelease, false, true, null);
                    Debug.Log("[Updater] applying " + info.TargetFullRelease.Version + ": closing");
                    Invoke(nameof(Close), 0.8f);
                }
                catch (Exception e) {
                    Debug.LogWarning("V2: update install: " + e.Message);
                    installing = false;
                    status.text = "Couldn't start the install: " + e.Message;
                    status.color = Theme.Danger;
                }
            }

            private void Close() => Quit();

            private void Update() {
                var p = Mathf.Clamp(progress, 0, 100);
                shown = Mathf.MoveTowards(shown, installing ? 1f : p / 100f, Time.unscaledDeltaTime * 1.5f);
                fill.rectTransform.anchorMax = new Vector2(shown, 1f);
                if (!installing && status.color != Theme.Danger) {
                    status.text = "Downloading…  " + p + "%";
                    amount.text = Megabytes((long)(bytes * p / 100.0)) + " of " + Megabytes(bytes);
                }
            }

            private class Spin : MonoBehaviour {
                private void Update() => transform.Rotate(0f, 0f, -90f * Time.unscaledDeltaTime);
            }
        }
    }
}
