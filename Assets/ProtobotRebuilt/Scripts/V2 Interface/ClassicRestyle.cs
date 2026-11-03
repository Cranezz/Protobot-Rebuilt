using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the original's pop-up windows (Preferences, the unsaved-changes question) in the new interface's colors.
    // Their controls, layout and behaviour stay the original's; only the colors change: the original's palette
    // (its blue header, greys) is swapped for Theme's, the header loses its big logo, and buttons and toggles get
    // the same hover and pressed shades as the rest of the interface.
    public static class ClassicRestyle {
        private static readonly Dictionary<string, Color> Palette = new() {
            { "383838", Theme.Panel },          // window frame
            { "0077C8", Theme.PanelRaised },    // header bar
            { "242424", Theme.Field },          // tab column
            { "1A1A1A", Theme.Panel },          // page
            { "525252", Theme.PanelRaised },    // setting row
            { "2E2E2E", Theme.PanelRaised },    // setting row
            { "7A7A7A", Theme.Field },          // key / value box
            { "0098FF", Theme.Accent },         // on
        };

        private static Color Swap(Color c) {
            if (!Palette.TryGetValue(ColorUtility.ToHtmlStringRGB(c), out var color)) return c;
            color.a = c.a;
            return color;
        }

        public static void Apply(GameObject root) {
            if (root == null) return;
            foreach (var image in root.GetComponentsInChildren<Image>(true)) {
                var hex = ColorUtility.ToHtmlStringRGB(image.color);
                if (image.sprite != null && image.sprite.name.StartsWith("Logo Icon")) {
                    image.enabled = false;  // the big logo behind the header
                    continue;
                }
                if (Palette.TryGetValue(hex, out var color)) {
                    color.a = image.color.a;
                    // A selected tab keeps the accent.
                    if (hex == "0077C8" && image.name.StartsWith("ToggleOn")) color = Theme.Accent;
                    image.color = color;
                }
            }
            foreach (var slider in root.GetComponentsInChildren<Slider>(true)) {
                if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fill)) fill.color = Theme.Accent;
                var background = slider.transform.Find("Background");
                if (background != null && background.TryGetComponent(out Image back)) back.color = Theme.Field;
                if (slider.handleRect != null && slider.handleRect.TryGetComponent(out Image handle)) handle.color = Color.white;
            }
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true)) {
                if (selectable.transition != Selectable.Transition.ColorTint) continue;
                // The original's tints keep their strength (its menu buttons are white at 10%); only its blue
                // and greys become the theme's.
                var colors = selectable.colors;
                colors.normalColor = Swap(colors.normalColor);
                colors.highlightedColor = Swap(colors.highlightedColor);
                colors.pressedColor = Swap(colors.pressedColor);
                colors.selectedColor = Swap(colors.selectedColor);
                selectable.colors = colors;
                // Toggles' check boxes (white squares) as fields.
                if (selectable is Toggle && selectable.targetGraphic is Image box && ColorUtility.ToHtmlStringRGB(box.color) == "FFFFFF" && selectable.transform.parent != null && selectable.transform.parent.name != "Category Selection")
                    box.color = Theme.Field;
                else if (selectable is Toggle && selectable.transform.parent != null && selectable.transform.parent.name == "Category Selection" && selectable.targetGraphic is Image tab)
                    tab.color = Theme.PanelRaised;
            }
            foreach (var text in root.GetComponentsInChildren<Text>(true)) {
                if (text.font != null && text.font.name == "Roboto-Black") text.color = Theme.TextDim;  // section titles
                else if (text.color == Color.white) text.color = Theme.Text;
            }
        }
    }
}
