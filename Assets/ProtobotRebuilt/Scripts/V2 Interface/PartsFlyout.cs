using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the menus a family tile opens in the parts browser. Each menu lists the family's parts and kinds; a kind
    // (with a chevron) opens its own menu beside it when the mouse is over it, like a desktop app's sub-menus.
    // They open to the left, away from the browser at the right edge. Clicking a part chooses it; clicking
    // elsewhere or Esc closes them.
    public class PartsFlyout : MonoBehaviour {
        private static PartsFlyout instance;
        private readonly List<(RectTransform panel, CatalogNode node)> menus = new();
        private CatalogNode root;
        private RectTransform anchor, beside;
        private Action<CatalogNode> choose;
        private const float ItemHeight = 30f, Width = 250f, MaxHeight = 520f;

        public static bool IsOpen => instance != null;

        /// <param name="panel">The first menu opens beside this (the browser), level with the tile.</param>
        public static void Toggle(CatalogNode group, RectTransform anchor, Action<CatalogNode> choose, RectTransform panel = null) {
            if (instance != null && instance.root == group) {
                Close();
                return;
            }
            Close();
            var go = Ui.Rect("Parts Menus", Interface.Root).Fill().gameObject;
            instance = go.AddComponent<PartsFlyout>();
            instance.root = group;
            instance.anchor = anchor;
            instance.choose = choose;
            instance.beside = panel;
            instance.OpenMenu(group, anchor, 0);
        }

        public static void Close() {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }

        private void OpenMenu(CatalogNode node, RectTransform beside, int level) {
            // Close deeper menus (and this level's old one).
            for (var i = menus.Count - 1; i >= level; i--) {
                Destroy(menus[i].panel.gameObject);
                menus.RemoveAt(i);
            }
            var height = Mathf.Min(node.children.Count * ItemHeight + 40f, MaxHeight);
            var panel = Ui.Panel(transform, node.name, Theme.PanelRaised, 8f).rectTransform;
            var corners = new Vector3[4];
            beside.GetWorldCorners(corners);
            var topLeft = Layout.FromScreen(corners[1]);
            if (level == 0 && this.beside != null) {
                var panelCorners = new Vector3[4];
                this.beside.GetWorldCorners(panelCorners);
                topLeft.x = Layout.FromScreen(panelCorners[1]).x;
            }
            var screenHeight = Interface.Root.rect.height;
            var top = Mathf.Clamp(topLeft.y + 4f, height + 10f, screenHeight - 10f);
            var left = Mathf.Max(topLeft.x - 6f - Width, 10f);
            panel.Place(Vector2.zero, new Vector2(0f, 1f), new Vector2(left, top), new Vector2(Width, height));
            menus.Add((panel, node));

            var title = Ui.Text(panel, "Title", node.name, Theme.FontSmall, Theme.TextFaint, Theme.Medium);
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -6f), new Vector2(Width - 24f, 26f));

            var viewport = Ui.Rect("Viewport", panel).Fill(4, 34, 4, 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, node.children.Count * ItemHeight);
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            for (var i = 0; i < node.children.Count; i++) {
                var child = node.children[i];
                var item = Ui.Image(content, child.name, new Color(1f, 1f, 1f, 0f), 5f, true);
                item.rectTransform.anchorMin = new Vector2(0f, 1f);
                item.rectTransform.anchorMax = new Vector2(1f, 1f);
                item.rectTransform.pivot = new Vector2(0.5f, 1f);
                item.rectTransform.anchoredPosition = new Vector2(0f, -i * ItemHeight);
                item.rectTransform.sizeDelta = new Vector2(0f, ItemHeight - 2f);
                var tint = item.gameObject.AddComponent<HoverTint>();
                tint.target = item;

                var icon = PartsCatalog.Type(child.IconType)?.icon;
                var textLeft = 10f;
                // V2: a VEX-CAD part shows a picture of itself (its family's icon is some other part).
                var model = PartsCatalog.ModelIcon(child);
                if (model != null) {
                    var picture = Ui.Rect("Icon", item.transform).Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(22f, 22f))
                        .gameObject.AddComponent<RawImage>();
                    picture.raycastTarget = false;
                    picture.texture = model;
                    textLeft = 34f;
                }
                else if (icon != null) {
                    var image = Ui.Image(item.transform, "Icon", Theme.TextDim);
                    image.sprite = icon;
                    image.preserveAspect = true;
                    image.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(18f, 18f));
                    textLeft = 34f;
                }
                var label = Ui.Text(item.transform, "Label", child.name, Theme.FontSmall, Theme.Text);
                label.rectTransform.Fill(textLeft, 0, child.IsGroup ? 52 : 10, 0);
                if (child.IsGroup) {
                    var count = Ui.Text(item.transform, "Count", child.Count.ToString(), 11f, Theme.TextFaint, null, TextAlignmentOptions.MidlineRight);
                    count.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(26f, 20f));
                    var chevron = Ui.Image(item.transform, "Chevron", Theme.TextDim);
                    chevron.sprite = Interface.Instance.chevronIcon;
                    chevron.preserveAspect = true;
                    chevron.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(9f, 9f));
                    chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, 90f);  // points left, where it opens
                }
                var hover = item.gameObject.AddComponent<FlyoutItem>();
                var depth = level + 1;
                var rect = item.rectTransform;
                hover.onEnter = () => {
                    if (child.IsGroup) {
                        if (menus.Count <= depth || menus[depth].node != child) OpenMenu(child, rect, depth);
                    }
                    else
                        for (var k = menus.Count - 1; k >= depth; k--) {
                            Destroy(menus[k].panel.gameObject);
                            menus.RemoveAt(k);
                        }
                };
                var button = item.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var nav = button.navigation;
                nav.mode = Navigation.Mode.None;
                button.navigation = nav;
                button.onClick.AddListener(() => {
                    if (child.IsGroup) {
                        OpenMenu(child, rect, depth);
                        return;
                    }
                    var picked = choose;
                    Close();
                    picked?.Invoke(child);
                });
                if (!child.IsGroup && child.tip != null) Ui.Tip(item, child.name, null, child.tip);
            }
        }

        private void Update() {
            var mouse = Mouse.current;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) {
                Close();
                return;
            }
            if (mouse == null || !(mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return;
            var point = mouse.position.ReadValue();
            foreach (var (panel, _) in menus)
                if (panel != null && RectTransformUtility.RectangleContainsScreenPoint(panel, point)) return;
            if (anchor != null && RectTransformUtility.RectangleContainsScreenPoint(anchor, point)) return;
            Close();
        }
    }

    public class FlyoutItem : MonoBehaviour, IPointerEnterHandler {
        public Action onEnter;
        public void OnPointerEnter(PointerEventData eventData) => onEnter?.Invoke();
    }
}
