using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using Protobot.UI;
using UnityEngine;

namespace Protobot.Tools {
    // V2: the drill. Hover a hole in a part: a red disc shows the round hole it will cut. Click drills it
    // (PartDrilling). The bit is chosen in the tool options bar: a jobber set from 1/16" to 1/2" in 64ths, then
    // 1/16" steps to 1". A round hole can be drilled again with a bigger bit. Undo brings the old hole back.
    public class PartDrill : MonoBehaviour {
        [SerializeField] private MouseCast mouseCast;
        [SerializeField] private FeedbackDisplay feedback;
        [SerializeField] private Material previewMaterial;

        private GameObject preview;

        /// <summary>The bits on offer, in inches.</summary>
        public static readonly float[] Bits = MakeBits();

        private const string BitKey = "V2.DrillBit";
        public static float Diameter {
            get => PlayerPrefs.GetFloat(BitKey, PartDrilling.BitDiameter);
            set => PlayerPrefs.SetFloat(BitKey, value);
        }

        private static float[] MakeBits() {
            var bits = new List<float>();
            for (var n = 4; n <= 32; n++)
                bits.Add(n / 64f);           // 1/16" .. 1/2" by 64ths
            for (var n = 9; n <= 16; n++)
                bits.Add(n / 16f);           // 9/16" .. 1" by 16ths
            return bits.ToArray();
        }

        [RuntimeInitializeOnLoadMethod]
        private static void RegisterOptions() => ToolRail.RegisterOptions("Drill", BuildOptions);

        // V2: one dropdown with every bit (the quick sizes beside it doubled it); the mouse wheel over it steps
        // through the sizes.
        private static void BuildOptions(RectTransform bar) {
            ToolOptions.Note(bar, "Bit");
            var all = Bits.Select(b => Inch.Format(b) + "  (" + b.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "\")").ToArray();
            var index = System.Array.FindIndex(Bits, b => Mathf.Abs(b - Diameter) < 0.0001f);
            var choice = Popup.Choice(bar, all, index, i => {
                Diameter = Bits[i];
                Interface.Interface.Say("Bit: " + Inch.Format(Diameter));
                ToolOptions.Rebuild();
            }, 150f);
            choice.gameObject.AddComponent<WheelStep>().step = direction => {
                var current = System.Array.FindIndex(Bits, b => Mathf.Abs(b - Diameter) < 0.0001f);
                var next = Mathf.Clamp((current < 0 ? 0 : current) + direction, 0, Bits.Length - 1);
                if (next == current) return;
                Diameter = Bits[next];
                Interface.Interface.Say("Bit: " + Inch.Format(Diameter));
                ToolOptions.Rebuild();
            };
            Ui.Tip(choice, "Drill bit", "Wheel", "1/16\" to 1/2\" by 64ths, then to 1\" by 16ths. Scroll over it to step through the sizes.");
            Ui.Hint(bar, "Click", "a hole to drill it");
        }

        private void OnDisable() {
            if (preview != null)
                preview.SetActive(false);
        }

        private static bool CanDrill(HoleCollider hole) =>
            !PartDrilling.IsRound(hole) || Diameter > hole.transform.localScale.x + 0.001f;

        private void Update() {
            HoleCollider hole = null;
            if (MouseInput.withinScreen && !MouseInput.overUI && mouseCast.overObj)
                hole = mouseCast.gameObject.GetComponent<HoleCollider>();
            // V2: the side of a standoff or HS shaft: a hole straight across it, for a screw.
            if (hole == null && Across()) return;

            var part = hole != null && hole.transform.parent != null ? hole.transform.parent.gameObject : null;
            var drillable = part != null && PartDrilling.IsDrillable(part) && CanDrill(hole);

            if (drillable)
                ShowPreview(hole);
            else if (preview != null)
                preview.SetActive(false);

            if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI || !mouseCast.overObj)
                return;

            if (hole == null) {
                feedback.Show("Click a hole to drill it", FeedbackDisplay.DisplayType.alert);
                return;
            }
            if (part == null || !PartDrilling.IsDrillable(part)) {
                feedback.Show("This part can't be drilled", FeedbackDisplay.DisplayType.alert);
                return;
            }
            if (!CanDrill(hole)) {
                feedback.Show("This hole is already " + Inch.Format(hole.transform.localScale.x) + " round: pick a bigger bit", FeedbackDisplay.DisplayType.alert);
                return;
            }
            Drill(part, hole);
        }

