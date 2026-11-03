using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the tool options bar, a strip beside the tool rail's top with the active tool's name and settings
    // (snapping, bit size, paint color...). Tools fill it through ToolRail.RegisterOptions.
    public class ToolOptions : MonoBehaviour {
        private static ToolOptions instance;
        private RectTransform panel, content;
        private TextMeshProUGUI title;
        private Action<RectTransform> builder;

        public static void Create(RectTransform root) {
            instance = Ui.Rect("Tool Options", root).Fill().gameObject.AddComponent<ToolOptions>();
            instance.Build();
        }

        private void Build() {
            panel = Ui.Panel(transform, "Panel", Theme.Panel, 10f).rectTransform;
            panel.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            panel.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(82f, -60f), new Vector2(0f, 44f));
            Ui.Row(panel, 10f, new RectOffset(14, 8, 0, 0));
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            title = Ui.Text(panel, "Title", "", Theme.FontTitle, Theme.Text, Theme.Medium);
            Ui.Divider(panel, true).Size(1f, 22f);
            content = Ui.Rect("Content", panel);
            Ui.Row(content, 8f);
            content.Size(-1, 44f);
            panel.gameObject.SetActive(false);
        }

        public static void Show(string toolName, Action<RectTransform> build) {
            if (instance == null)
                return;
            instance.builder = build;
            instance.title.text = toolName;
            instance.title.Size(-1, 44f);
            instance.Rebuild();
            instance.panel.gameObject.SetActive(true);
        }

        public static void Hide() {
            if (instance != null)
                instance.panel.gameObject.SetActive(false);
        }

        /// <summary>Builds the active tool's settings again (e.g. a list that changed).</summary>
        public static void Rebuild(Action<RectTransform> build = null) {
            if (instance == null)
                return;
            if (build != null)
                instance.builder = build;
            instance.Rebuild();
        }

        private void Rebuild() {
            foreach (Transform child in content)
                Destroy(child.gameObject);
            content.DetachChildren();
            builder?.Invoke(content);
        }

        private void Update() {
            panel.anchoredPosition = new Vector2(82f, -(Layout.TopBarBottom + 14f));
        }

        // ---- widgets used by several tools ----

        /// <summary>Two or more options side by side, one selected (like the bit sizes).</summary>
        public static void Segmented(RectTransform parent, string[] labels, int selected, Action<int> onSelect, float width = 0f) {
            var holder = Ui.Image(parent, "Segmented", Theme.Field, 6f);
            Ui.Row(holder.rectTransform, 2f, new RectOffset(3, 3, 3, 3));
            holder.Size(-1, 32f);
            for (var i = 0; i < labels.Length; i++) {
                var index = i;
                var button = Ui.Button(holder.transform, labels[i], labels[i], () => onSelect(index), i == selected, 4f, Theme.FontSmall);
                var label = button.GetComponentInChildren<TextMeshProUGUI>();
                // Equal shares when the holder is stretched (a side panel), so the selected one doesn't spill over.
                button.Size(width > 0f ? width : -1f, 26f, 1f);
                button.GetComponent<LayoutElement>().minWidth = 40f;
                if (i != selected)
                    button.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
                if (i != selected)
                    label.color = Theme.TextDim;
            }
        }

        /// <summary>A labelled on/off switch.</summary>
        public static Toggle Switch(RectTransform parent, string label, bool on, Action<bool> changed) {
            var row = Ui.Rect(label, parent);
            Ui.Row(row, 8f);
            row.Size(-1, 32f);
            var track = Ui.Image(row, "Track", on ? Theme.Accent : Theme.Hover, 9f, true);
            track.Size(34f, 18f);
            var knob = Ui.Image(track.transform, "Knob", Color.white, 7f);
            knob.rectTransform.Place(new Vector2(on ? 1f : 0f, 0.5f), new Vector2(on ? 1f : 0f, 0.5f), new Vector2(on ? -2f : 2f, 0f), new Vector2(14f, 14f));
            var toggle = track.gameObject.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            var nav = toggle.navigation;
            nav.mode = Navigation.Mode.None;
            toggle.navigation = nav;
            toggle.SetIsOnWithoutNotify(on);
            toggle.onValueChanged.AddListener(value => {
                track.color = value ? Theme.Accent : Theme.Hover;
                knob.rectTransform.Place(new Vector2(value ? 1f : 0f, 0.5f), new Vector2(value ? 1f : 0f, 0.5f), new Vector2(value ? -2f : 2f, 0f), new Vector2(14f, 14f));
                changed(value);
            });
            var text = Ui.Text(row, "Label", label, Theme.FontBody, Theme.TextDim);
            text.Size(-1, 32f);
            return toggle;
        }

        /// <summary>Dim text inside the bar.</summary>
        public static TextMeshProUGUI Note(RectTransform parent, string text) {
            var note = Ui.Text(parent, "Note", text, Theme.FontSmall, Theme.TextDim);
            note.Size(-1, 32f);
            return note;
        }
    }
}
