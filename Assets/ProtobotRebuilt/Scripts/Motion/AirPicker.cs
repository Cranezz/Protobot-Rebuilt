using System;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: which ends of a cylinder get air, picked on a picture of it: the cylinder side on with a fitting in its back
    // port and one in its front port. Clicking a fitting turns its air on (the fitting in full color) or off (blacked
    // out). Air at the back pushes the rod out, at the front pulls it in; an end with no air can't push, so the rod
    // goes loose there (outside forces still move it). At least one end has air.
    public static class AirPicker {
        public static RectTransform Create(Transform parent, PneumaticCylinder cylinder, Action changed, float width = 380f, float height = 128f) {
            var root = Ui.Image(parent, "Air Picker", Theme.Field, 8f).rectTransform;
            root.Size(width, height);
            var fitting = Custom.Thumbnails.ForVexPart("VXPNEUMATICS", "Straight Pneumatic Fitting");
            var scale = height / 128f;

            RectTransform Box(string name, Color color, float x, float y, float w, float h, float radius = 3f) {
                var image = Ui.Image(root, name, color, radius);
                image.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(x * scale, y * scale), new Vector2(w * scale, h * scale));
                return image.rectTransform;
            }

            // The cylinder: barrel with end caps, the rod out of the front.
            var left = (width / scale - 330f) / 2f;
            var rodOut = 30f + 40f * Mathf.Clamp01(cylinder.Position);
            Box("Rod", new Color(0.72f, 0.73f, 0.75f), left + 250f, 44f, rodOut, 9f, 2f);
            Box("Rod End", new Color(0.55f, 0.56f, 0.58f), left + 250f + rodOut - 4f, 44f, 12f, 15f, 2f);
            Box("Barrel", new Color(0.13f, 0.13f, 0.14f), left + 30f, 44f, 220f, 36f, 6f);
            Box("Back Cap", new Color(0.24f, 0.24f, 0.26f), left + 26f, 44f, 34f, 42f, 4f);
            Box("Front Cap", new Color(0.24f, 0.24f, 0.26f), left + 220f, 44f, 34f, 42f, 4f);
            Box("Label", new Color(0.62f, 0.09f, 0.10f), left + 110f, 44f, 60f, 36f, 0f);

            void Port(string name, float x, bool back) {
                bool On() => back ? cylinder.Ports != PneumaticCylinder.AirPorts.Front : cylinder.Ports != PneumaticCylinder.AirPorts.Back;
                var button = Ui.Rect(name, root);
                button.Place(new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(x * scale, 62f * scale), new Vector2(58f * scale, 58f * scale));
                var hit = button.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                var picture = Ui.Rect("Fitting", button).gameObject.AddComponent<RawImage>();
                picture.rectTransform.Fill();
                picture.texture = fitting;
                picture.raycastTarget = false;
                var caption = Ui.Text(root, name + " Caption", "", Theme.FontSmall * Mathf.Max(0.85f, scale), Theme.Text, null, TextAlignmentOptions.Center);
                caption.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(x * scale, 4f * scale), new Vector2(110f * scale, 18f * scale));
                void Show() {
                    var on = On();
                    if (cylinder.SingleActing) on = back;
                    picture.color = on ? Color.white : new Color(0.05f, 0.05f, 0.06f, 1f);
                    caption.text = (back ? "Back" : "Front") + (on ? ": air" : cylinder.SingleActing ? ": spring" : ": no air");
                    caption.color = on ? Theme.Text : Theme.TextDim;
                }
                Show();
                var click = button.gameObject.AddComponent<Button>();
                click.targetGraphic = hit;
                click.transition = Selectable.Transition.None;
                click.onClick.AddListener(() => {
                    if (cylinder.SingleActing) {
                        Interface.Interface.Say("A single acting cylinder has one port, at the back: a spring brings the rod back in", false);
                        return;
                    }
                    var backOn = cylinder.Ports != PneumaticCylinder.AirPorts.Front;
                    var frontOn = cylinder.Ports != PneumaticCylinder.AirPorts.Back;
                    if (back) backOn = !backOn;
                    else frontOn = !frontOn;
                    if (!backOn && !frontOn) {
                        Interface.Interface.Say("One end needs air: with none the cylinder can't move the rod", false);
                        return;
                    }
                    cylinder.Ports = backOn && frontOn ? PneumaticCylinder.AirPorts.Both : backOn ? PneumaticCylinder.AirPorts.Back : PneumaticCylinder.AirPorts.Front;
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    foreach (var show in root.GetComponentsInChildren<PortView>()) show.refresh?.Invoke();
                    changed?.Invoke();
                });
                button.gameObject.AddComponent<PortView>().refresh = Show;
                var tint = button.gameObject.AddComponent<HoverTint>();
                tint.target = hit;
                tint.hover = Theme.AccentSoft;
                Ui.Tip(click, (back ? "Back" : "Front") + " port", "Click",
                       back ? "Air here pushes the rod out. Without it the rod can't be pushed out by the cylinder (it's loose that way)."
                            : "Air here pulls the rod in. Without it the rod can't be pulled in by the cylinder (it's loose that way).");
            }
            Port("Back Port", left + 46f, true);
            Port("Front Port", left + 234f, false);
            return root;
        }

        /// <summary>What the key does with these ports, in a few words (for the notes beside the picture).</summary>
        public static string Summary(PneumaticCylinder.AirPorts ports) => ports switch {
            PneumaticCylinder.AirPorts.Back => "Its key pushes the rod out, then lets the air out (the rod goes loose), then pushes it out again. It starts loose.",
            PneumaticCylinder.AirPorts.Front => "Its key pulls the rod in, then lets the air out (the rod goes loose), then pulls it in again. It starts loose.",
            _ => "Its key pushes the rod out, then pulls it back in.",
        };

        private class PortView : MonoBehaviour {
            public Action refresh;
        }
    }
}
