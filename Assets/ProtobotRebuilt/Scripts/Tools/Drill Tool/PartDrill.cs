using System.Collections.Generic;
using Protobot.StateSystems;
using Protobot.UI;
using UnityEngine;

namespace Protobot.Tools {
    // V2: the drill. Hover a hole in a metal part: a red disc shows the 1/4" round hole it will cut. Click drills
    // it (PartDrilling). Undo brings the square hole back.
    public class PartDrill : MonoBehaviour {
        [SerializeField] private MouseCast mouseCast;
        [SerializeField] private FeedbackDisplay feedback;
        [SerializeField] private Material previewMaterial;

        private GameObject preview;

        private void OnDisable() {
            if (preview != null)
                preview.SetActive(false);
        }

        private void Update() {
            HoleCollider hole = null;
            if (MouseInput.withinScreen && !MouseInput.overUI && mouseCast.overObj)
                hole = mouseCast.gameObject.GetComponent<HoleCollider>();

            var part = hole != null && hole.transform.parent != null ? hole.transform.parent.gameObject : null;
            var drillable = part != null && PartDrilling.IsDrillable(part) && !PartDrilling.IsRound(hole);

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
            if (PartDrilling.IsRound(hole)) {
                feedback.Show("This hole is already round", FeedbackDisplay.DisplayType.alert);
                return;
            }
            Drill(part, hole);
        }

        private void Drill(GameObject part, HoleCollider hole) {
            var index = PartDrilling.Holes(part).IndexOf(hole);
            var before = new DrillElement(part);
            if (!PartDrilling.Drill(part, index, PartDrilling.BitDiameter)) {
                feedback.Show("There's nothing to drill here", FeedbackDisplay.DisplayType.alert);
                return;
            }
            PartDrilling.Record(part.GetComponent<SavedObject>(), index, PartDrilling.BitDiameter);

            StateSystem.AddElement(before);
            StateSystem.AddState(new State(new List<IElement> { new DrillElement(part) }));
            feedback.Show("Drilled a 1/4\" round hole", FeedbackDisplay.DisplayType.success);
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
            preview.transform.localScale = new Vector3(PartDrilling.BitDiameter, depth / 2f, PartDrilling.BitDiameter);
        }
    }
}
