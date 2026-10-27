using System.Collections.Generic;
using Protobot.Core;
using Protobot.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Menus
{
    /// <summary>
    /// Tool hotbar along the bottom of the screen (icon, number key, the equipped tool highlighted, and a
    /// one-line reminder of its controls) plus the radial tool wheel that appears while Tab is held.
    /// </summary>
    public class ToolHud : MonoBehaviour
    {
        const float SlotSize = 76f;
        const float WheelRadius = 150f;

        [SerializeField] UiTheme theme;
        [SerializeField] ToolBelt belt;
        [SerializeField] Sprite handsIcon;

        readonly List<(Image back, Image icon, TextMeshProUGUI key)> slots = new();
        readonly List<(Image back, Image icon, TextMeshProUGUI label)> wheelSlots = new();
        RectTransform hotbar;
        TextMeshProUGUI toolName;
        TextMeshProUGUI controls;
        Image controlsBack;
        string shownHint;
        GameObject wheel;
        Image wheelCentre;
        TextMeshProUGUI wheelCentreLabel;
        CanvasGroup group;
        Image modesBack;
        TextMeshProUGUI modesText;
        string shownModes = "";
        Player.PlayerMotor motor;

        void Awake()
        {
            UiFactory.Canvas(gameObject, 11);
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            BuildHotbar();
            BuildWheel();
        }

        void OnEnable() => belt.Equipped += OnEquipped;
        void OnDisable() => belt.Equipped -= OnEquipped;

        void Start()
        {
            if (belt.Current != null)
                OnEquipped(belt.Current);
        }

        void Update()
        {
            group.alpha = MenuState.AnyOpen ? 0f : 1f;
            var hint = belt.Current != null ? belt.Current.ControlsHint : "";
            if (hint != shownHint)
            {
                shownHint = hint;
                controls.text = Format(hint);
                controls.ForceMeshUpdate();
                controlsBack.gameObject.SetActive(hint.Length > 0);
                controlsBack.rectTransform.sizeDelta = new Vector2(controls.preferredWidth + 36f, 38f);
            }

            ShowModes();
            wheel.SetActive(belt.WheelOpen);
            if (!belt.WheelOpen)
                return;
            for (var i = 0; i < wheelSlots.Count; i++)
            {
                var hovered = belt.WheelHover == i + 1;
                wheelSlots[i].back.color = hovered ? theme.accent : theme.panel;
                wheelSlots[i].icon.color = hovered ? theme.accentText : Color.white;
                wheelSlots[i].label.color = hovered ? theme.accentText : theme.text;
                wheelSlots[i].back.rectTransform.localScale = Vector3.one * (hovered ? 1.12f : 1f);
            }
            var centreHovered = belt.WheelHover < 0;
            wheelCentre.color = centreHovered ? theme.accent : theme.panel;
            wheelCentreLabel.color = centreHovered ? theme.accentText : theme.text;
        }

        void OnEquipped(PlayerTool tool)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                var active = belt.Tools[i] == tool;
                slots[i].back.color = active ? theme.accent : new Color(theme.panel.r, theme.panel.g, theme.panel.b, 0.85f);
                slots[i].icon.color = active ? theme.accentText : Color.white;
                slots[i].key.color = active ? theme.accentText : theme.textDim;
                slots[i].back.rectTransform.sizeDelta = Vector2.one * (active ? SlotSize + 8f : SlotSize);
            }
            toolName.text = tool.DisplayName.ToUpperInvariant();
        }

        Sprite IconFor(PlayerTool tool) => tool.Icon != null ? tool.Icon : handsIcon;

        /// <summary>Modes that change what the keys do (build view, hole grab), beside the hotbar while they're on.</summary>
        void ShowModes()
        {
            if (motor == null)
                motor = FindFirstObjectByType<Player.PlayerMotor>();
            var accent = ColorUtility.ToHtmlStringRGB(theme.accent);
            var modes = "";
            if (motor != null && motor.BuildView)
                modes += $"<color=#{accent}>BUILD VIEW</color> (B)";
            if (HandsTool.HoleMode && belt.Current is HandsTool)
                modes += (modes.Length > 0 ? "     " : "") + $"<color=#{accent}>HOLE GRAB</color> (H)";
            if (modes == shownModes)
                return;
            shownModes = modes;
            modesBack.gameObject.SetActive(modes.Length > 0);
            modesText.text = modes;
            modesText.ForceMeshUpdate();
            modesBack.rectTransform.sizeDelta = new Vector2(modesText.preferredWidth + 32f, 36f);
        }

        /// <summary>"Left click: cut · Wheel: angle" → the input names in the accent colour, the actions in white.</summary>
        string Format(string hint)
        {
            var accent = ColorUtility.ToHtmlStringRGB(theme.accent);
            var parts = hint.Split('·');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                var colon = part.IndexOf(':');
                parts[i] = colon < 0 ? part : $"<color=#{accent}>{part.Substring(0, colon)}</color>{part.Substring(colon + 1)}";
            }
            return string.Join("<color=#FFFFFF55>   |   </color>", parts);
        }

        void BuildHotbar()
        {
            hotbar = UiFactory.Rect("Hotbar", transform);
            var count = belt.Tools.Count;
            var width = count * (SlotSize + 12f);
            hotbar.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(width, SlotSize + 8f));
            for (var i = 0; i < count; i++)
            {
                var back = UiFactory.Image(hotbar, "Slot" + i, theme.panel, theme.rounded);
                back.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2((i + 0.5f) * (SlotSize + 12f), 0f), Vector2.one * SlotSize);
                var icon = UiFactory.Image(back.transform, "Icon", Color.white, IconFor(belt.Tools[i]));
                icon.rectTransform.Fill(12f, 12f, 12f, 12f);
                icon.preserveAspect = true;
                var key = UiFactory.Text(back.transform, "Key", theme, (i + 1).ToString(), 18f, theme.textDim, true, TextAlignmentOptions.TopLeft);
                key.rectTransform.Fill(7f, 0f, 0f, 3f);
                slots.Add((back, icon, key));
            }

            toolName = UiFactory.Text(transform, "ToolName", theme, "", 26f, theme.text, true, TextAlignmentOptions.Bottom);
            toolName.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, SlotSize + 40f), new Vector2(600f, 32f));
            controlsBack = UiFactory.Image(transform, "ControlsBack", new Color(0.05f, 0.05f, 0.06f, 0.62f), theme.rounded);
            controlsBack.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, SlotSize + 64f), new Vector2(600f, 38f));
            controls = UiFactory.Text(controlsBack.transform, "Controls", theme, "", 20f, theme.text, false, TextAlignmentOptions.Center);
            controls.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 30f));
            var tabBack = UiFactory.Image(transform, "TabHintBack", new Color(0.05f, 0.05f, 0.06f, 0.62f), theme.rounded);
            tabBack.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(width / 2f + 14f, 24f + (SlotSize + 8f) / 2f), new Vector2(170f, 36f));
            var tabHint = UiFactory.Text(tabBack.transform, "TabHint", theme, Format("Hold Tab: tool wheel"), 19f, theme.text, false, TextAlignmentOptions.Center);
            tabHint.rectTransform.Fill();

            modesBack = UiFactory.Image(transform, "ModesBack", new Color(0.05f, 0.05f, 0.06f, 0.62f), theme.rounded);
            modesBack.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(1f, 0.5f), new Vector2(-width / 2f - 14f, 24f + (SlotSize + 8f) / 2f), new Vector2(200f, 36f));
            modesText = UiFactory.Text(modesBack.transform, "Modes", theme, "", 19f, theme.text, true, TextAlignmentOptions.Center);
            modesText.rectTransform.Fill();
            modesBack.gameObject.SetActive(false);
        }

        void BuildWheel()
        {
            var root = UiFactory.Rect("Wheel", transform);
            root.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WheelRadius * 2f + 160f, WheelRadius * 2f + 160f));
            wheel = root.gameObject;

            var disc = UiFactory.Circle();
            var ring = UiFactory.Image(root, "Ring", new Color(0.05f, 0.05f, 0.06f, 0.55f), disc);
            ring.rectTransform.Fill(0f, 0f, 0f, 0f);

            wheelCentre = UiFactory.Image(root, "Centre", theme.panel, disc);
            wheelCentre.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118f, 118f));
            var hands = UiFactory.Image(wheelCentre.transform, "Icon", Color.white, handsIcon);
            hands.rectTransform.Fill(28f, 34f, 28f, 22f);
            hands.preserveAspect = true;
            wheelCentreLabel = UiFactory.Text(wheelCentre.transform, "Label", theme, "HANDS", 18f, theme.text, true, TextAlignmentOptions.Bottom);
            wheelCentreLabel.rectTransform.Fill(0f, 8f, 0f, 0f);

            for (var i = 1; i < belt.Tools.Count; i++)
            {
                var angle = belt.WheelAngle(i) * Mathf.Deg2Rad;
                var pos = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * WheelRadius;
                var back = UiFactory.Image(root, "Tool" + i, theme.panel, disc);
                back.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(112f, 112f));
                var icon = UiFactory.Image(back.transform, "Icon", Color.white, IconFor(belt.Tools[i]));
                icon.rectTransform.Fill(18f, 26f, 18f, 12f);
                icon.preserveAspect = true;
                var label = UiFactory.Text(back.transform, "Label", theme, belt.Tools[i].DisplayName.ToUpperInvariant(), 17f, theme.text, true, TextAlignmentOptions.Bottom);
                label.rectTransform.Fill(0f, 4f, 0f, 0f);
                wheelSlots.Add((back, icon, label));
            }
            wheel.SetActive(false);
        }
    }
}
