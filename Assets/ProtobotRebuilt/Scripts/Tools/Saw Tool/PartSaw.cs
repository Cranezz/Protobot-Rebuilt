using System.Collections.Generic;
using Parts_List;
using Protobot.StateSystems;
using Protobot.UI;
using UnityEngine;

namespace Protobot.Tools {
    // V2: the hack saw. Hover a C-channel, U-channel, angle or plate: a red cut plane snaps to the gap between the
    // two holes nearest the cursor. Click cuts there. The part is replaced by two shorter ones made by its own
    // generator (a 15 hole C-channel cut after 10 holes becomes a 10 and a 5), so names, weights, saving and
    // undo all work as for any other part.
    public class PartSaw : MonoBehaviour {
        [SerializeField] private MouseCast mouseCast;
        [SerializeField] private FeedbackDisplay feedback;
        [SerializeField] private Material planeMaterial;

        private const float HoleSpacing = 0.5f; // inches between hole centres
        private const float PlaneThickness = 0.02f;
        private const float PlaneMargin = 0.15f;

        private GameObject plane;

        private struct Cut {
            public GameObject part;
            public string typeId, param1;
            public bool plate;
            public int axis;            // 0: along local x, 1: along local y (plates)
            public int count, other;    // holes along the cut axis, and across it (plates)
            public int first;           // holes kept on the negative side
            public float boundary;      // local coordinate of the cut
        }

        private void OnDisable() {
            if (plane != null)
                plane.SetActive(false);
        }

        private void Update() {
            if (!MouseInput.withinScreen || MouseInput.overUI || !mouseCast.overObj || !TryPlan(mouseCast.hit, out var cut)) {
                if (plane != null)
                    plane.SetActive(false);
                return;
            }

            ShowPlane(cut);

            if (MouseInput.LeftButton.wasPressedThisFrame)
                Apply(cut);
        }

        private static GameObject PartOf(GameObject hitObject) {
            if (hitObject.GetComponent<HoleCollider>() != null && hitObject.transform.parent != null)
                return hitObject.transform.parent.gameObject;
            return hitObject;
        }

        private static bool TryPlan(RaycastHit hit, out Cut cut) {
            cut = default;
            var part = PartOf(hit.collider.gameObject);
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id))
                return false;

            var idParts = saved.id.Split('-');
            var type = PartsManager.GetPartType(saved.id);
            if (type == null || idParts.Length < 3)
                return false;

            var generator = type.GetComponent<PartGenerator>();
            var local = part.transform.InverseTransformPoint(hit.point);
            cut.part = part;
            cut.typeId = idParts[0];
            cut.param1 = idParts[1];

            if (generator is AluminumPartGenerator) {
                if (!int.TryParse(idParts[2], out cut.count))
                    return false;
                cut.axis = 0;
            }
            else if (generator is PlatePartGenerator) {
                if (!int.TryParse(idParts[1], out var length) || !int.TryParse(idParts[2], out var width))
                    return false;
                cut.plate = true;
                // Cut along whichever row of gaps is closer to the cursor.
                var alongX = DistanceToGap(local.x, length);
                var alongY = DistanceToGap(local.y, width);
                cut.axis = alongX <= alongY ? 0 : 1;
                cut.count = cut.axis == 0 ? length : width;
                cut.other = cut.axis == 0 ? width : length;
            }
            else
                return false;

