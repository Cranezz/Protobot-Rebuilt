using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Builds {
    // V2: saving reminders and autosave.
    // - Autosave (File > Autosave, remembered): every minute, a build with changes that has a file is saved to it.
    // - A new build that's been worked on for a couple of minutes without a file gets a notice: save it now, and
    //   optionally turn on autosave.
    // - With autosave off, 20 minutes of unsaved changes bring a reminder (and again every 20 minutes).
    // Notices sit at the bottom right with their buttons; they never block the build.
    public class AutoSave : MonoBehaviour {
        private const string EnabledKey = "V2.AutoSave";
        private const float Interval = 60f;
        private const float RemindAfter = 20f * 60f;
        private const float NewFileAfter = 2f * 60f;   // within the first five minutes of a new build

        private static AutoSave instance;
        private BuildsManager builds;
        private float lastSave, lastAutoSave, lastReminder;
        private float buildStarted;
        private bool askedAboutNewFile;
        private RectTransform notice;

        public static bool Enabled {
            get => PlayerPrefs.GetInt(EnabledKey, 0) == 1;
            set {
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                if (instance != null) instance.lastAutoSave = Time.unscaledTime;
            }
        }

        public static void Create(Transform root) => root.gameObject.AddComponent<AutoSave>();

        public static void Toggle() {
            Enabled = !Enabled;
            Interface.Interface.Say(Enabled ? "Autosave is on: the build saves every minute" + (instance != null && instance.builds != null && instance.builds.buildPath == "" ? " once it has a file (Save it first)" : "") : "Autosave is off", Enabled);
        }

        private void Awake() {
            instance = this;
            builds = FindFirstObjectByType<BuildsManager>(FindObjectsInactive.Include);
            lastSave = lastAutoSave = lastReminder = buildStarted = Time.unscaledTime;
            if (builds != null) {
                builds.OnSaveBuild.AddListener(_ => {
                    lastSave = lastReminder = Time.unscaledTime;
                    CloseNotice();
                });
                builds.OnLoadBuild.AddListener(_ => {
                    lastSave = lastReminder = buildStarted = Time.unscaledTime;
                    askedAboutNewFile = false;
                });
            }
        }

        private void Update() {
            if (builds == null) return;
            var now = Time.unscaledTime;
            var hasFile = builds.buildPath != "";

            if (Enabled && hasFile && now - lastAutoSave >= Interval) {
                lastAutoSave = now;
                if (builds.HasUnsavedChanges() && Interface.Dialog.OpenCount == 0 && !Polycarb.SheetEditor.IsOpen) builds.Save();
                return;
            }
            // Checking for changes reads the whole build: only now and then.
            if (Time.frameCount % 120 != 0 || notice != null) return;

            if (!hasFile && !askedAboutNewFile && now - buildStarted >= NewFileAfter && now - buildStarted < 5f * 60f && builds.HasUnsavedChanges()) {
                askedAboutNewFile = true;
                ShowNotice("This build isn't saved yet. Save it now, and save every minute from then on?",
                    ("Save + autosave", () => {
                        Enabled = true;
                        builds.Save();
                    }),
                    ("Just save", () => builds.Save()),
                    ("Not now", null));
                return;
            }
            if (!Enabled && now - lastReminder >= RemindAfter && builds.HasUnsavedChanges()) {
                lastReminder = now;
                var minutes = Mathf.RoundToInt((now - lastSave) / 60f);
                ShowNotice("You haven't saved in " + minutes + " minutes.",
                    ("Save now", () => builds.Save()),
                    ("Turn on autosave", () => {
                        Enabled = true;
                        if (builds.buildPath == "") builds.Save();
                        else Interface.Interface.Say("Autosave is on: the build saves every minute");
                    }),
                    ("Later", null));
            }
        }

        private void ShowNotice(string message, params (string label, System.Action action)[] buttons) {
            CloseNotice();
            var panel = Ui.Panel(Interface.Interface.Root, "Save Notice", Theme.PanelRaised, 10f).rectTransform;
            panel.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            panel.GetChild(0).GetComponent<Image>().raycastTarget = true;
            panel.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-370f, 24f), new Vector2(420f, 0f));
            Ui.Column(panel, 8f, new RectOffset(14, 14, 12, 12));
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = Ui.Text(panel, "Message", message, Theme.FontBody, Theme.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.Size(-1, 44f);
            var row = Ui.Rect("Buttons", panel);
            Ui.Row(row, 6f, null, TextAnchor.MiddleRight);
            row.Size(-1, 32f);
            for (var i = 0; i < buttons.Length; i++) {
                var (label, action) = buttons[i];
                var button = Ui.Button(row, label, label, () => {
                    CloseNotice();
                    if (action != null) action();
                    else Interface.Interface.Say("Not saved", false);
                }, i == 0, 6f, Theme.FontSmall);
                button.Size(-1, 30f);
            }
            notice = panel;
            Interface.Interface.Say(message, false);
        }

        private void CloseNotice() {
            if (notice != null) Destroy(notice.gameObject);
            notice = null;
        }
    }
}
