using System;
using UnityEngine;

namespace Protobot.Interface {
    /// <summary>
    /// V2 (build 77, asked for by a tester: the settings were in four windows under the Edit menu, hard to find): one
    /// Settings window (the top bar's gear, Edit > Settings…) with its sections side by side in its title bar:
    /// Preferences (General / Camera / Parts list), Graphics, Keyboard shortcuts and Controller. Each section is the
    /// window it used to be, built into this one (`Fill(dialog)`); their old `Open()`s open Settings at that section.
    /// The section last shown is opened again (PlayerPrefs V2.Settings.Section).
    /// </summary>
    public class SettingsWindow : MonoBehaviour {
        public enum Section { Preferences, Graphics, Keys, Controller }

        private static readonly string[] Names = { "Preferences", "Graphics", "Keyboard shortcuts", "Controller" };
        private static readonly Vector2 Size = new(1240f, 720f);
        private const string LastKey = "V2.Settings.Section";

        private static SettingsWindow open;
        private Dialog dialog;
        private Section section;
        private MonoBehaviour page;
        private RectTransform nav;

        public static bool IsOpen => open != null;

        /// <summary>Opens Settings at the section shown last.</summary>
        public static void Open() {
            var last = PlayerPrefs.GetInt(LastKey, 0);
            Open(Enum.IsDefined(typeof(Section), last) ? (Section)last : Section.Preferences);
        }

        public static void Open(Section to) {
            if (to == Section.Graphics && GraphicsQuality.GraphicsSettingsManager.Current == null) {
                Interface.Say("The graphics settings aren't available (no URP asset)", false);
                to = Section.Preferences;
            }
            if (open == null) {
                var dialog = Dialog.Open("Settings", Size);
                open = dialog.gameObject.AddComponent<SettingsWindow>();
                open.dialog = dialog;
                open.nav = Ui.Rect("Sections", dialog.Title.transform.parent)
                    .Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 32f));
                Ui.Row(open.nav, 0f);
            }
            else if (open.page != null && open.section == to) return;
            open.Show(to);
        }

        /// <summary>The section showing, or null when Settings is closed.</summary>
        public static Section? Showing => open != null ? open.section : null;

        private void Show(Section to) {
            if (page != null) {
                page.enabled = false;
                Destroy(page);
                dialog.ResetContent();
            }
            section = to;
            PlayerPrefs.SetInt(LastKey, (int)to);
            foreach (Transform child in nav) Destroy(child.gameObject);
            ToolOptions.Segmented(nav, Names, (int)to, i => {
                if ((Section)i == section) return;
                Audio.Sounds.Play(Audio.Sounds.Tick);
                Show((Section)i);
            });
            page = to switch {
                Section.Graphics => GraphicsWindow.Fill(dialog),
                Section.Keys => Tools.KeybindsWindow.Fill(dialog),
                Section.Controller => Devices.ControllerScreen.Fill(dialog),
                _ => PreferencesWindow.Fill(dialog),
            };
        }

        private void OnDestroy() {
            if (open == this) open = null;
        }
    }
}
