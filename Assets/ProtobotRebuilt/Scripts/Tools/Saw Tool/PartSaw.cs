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
    //   it (5° steps); Esc goes back to the arrow, Esc again cancels and switches to the Transform tool.
    // - Click the dimension's value to type a distance; click its reference dot (or "Measure from") and then a
    //   hole, corner or face to measure from there instead, like dimensioning in a CAD program.
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

        private enum Mode { Pick, Move, Rotate, Reference }

        private Mode mode = Mode.Pick;
        private GameObject part;
        private Vector3 center, normal, u, v;  // the plane, world space (u, v span it)
        private FeaturePicker.Feature reference;
        private bool referenceIsEnd;
        private float planeHalfU = 1f, planeHalfV = 1f;
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
        private static void RegisterOptions() => ToolRail.RegisterOptions("Saw", BuildOptions);

        private static void BuildOptions(RectTransform bar) {
            if (active == null || active.part == null) {
                ToolOptions.Note(bar, "Click a part to place a cut");
                Ui.Hint(bar, "Esc", "Transform tool");
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
            var typing = TextInput.IsTyping;
            var keyboard = Keyboard.current;

            if (part == null || !part.activeInHierarchy) {
                if (part != null) EndCut();
                UpdatePick();
                if (!typing && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                    ToolRail.Select("Transform");
                return;
            }

            if (!typing && keyboard != null) {
                if (keyboard.escapeKey.wasPressedThisFrame) {
                    if (mode == Mode.Reference || mode == Mode.Rotate) SetMode(Mode.Move);
                    else {
                        EndCut();
                        feedback.Show("Cut cancelled", FeedbackDisplay.DisplayType.alert);
                        ToolRail.Select("Transform");
                        return;
                    }
                }
                else if (keyboard.rKey.wasPressedThisFrame && mode != Mode.Reference)
                    SetMode(mode == Mode.Rotate ? Mode.Move : Mode.Rotate);
                else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) {
                    ApplyCut();
                    return;
                }
            }

            if (mode == Mode.Reference) UpdateReferencePick();
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

            SetEndReference();
            MeasurePlane();
            mode = Mode.Move;
            ToolOptions.Rebuild();
            feedback.Show("Drag the arrow or click the dimension to set the cut", FeedbackDisplay.DisplayType.success);
        }

        private void EndCut() {
            if (part != null) SetOutline(part, false);
            part = null;
            mode = Mode.Pick;
            dragging = Handle.None;
            CloseEditField();
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
        }

        // How big to draw the plane: the part's extent across it.
        private void MeasurePlane() {
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var c in FeaturePicker.Corners(part)) {
                var d = c - center;
                var du = Vector3.Dot(d, u);
                var dv = Vector3.Dot(d, v);
                minU = Mathf.Min(minU, du); maxU = Mathf.Max(maxU, du);
                minV = Mathf.Min(minV, dv); maxV = Mathf.Max(maxV, dv);
            }
            if (minU > maxU) { minU = minV = -1f; maxU = maxV = 1f; }
            // Keep the plane centred on the part's cross-section.
            center += u * (minU + maxU) / 2f + v * (minV + maxV) / 2f;
            planeHalfU = (maxU - minU) / 2f + 0.25f;
            planeHalfV = (maxV - minV) / 2f + 0.25f;
        }

        // ------------------------------------------------------------------ the dimension

        /// <summary>Signed distance from the reference to the plane, along the plane's normal (inches).</summary>
        private float Distance => Vector3.Dot(center - reference.point, normal);

        private void SetDistance(float value) {
            center += normal * (value - Distance);
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

        private Vector3 DimensionOffset => -v * (planeHalfV + 0.35f);

        private void DrawDimension() {
            var d = Distance;
            var r = reference.point;
            var q = r + normal * d;               // the reference projected onto the plane
            var offset = DimensionOffset;
            var color = Theme.Dimension;
            // Extension lines from the reference and from the plane, then the dimension line between them.
            Overlay.Line(r, r + offset * 1.1f, new Color(color.r, color.g, color.b, 0.55f), 1f);
            Overlay.Line(q, q + offset * 1.1f, new Color(color.r, color.g, color.b, 0.55f), 1f);
            if (!Overlay.ToScreen(r + offset, out var a) || !Overlay.ToScreen(q + offset, out var b))
                return;
            Overlay.Line(a, b, color, 1.5f);
            if (Vector2.Distance(a, b) > 30f) {
                Overlay.Arrowhead(a, b, color);
                Overlay.Arrowhead(b, a, color);
            }
            // The reference end: a dot to click to measure from somewhere else.
            Overlay.Disc(a, 6f, Color.black, true);
            Overlay.Disc(a, 5f, mode == Mode.Reference ? Theme.Accent : color, true);
            var overDot = Vector2.Distance(Overlay.Mouse, a) < 10f;
            if (overDot) Overlay.Ring(a, 9f, Color.white, 1.5f);
            if (overDot && MouseInput.LeftButton.wasPressedThisFrame)
                SetMode(Mode.Reference);
            FeaturePicker.Draw(reference.kind == FeaturePicker.Kind.Face ? default : reference, color);

            if (editField == null) {
                var label = Overlay.Text((a + b) / 2f, Inch.Format(Mathf.Abs(d)), color, OpenEditField, true);
                Ui.Tip(label, "Cut distance", null, "Click to type a distance. Measured from " + ReferenceName + ".");
            }
            else
                ((RectTransform)editField.transform).anchoredPosition = (a + b) / 2f;
        }

        private string ReferenceName => referenceIsEnd ? "the end of the part" : reference.Describe().ToLowerInvariant();

        private void OpenEditField() {
            if (editField != null) return;
            editField = Ui.Field(Overlay.Instance.transform, "Distance", Inch.Format(Mathf.Abs(Distance), false), CommitEditField,
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
            if (Inch.TryParse(text, out var inches)) {
                var sign = Distance < 0f ? -1f : 1f;
                SetDistance(sign * inches);
                ToolOptions.Rebuild();
            }
            else if (!string.IsNullOrWhiteSpace(text))
                feedback.Show("Type a distance like 3 1/2 or 3.5", FeedbackDisplay.DisplayType.alert);
            CloseEditField();
        }

        private void CloseEditField() {
            if (editField != null) Destroy(editField.gameObject);
            editField = null;
        }

        private void UpdateReferencePick() {
            var feature = MouseInput.withinScreen && !MouseInput.overUI ? FeaturePicker.Pick() : default;
            FeaturePicker.Draw(feature, Theme.Accent);
            if (feature.Valid && Overlay.ToScreen(feature.point, out var p))
                Overlay.Text(p + new Vector2(0f, 22f), feature.Describe(), Theme.Text);
            if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI)
                return;
            if (!feature.Valid) {
                feedback.Show("Click a hole, corner or face to measure from", FeedbackDisplay.DisplayType.alert);
                return;
            }
            reference = feature;
            referenceIsEnd = false;
            SetMode(Mode.Move);
            feedback.Show("Measuring from the " + feature.Describe().ToLowerInvariant(), FeedbackDisplay.DisplayType.success);
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
                    MeasurePlane();  // tilted: fit the plane to the part again
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
                    var tilt = Vector3.Angle(dragStartNormal, normal);
                    Overlay.Text(c + new Vector2(0f, 40f), tilt.ToString("0.#", CultureInfo.InvariantCulture) + " deg", Theme.Text);
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
            planeObject.transform.SetPositionAndRotation(center, Quaternion.LookRotation(normal, v));
            planeObject.transform.localScale = new Vector3(planeHalfU * 2f, planeHalfV * 2f, 0.004f);
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

        private void DrawPlaneOutline() {
            var corners = new[] {
                center + u * planeHalfU + v * planeHalfV, center - u * planeHalfU + v * planeHalfV,
                center - u * planeHalfU - v * planeHalfV, center + u * planeHalfU - v * planeHalfV,
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
            var field = Ui.Field(bar, "Distance", Inch.Format(Mathf.Abs(Distance), false), text => {
                if (Inch.TryParse(text, out var inches)) {
                    SetDistance((Distance < 0f ? -1f : 1f) * inches);
                    ToolOptions.Rebuild();
                }
            }, Theme.FontBody, TextAlignmentOptions.Center);
            field.Size(80f, 30f);
            var from = Ui.Button(bar, "From", "From: " + (referenceIsEnd ? "part end" : reference.Describe().ToLowerInvariant()), () => SetMode(Mode.Reference), mode == Mode.Reference, 5f, Theme.FontSmall);
            from.Size(-1f, 30f);
            Ui.Tip(from, "Measure from", null, "Click, then click a hole, corner or face on any part to measure the cut from it.");
            ToolOptions.Note(bar, "Pieces " + Inch.Format(behind) + " + " + Inch.Format(ahead));
            Ui.Divider(bar, true).Size(1f, 22f);
            if (mode == Mode.Rotate) Ui.Hint(bar, "R", "slide", "Esc", "back");
            else if (mode == Mode.Reference) Ui.Hint(bar, "Esc", "back");
            else Ui.Hint(bar, "R", "tilt", "Shift", "free", "Esc", "cancel");
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
            var cut = Plan();
            var target = part;
            if (cut.kind == Kind.Holes && cut.count < 2) {
                feedback.Show("A single-hole piece can't be cut any shorter", FeedbackDisplay.DisplayType.alert);
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
                feedback.Show("The plane doesn't go through the part", FeedbackDisplay.DisplayType.alert);
                return;
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

            EndCut();
            // Nothing stays selected on the part that's gone (a selected hole's marker would float there).
            foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None))
                manager.ClearCurrent();
            // Undo: the current state learns the pieces didn't exist yet; the new state swaps the part for them.
            StateSystem.AddElement(Element(target, true));
            StateSystem.AddElement(Element(first, false));
            StateSystem.AddElement(Element(second, false));
            new ObjectElement(target).ApplyExistence(false);
            StateSystem.AddState(new State(new List<IElement> {
                Element(target, false), Element(first, true), Element(second, true)
            }));

            FindObjectOfType<PartListOutput>()?.CalculatePartsList();
            feedback.Show("Cut " + NameOf(target) + " " + result, FeedbackDisplay.DisplayType.success);
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

        private static string NameOf(GameObject part) =>
            part.TryGetComponent(out PartName named) && !string.IsNullOrEmpty(named.name) ? named.name : part.name;

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