        private void Drill(GameObject part, HoleCollider hole) {
            var index = PartDrilling.Holes(part).IndexOf(hole);
            var before = new DrillElement(part);
            var diameter = Diameter;
            if (!PartDrilling.Drill(part, index, diameter)) {
                feedback.Show("The " + Inch.Format(diameter) + " bit is smaller than this hole: nothing to drill", FeedbackDisplay.DisplayType.alert);
                return;
            }
            PartDrilling.Record(part.GetComponent<SavedObject>(), index, diameter);

            StateSystem.AddElement(before);
            StateSystem.AddState(new State(new List<IElement> { new DrillElement(part) }));
            feedback.Show("Drilled a " + Inch.Format(diameter) + " round hole", FeedbackDisplay.DisplayType.success);
            SelectionSystem.HoverSelector.Changes++;   // the hole's outline shows its new shape at once
        }

        // A hole across a standoff or HS shaft: shown where it goes, drilled on a click. True when the mouse is on one.
        private bool Across() {
            var camera = Camera.main;
            if (camera == null || !MouseInput.withinScreen || MouseInput.overUI) return false;
            if (!Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default"))) return false;
            var part = Interface.FeaturePicker.PartOf(hit.collider.gameObject);
            if (!PartDrilling.CanCrossDrill(part)) return false;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var free = keyboard != null && keyboard.shiftKey.isPressed;
            var diameter = Diameter;
            if (!PartDrilling.CrossSpot(part, hit.point, hit.normal, diameter, free, out var center, out var axis, out var width, out var along)) {
                if (preview != null) preview.SetActive(false);
                return false;
            }
            // Where it is now (the straight spot carried by the part's bends).
            var bends = PartBend.SavedBends(part);
            var (point, turn) = PartBend.CenterAt(part, along, bends);
            var world = PartBend.ToWorld(part, point);
            var worldAxis = part.transform.rotation * (turn * axis);
            if (preview == null) ShowPreview(null);
            preview.SetActive(true);
            preview.transform.SetPositionAndRotation(world, Quaternion.LookRotation(worldAxis) * Quaternion.Euler(90f, 0f, 0f));
            preview.transform.localScale = new Vector3(diameter, width / 2f + 0.02f, diameter);
            Interface.Overlay.Begin();
            Interface.Overlay.MouseText(Inch.Format(diameter) + " hole across, " + Inch.Format(along) + " from the end (Shift: free)", Color.white);
            if (MouseInput.LeftButton.wasPressedThisFrame) {
                if (diameter > width - 0.04f) {
                    feedback.Show("A " + Inch.Format(diameter) + " hole is too big for this part: pick a smaller bit (11/64\" fits a screw)", FeedbackDisplay.DisplayType.alert);
                    return true;
                }
                var before = new DrillElement(part);
                if (!PartDrilling.CrossDrill(part, center, axis, diameter)) {
                    feedback.Show("Couldn't drill there", FeedbackDisplay.DisplayType.alert);
                    return true;
                }
                PartDrilling.RecordCross(part.GetComponent<SavedObject>(), center, axis, diameter);
                StateSystem.AddElement(before);
                StateSystem.AddState(new State(new List<IElement> { new DrillElement(part) }));
                feedback.Show("Drilled a " + Inch.Format(diameter) + " hole across the " + PartLabels.Short(part) + ": a screw goes through it", FeedbackDisplay.DisplayType.success);
                SelectionSystem.HoverSelector.Changes++;
            }
            return true;
        }

        private void ShowPreview(HoleCollider hole) {
            if (preview == null) {
                preview = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                preview.name = "Drill Preview";
                Destroy(preview.GetComponent<Collider>());
                preview.GetComponent<Renderer>().sharedMaterial = previewMaterial;
                preview.layer = LayerMask.NameToLayer("Ignore Raycast");
            }
            preview.SetActive(true);
            if (hole == null) return;   // V2: made for a hole across a part (placed by the caller)

            var t = hole.transform;
            var depth = Mathf.Abs(t.lossyScale.z) + 0.02f;
            // A Unity cylinder is 2 units tall along its y axis and 1 unit across.
            preview.transform.SetPositionAndRotation(t.position, t.rotation * Quaternion.Euler(90f, 0f, 0f));
            preview.transform.localScale = new Vector3(Diameter, depth / 2f, Diameter);
        }
    }
}
