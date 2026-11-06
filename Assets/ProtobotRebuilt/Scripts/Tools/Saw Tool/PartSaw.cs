using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Parts_List;
using Protobot.Interface;
using Protobot.InputEvents;
using Protobot.Rendering;
using Protobot.StateSystems;
using Protobot.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Tools {
    // V2: the saw. Click a part: a cut plane appears through its middle, across its longest side, with a
    // dimension from a reference point (the part's end at first) to the plane.
    // - Drag the arrow to slide the plane (1/16" steps with snapping, Shift for free). R: rotation rings to tilt
    //   it (5° steps); Esc goes back to the arrow, Esc again cancels and switches to the Select tool.
    // - The dimension runs from its reference point square to the plane (V2), with legs along the part's axes
    //   (each typeable) to the plane's centre. Click its value to type a distance; drag its reference dot onto a
    //   hole, corner, edge or face (or click the dot, then the point) to measure from there instead, and drag the
    //   plane's centre dot onto a point to put the cut there, like dimensioning in a CAD program.
    // - Enter or Cut makes the cut. Channels, angles and plates cut squarely between two holes become two parts
    //   their generator makes (names, weights and saving as any part); shafts cut squarely become two exact
    //   shafts; anything else is cut through its mesh (PartCutting) and keeps both pieces. Undo puts it back.
    public class PartSaw : MonoBehaviour {
        [SerializeField] private MouseCast mouseCast;
        [SerializeField] private FeedbackDisplay feedback;
        [SerializeField] private Material planeMaterial;

        private const float HoleSpacing = 0.5f; // inches between hole centres
        private const float Step = 1f / 16f;
        private const float AngleStep = 5f;

        private enum Mode { Pick, Move, Rotate, Reference, Target }

        private Mode mode = Mode.Pick;
        private GameObject part;
        private Vector3 center, normal, u, v;  // the plane, world space (u, v span it)
        private FeaturePicker.Feature reference;
        private bool referenceIsEnd;
        // V2: the direction the cut is measured along (square to the plane when the reference was set: the part's
        // length for its end). Tilting the plane keeps it, so the dimension still runs along the part to the
        // plane's centre, and the angle shows how far the cut is from square.
        private Vector3 measureAxis;
        /// <summary>V2 (for fun): cut every part the plane goes through, not only the one picked.</summary>
        private static bool cutAll;
        private float planeHalfU = 1f, planeHalfV = 1f;
        // V2: with All parts on, the plane is drawn over every part it cuts (its own middle and size), and those
        // parts are outlined; worked out again when the plane moves.
        private Vector3 drawCenter;
        private float drawHalfU = 1f, drawHalfV = 1f;
        private List<GameObject> crossed = new();
        private (Vector3 center, Vector3 normal, bool all) crossedFor;
        private GameObject planeObject;
        private Material planeInstance;
        private GameObject hovered;

        // Dragging
        private enum Handle { None, Arrow, RingU, RingV }
        private Handle hover, dragging;
        private Vector3 dragStartCenter, dragStartNormal, dragStartU, dragStartV;
        private float dragStartT;
        private Vector2 dragStartMouse;
        private List<Vector2> ringU = new(), ringV = new();

        // Typing a distance on the dimension
        private TMP_InputField editField;

        private static PartSaw active;

        [RuntimeInitializeOnLoadMethod]
        private static void RegisterOptions() {
            ToolRail.RegisterOptions("Saw", BuildOptions);
            ToolRail.RegisterBusy("Saw", () => true);  // V2: the saw handles Esc itself (steps back, then Select)
        }

        private static void BuildOptions(RectTransform bar) {
            if (active == null || active.part == null) {
                ToolOptions.Note(bar, "Click a part to place a cut");
                Ui.Hint(bar, "Esc", "Select tool");
                return;
            }
            active.BuildEditOptions(bar);
        }

        private void OnEnable() {
            active = this;
            mode = Mode.Pick;
            ToolOptions.Rebuild();
        }

        private void OnDisable() {
            EndCut();
            SetHover(null);
            if (active == this) active = null;
        }

        // ------------------------------------------------------------------ update

        private void Update() {
            Overlay.Begin();
            var typing = TextInput.IsTyping || TextInput.ShortcutsPaused;
            var keyboard = Keyboard.current;

            if (part == null || !part.activeInHierarchy) {
                if (part != null) EndCut();
                UpdatePick();
                if (!typing && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                    ToolRail.Select("Select");
                return;
            }

            if (!typing && keyboard != null) {
                if (keyboard.escapeKey.wasPressedThisFrame) {
                    if (mode == Mode.Reference || mode == Mode.Target || mode == Mode.Rotate) SetMode(Mode.Move);
                    else {
                        EndCut();
                        feedback.Show("Cut cancelled", FeedbackDisplay.DisplayType.alert);
                        ToolRail.Select("Select");
                        return;
                    }
                }
                else if (keyboard.rKey.wasPressedThisFrame && mode != Mode.Reference && mode != Mode.Target)
                    SetMode(mode == Mode.Rotate ? Mode.Move : Mode.Rotate);
                else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) {
                    ApplyCut();
                    return;
                }
            }

            if (mode == Mode.Reference) UpdateReferencePick();
            else if (mode == Mode.Target) UpdateTargetPick();
            else UpdateHandles();

            KeepOutline();
            UpdatePlaneObject();
            DrawPlaneOutline();
            DrawDimension();
            DrawHandles();
        }

        private void SetMode(Mode newMode) {
            mode = newMode;
            dragging = Handle.None;
            ToolOptions.Rebuild();
        }

        // ------------------------------------------------------------------ choosing a part

        private void UpdatePick() {
            GameObject target = null;
            if (MouseInput.withinScreen && !MouseInput.overUI) {
                target = FeaturePicker.PartUnderMouse(out _);
                if (target != null && !PartCutting.IsCuttable(target))
                    target = null;
            }
            SetHover(target);
            if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI || !MouseInput.withinScreen)
                return;
            var clicked = FeaturePicker.PartUnderMouse(out _);
            if (clicked == null)
                return;
            if (!PartCutting.IsCuttable(clicked)) {
                feedback.Show("This part can't be cut", FeedbackDisplay.DisplayType.alert);
                return;
            }
            if (clicked.transform.parent != null && clicked.transform.parent.GetComponent<Pivot>() != null) {
                feedback.Show("Take the part out of its group before cutting it", FeedbackDisplay.DisplayType.alert);
                return;
            }
            BeginCut(clicked);
        }

        private void SetHover(GameObject target) {
            if (hovered == target) return;
            if (hovered != null && hovered != part) SetOutline(hovered, false);
            hovered = target;
            if (hovered != null) SetOutline(hovered, true);
        }

        private static void SetOutline(GameObject obj, bool on) {
            if (obj == null) return;
            // The part itself, not its hole markers.
            if (obj.TryGetComponent(out MeshRenderer renderer))
                OutlineFeature.SetOutline(renderer, on, Theme.Accent);
        }

        private void KeepOutline() {
            var renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null && (renderer.renderingLayerMask & OutlineFeature.Layer) == 0)
                SetOutline(part, true);
        }

        private void BeginCut(GameObject target) {
            EndCut();
            part = target;
            hovered = null;
            SetOutline(part, true);

            // Across the longest side, through the middle.
            var t = part.transform;
            var mesh = part.GetComponent<MeshFilter>().sharedMesh;
            var axis = PartCutting.LongestAxis(part);
            var localNormal = Vector3.zero;
            localNormal[axis] = 1f;
            normal = t.TransformDirection(localNormal).normalized;
            var localCenter = mesh.bounds.center;
            center = t.TransformPoint(localCenter);
            MakeBasis();

            // Snap into the middle of a gap between holes when there are holes along this axis.
            var snapped = PartCutting.Snap(part, localNormal, localCenter[axis], false);
            center += normal * (snapped - localCenter[axis]) * t.lossyScale[axis];

            MeasurePlane(true);
            SetEndReference();
            mode = Mode.Move;
            ToolOptions.Rebuild();
            feedback.Show("Drag the arrow or click the dimension to set the cut", FeedbackDisplay.DisplayType.success);
        }

        private void EndCut() {
            ClearCrossed();
            if (part != null) SetOutline(part, false);
            part = null;
            mode = Mode.Pick;
            dragging = Handle.None;
            CloseEditField();
            Dimension.Close();
            if (planeObject != null) planeObject.SetActive(false);
            ToolOptions.Rebuild();
        }

        private void MakeBasis() {
            var t = part.transform;
            // In-plane axes: the part's own axes closest to the plane, so the rings line up with the part.
            var candidates = new[] { t.right, t.up, t.forward };
            var first = candidates.OrderBy(a => Mathf.Abs(Vector3.Dot(a, normal))).First();
            u = Vector3.ProjectOnPlane(first, normal).normalized;
            if (u.sqrMagnitude < 0.5f) u = Vector3.Cross(normal, Vector3.up).normalized;
            v = Vector3.Cross(normal, u).normalized;
        }

        // The end of the part the plane faces away from: its extreme along -normal, on the plane's centre line.
        private void SetEndReference() {
            var corners = FeaturePicker.Corners(part);
            var min = float.MaxValue;
            foreach (var c in corners)
                min = Mathf.Min(min, Vector3.Dot(c - center, normal));
            if (corners.Length == 0) min = -1f;
            reference = new FeaturePicker.Feature {
                kind = FeaturePicker.Kind.Face, point = center + normal * min, normal = -normal, part = part
            };
            referenceIsEnd = true;
            measureAxis = normal;
        }

        // How big to draw the plane: the part's extent across it. Recentring (only when the cut starts) puts the
        // plane's centre in the middle of the part's cross-section; later the centre stays where it is, so the
        // dimension (measured to it) doesn't change when the plane is tilted.
        private void MeasurePlane(bool recenter) {
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var c in FeaturePicker.Corners(part)) {
                var d = c - center;
                var du = Vector3.Dot(d, u);
                var dv = Vector3.Dot(d, v);
                minU = Mathf.Min(minU, du); maxU = Mathf.Max(maxU, du);
                minV = Mathf.Min(minV, dv); maxV = Mathf.Max(maxV, dv);
            }
            if (minU > maxU) { minU = minV = -1f; maxU = maxV = 1f; }
            if (recenter) {
                center += u * (minU + maxU) / 2f + v * (minV + maxV) / 2f;
                planeHalfU = (maxU - minU) / 2f + 0.25f;
                planeHalfV = (maxV - minV) / 2f + 0.25f;
            }
            else {
                planeHalfU = Mathf.Max(-minU, maxU) + 0.25f;
                planeHalfV = Mathf.Max(-minV, maxV) + 0.25f;
            }
        }

        // ------------------------------------------------------------------ the dimension

        /// <summary>V2: distance from the reference point to the cut plane, square to the plane (what you'd
        /// measure with a ruler from the end of the part), in inches.</summary>
        private float Distance => Mathf.Abs(Vector3.Dot(center - reference.point, measureAxis));

        /// <summary>V2: how far the cut is from square, in degrees (0 to 90).</summary>
        private float Tilt {
            get {
                var angle = Vector3.Angle(measureAxis, normal);
                return angle > 90f ? 180f - angle : angle;
            }
        }

        /// <summary>V2: tilts the plane to this angle from square, about the line it's tilted on now.</summary>
        private void SetTilt(float degrees) {
            degrees = Mathf.Clamp(degrees, 0f, 89f);
            var baseAxis = Vector3.Dot(normal, measureAxis) < 0f ? -measureAxis : measureAxis;
            var hinge = Vector3.Cross(baseAxis, normal);
            if (hinge.sqrMagnitude < 1e-8f) hinge = u;
            var target = (Quaternion.AngleAxis(degrees, hinge.normalized) * baseAxis).normalized;
            var turn = Quaternion.FromToRotation(normal, target);
            normal = target;
            u = (turn * u).normalized;
            v = (turn * v).normalized;
            MeasurePlane(false);
            feedback.Show("Cut " + degrees.ToString("0.#", CultureInfo.InvariantCulture) + "° from square", FeedbackDisplay.DisplayType.success);
            ToolOptions.Rebuild();
        }

        /// <summary>Slides the plane along its normal until it's <paramref name="value"/> from the reference, on
        /// the side it's on now.</summary>
        private void SetDistance(float value) {
            var along = Vector3.Dot(center - reference.point, measureAxis);
            var side = along < 0f ? -1f : 1f;
            center += measureAxis * (side * value - along);
        }

        /// <summary>V2: a leg of the dimension was typed: move the plane's centre along that axis of the part.</summary>
        private void SetLeg(int axis, float value) {
            var frame = part.transform.rotation;
            var c = Dimension.Components(reference.point, center, frame);
            var side = c[axis] < 0f ? -1f : 1f;
            var direction = frame * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
            center += direction * (side * value - c[axis]);
            feedback.Show(Dimension.AxisName(axis) + " set to " + Inch.Format(value), FeedbackDisplay.DisplayType.success);
            ToolOptions.Rebuild();
        }

        private void TypeDistance(float inches) {
            SetDistance(Mathf.Abs(inches));
            feedback.Show("Cut " + Inch.Format(Mathf.Abs(inches)) + " from " + ReferenceName, FeedbackDisplay.DisplayType.success);
            ToolOptions.Rebuild();
        }

        private float Snap(float value) {
            var free = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            return PositionTool.snapping && !free ? Mathf.Round(value / Step) * Step : value;
        }

        // The two pieces' lengths along the normal.
        private (float behind, float ahead) Pieces() {
            float min = 0f, max = 0f;
            foreach (var c in FeaturePicker.Corners(part)) {
                var d = Vector3.Dot(c - center, normal);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
            return (-min, max);
        }

        // Dragging the reference dot onto another point.
        private bool dotPressed, dotDragging;
        private Vector2 dotPressMouse;

        // Dragging the plane's centre dot onto a point: the plane moves there.
        private bool targetPressed, targetDragging;
        private Vector2 targetPressMouse;

        // V2: like a CAD dimension. The main one runs square to the plane, from the reference point (the part's
        // end at first) to the plane; the legs show how far apart the reference and the plane's centre are along
        // the part's own axes, each typeable. Two dots: the reference (drag it onto a hole, corner, edge or face to
        // measure from there) and the plane's centre (drag it onto a point and the plane moves there).
        private void DrawDimension() {
            var d = Distance;
            var color = Theme.Dimension;
            var along = Vector3.Dot(center - reference.point, measureAxis);
            // The dimension runs along the part to the plane's centre (tilted or not); it starts level with the
            // reference, joined to it by a faint extension line.
            var start = center - measureAxis * along;
            if (!Overlay.ToScreen(reference.point, out var a) || !Overlay.ToScreen(start, out var f) || !Overlay.ToScreen(center, out var b))
                return;
            if (Vector2.Distance(a, f) > 3f) Overlay.Line(a, f, new Color(color.r, color.g, color.b, 0.45f), 1f);
            Overlay.Line(f, b, color, 1.8f);
            if (Vector2.Distance(f, b) > 30f) {
                Overlay.Arrowhead(f, b, color);
                Overlay.Arrowhead(b, f, color);
            }
            // The angle from square, beside the plane's centre (click to type one).
            var tilt = Tilt;
            if (tilt > 0.05f && angleField == null) {
                var angleLabel = Overlay.Text(b + new Vector2(0f, -34f), tilt.ToString("0.#", CultureInfo.InvariantCulture) + "°", color, OpenAngleField, false);
                Ui.Tip(angleLabel, "Cut angle", null, "How far the cut is from square. Click to type an angle.");
            }
            else if (angleField != null)
                ((RectTransform)angleField.transform).anchoredPosition = b + new Vector2(0f, -34f);
            Dimension.Legs("saw", reference.point, center, part.transform.rotation, SetLeg, 0.75f);

            // The reference end: drag the dot onto another point, or click it, then click a point.
            var overDot = Vector2.Distance(Overlay.Mouse, a) < 10f;
            var overTarget = !overDot && Vector2.Distance(Overlay.Mouse, b) < 10f;
            if (overDot && MouseInput.LeftButton.wasPressedThisFrame && dragging == Handle.None) {
                dotPressed = true;
                dotDragging = false;
                dotPressMouse = Overlay.Mouse;
            }
            if (overTarget && MouseInput.LeftButton.wasPressedThisFrame && dragging == Handle.None) {
                targetPressed = true;
                targetDragging = false;
                targetPressMouse = Overlay.Mouse;
            }
            if (dotPressed) {
                if (MouseInput.LeftButton.isPressed) {
                    if (Vector2.Distance(Overlay.Mouse, dotPressMouse) > 5f) dotDragging = true;
                    if (dotDragging) {
                        var target = FeaturePicker.Pick();
                        FeaturePicker.DrawHover(target, Theme.Accent);
                        if (!target.Valid) Overlay.Disc(Overlay.Mouse, 5f, Theme.Accent);
                    }
                }
                else {
                    dotPressed = false;
                    if (!dotDragging)
                        SetMode(Mode.Reference);
                    else {
                        var target = FeaturePicker.Pick();
                        if (target.Valid) SetReference(target);
                        else feedback.Show("Drop the dot on a hole, corner, edge or face to measure from it", FeedbackDisplay.DisplayType.alert);
                    }
                    dotDragging = false;
                }
            }
            if (targetPressed) {
                if (MouseInput.LeftButton.isPressed) {
                    if (Vector2.Distance(Overlay.Mouse, targetPressMouse) > 5f) targetDragging = true;
                    if (targetDragging) {
                        var target = FeaturePicker.Pick();
                        FeaturePicker.DrawHover(target, Theme.Accent);
                        if (!target.Valid) Overlay.Disc(Overlay.Mouse, 5f, Theme.Accent);
                    }
                }
                else {
                    targetPressed = false;
                    if (!targetDragging)
                        SetMode(Mode.Target);
                    else {
                        var target = FeaturePicker.Pick();
                        if (target.Valid) MovePlaneTo(target);
                        else feedback.Show("Drop the dot on a hole, corner, edge or face to put the cut there", FeedbackDisplay.DisplayType.alert);
                    }
                    targetDragging = false;
                }
            }
            if (!dotDragging) {
                Overlay.Disc(a, 6f, Color.black, true);
                Overlay.Disc(a, 5f, mode == Mode.Reference ? Theme.Accent : color, true);
                if (overDot) Overlay.Ring(a, 9f, Color.white, 1.5f);
            }
            FeaturePicker.Draw(reference.kind == FeaturePicker.Kind.Face ? default : reference, color);
            if (!targetDragging) {
                Overlay.Disc(b, 5.5f, Color.black, true);
                Overlay.Disc(b, 4.5f, mode == Mode.Target ? Theme.Accent : Color.white, true);
                if (overTarget) Overlay.Ring(b, 9f, Color.white, 1.5f);
            }

            if (editField == null) {
                var label = Overlay.Text((f + b) / 2f, Inch.Format(d), color, OpenEditField, true);
                Ui.Tip(label, "Cut distance", null, "From " + ReferenceName + " to the cut, square to it. Click to type a distance. Drag the colored dot to measure from another point; drag the white dot to put the cut on a point.");
            }
            else
                ((RectTransform)editField.transform).anchoredPosition = (f + b) / 2f;
        }

        // V2: the plane's centre goes to a point (the second end of the measurement); the plane keeps its angle.
        private void MovePlaneTo(FeaturePicker.Feature target) {
            center = target.point;
            MeasurePlane(false);
            SetMode(Mode.Move);
            feedback.Show("Cut through the " + target.Describe().ToLowerInvariant() + ": " + Inch.Format(Distance) + " from " + ReferenceName, FeedbackDisplay.DisplayType.success);
            ToolOptions.Rebuild();
        }

        private void UpdateTargetPick() {
            var feature = MouseInput.withinScreen && !MouseInput.overUI ? FeaturePicker.Pick() : default;
            FeaturePicker.DrawHover(feature, Theme.Accent);
            if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI)
                return;
            if (!feature.Valid) {
                feedback.Show("Click a hole, corner, edge or face to put the cut there", FeedbackDisplay.DisplayType.alert);
                return;
            }
            MovePlaneTo(feature);
        }

        private void SetReference(FeaturePicker.Feature feature) {
            reference = feature;
            referenceIsEnd = false;
            measureAxis = normal;
            SetMode(Mode.Move);
            feedback.Show("Measuring from the " + feature.Describe().ToLowerInvariant(), FeedbackDisplay.DisplayType.success);
        }

        private string ReferenceName => referenceIsEnd ? "the end of the part" : reference.Describe().ToLowerInvariant();

        private void OpenEditField() {
            if (editField != null) return;
            editField = Ui.Field(Overlay.Instance.transform, "Distance", Inch.Format(Distance, false), CommitEditField,
                Theme.FontBody, TextAlignmentOptions.Center);
            var rect = (RectTransform)editField.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(96f, 28f);
            editField.onFocusSelectAll = true;
            editField.Select();
            editField.ActivateInputField();
        }

        private void CommitEditField(string text) {
            if (Inch.TryParse(text, out var inches))
                TypeDistance(inches);
            else if (!string.IsNullOrWhiteSpace(text))
                feedback.Show("Type a distance like 3 1/2 or 3.5", FeedbackDisplay.DisplayType.alert);
            CloseEditField();
        }

        private void CloseEditField() {
            if (editField != null) Destroy(editField.gameObject);
            editField = null;
            if (angleField != null) Destroy(angleField.gameObject);
            angleField = null;
        }

        private TMP_InputField angleField;

        private void OpenAngleField() {
            if (angleField != null) return;
            angleField = Ui.Field(Overlay.Instance.transform, "Angle", Tilt.ToString("0.#", CultureInfo.InvariantCulture), text => {
                if (float.TryParse(text.Replace("°", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)) SetTilt(degrees);
                else if (!string.IsNullOrWhiteSpace(text)) feedback.Show("Type an angle in degrees, like 45", FeedbackDisplay.DisplayType.alert);
                if (angleField != null) Destroy(angleField.gameObject);
                angleField = null;
            }, Theme.FontBody, TextAlignmentOptions.Center);
            var rect = (RectTransform)angleField.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(72f, 28f);
            angleField.onFocusSelectAll = true;
            angleField.Select();
            angleField.ActivateInputField();
        }

        private void UpdateReferencePick() {
            var feature = MouseInput.withinScreen && !MouseInput.overUI ? FeaturePicker.Pick() : default;
            FeaturePicker.DrawHover(feature, Theme.Accent);
            if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI)
                return;
            if (!feature.Valid) {
                feedback.Show("Click a hole, corner, edge or face to measure from", FeedbackDisplay.DisplayType.alert);
                return;
            }
            SetReference(feature);
        }

        // ------------------------------------------------------------------ handles

        private float HandleLength {
            get {
                // About 120 interface units (pixels at 1080p) on screen, whatever the zoom.
                var camera = Overlay.ViewCamera;
                var distance = Vector3.Distance(camera.transform.position, center);
                var worldPerPixel = camera.orthographic
                    ? 2f * camera.orthographicSize / Screen.height
                    : 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
                return worldPerPixel * 120f * Interface.Interface.Root.lossyScale.y;
            }
        }

        private void UpdateHandles() {
            var mouse = Overlay.Mouse;
            if (dragging == Handle.None) {
                hover = Handle.None;
                if (mode == Mode.Move && Overlay.ToScreen(center - normal * HandleLength, out var a) && Overlay.ToScreen(center + normal * HandleLength, out var b)
                    && Overlay.DistanceToSegment(mouse, a, b) < 9f)
                    hover = Handle.Arrow;
                if (mode == Mode.Rotate) {
                    var du = ringU.Count > 1 ? Overlay.DistanceTo(ringU, mouse) : float.MaxValue;
                    var dv = ringV.Count > 1 ? Overlay.DistanceTo(ringV, mouse) : float.MaxValue;
                    if (Mathf.Min(du, dv) < 8f) hover = du <= dv ? Handle.RingU : Handle.RingV;
                }
                if (dotPressed) return;
                if (hover != Handle.None && MouseInput.LeftButton.wasPressedThisFrame)
                    StartDrag(hover);
                else if (hover == Handle.None && MouseInput.LeftButton.wasPressedThisFrame && !MouseInput.overUI) {
                    // Another part: cut that one instead.
                    var other = FeaturePicker.PartUnderMouse(out _);
                    if (other != null && other != part && PartCutting.IsCuttable(other))
                        BeginCut(other);
                }
                return;
            }
            if (!MouseInput.LeftButton.isPressed) {
                if (dragging != Handle.Arrow)
                    MeasurePlane(false);  // tilted: fit the plane to the part again
                dragging = Handle.None;
                ToolOptions.Rebuild();
                return;
            }
            Drag();
        }

        private void StartDrag(Handle handle) {
            dragging = handle;
            dragStartCenter = center;
            dragStartNormal = normal;
            dragStartU = u;
            dragStartV = v;
            dragStartMouse = Overlay.Mouse;
            dragStartT = AxisParameter(center, normal);
        }

        // Where the mouse ray passes closest to the line through origin along axis (as a distance along it).
        private static float AxisParameter(Vector3 origin, Vector3 axis) {
            var ray = Overlay.ViewCamera.ScreenPointToRay(MouseInput.Position);
            var w0 = origin - ray.origin;
            float a = Vector3.Dot(axis, axis), b = Vector3.Dot(axis, ray.direction), c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(axis, w0), e = Vector3.Dot(ray.direction, w0);
            var denominator = a * c - b * b;
            return Mathf.Abs(denominator) < 1e-6f ? 0f : (b * e - c * d) / denominator;
        }

        private void Drag() {
            if (dragging == Handle.Arrow) {
                var t = AxisParameter(dragStartCenter, dragStartNormal);
                center = dragStartCenter + normal * (t - dragStartT);
                SetDistance(Snap(Distance));
                return;
            }
            // Rings: the angle the mouse has turned around the plane's centre on screen.
            if (!Overlay.ToScreen(dragStartCenter, out var c)) return;
            var angle = Vector2.SignedAngle(Overlay.Mouse - c, dragStartMouse - c);
            var free = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            angle = PositionTool.snapping && !free ? Mathf.Round(angle / AngleStep) * AngleStep : Mathf.Round(angle);
            var axis = dragging == Handle.RingU ? dragStartU : dragStartV;
            var camDot = Vector3.Dot(Overlay.ViewCamera.transform.forward, axis);
            var rotation = Quaternion.AngleAxis(angle, -axis * Mathf.Sign(camDot));
            normal = (rotation * dragStartNormal).normalized;
            u = (rotation * dragStartU).normalized;
            v = (rotation * dragStartV).normalized;
        }

        private static readonly Color ArrowColor = new(0.35f, 0.9f, 1f, 1f);

        private void DrawHandles() {
            var length = HandleLength;
            if (mode == Mode.Move) {
                var color = hover == Handle.Arrow || dragging == Handle.Arrow ? Theme.Warning : ArrowColor;
                if (Overlay.ToScreen(center - normal * length, out var a) && Overlay.ToScreen(center + normal * length, out var b)) {
                    Overlay.Line(a, b, color, 3f, true, 9f);
                    Overlay.Arrowhead(b, a, color, 16f, 13f);
                    Overlay.Arrowhead(a, b, color, 16f, 13f);
                }
                if (Overlay.ToScreen(center, out var mid)) {
                    Overlay.Disc(mid, 5f, Color.white);
                }
            }
            else if (mode == Mode.Rotate) {
                var hu = hover == Handle.RingU || dragging == Handle.RingU;
                var hv = hover == Handle.RingV || dragging == Handle.RingV;
                // Turning about u tilts the plane one way, about v the other (drawn in the plane's colours).
                ringU = Overlay.WorldCircle(center, u, length, hu ? Theme.Warning : Theme.AxisX, 2.5f, true);
                ringV = Overlay.WorldCircle(center, v, length, hv ? Theme.Warning : Theme.AxisY, 2.5f, true);
                if (dragging != Handle.None && Overlay.ToScreen(center, out var c)) {
                    Overlay.Text(c + new Vector2(0f, 40f), Tilt.ToString("0.#", CultureInfo.InvariantCulture) + "° from square", Theme.Text);
                }
            }
        }

        // ------------------------------------------------------------------ the plane

        private void UpdatePlaneObject() {
            if (planeObject == null) {
                planeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                planeObject.name = "Saw Cut Plane";
                Destroy(planeObject.GetComponent<Collider>());
                planeObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                planeInstance = new Material(planeMaterial);
                TryMakeTransparent(planeInstance, new Color(0.95f, 0.25f, 0.22f, 0.32f));
                planeObject.GetComponent<Renderer>().sharedMaterial = planeInstance;
                planeObject.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            planeObject.SetActive(true);
            UpdateCrossed();
            planeObject.transform.SetPositionAndRotation(drawCenter, Quaternion.LookRotation(normal, v));
            planeObject.transform.localScale = new Vector3(drawHalfU * 2f, drawHalfV * 2f, 0.004f);
        }

        // URP Lit, see-through.
        private static void TryMakeTransparent(Material material, Color color) {
            if (!material.HasProperty("_Surface")) {
                material.color = color;
                return;
            }
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
        }

        // V2: the parts the plane cuts with All parts on (outlined, and the plane covers them all); else just the
        // picked part's size.
        private void UpdateCrossed() {
            var key = (center, normal, cutAll);
            if (key == crossedFor && crossed.All(c => c != null)) return;
            crossedFor = key;
            foreach (var old in crossed)
                if (old != null && old != part) SetOutline(old, false);
            crossed = cutAll ? Crossed(out _) : new List<GameObject>();
            drawCenter = center;
            drawHalfU = planeHalfU;
            drawHalfV = planeHalfV;
            if (crossed.Count == 0) return;
            float minU = -planeHalfU, maxU = planeHalfU, minV = -planeHalfV, maxV = planeHalfV;
            foreach (var obj in crossed) {
                if (obj != part) OutlineFeature.SetOutline(obj.GetComponent<MeshRenderer>(), true, CrossedColor);
                foreach (var c in FeaturePicker.Corners(obj)) {
                    var d = c - center;
                    minU = Mathf.Min(minU, Vector3.Dot(d, u) - 0.25f);
                    maxU = Mathf.Max(maxU, Vector3.Dot(d, u) + 0.25f);
                    minV = Mathf.Min(minV, Vector3.Dot(d, v) - 0.25f);
                    maxV = Mathf.Max(maxV, Vector3.Dot(d, v) + 0.25f);
                }
            }
            drawCenter = center + u * (minU + maxU) / 2f + v * (minV + maxV) / 2f;
            drawHalfU = (maxU - minU) / 2f;
            drawHalfV = (maxV - minV) / 2f;
        }

        private static readonly Color CrossedColor = new(1f, 0.45f, 0.35f, 1f);

        private void ClearCrossed() {
            foreach (var old in crossed)
                if (old != null && old != part) SetOutline(old, false);
            crossed.Clear();
            crossedFor = default;
        }

        private void DrawPlaneOutline() {
            var corners = new[] {
                drawCenter + u * drawHalfU + v * drawHalfV, drawCenter - u * drawHalfU + v * drawHalfV,
                drawCenter - u * drawHalfU - v * drawHalfV, drawCenter + u * drawHalfU - v * drawHalfV,
            };
            var color = new Color(1f, 0.35f, 0.3f, 0.9f);
            for (var i = 0; i < 4; i++)
                Overlay.Line(corners[i], corners[(i + 1) % 4], color, 1.5f);
        }

        // ------------------------------------------------------------------ options bar

        private void BuildEditOptions(RectTransform bar) {
            var (behind, ahead) = Pieces();
            var name = NameOf(part);
            ToolOptions.Note(bar, name.Length > 34 ? name.Substring(0, 33) + "…" : name);
            Ui.Divider(bar, true).Size(1f, 22f);
            ToolOptions.Note(bar, "Distance");
            var field = Ui.Field(bar, "Distance", Inch.Format(Distance, false), text => {
                if (Inch.TryParse(text, out var inches)) TypeDistance(inches);
            }, Theme.FontBody, TextAlignmentOptions.Center);
            field.Size(80f, 30f);
            ToolOptions.Note(bar, "Angle");
            var angle = Ui.Field(bar, "Angle", Tilt.ToString("0.#", CultureInfo.InvariantCulture) + "°", text => {
                if (float.TryParse(text.Replace("°", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)) SetTilt(degrees);
                else feedback.Show("Type an angle in degrees, like 45", FeedbackDisplay.DisplayType.alert);
            }, Theme.FontBody, TextAlignmentOptions.Center);
            angle.Size(60f, 30f);
            Ui.Tip(angle, "Cut angle", null, "Degrees from square (0 = straight across). R shows rings to tilt it by dragging.");
            var from = Ui.Button(bar, "From", "From: " + (referenceIsEnd ? "part end" : reference.Describe().ToLowerInvariant()), () => SetMode(Mode.Reference), mode == Mode.Reference, 5f, Theme.FontSmall);
            from.Size(-1f, 30f);
            Ui.Tip(from, "Measure from", null, "Click, then click a hole, corner or face on any part to measure the cut from it.");
            var to = Ui.Button(bar, "To", "Cut at a point", () => SetMode(Mode.Target), mode == Mode.Target, 5f, Theme.FontSmall);
            to.Size(-1f, 30f);
            Ui.Tip(to, "Cut at a point", null, "Click, then click a hole, corner, edge or face: the cut moves there (keeping its angle).");
            ToolOptions.Note(bar, "Pieces " + Inch.Format(behind) + " + " + Inch.Format(ahead));
            Ui.Divider(bar, true).Size(1f, 22f);
            if (mode == Mode.Rotate) Ui.Hint(bar, "R", "slide", "Esc", "back");
            else if (mode == Mode.Reference || mode == Mode.Target) Ui.Hint(bar, "Esc", "back");
            else Ui.Hint(bar, "R", "tilt", "Shift", "free", "Esc", "cancel");
            ToolOptions.Switch(bar, "All parts", cutAll, on => {
                cutAll = on;
                if (!on) ClearCrossed();
                var count = on ? Crossed(out _).Count : 1;
                feedback.Show(on ? "The plane reaches every part it crosses: " + count + (count == 1 ? " part" : " parts") + " (outlined) will be cut" : "Only the picked part is cut", FeedbackDisplay.DisplayType.success);
            });
            var cut = Ui.Button(bar, "Cut", "Cut", ApplyCut, true, 5f);
            cut.Size(64f, 30f);
            Ui.Tip(cut, "Cut", "Enter", "Cut the part in two along the plane.");
        }

        private void LateUpdate() {
            // The options bar shows the live distance and pieces while dragging.
            if (part != null && dragging != Handle.None && Time.frameCount % 6 == 0)
                ToolOptions.Rebuild();
        }

        // ------------------------------------------------------------------ cutting

        private struct Cut {
            public Kind kind;
            public GameObject part;
            public string typeId, param1;
            public bool plate;
            public int axis;            // part space axis the cut goes across
            public int count, other;    // Holes: holes along the axis, and across it (plates)
            public int first;           // Holes: holes kept on the negative side
            public float length, at;    // Shaft: total length and where it's cut (from its start), inches
            public Vector3 point, normal; // Mesh: the plane in part space
        }

        private enum Kind { Holes, Shaft, Mesh }

        private void ApplyCut() {
            if (part == null) return;
            CloseEditField();
            var main = part;
            var targets = new List<GameObject> { main };
            var grouped = 0;
            if (cutAll) targets.AddRange(Crossed(out grouped).Where(p => p != main));
            ClearCrossed();
            var before = new List<IElement>();
            var after = new List<IElement>();
            string mainResult = null;
            var done = 0;
            foreach (var target in targets) {
                part = target;
                if (CutOne(out var result, before, after, targets.Count == 1)) {
                    done++;
                    mainResult ??= NameOf(target) + " " + result;
                }
            }
            part = main;
            if (done == 0) {
                if (targets.Count > 1) feedback.Show("The plane doesn't go through any part", FeedbackDisplay.DisplayType.alert);
                return;
            }

            EndCut();
            // Nothing stays selected on a part that's gone (a selected hole's marker would float there).
            foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None))
                manager.ClearCurrent();
            // Undo: the current state learns the pieces didn't exist yet; the new state swaps the parts for them.
            StateSystem.AddElements(before);
            StateSystem.AddState(new State(after));

            FindObjectOfType<PartListOutput>()?.CalculatePartsList();
            var missed = targets.Count - done;
            var note = (missed > 0 ? ", " + missed + " couldn't be cut there (a one-hole piece)" : "") + (grouped > 0 ? ", " + grouped + " in groups left alone (take them out of the group to cut them)" : "");
            feedback.Show((done == 1 ? "Cut " + mainResult : "Cut " + done + " parts along the plane") + note, FeedbackDisplay.DisplayType.success);
        }

        // V2: every cuttable part the (endless) plane goes through, connected to the picked one or not: its mesh has
        // points on both sides (its box isn't enough: a bent part's box can cross where the part doesn't). Parts in
        // a group (a Pivot) are counted apart: they're cut on their own.
        private List<GameObject> Crossed(out int grouped) {
            grouped = 0;
            var list = new List<GameObject>();
            foreach (var obj in PartsManager.FindLoadedObjects()) {
                if (obj == null || obj.IsDeleted() || !PartCutting.IsCuttable(obj)) continue;
                if (!obj.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                var t = obj.transform;
                // Quick test on its box first.
                float min = float.MaxValue, max = float.MinValue;
                foreach (var c in FeaturePicker.Corners(obj)) {
                    var d = Vector3.Dot(c - center, normal);
                    min = Mathf.Min(min, d);
                    max = Mathf.Max(max, d);
                }
                if (!(min < -0.01f && max > 0.01f)) continue;
                // Then its points (in its own space: the plane taken there).
                var localPoint = t.InverseTransformPoint(center);
                var localNormal = t.InverseTransformDirection(normal);
                var vertices = MeshVertices(filter.sharedMesh);
                bool below = false, above = false;
                var step = Mathf.Max(1, vertices.Length / 6000);
                for (var i = 0; i < vertices.Length && !(below && above); i += step) {
                    var d = Vector3.Dot(vertices[i] - localPoint, localNormal);
                    if (d < -0.005f) below = true;
                    else if (d > 0.005f) above = true;
                }
                if (!(below && above)) continue;
                if (obj != part && obj.transform.parent != null && obj.transform.parent.GetComponent<Pivot>() != null) {
                    grouped++;
                    continue;
                }
                list.Add(obj);
            }
            return list;
        }

        private static readonly Dictionary<Mesh, Vector3[]> vertexCache = new();

        private static Vector3[] MeshVertices(Mesh mesh) {
            if (vertexCache.TryGetValue(mesh, out var v)) return v;
            v = mesh.isReadable ? mesh.vertices : new Vector3[0];
            if (vertexCache.Count > 400) vertexCache.Clear();
            vertexCache[mesh] = v;
            return v;
        }

        // Cuts the part in two; the undo elements go into before/after. False (with a message when it's the only
        // part being cut) if it can't be cut here.
        private bool CutOne(out string result, List<IElement> before, List<IElement> after, bool alone) {
            result = null;
            var cut = Plan();
            var target = part;
            if (cut.kind == Kind.Holes && cut.count < 2) {
                if (alone) feedback.Show("A single-hole piece can't be cut any shorter", FeedbackDisplay.DisplayType.alert);
                return false;
            }

            GameObject first, second;
            switch (cut.kind) {
                case Kind.Holes: {
                    var firstCount = cut.first;
                    var secondCount = cut.count - cut.first;
                    first = Make(cut, firstCount, FirstHole(cut.count) + HoleSpacing * (firstCount - 1) / 2f);
                    second = Make(cut, secondCount, FirstHole(cut.count) + HoleSpacing * firstCount + HoleSpacing * (secondCount - 1) / 2f);
                    result = "into " + firstCount + " and " + secondCount + (cut.plate ? " rows" : " holes");
                    break;
                }
                case Kind.Shaft:
                    first = MakeShaft(cut, cut.at, 0f);
                    second = MakeShaft(cut, cut.length - cut.at, cut.at);
                    result = "into " + Inch.Format(cut.at) + " and " + Inch.Format(cut.length - cut.at);
                    break;
                default: {
                    var (behind, ahead) = Pieces();
                    MeshPieces(cut, out first, out second);
                    result = "into " + Inch.Format(behind) + " and " + Inch.Format(ahead);
                    break;
                }
            }

            if (first == null || second == null) {
                if (first != null) Destroy(first);
                if (second != null) Destroy(second);
                if (alone) feedback.Show("The plane doesn't go through the part", FeedbackDisplay.DisplayType.alert);
                return false;
            }

            if (cut.kind != Kind.Mesh) {
                // Same color and place in the hierarchy as the part that was cut, and its drilled holes.
                var color = target.GetComponent<Renderer>().material.color;
                foreach (var piece in new[] { first, second }) {
                    piece.transform.SetParent(target.transform.parent, true);
                    piece.GetComponent<Renderer>().material.color = color;
                    CarryDrilledHoles(target, piece);
                }
            }
            foreach (var piece in new[] { first, second })
                SetOutline(piece, false);

            before.Add(Element(target, true));
            before.Add(Element(first, false));
            before.Add(Element(second, false));
            new ObjectElement(target).ApplyExistence(false);
            after.Add(Element(target, false));
            after.Add(Element(first, true));
            after.Add(Element(second, true));
            return true;
        }

        // Which kind of cut: generated parts cut squarely where their generator can make the pieces.
        private Cut Plan() {
            var t = part.transform;
            var saved = part.GetComponent<SavedObject>();
            var idParts = saved.id.Split('-');
            var type = PartsManager.GetPartType(saved.id);
            var generator = type != null ? type.GetComponent<PartGenerator>() : null;
            var cut = new Cut {
                part = part, typeId = idParts[0], param1 = idParts.Length > 1 ? idParts[1] : "",
                point = t.InverseTransformPoint(center), normal = t.InverseTransformDirection(normal).normalized,
                kind = Kind.Mesh,
            };
            var edited = !string.IsNullOrEmpty(saved.state) && saved.state.Contains("cut:");
            if (edited) return cut;

            int SquareAxis() {
                for (var i = 0; i < 3; i++)
                    if (Mathf.Abs(cut.normal[i]) > 0.99999f) return i;
                return -1;
            }
            var square = SquareAxis();
            if (square < 0) return cut;
            var coordinate = cut.point[square];

            if (generator is AluminumPartGenerator && square == 0 && idParts.Length >= 3 && int.TryParse(idParts[2], out cut.count)) {
                if (OnGap(coordinate, cut.count, out cut.first)) { cut.kind = Kind.Holes; cut.axis = 0; }
            }
            else if (generator is PlatePartGenerator && square <= 1 && idParts.Length >= 3
                     && int.TryParse(idParts[1], out var length) && int.TryParse(idParts[2], out var width)) {
                var count = square == 0 ? length : width;
                if (OnGap(coordinate, count, out cut.first)) {
                    cut.kind = Kind.Holes;
                    cut.plate = true;
                    cut.axis = square;
                    cut.count = count;
                    cut.other = square == 0 ? width : length;
                }
            }
            else if (generator is ShaftPartGenerator && square == 2 && idParts.Length >= 3
                     && float.TryParse(idParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out cut.length)) {
                var bounds = part.GetComponent<MeshFilter>().sharedMesh.bounds;
                var along = (coordinate - bounds.min.z) / Mathf.Max(bounds.size.z, 1e-5f) * cut.length;
                if (along > 0.01f && along < cut.length - 0.01f) {
                    cut.kind = Kind.Shaft;
                    cut.axis = 2;
                    cut.at = Mathf.Round(along * 1000f) / 1000f;
                }
            }
            return cut;
        }

        // Whether a coordinate is in the middle of a gap between two holes (and which).
        private static bool OnGap(float coordinate, int count, out int first) {
            first = Mathf.RoundToInt((coordinate - FirstHole(count) - HoleSpacing / 2f) / HoleSpacing) + 1;
            var gap = FirstHole(count) + HoleSpacing * (first - 1) + HoleSpacing / 2f;
            return first >= 1 && first <= count - 1 && Mathf.Abs(gap - coordinate) < 0.002f;
        }

        private static float FirstHole(int count) => HoleSpacing * (-count + 1) / 2f;

        // Two copies of the part, each keeping one side of the cut.
        private static void MeshPieces(Cut cut, out GameObject first, out GameObject second) {
            first = Clone(cut.part);
            second = Clone(cut.part);
            var firstOk = PartCutting.Cut(first, cut.point, -cut.normal);
            var secondOk = PartCutting.Cut(second, cut.point, cut.normal);
            if (!firstOk || !secondOk) {
                Destroy(first);
                Destroy(second);
                first = second = null;
                return;
            }
            PartCutting.Record(first.GetComponent<SavedObject>(), cut.point, -cut.normal);
            PartCutting.Record(second.GetComponent<SavedObject>(), cut.point, cut.normal);
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

        private static string NameOf(GameObject part) => PartLabels.Short(part);

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
