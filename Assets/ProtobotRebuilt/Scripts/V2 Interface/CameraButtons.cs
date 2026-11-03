using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the original's camera buttons beside the view cube, in the new interface's look. The drag-to-zoom and
    // drag-to-pan buttons are gone (the mouse wheel and the middle/right buttons do that); what's left, the
    // orthographic view and focusing on the selection, sits in a row under the view cube as rounded dark buttons.
    public static class CameraButtons {
        private const float Size = 30f, Gap = 6f;

        public static void Apply() {
            var canvas = GameObject.Find("Build Canvas");
            var options = canvas != null ? canvas.transform.Find("Camera Options") : null;
            if (options == null) return;
            foreach (var name in new[] { "Zoom Button", "Pan Button" }) {
                var old = options.Find(name);
                if (old != null) old.gameObject.SetActive(false);
            }
            var cube = options.Find("Orbit Button") as RectTransform;
            var centre = cube != null ? cube.anchoredPosition : new Vector2(82.5f, -17.5f);
            var below = centre.y - (cube != null ? cube.sizeDelta.y / 2f : 37.5f) - Gap - Size / 2f;
            Style(options.Find("Ortho Toggle") as RectTransform, new Vector2(centre.x - (Size + Gap) / 2f, below));
            Style(options.Find("Focus Button") as RectTransform, new Vector2(centre.x + (Size + Gap) / 2f, below));
            if (cube != null && cube.TryGetComponent(out Image disc))
                disc.color = new Color(Theme.Panel.r, Theme.Panel.g, Theme.Panel.b, 0.75f);
        }

        private static void Style(RectTransform button, Vector2 at) {
            if (button == null) return;
            button.anchoredPosition = at;
            button.sizeDelta = new Vector2(Size, Size);
            if (button.TryGetComponent(out Image background)) Round(background, Theme.PanelRaised);
            foreach (Transform child in button) {
                if (!child.TryGetComponent(out Image image)) continue;
                if (child.name == "Icon") {
                    image.color = Theme.Text;
                    ((RectTransform)child).sizeDelta = new Vector2(15f, 15f);
                }
                else Round(image, Theme.Accent);  // the toggle's "on" fill
            }
            if (button.TryGetComponent(out Selectable selectable)) {
                var colors = selectable.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f);
                colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
                colors.selectedColor = Color.white;
                colors.colorMultiplier = 1.5f;
                selectable.colors = colors;
            }
        }

        private static void Round(Image image, Color color) {
            image.sprite = Theme.Rounded;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = Theme.RadiusMultiplier(7f);
            image.color = color;
        }
    }
}
