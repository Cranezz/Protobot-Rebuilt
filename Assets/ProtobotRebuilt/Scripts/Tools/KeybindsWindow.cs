using System.Collections.Generic;
using System.Linq;
using Protobot.InputEvents;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Tools {
    // V2: Edit > Keyboard shortcuts… (and Preferences' footer button): every key in the app, changeable.
    // The original's shortcuts (InputEvents: tools, undo, delete…) are changed through their own RebindAction, as its
    // Preferences page does; the V2 keys through Keybinds. Click a key, press the new one (with Ctrl/Shift/Alt if
    // wanted); Esc keeps it, Backspace clears it. Keys used twice in the same place are marked.
    public class KeybindsWindow : MonoBehaviour {
        private static KeybindsWindow open;
        private Dialog dialog;
        private RectTransform list;
        private string search = "";
        private Keybinds.Bind capturing;
        private InputEvent capturingOriginal;
        private int captureFrame;
        private ScrollRect scroll;

        public static void Open() {
            if (open != null) return;
            var dialog = Dialog.Open("Keyboard shortcuts", new Vector2(680f, 640f));
            var window = dialog.gameObject.AddComponent<KeybindsWindow>();
            window.dialog = dialog;
            open = window;
            window.Build();
            Interface.Interface.Say("Click a key to change it");
        }

        private static List<InputEvent> Originals() =>
            FindObjectsByType<InputEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(e => e != null && e.defaultAction != null && e.IsKeyboardOnly && e.ShortcutKey != UnityEngine.InputSystem.Key.Escape)
                .GroupBy(e => e.name).Select(g => g.First())
                .OrderBy(e => e.name).ToList();

        private void Build() {
            var body = dialog.Body;
            Ui.Column(body, 8f);
            var top = Ui.Rect("Top", body);
            Ui.Row(top, 8f);
            top.Size(-1, 34f);
            var field = Ui.Field(top, "Search", "", text => { search = text ?? ""; Rebuild(); });
            field.Size(-1, 32f, 1f);
            field.onValueChanged.AddListener(text => { search = text ?? ""; Rebuild(); });
            if (field.placeholder is TMP_Text hint) hint.text = "Search shortcuts";

            var frame = Ui.Image(body, "List", Theme.Field, 8f);
            frame.Size(-1, -1).GetComponent<LayoutElement>().flexibleHeight = 1f;
            var viewport = frame.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            list = Ui.Rect("Rows", viewport);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.sizeDelta = Vector2.zero;
            Ui.Column(list, 2f, new RectOffset(8, 8, 6, 8));
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = list;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var resetAll = Ui.Button(dialog.Footer, "Reset all", "Reset all", ResetAll, false, 6f);
            resetAll.Size(-1f, 36f);
            Ui.Tip(resetAll, "Reset all", null, "Every shortcut back to its first key.");
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(-1f, 36f);
            Keybinds.Changed += Rebuild;
            Rebuild();
        }

        private void OnDestroy() {
            Keybinds.Changed -= Rebuild;
            if (open == this) open = null;
        }

        private bool Matches(string group, string label) =>
            string.IsNullOrEmpty(search) || label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0
                                          || group.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private void Rebuild() {
            if (list == null) return;
            foreach (Transform child in list) Destroy(child.gameObject);

            // Keys used twice in the same place.
            var originals = Originals();
            var originalKeys = originals.Select(e => (e, text: e.CurrentKeysText)).ToList();

            const string app = "Tools and app";
            if (originals.Any(e => Matches(app, e.name))) {
                Heading(app);
                foreach (var (e, text) in originalKeys) {
                    if (!Matches(app, e.name)) continue;
                    var clash = originalKeys.Where(o => o.e != e && o.text == text && text != "").Select(o => o.e.name).FirstOrDefault()
                                ?? Keybinds.All.Where(b => b.id == "Mirror" && Keybinds.Display(b.Key) == text && text != "").Select(b => b.label).FirstOrDefault();
                    var changed = e.rebindAction != null && !e.rebindAction.IsEmpty;
                    Row(e.name, capturingOriginal == e ? "press a key…" : (text == "" ? "none" : text), changed, clash,
                        () => StartOriginal(e), () => { e.rebindAction.ResetRebinds(); Interface.Interface.Say(e.name + " is back to " + e.CurrentKeysText); Rebuild(); });
                }
            }

            foreach (var group in Keybinds.All.GroupBy(b => b.group)) {
                var shown = group.Where(b => Matches(group.Key, b.label)).ToList();
                if (shown.Count == 0) continue;
                Heading(group.Key);
                foreach (var bind in shown) {
                    var key = bind.Key;
                    string clash = null;
                    if (key != "") {
                        clash = Keybinds.All.Where(b => b != bind && b.Key == key && SamePlace(b.group, bind.group)).Select(b => b.label).FirstOrDefault();
                        if (clash == null && bind.id == "Mirror")
                            clash = originalKeys.Where(o => o.text == Keybinds.Display(key)).Select(o => o.e.name).FirstOrDefault();
                    }
                    var b2 = bind;
                    Row(bind.label, capturing == bind ? "press a key…" : (key == "" ? "none" : Keybinds.Display(key)), bind.Changed, clash,
                        () => StartV2(b2), () => { Keybinds.Reset(b2.id); Interface.Interface.Say(b2.label + " is back to " + (b2.defaultKey == "" ? "no key" : Keybinds.Display(b2.defaultKey))); });
                }
            }
        }

        private static bool SamePlace(string a, string b) {
            if (a == b) return true;
            bool Sketch(string g) => g.StartsWith("Sketch editor");
            return Sketch(a) && Sketch(b);
        }

        private void Heading(string text) {
            var heading = Ui.Text(list, "Heading", text.ToUpperInvariant(), 12f, Theme.TextFaint, Theme.Medium);
            heading.characterSpacing = 6f;
            heading.Size(-1, 26f);
            heading.alignment = TextAlignmentOptions.BottomLeft;
        }

        private void Row(string label, string key, bool changed, string clash, System.Action change, System.Action reset) {
            var row = Ui.Rect(label, list);
            Ui.Row(row, 8f);
            row.Size(-1, 30f);
            var text = Ui.Text(row, "Label", label, Theme.FontSmall, Theme.Text);
            text.Size(-1, 30f, 1f);
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (clash != null) {
                var warn = Ui.Text(row, "Clash", "also " + clash, 12f, Theme.Danger, null, TextAlignmentOptions.MidlineRight);
                warn.Size(150f, 30f);
                warn.overflowMode = TextOverflowModes.Ellipsis;
            }
            var chip = Ui.Button(row, "Key", key, change, false, 5f, Theme.FontSmall);
            chip.targetGraphic.color = Theme.PanelRaised;
            chip.Size(130f, 26f);
            Ui.Tip(chip, label, null, "Click, then press the new key (hold Ctrl, Shift or Alt with it if you like). Esc keeps it, Backspace: no key.");
            var undo = Ui.Button(row, "Reset", "Reset", reset, false, 5f, 12f);
            undo.Size(54f, 26f);
            undo.interactable = changed;
            if (!changed) {
                undo.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
                undo.GetComponentInChildren<TextMeshProUGUI>().color = new Color(1f, 1f, 1f, 0f);
            }
            Ui.Tip(undo, "Back to its first key");
        }

        private void StartV2(Keybinds.Bind bind) {
            if (RebindAction.Rebinding) return;
            capturing = bind;
            capturingOriginal = null;
            captureFrame = Time.frameCount;
            dialog.closeOnEscape = false;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);   // not into the search box
            Rebuild();
        }

        private void StartOriginal(InputEvent e) {
            if (RebindAction.Rebinding || e.rebindAction == null) return;
            capturing = null;
            capturingOriginal = e;
            dialog.closeOnEscape = false;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            void Done() {
                e.rebindAction.OnEndRebind -= Done;
                capturingOriginal = null;
                if (this == null) return;
                StartCoroutine(EscapeLater());
                Interface.Interface.Say(e.name + ": " + (e.CurrentKeysText == "" ? "no key" : e.CurrentKeysText));
                Rebuild();
            }
            e.rebindAction.OnEndRebind += Done;
            e.rebindAction.AttemptRebind();
            Rebuild();
        }

        private System.Collections.IEnumerator EscapeLater() {
            yield return null;
            if (dialog != null) dialog.closeOnEscape = true;
        }

        private void ResetAll() {
            foreach (var e in Originals())
                if (e.rebindAction != null && !e.rebindAction.IsEmpty) e.rebindAction.ResetRebinds();
            Keybinds.ResetAll();
            Interface.Interface.Say("Every shortcut is back to its first key");
            Rebuild();
        }

        private void Update() {
            if (capturing == null || Time.frameCount == captureFrame) return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.backspaceKey.wasPressedThisFrame) {
                var bind = capturing;
                capturing = null;
                Keybinds.Set(bind.id, "");
                Interface.Interface.Say(bind.label + ": no key");
                StartCoroutine(EscapeLater());
                return;
            }
            var combo = Keybinds.Capture(out var cancel);
            if (cancel) {
                capturing = null;
                Interface.Interface.Say("Kept the key");
                StartCoroutine(EscapeLater());
                Rebuild();
                return;
            }
            if (combo == null) return;
            var chosen = capturing;
            capturing = null;
            Keybinds.Set(chosen.id, combo);
            var clash = Keybinds.All.FirstOrDefault(b => b != chosen && b.Key == combo && SamePlace(b.group, chosen.group));
            Interface.Interface.Say(chosen.label + ": " + Keybinds.Display(combo) + (clash != null ? " (also " + clash.label + ")" : ""), clash == null);
            StartCoroutine(EscapeLater());
        }
    }
}
