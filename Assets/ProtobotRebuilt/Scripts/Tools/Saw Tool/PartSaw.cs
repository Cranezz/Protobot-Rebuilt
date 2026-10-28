using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Parts_List;
using Protobot.StateSystems;
using Protobot.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools {
    // V2: the hack saw cuts every part. Hover a part: a red plane shows the cut, click cuts there. Undo puts the
    // part back whole.
    // - C/U-channels, angles and plates made by the original's generators cut between two holes, into two parts
    //   that generator makes (a 15 hole channel cut after 10 becomes a 10 and a 5): names, weights and saving work
    //   as for any part. Holes drilled in it are drilled in the pieces too.
    // - Shafts cut into two shafts of exact lengths (1/8" steps).
    // - Any other part is cut through its mesh (PartCutting): across its longest side, R for the next side; the
    //   cut lines up with the gaps between its holes, Shift for free 1/16" steps.
    public class PartSaw : MonoBehaviour {
        [SerializeField] private MouseCast mouseCast;
        [SerializeField] private FeedbackDisplay feedback;
        [SerializeField] private Material planeMaterial;

        private const float HoleSpacing = 0.5f; // inches between hole centres
        private const float ShaftStep = 0.125f;
        private const float PlaneThickness = 0.02f;
        private const float PlaneMargin = 0.15f;

        private GameObject plane;
        private int axisOffset;          // R: which side of a mesh-cut part to cut across
        private GameObject lastPart;

        private enum Kind { Holes, Shaft, Mesh }

        private struct Cut {
            public Kind kind;
            public GameObject part;
            public string typeId, param1;
            public bool plate;
            public int axis;            // part space axis the cut goes across
            public int count, other;    // Holes: holes along the axis, and across it (plates)
            public int first;           // Holes: holes kept on the negative side
            public float length, at;    // Shaft: total length and where it's cut (from its start), inches
            public float boundary;      // part space coordinate of the cut along the axis
        }

        private void OnDisable() {
            if (plane != null)
                plane.SetActive(false);
        }

        private void Update() {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                axisOffset++;

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

        private bool TryPlan(RaycastHit hit, out Cut cut) {
            cut = default;
            var part = PartOf(hit.collider.gameObject);
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id))
                return false;
            if (part != lastPart) {
                axisOffset = 0;
                lastPart = part;
            }

            var idParts = saved.id.Split('-');
            var type = PartsManager.GetPartType(saved.id);
            var generator = type != null ? type.GetComponent<PartGenerator>() : null;
            var local = part.transform.InverseTransformPoint(hit.point);
            cut.part = part;
            cut.typeId = idParts[0];
            cut.param1 = idParts.Length > 1 ? idParts[1] : "";

            // Parts cut before keep being cut through their mesh (their generator can't make them any more).
            var edited = !string.IsNullOrEmpty(saved.state) && saved.state.Contains("cut:");

            if (!edited && generator is AluminumPartGenerator && idParts.Length >= 3 && int.TryParse(idParts[2], out cut.count)) {
                cut.kind = Kind.Holes;
                cut.axis = 0;
            }
            else if (!edited && generator is PlatePartGenerator && idParts.Length >= 3
                     && int.TryParse(idParts[1], out var length) && int.TryParse(idParts[2], out var width)) {
                cut.kind = Kind.Holes;
                cut.plate = true;
                // Cut along whichever row of gaps is closer to the cursor.
                cut.axis = DistanceToGap(local.x, length) <= DistanceToGap(local.y, width) ? 0 : 1;
                cut.count = cut.axis == 0 ? length : width;
                cut.other = cut.axis == 0 ? width : length;
            }
            else if (!edited && generator is ShaftPartGenerator && idParts.Length >= 3
                     && float.TryParse(idParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out cut.length)) {
                cut.kind = Kind.Shaft;
                cut.axis = 2;
                var bounds = part.GetComponent<MeshFilter>().sharedMesh.bounds;
                var along = (local.z - bounds.min.z) / Mathf.Max(bounds.size.z, 1e-5f) * cut.length;
                cut.at = Mathf.Clamp(Mathf.Round(along / ShaftStep) * ShaftStep, ShaftStep, cut.length - ShaftStep);
                cut.boundary = bounds.min.z + cut.at / cut.length * bounds.size.z;
                return cut.length > 2 * ShaftStep - 0.001f;
            }
            else if (PartCutting.IsCuttable(part)) {
                cut.kind = Kind.Mesh;
                cut.axis = (PartCutting.LongestAxis(part) + axisOffset) % 3;
                var axis = Vector3.zero;
                axis[cut.axis] = 1f;
                var free = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                cut.boundary = PartCutting.Snap(part, axis, local[cut.axis], free);
                return true;
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

            var t = cut.part.transform;
            var bounds = cut.part.GetComponent<MeshFilter>().sharedMesh.bounds;
            var center = bounds.center;
            center[cut.axis] = cut.boundary;
            var scale = t.lossyScale;
            var size = Vector3.zero;
            for (var i = 0; i < 3; i++)
                size[i] = i == cut.axis ? PlaneThickness : bounds.size[i] * Mathf.Abs(scale[i]) + PlaneMargin;

            plane.transform.SetPositionAndRotation(t.TransformPoint(center), t.rotation);
            plane.transform.localScale = size;
        }

        private void Apply(Cut cut) {
            if (cut.kind == Kind.Holes && cut.count < 2) {
                feedback.Show("A single-hole piece can't be cut any shorter", FeedbackDisplay.DisplayType.alert);
                return;
            }
            if (cut.part.transform.parent != null && cut.part.transform.parent.GetComponent<Pivot>() != null) {
                feedback.Show("Take the part out of its group before cutting it", FeedbackDisplay.DisplayType.alert);
                return;
            }

            GameObject first, second;
            string result;
            switch (cut.kind) {
                case Kind.Holes: {
                    var firstCount = cut.first;
                    var secondCount = cut.count - cut.first;
                    first = Make(cut, firstCount, FirstHole(cut.count) + HoleSpacing * (firstCount - 1) / 2f);
                    second = Make(cut, secondCount, FirstHole(cut.count) + HoleSpacing * firstCount + HoleSpacing * (secondCount - 1) / 2f);
                    result = "into " + firstCount + " and " + secondCount + (cut.plate ? " rows" : " holes");
                    break;
                }
                case Kind.Shaft: {
                    first = MakeShaft(cut, cut.at, 0f);
                    second = MakeShaft(cut, cut.length - cut.at, cut.at);
                    result = "into " + Inches(cut.at) + " and " + Inches(cut.length - cut.at);
                    break;
                }
                default: {
                    MeshPieces(cut, out first, out second);
                    result = first != null && second != null ? "in two" : null;
                    break;
                }
            }

            if (first == null || second == null) {
                if (first != null) Destroy(first);
                if (second != null) Destroy(second);
                feedback.Show("There's nothing to cut off on one side", FeedbackDisplay.DisplayType.alert);
                return;
            }

            if (cut.kind != Kind.Mesh) {
                // Same color and place in the hierarchy as the part that was cut, and its drilled holes.
                var color = cut.part.GetComponent<Renderer>().material.color;
                foreach (var piece in new[] { first, second }) {
                    piece.transform.SetParent(cut.part.transform.parent, true);
                    piece.GetComponent<Renderer>().material.color = color;
                    CarryDrilledHoles(cut.part, piece);
                }
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
            feedback.Show("Cut " + NameOf(cut.part) + " " + result, FeedbackDisplay.DisplayType.success);
        }

        // Two copies of the part, each keeping one side of the cut.
        private static void MeshPieces(Cut cut, out GameObject first, out GameObject second) {
            var normal = Vector3.zero;
            normal[cut.axis] = 1f;
            var point = Vector3.zero;
            point[cut.axis] = cut.boundary;

            first = Clone(cut.part);
            second = Clone(cut.part);
            var firstOk = PartCutting.Cut(first, point, -normal);
            var secondOk = PartCutting.Cut(second, point, normal);
            if (!firstOk || !secondOk) {
                Destroy(first);
                Destroy(second);
                first = second = null;
                return;
            }
            PartCutting.Record(first.GetComponent<SavedObject>(), point, -normal);
            PartCutting.Record(second.GetComponent<SavedObject>(), point, normal);
            foreach (var piece in new[] { first, second })
                if (piece.TryGetComponent(out PartName partName) && !partName.name.EndsWith("(cut)"))
                    partName.name += " (cut)";
        }

        private static GameObject Clone(GameObject part) {
            var copy = Instantiate(part, part.transform.position, part.transform.rotation, part.transform.parent);
            copy.name = part.name;
            var source = part.GetComponent<Renderer>();
            var renderer = copy.GetComponent<Renderer>();
            renderer.materials = source.sharedMaterials.Select(m => m != null ? new Material(m) : null).ToArray();
            return copy;
        }

        private static void CarryDrilledHoles(GameObject from, GameObject to) {
            var holes = PartDrilling.Holes(to);
            foreach (var drilled in PartDrilling.Holes(from).Where(PartDrilling.IsRound)) {
                var position = drilled.transform.position;
                var match = holes.FirstOrDefault(h => (h.transform.position - position).sqrMagnitude < 0.0001f && !PartDrilling.IsRound(h));
                if (match == null)
                    continue;
                var index = holes.IndexOf(match);
                var diameter = drilled.transform.localScale.x;
                if (PartDrilling.Drill(to, index, diameter))
                    PartDrilling.Record(to.GetComponent<SavedObject>(), index, diameter);
            }
        }

        private static ObjectElement Element(GameObject obj, bool exists) {
            var element = new ObjectElement(obj);
            element.existing = exists;
            return element;
        }

        private static string NameOf(GameObject part) =>
            part.TryGetComponent(out PartName named) && !string.IsNullOrEmpty(named.name) ? named.name : part.name;

        private static string Inches(float value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "\"";

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

        // A shaft <paramref name="length"/> long starting <paramref name="from"/> inches along the cut one.
        private static GameObject MakeShaft(Cut cut, float length, float from) {
            var t = cut.part.transform;
            var bounds = cut.part.GetComponent<MeshFilter>().sharedMesh.bounds;
            // The shaft mesh is one unit long, scaled to its length along z: its start sits at min.z * length.
            var start = t.position + t.forward * (bounds.min.z * cut.length);
            var position = start + t.forward * (from - bounds.min.z * length);
            var id = cut.typeId + "-" + cut.param1 + "-" + length.ToString("0.###", CultureInfo.InvariantCulture);
            return PartsManager.GeneratePart(id, position, t.rotation);
        }
    }
}