            var coordinate = cut.axis == 0 ? local.x : local.y;
            cut.first = Mathf.Clamp(Mathf.RoundToInt((coordinate - FirstHole(cut.count) - HoleSpacing / 2f) / HoleSpacing), 0, Mathf.Max(0, cut.count - 2)) + 1;
            cut.boundary = FirstHole(cut.count) + HoleSpacing * (cut.first - 1) + HoleSpacing / 2f;
            return true;
        }

        private static float FirstHole(int count) => HoleSpacing * (-count + 1) / 2f;

        private static float DistanceToGap(float coordinate, int count) {
            if (count < 2)
                return float.MaxValue;
            var index = Mathf.Clamp(Mathf.RoundToInt((coordinate - FirstHole(count) - HoleSpacing / 2f) / HoleSpacing), 0, count - 2);
            return Mathf.Abs(coordinate - (FirstHole(count) + HoleSpacing * index + HoleSpacing / 2f));
        }

        private void ShowPlane(Cut cut) {
            if (plane == null) {
                plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plane.name = "Saw Cut Plane";
                Destroy(plane.GetComponent<Collider>());
                plane.GetComponent<Renderer>().sharedMaterial = planeMaterial;
                plane.layer = LayerMask.NameToLayer("Ignore Raycast");
            }
            plane.SetActive(true);

            var bounds = cut.part.GetComponent<MeshFilter>().sharedMesh.bounds;
            var center = bounds.center;
            var size = bounds.size + Vector3.one * PlaneMargin;
            if (cut.axis == 0) {
                center.x = cut.boundary;
                size.x = PlaneThickness;
            }
            else {
                center.y = cut.boundary;
                size.y = PlaneThickness;
            }

            var t = cut.part.transform;
            plane.transform.SetPositionAndRotation(t.TransformPoint(center), t.rotation);
            plane.transform.localScale = Vector3.Scale(size, t.lossyScale);
        }

        private void Apply(Cut cut) {
            if (cut.count < 2) {
                feedback.Show("A single-hole piece can't be cut any shorter", FeedbackDisplay.DisplayType.alert);
                return;
            }
            if (cut.part.transform.parent != null && cut.part.transform.parent.GetComponent<Pivot>() != null) {
                feedback.Show("Take the part out of its group before cutting it", FeedbackDisplay.DisplayType.alert);
                return;
            }

            var t = cut.part.transform;
            var firstCount = cut.first;
            var secondCount = cut.count - cut.first;
            var firstCenter = FirstHole(cut.count) + HoleSpacing * (firstCount - 1) / 2f;
            var secondCenter = FirstHole(cut.count) + HoleSpacing * firstCount + HoleSpacing * (secondCount - 1) / 2f;

            var first = Make(cut, firstCount, firstCenter);
            var second = Make(cut, secondCount, secondCenter);
            if (first == null || second == null) {
                if (first != null) Destroy(first);
                if (second != null) Destroy(second);
                feedback.Show("This part can't be cut", FeedbackDisplay.DisplayType.alert);
                return;
            }

            // Same color and place in the hierarchy as the part that was cut.
            var color = cut.part.GetComponent<Renderer>().material.color;
            foreach (var piece in new[] { first, second }) {
                piece.transform.SetParent(t.parent, true);
                piece.GetComponent<Renderer>().material.color = color;
            }

            // Undo: the current state learns the pieces didn't exist yet; the new state swaps the part for them.
            StateSystem.AddElement(Element(cut.part, true));
            StateSystem.AddElement(Element(first, false));
            StateSystem.AddElement(Element(second, false));
            new ObjectElement(cut.part).ApplyExistence(false);
            StateSystem.AddState(new State(new List<IElement> {
                Element(cut.part, false), Element(first, true), Element(second, true)
            }));

            FindObjectOfType<PartListOutput>()?.CalculatePartsList();

            var partName = cut.part.TryGetComponent(out PartName named) && !string.IsNullOrEmpty(named.name) ? named.name : cut.part.name;
            var unit = cut.plate ? " rows" : " holes";
            feedback.Show("Cut " + partName + " into " + firstCount + " and " + secondCount + unit, FeedbackDisplay.DisplayType.success);
        }

        private static ObjectElement Element(GameObject obj, bool exists) {
            var element = new ObjectElement(obj);
            element.existing = exists;
            return element;
        }

        private static GameObject Make(Cut cut, int count, float center) {
            string id;
            if (!cut.plate)
                id = cut.typeId + "-" + cut.param1 + "-" + count;
            else if (cut.axis == 0)
                id = cut.typeId + "-" + count + "-" + cut.other;
            else
                id = cut.typeId + "-" + cut.other + "-" + count;

            var local = Vector3.zero;
            local[cut.axis] = center;
            var t = cut.part.transform;
            return PartsManager.GeneratePart(id, t.TransformPoint(local), t.rotation);
        }
    }
}
