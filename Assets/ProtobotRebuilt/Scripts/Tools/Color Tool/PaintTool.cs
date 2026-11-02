using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Tools {
    // V2: painting, with the earlier V2's color picker. While the Paint tool is on, the tool options bar shows
    // the current color (click it for the picker) and the recently used colors; clicking a part paints it
    // (metal parts: the original's paintable "Metal Color" material), Alt+click picks up a part's color. Undo
    // puts the old color back. It replaces the original's painting on selection (ColorSelectionResponse).
    public class PaintTool : MonoBehaviour {
        private const string ColorKey = "V2.PaintColor";
        private static Color current = new(0.784f, 0.063f, 0.18f);
        private static bool loaded;

        public static Color Current {
            get {
                if (!loaded) {
                    loaded = true;
                    if (ColorUtility.TryParseHtmlString("#" + PlayerPrefs.GetString(ColorKey, "C8102E"), out var c)) current = c;
                }
                return current;
            }
            set {
                current = value;
                loaded = true;
                PlayerPrefs.SetString(ColorKey, ColorUtility.ToHtmlStringRGB(value));
            }
        }

        private bool wasActive;

        [RuntimeInitializeOnLoadMethod]
        private static void RegisterOptions() => ToolRail.RegisterOptions("Paint", BuildOptions);

        private static void BuildOptions(RectTransform bar) {
            var swatch = Ui.Image(bar, "Current", Current, 6f, true);
            swatch.Size(46f, 30f);
            var outline = swatch.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.35f);
            var button = swatch.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => ColorPickerPanel.Toggle(swatch.rectTransform, Current, color => {
                Current = color;
                swatch.color = color;
                var hexNote = bar.Find("Hex");
                if (hexNote != null) hexNote.GetComponent<TMPro.TMP_Text>().text = "#" + ColorUtility.ToHtmlStringRGB(color);
            }));
            Ui.Tip(swatch, "Paint color", null, "Click to pick any color.");
            var hex = ToolOptions.Note(bar, "#" + ColorUtility.ToHtmlStringRGB(Current));
            hex.name = "Hex";
            hex.color = Theme.Text;

            Ui.Divider(bar, true).Size(1f, 22f);
            var recent = Theme.RecentColors;
            var shown = recent.Count > 0 ? recent : ColorPickerPanel.TeamColors.ToList();
            ToolOptions.Note(bar, recent.Count > 0 ? "Recent" : "Team");
            foreach (var color in shown.Take(10)) {
                var chip = Ui.Image(bar, "Chip", color, 5f, true);
                chip.Size(24f, 24f);
                var chipButton = chip.gameObject.AddComponent<Button>();
                chipButton.transition = Selectable.Transition.None;
                var c = color;
                chipButton.onClick.AddListener(() => {
                    Current = c;
                    ToolOptions.Rebuild();
                });
                Ui.Tip(chip, "#" + ColorUtility.ToHtmlStringRGB(color));
            }
            Ui.Divider(bar, true).Size(1f, 22f);
            Ui.Hint(bar, "Click", "paint", "Alt+Click", "pick up");
        }

        private void Update() {
            var active = ToolRail.Current == "Paint";
            if (active != wasActive) {
                wasActive = active;
                if (!active) ColorPickerPanel.Close();
            }
            // The original painted on selection; the new tool does it (with undo) instead.
            ColorToolActiveCheck.colorToolActive = false;
            if (!active) return;

            if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI || !MouseInput.withinScreen)
                return;
            var part = FeaturePicker.PartUnderMouse(out _);
            if (part == null) return;
            var renderer = part.GetComponent<Renderer>();
            if (renderer == null) return;

            var alt = Keyboard.current != null && Keyboard.current.altKey.isPressed;
            if (alt) {
                Current = renderer.material.color;
                ToolOptions.Rebuild();
                Interface.Interface.Say("Picked up #" + ColorUtility.ToHtmlStringRGB(Current));
                return;
            }
            if (!IsPaintable(renderer)) {
                Interface.Interface.Say("Only metal parts can be painted", false);
                return;
            }
            Paint(part, renderer, Current);
        }

        /// <summary>The original's rule: the material made from "Metal Color" (metallic 0.754) takes paint.</summary>
        public static bool IsPaintable(Renderer renderer) {
            var material = renderer.sharedMaterial;
            return material != null && material.HasProperty("_Metallic") && Mathf.Abs(material.GetFloat("_Metallic") - 0.754f) < 0.0005f;
        }

        private static void Paint(GameObject part, Renderer renderer, Color color) {
            var before = new PaintElement(renderer);
            renderer.material.color = color;
            StateSystem.AddElement(before);
            StateSystem.AddState(new State(new List<IElement> { new PaintElement(renderer) }));
            Theme.AddRecentColor(color);
            ToolOptions.Rebuild();
            Interface.Interface.Say("Painted #" + ColorUtility.ToHtmlStringRGB(color));
        }

        // Undo/redo of a paint job.
        private class PaintElement : IElement {
            private readonly Renderer renderer;
            private readonly Color color;

            public PaintElement(Renderer renderer) {
                this.renderer = renderer;
                color = renderer.material.color;
            }

            public void Load() {
                if (renderer != null) renderer.material.color = color;
            }
        }
    }
}
