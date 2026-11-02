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
        private static readonly float[] QuickBits = { 0.125f, 0.1875f, 0.25f, 0.375f, 0.5f };

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

        private static void BuildOptions(RectTransform bar) {
            ToolOptions.Note(bar, "Bit");
            var quick = System.Array.IndexOf(QuickBits, QuickBits.FirstOrDefault(b => Mathf.Abs(b - Diameter) < 0.0001f));
            ToolOptions.Segmented(bar, QuickBits.Select(b => Inch.Format(b)).ToArray(), quick, i => {
                Diameter = QuickBits[i];
                ToolOptions.Rebuild();
            }, 52f);
            var all = Bits.Select(b => Inch.Format(b)).ToArray();
            var index = System.Array.FindIndex(Bits, b => Mathf.Abs(b - Diameter) < 0.0001f);
            Popup.Choice(bar, all, index, i => {
                Diameter = Bits[i];
                ToolOptions.Rebuild();
            }, 92f);
            ToolOptions.Note(bar, Inch.Format(Diameter).Replace("\"", "") + "\" = " + Diameter.ToString("0.000") + "\"");
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

            var t = hole.transform;
            var depth = Mathf.Abs(t.lossyScale.z) + 0.02f;
            // A Unity cylinder is 2 units tall along its y axis and 1 unit across.
            preview.transform.SetPositionAndRotation(t.position, t.rotation * Quaternion.Euler(90f, 0f, 0f));
            preview.transform.localScale = new Vector3(Diameter, depth / 2f, Diameter);
        }
    }
}
