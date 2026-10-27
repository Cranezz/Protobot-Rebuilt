using System;
using System.Collections.Generic;
using Protobot.Core;
using Protobot.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Menus
{
    /// <summary>
    /// Drill bit picker: a vertical "tape" of bit sizes with tick marks and labels. Drag it or use the mouse
    /// wheel; the size under the centre line is selected. The top and bottom fade out, and the tape stops at
    /// the smallest and largest bits. The check button confirms.
    /// </summary>
    public class DrillBitMenu : MonoBehaviour
    {
        const float RowHeight = 64f;

        [SerializeField] UiTheme theme;

        GameObject root;
        ScrollRect scroll;
        RectTransform content;
        RectTransform viewport;
        readonly List<(TextMeshProUGUI label, Image tick)> rows = new();
        TextMeshProUGUI selectedText;
        Action<int> onChosen;
        int selected;
        bool dragging;

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            UiFactory.Canvas(gameObject, 25);
            Build();
            root.SetActive(false);
        }

        public void Open(int current, Action<int> chosen)
        {
            onChosen = chosen;
            root.SetActive(true);
            MenuState.Open(this);
            Canvas.ForceUpdateCanvases();
            Select(current, true);
        }

        void Close()
        {
            root.SetActive(false);
            MenuState.Close(this);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (!IsOpen)
                return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame && MenuState.ChangedFrame != Time.frameCount)
            {
                Close();
                return;
            }
            if (Keyboard.current.enterKey.wasPressedThisFrame)
                Confirm();

            var wheel = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
                Select(selected + (wheel > 0f ? -1 : 1), false);

            // Follow the tape while dragging, ease onto the nearest size when let go.
            var nearest = Mathf.Clamp(Mathf.RoundToInt(content.anchoredPosition.y / RowHeight), 0, DrillTool.Bits.Length - 1);
            if (nearest != selected)
                SetSelected(nearest);
            if (!dragging && scroll.velocity.sqrMagnitude < 400f)
            {
                var target = selected * RowHeight;
                var y = Mathf.Lerp(content.anchoredPosition.y, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                content.anchoredPosition = new Vector2(0f, y);
                scroll.velocity = Vector2.zero;
            }
        }

        void Select(int index, bool instant)
        {
            SetSelected(Mathf.Clamp(index, 0, DrillTool.Bits.Length - 1));
            if (instant)
                content.anchoredPosition = new Vector2(0f, selected * RowHeight);
        }

        void SetSelected(int index)
        {
            selected = index;
            for (var i = 0; i < rows.Count; i++)
            {
                var on = i == selected;
                rows[i].label.color = on ? theme.accent : theme.text;
                rows[i].label.fontSize = on ? 32f : 24f;
                rows[i].tick.color = on ? theme.accent : theme.textDim;
                rows[i].tick.rectTransform.sizeDelta = new Vector2(on ? 70f : 44f, 4f);
            }
            var (name, diameter) = DrillTool.Bits[selected];
            selectedText.text = $"{name}  =  {Units.ToInches(diameter):0.###} in";
        }

        void Confirm()
        {
            onChosen?.Invoke(selected);
            Close();
        }

        void Build()
        {
            var dim = UiFactory.Image(transform, "Dim", new Color(0f, 0f, 0f, 0.35f), null, true);
            dim.rectTransform.Fill();
            root = dim.gameObject;
            var panel = UiFactory.Panel(dim.transform, "Panel", theme, theme.panel, true).rectTransform;
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(380f, 0f), new Vector2(360f, 640f));

            var title = UiFactory.Text(panel, "Title", theme, "DRILL BIT", 40f, theme.text, true, TextAlignmentOptions.Center);
            title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(320f, 50f));
            selectedText = UiFactory.Text(panel, "Selected", theme, "", 22f, theme.textDim, false, TextAlignmentOptions.Center);
            selectedText.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(320f, 30f));

            // The tape.
            var tape = UiFactory.Rect("Tape", panel);
            tape.anchorMin = new Vector2(0f, 0f);
            tape.anchorMax = new Vector2(1f, 1f);
            tape.offsetMin = new Vector2(20f, 110f);
            tape.offsetMax = new Vector2(-20f, -110f);
            var tapeImage = tape.gameObject.AddComponent<Image>();
            tapeImage.color = new Color(1f, 1f, 1f, 0.001f);  // catches drags
            scroll = tape.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.05f;
            scroll.scrollSensitivity = 0f;  // the wheel steps one size at a time instead
            viewport = UiFactory.Rect("Viewport", tape).Fill();
            viewport.gameObject.AddComponent<RectMask2D>();
            content = UiFactory.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            scroll.viewport = viewport;
            scroll.content = content;
            var drag = tape.gameObject.AddComponent<DragFlag>();
            drag.changed = d => dragging = d;

            // Padding so the first and last sizes can sit on the centre line.
            Canvas.ForceUpdateCanvases();
            var half = (640f - 220f) / 2f - RowHeight / 2f;
            content.sizeDelta = new Vector2(0f, half * 2f + DrillTool.Bits.Length * RowHeight);
            for (var i = 0; i < DrillTool.Bits.Length; i++)
            {
                var row = UiFactory.Rect("Bit" + i, content);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(0f, RowHeight);
                row.anchoredPosition = new Vector2(0f, -(half + i * RowHeight));
                var tick = UiFactory.Image(row, "Tick", theme.textDim);
                tick.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(44f, 4f));
                var (name, diameter) = DrillTool.Bits[i];
                var label = UiFactory.Text(row, "Label", theme, $"{name}   {Units.ToInches(diameter):0.###} in", 24f, theme.text, true, TextAlignmentOptions.MidlineLeft);
                label.rectTransform.Fill(100f, 0f, 8f, 0f);
                var index = i;
                var button = row.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var hit = row.gameObject.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0.001f);
                button.targetGraphic = hit;
                button.onClick.AddListener(() => Select(index, false));
                rows.Add((label, tick));
            }

            // Centre line and the fades at the top and bottom of the tape.
            var line = UiFactory.Image(tape, "CentreLine", new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.35f), theme.rounded);
            line.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, RowHeight - 6f));
            var fade = FadeTexture();
            foreach (var top in new[] { true, false })
            {
                var f = UiFactory.Rect(top ? "FadeTop" : "FadeBottom", tape);
                f.anchorMin = new Vector2(0f, top ? 1f : 0f);
                f.anchorMax = new Vector2(1f, top ? 1f : 0f);
                f.pivot = new Vector2(0.5f, top ? 1f : 0f);
                f.sizeDelta = new Vector2(0f, 130f);
                var raw = f.gameObject.AddComponent<RawImage>();
                raw.texture = fade;
                raw.color = theme.panel;
                raw.raycastTarget = false;
                if (!top)
                    raw.uvRect = new Rect(0f, 1f, 1f, -1f);  // same fade, upside down
            }

            var use = UiFactory.Button(panel, "Use", theme, "USE THIS BIT", Confirm, true, 26f);
            ((RectTransform)use.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(300f, 62f));
        }

        static Texture2D FadeTexture()
        {
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var i = 0; i < 64; i++)
                tex.SetPixel(0, i, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, i / 63f)));
            tex.Apply();
            return tex;
        }

        /// <summary>Reports when the tape starts and stops being dragged.</summary>
        class DragFlag : MonoBehaviour, IBeginDragHandler, IEndDragHandler
        {
            public Action<bool> changed;
            public void OnBeginDrag(PointerEventData e) => changed?.Invoke(true);
            public void OnEndDrag(PointerEventData e) => changed?.Invoke(false);
        }
    }
}
