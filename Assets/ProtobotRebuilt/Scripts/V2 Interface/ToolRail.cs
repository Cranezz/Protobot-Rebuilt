using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.InputEvents;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the tool rail on the left, in groups (arrange / shop / inspect), with each tool's key on its button and
    // a tooltip. It drives the original sidebar's toggles (now hidden), so tools switch exactly as before. The
    // active tool's settings show in the tool options bar beside it.
    public class ToolRail : MonoBehaviour {
        private class Tool {
            public string name, sidebarName, keyEvent, description;
            public int group;
            public Toggle sidebarToggle;
            public InputEvent input;
            public Image background, icon, marker;
            public TextMeshProUGUI keyLabel;
        }

        private static readonly (string name, string sidebar, string keyEvent, int group, string description)[] Tools = {
            ("Transform", "Transform Tool", "Enable Transform Tool", 0, "Move and turn parts with the arrows and rings. Snapping and local/global are in the options above."),
            ("Move", "Move Tool", "Enable Move Tool", 0, "Click a hole on a part, then a hole on another part to put them together."),
            ("Join", "Join Tool", "Enable Join Tool", 0, "Click a face, edge, corner or hole on one part, then on another: the first part moves to meet it."),
            ("Paint", "Color Tool", "Enable Color Tool", 0, "Click parts to paint them. Pick the color in the options above."),
            ("Saw", "Saw Tool", "Enable Saw Tool", 1, "Click a part to place a cut. Drag the plane or type its distance, then press Enter to cut."),
            ("Drill", "Drill Tool", "Enable Drill Tool", 1, "Click a hole to drill it out round. Choose the bit size above."),
            ("Measure", "Measure Tool", "Enable Measure Tool", 2, "Click two points, edges or holes to measure the distance between them."),
        };

        public static ToolRail Instance { get; private set; }
        private static readonly Dictionary<string, Action<RectTransform>> optionBuilders = new();

        private readonly List<Tool> tools = new();
        private RectTransform panel;
        private Transform oldSidebar;
        private Tool shown;
        private string currentName;

        /// <summary>The active tool's name ("Saw"), or null.</summary>
        public static string Current => Instance != null ? Instance.currentName : null;

        /// <summary>Lets a tool fill the options bar while it's active.</summary>
        public static void RegisterOptions(string tool, Action<RectTransform> build) => optionBuilders[tool] = build;

        /// <summary>Rebuilds the options bar for the active tool (after its settings changed).</summary>
        public static void RefreshOptions() {
            if (Instance != null)
                Instance.shown = null;
        }

        public static void Create(RectTransform root) {
            Instance = Ui.Rect("Tool Rail", root).Fill().gameObject.AddComponent<ToolRail>();
            Instance.Build();
        }

        /// <summary>Switches to a tool by name (as its rail button does).</summary>
        public static void Select(string name) {
            var tool = Instance?.tools.FirstOrDefault(t => t.name == name);
            if (tool?.sidebarToggle != null)
                tool.sidebarToggle.isOn = true;
        }

        private void Build() {
            var buildCanvas = GameObject.Find("Build Canvas");
            oldSidebar = buildCanvas != null ? buildCanvas.transform.Find("Tools Sidebar") : null;
            var keybindings = FindObjectsByType<InputEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            panel = Ui.Panel(transform, "Panel", Theme.Panel, 10f).rectTransform;
            panel.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Column(panel, 4f, new RectOffset(6, 6, 6, 6));
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -60f), new Vector2(56f, 0f));

            var group = -1;
            foreach (var def in Tools) {
                var sidebarButton = oldSidebar != null ? oldSidebar.Find(def.sidebar) : null;
                if (sidebarButton == null)
                    continue;
                if (group != -1 && def.group != group)
                    Ui.Divider(panel).Size(-1, 1f);
                group = def.group;
                var tool = new Tool {
                    name = def.name, sidebarName = def.sidebar, keyEvent = def.keyEvent, group = def.group,
                    description = def.description,
                    sidebarToggle = sidebarButton.GetComponent<Toggle>(),
                    input = keybindings.FirstOrDefault(e => e.name == def.keyEvent),
                };
                BuildButton(tool, IconOf(sidebarButton, def.name));
                tools.Add(tool);
            }

            // The original sidebar stays (its toggles switch the tools) but out of sight.
            if (oldSidebar != null) {
                var group2 = oldSidebar.gameObject.GetOrAdd<CanvasGroup>();
                group2.alpha = 0f;
                group2.blocksRaycasts = false;
                group2.interactable = true;
            }
        }

        private static Sprite IconOf(Transform sidebarButton, string name) {
            if (name == "Measure" && Interface.Instance.measureIcon != null)
                return Interface.Instance.measureIcon;
            if (name == "Saw" && Interface.Instance.sawIcon != null)
                return Interface.Instance.sawIcon;
            if (name == "Join" && Interface.Instance.joinIcon != null)
                return Interface.Instance.joinIcon;
            var icon = sidebarButton.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name.EndsWith("Icon"));
            return icon != null ? icon.sprite : null;
        }

        private void BuildButton(Tool tool, Sprite icon) {
            tool.background = Ui.Image(panel, tool.name, new Color(1f, 1f, 1f, 0f), 7f, true);
            tool.background.Size(44f, 44f);
            var button = tool.background.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.onClick.AddListener(() => {
                if (tool.sidebarToggle != null)
                    tool.sidebarToggle.isOn = true;
            });
            var hover = tool.background.gameObject.AddComponent<HoverTint>();
            hover.target = tool.background;

            tool.marker = Ui.Image(tool.background.transform, "Marker", Theme.Accent, 2f);
            tool.marker.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-6f, 0f), new Vector2(3f, 20f));

            tool.icon = Ui.Image(tool.background.transform, "Icon", Theme.TextDim);
            tool.icon.sprite = icon;
            tool.icon.preserveAspect = true;
            tool.icon.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(22f, 22f));

            tool.keyLabel = Ui.Text(tool.background.transform, "Key", "", 10f, Theme.TextFaint, Theme.Medium, TextAlignmentOptions.BottomRight);
            tool.keyLabel.rectTransform.Fill(0, 0, 4, 2);
            Ui.Tip(tool.background, tool.name, null, tool.description);
        }

        private void Update() {
            // Below the original top bar, wherever the screen size puts it.
            panel.anchoredPosition = new Vector2(14f, -(Layout.TopBarBottom + 14f));

            Tool active = null;
            foreach (var tool in tools) {
                var on = tool.sidebarToggle != null && tool.sidebarToggle.isOn;
                if (on) active = tool;
                tool.background.color = on ? Theme.Accent : new Color(1f, 1f, 1f, 0f);
                tool.icon.color = on ? Color.white : Theme.TextDim;
                tool.marker.enabled = false;
                var key = tool.input != null ? tool.input.GetCurrentKeybind() : "";
                if (tool.keyLabel.text != key) {
                    tool.keyLabel.text = key;
                    var tip = tool.background.GetComponent<TipSource>();
                    if (tip != null) tip.shortcut = key;
                }
                tool.keyLabel.color = on ? new Color(1f, 1f, 1f, 0.7f) : Theme.TextFaint;
                tool.background.GetComponent<HoverTint>().enabled = !on;
            }

            currentName = active?.name;
            if (active != shown) {
                shown = active;
                if (active != null && optionBuilders.TryGetValue(active.name, out var build))
                    ToolOptions.Show(active.name, build);
                else
                    ToolOptions.Hide();
            }
        }
    }

    // Lightens an image while the mouse is over it.
    public class HoverTint : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler {
        public Image target;
        public Color hover = Theme.Hover;
        private bool over;

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => over = true;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => over = false;

        private void LateUpdate() {
            if (target != null && over)
                target.color = hover;
        }

        private void OnDisable() => over = false;
    }
}
