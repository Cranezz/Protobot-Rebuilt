using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.SelectionSystem;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Tools {
    // V2: painting. The color picker opens with the tool as a floating panel (drag it by its title, "-" folds it
    // into a bar that shows the color). Two ways to paint, chosen in the tool options:
    //  - Paint: click any part and the color under the mouse takes the paint (a part made of several colors, like
    //    a motor or a wheel, only changes the one you clicked). Alt+click picks a color up.
    //  - Part colors: click a part and every color on it is listed; pick one and change it in the picker: all of
    //    that color on the part changes (all the reds to blue).
    // Parts can't be selected or highlighted while painting (a click paints). Every change can be undone. It
    // replaces the original's painting on selection (ColorSelectionResponse).
    public class PaintTool : MonoBehaviour {
        private const string ColorKey = "V2.PaintColor";
        private static Color current = new(0.784f, 0.063f, 0.18f);
        private static bool loaded;
        private static bool editColors;

        // Part colors mode: the part, its colors, and which one the picker is changing.
        private static Renderer editing;
        private static List<Color> partColors = new();
        private static int editingColor = -1;
        private static PaintElement editBefore;

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
        private static void RegisterOptions() {
            ToolRail.RegisterOptions("Paint", BuildOptions);
            ToolRail.RegisterBusy("Paint", () => editing != null);
            SelectionManager.Blocked = () => ToolRail.Current == "Paint";
        }

        /// <summary>V2: right-click on a part in the Paint tool: its colors listed, to change each.</summary>
        public static void EditColorsOf(GameObject part, Renderer clicked) {
            // V2: any part's colors can be changed (not only the original's paintable metal).
            var renderer = clicked != null && clicked.GetComponentInParent<SavedObject>() == part?.GetComponent<SavedObject>() ? clicked : part != null ? part.GetComponent<Renderer>() : null;
            if (renderer == null || renderer.sharedMaterials.Length == 0) {
                Interface.Interface.Say("This part has no colors to change", false);
                return;
            }
            editColors = true;
            StartColorEdit(renderer);
        }

        private static void BuildOptions(RectTransform bar) {
            if (editColors && editing != null) {
                var back = Ui.Button(bar, "Back", "Back to painting", () => {
                    FinishColorEdit();
                    editing = null;
                    editColors = false;
                    ToolOptions.Rebuild();
                    OpenPicker();
                    Interface.Interface.Say("Click a part to paint it");
                }, false, 5f, Theme.FontSmall);
                back.Size(-1f, 30f);
                Ui.Divider(bar, true).Size(1f, 22f);
                ToolOptions.Note(bar, PartLabels.Short(editing.gameObject) + ":").color = Theme.Text;
                for (var i = 0; i < partColors.Count; i++) {
                    var index = i;
                    var chip = Ui.Image(bar, "Part Color", partColors[i], 5f, true);
                    chip.Size(30f, 26f);
                    if (i == editingColor) {
                        var ring = chip.gameObject.AddComponent<Outline>();
                        ring.effectColor = Theme.Accent;
                        ring.effectDistance = new Vector2(2f, -2f);
                    }
                    var button = chip.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    button.onClick.AddListener(() => ChooseColor(index));
                    Ui.Tip(chip, "#" + ColorUtility.ToHtmlStringRGB(partColors[i]), null, "Click, then change it in the color picker: all of this color on the part changes.");
                }
                Ui.Hint(bar, "Esc", "done");
                return;
            }

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
                    ColorPickerPanel.Show(c);
                    ToolOptions.Rebuild();
                });
                Ui.Tip(chip, "#" + ColorUtility.ToHtmlStringRGB(color));
            }
            Ui.Divider(bar, true).Size(1f, 22f);
            Ui.Hint(bar, "Click", "paint", "Alt+Click", "pick up", "Right-click", "a part's colors");
        }

        private static void OpenPicker() {
            if (editColors && editing != null && editingColor >= 0)
                ColorPickerPanel.OpenFloating("Part color", partColors[editingColor], RecolorLive);
            else
                ColorPickerPanel.OpenFloating("Paint color", Current, color => Current = color);
        }

        private void Update() {
            var active = ToolRail.Current == "Paint";
            if (active != wasActive) {
                wasActive = active;
                if (active) {
                    // Whatever was selected or highlighted lets go: clicks paint now.
                    foreach (var manager in FindObjectsByType<SelectionManager>(FindObjectsSortMode.None))
                        manager.ClearCurrent();
                    OpenPicker();
                }
                else {
                    FinishColorEdit();
                    editing = null;
                    ColorPickerPanel.Close();
                }
            }
            // The original painted on selection; the new tool does it (with undo) instead.
            ColorToolActiveCheck.colorToolActive = false;
            if (!active) return;

            var keyboard = Keyboard.current;
            if (editing != null && keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputEvents.TextInput.IsTyping) {
                FinishColorEdit();
                editing = null;
                ToolOptions.Rebuild();
                OpenPicker();
                Interface.Interface.Say("Done changing colors");
                return;
            }

            if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI || !MouseInput.withinScreen)
                return;
            if (!PartRendererUnderMouse(out var renderer, out var materialIndex)) return;

            var alt = keyboard != null && keyboard.altKey.isPressed;
            if (alt) {
                Current = renderer.sharedMaterials[materialIndex].color;
                ColorPickerPanel.Show(Current);
                ToolOptions.Rebuild();
                Interface.Interface.Say("Picked up #" + ColorUtility.ToHtmlStringRGB(Current));
                return;
            }
            if (editColors && editing != null) {
                // V2: a click while changing a part's colors: a click on it picks the color under the mouse to
                // change; on another part it goes back to painting.
                if (renderer == editing) {
                    var color = renderer.sharedMaterials[materialIndex].color;
                    var index = partColors.FindIndex(c => Same(c, color));
                    if (index >= 0) ChooseColor(index);
                    return;
                }
                FinishColorEdit();
                editing = null;
                editColors = false;
                ToolOptions.Rebuild();
            }
            Paint(renderer, materialIndex, Current);
        }

        // The part under the mouse, its renderer, and which of its materials (colors) is under it.
        private static bool PartRendererUnderMouse(out Renderer renderer, out int materialIndex) {
            renderer = null;
            materialIndex = 0;
            var camera = Overlay.ViewCamera;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            if (!Physics.Raycast(ray, out var hit, Mathf.Infinity, LayerMask.GetMask("Default"))) return false;
            var part = FeaturePicker.PartOf(hit.collider.gameObject);
            if (part == null || !part.TryGetComponent(out renderer) || renderer.sharedMaterials.Length == 0) return false;
            if (renderer.sharedMaterials.Length > 1 && hit.collider is MeshCollider meshCollider && meshCollider.sharedMesh != null && hit.triangleIndex >= 0) {
                var mesh = meshCollider.sharedMesh;
                var first = hit.triangleIndex * 3;
                for (var s = 0; s < mesh.subMeshCount && s < renderer.sharedMaterials.Length; s++) {
                    var sub = mesh.GetSubMesh(s);
                    if (first >= sub.indexStart && first < sub.indexStart + sub.indexCount) {
                        materialIndex = s;
                        break;
                    }
                }
            }
            return true;
        }

        /// <summary>The original's rule: the material made from "Metal Color" (metallic 0.754) takes paint.</summary>
        public static bool IsPaintable(Renderer renderer) {
            var material = renderer.sharedMaterial;
            return material != null && material.HasProperty("_Metallic") && Mathf.Abs(material.GetFloat("_Metallic") - 0.754f) < 0.0005f;
        }

        private static void Paint(Renderer renderer, int materialIndex, Color color) {
            var before = new PaintElement(renderer);
            var materials = renderer.materials;
            SetColor(materials[materialIndex], color);
            StateSystem.AddElement(before);
            StateSystem.AddState(new State(new List<IElement> { new PaintElement(renderer) }));
            Theme.AddRecentColor(color);
            ToolOptions.Rebuild();
            Interface.Interface.Say("Painted " + PartLabels.Short(renderer.gameObject) + " #" + ColorUtility.ToHtmlStringRGB(color));
        }

        private static void SetColor(Material material, Color color) {
            // Keep a see-through material see-through.
            color.a = material.color.a;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        // ---- part colors

        private static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

        private static void StartColorEdit(Renderer renderer) {
            FinishColorEdit();
            editing = renderer;
            partColors = new List<Color>();
            foreach (var material in renderer.sharedMaterials)
                if (material != null && !partColors.Any(c => Same(c, material.color)))
                    partColors.Add(material.color);
            editingColor = partColors.Count == 1 ? 0 : -1;
            if (editingColor >= 0) editBefore = new PaintElement(renderer);
            ToolOptions.Rebuild();
            OpenPicker();
            Interface.Interface.Say(PartLabels.Short(renderer.gameObject) + " has " + partColors.Count + (partColors.Count == 1 ? " color: change it in the picker" : " colors: click one, then change it in the picker"));
        }

        private static void ChooseColor(int index) {
            FinishColorEdit();
            editingColor = index;
            editBefore = new PaintElement(editing);
            ToolOptions.Rebuild();
            OpenPicker();
        }

        // The picker moved: every material of the chosen color takes the new one.
        private static void RecolorLive(Color color) {
            if (editing == null || editingColor < 0 || editingColor >= partColors.Count) return;
            var from = partColors[editingColor];
            foreach (var material in editing.materials)
                if (Same(material.color, from))
                    SetColor(material, color);
            partColors[editingColor] = color;
            var bar = Object.FindFirstObjectByType<ToolOptions>();
            if (bar != null) {
                var chips = bar.GetComponentsInChildren<Image>().Where(i => i.name == "Part Color").ToList();
                if (editingColor < chips.Count) chips[editingColor].color = color;
            }
        }

        // One undo step for all the changes made to one color.
        private static void FinishColorEdit() {
            if (editing == null || editBefore == null) {
                editBefore = null;
                return;
            }
            var after = new PaintElement(editing);
            if (!after.SameAs(editBefore)) {
                StateSystem.AddElement(editBefore);
                StateSystem.AddState(new State(new List<IElement> { after }));
                if (editingColor >= 0 && editingColor < partColors.Count) Theme.AddRecentColor(partColors[editingColor]);
                Interface.Interface.Say("Changed a color on " + PartLabels.Short(editing.gameObject));
            }
            editBefore = null;
            editingColor = -1;
        }

        // Undo/redo of a paint job: every color on the part.
        private class PaintElement : IElement {
            private readonly Renderer renderer;
            private readonly Color[] colors;

            public PaintElement(Renderer renderer) {
                this.renderer = renderer;
                colors = renderer.sharedMaterials.Select(m => m != null ? m.color : Color.white).ToArray();
            }

            public bool SameAs(PaintElement other) => other.renderer == renderer && other.colors.Length == colors.Length
                                                      && colors.Zip(other.colors, (a, b) => a == b).All(x => x);

            public void Load() {
                if (renderer == null) return;
                var materials = renderer.materials;
                for (var i = 0; i < materials.Length && i < colors.Length; i++) {
                    materials[i].color = colors[i];
                    if (materials[i].HasProperty("_BaseColor")) materials[i].SetColor("_BaseColor", colors[i]);
                }
            }
        }
    }
}
