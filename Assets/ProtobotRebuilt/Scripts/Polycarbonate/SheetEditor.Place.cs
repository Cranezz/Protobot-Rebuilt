using System.Collections.Generic;
using System.Linq;
using Protobot.Custom;
using Protobot.Interface;
using Protobot.StateSystems;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Polycarb {
    // V2: sketching on a part, as SolidWorks sketches on a face. "Sketch on a part" leaves only the build on screen
    // (no parts list or tools; an Exit button in the corner): turn the view with the scene's own buttons, point at a
    // flat face of any part (it's outlined) and click. The editor comes back over the build with the sketch lying on
    // that face, looked at straight on in orthographic (Normal To); the view still turns, pans and zooms, and the
    // sketch stays on its face. Convert Entities brings the parts' edges and holes onto the sketch (purple, fixed)
    // to draw from and snap to, so the piece fits the robot exactly. Make piece puts the piece right there on the
    // face, in the build. A piece made this way can be edited in place again (right-click it).
    public partial class SheetEditor {
        private bool inPlace;
        private bool pickingFace;
        private SketchPlane plane;
        private GameObject planePart;
        private GameObject placedPiece;   // editing a piece in place: the one in the build

        private readonly List<GameObject> hiddenChrome = new();
        private readonly Dictionary<GameObject, bool> editorPanels = new();
        private (Vector3 position, Vector3 angle, float distance, bool ortho, float snap)? savedCamera;
        private RectTransform pickBanner;
        // The face under the mouse is drawn on the editor's own layer (the scene's Overlay is cleared by the tool
        // that's active in the build).
        private RectTransform pickDrawing;
        private ShapeGraphic pickShapes;
        private TextMeshProUGUI pickLabel;
        private Button placeButton;

        /// <summary>Opens the editor on a piece in the build, sketching on it where it is.</summary>
        public static void EditInPlace(CustomPartData part, GameObject piece) {
            if (Instance != null || part == null || piece == null) return;
            Show(part);
            var editor = Instance;
            if (editor == null) return;
            editor.placedPiece = piece;
            // The piece's frame: its x and z are the sketch's x and y, its y out of the face; its middle is the middle
            // of the outline it was made from.
            var t = piece.transform;
            var center = editor.OutlineCenter(out var found);
            if (!found) center = Vector2.zero;
            editor.plane = new SketchPlane { u = t.right, v = t.forward, n = t.up };
            editor.plane.origin = t.position - t.up * (editor.thickness / 2f + PieceGap) - t.right * center.x - t.forward * center.y;
            // The part it lies on (its face is the sketch's), found under the piece's middle.
            editor.planePart = null;
            Physics.SyncTransforms();
            var from = editor.plane.To3(center, 0.05f);
            foreach (var hit in Physics.RaycastAll(from, -t.up, 0.2f, LayerMask.GetMask("Default")).OrderBy(h => h.distance)) {
                var under = FeaturePicker.PartOf(hit.collider.gameObject);
                if (under == null || under == piece) continue;
                editor.planePart = under;
                break;
            }
            editor.BeginInPlace(editor.planePart);
        }

        private const float PieceGap = 0.001f;

        // The middle of the piece's outline (what PolyPart centres the piece on).
        private Vector2 OutlineCenter(out bool found) {
            found = false;
            var faces = ChosenFaces();
            if (faces.Count == 0 || regions == null) return Vector2.zero;
            var outers = regions.Outline(faces).Where(l => Regions.SignedArea(l) > 0f).SelectMany(l => l).ToList();
            if (outers.Count == 0) return Vector2.zero;
            found = true;
            var min = outers.Aggregate(Vector2.Min);
            var max = outers.Aggregate(Vector2.Max);
            return (min + max) / 2f;
        }

        private void BuildPlaceButton(RectTransform top) {
            placeButton = Ui.Button(top, "Place", "Sketch on a part", () => {
                if (inPlace) LeaveInPlace(true);
                else StartFacePick();
            }, false, 6f, Theme.FontSmall);
            placeButton.Size(-1, 32f);
            Ui.Tip(placeButton, "Sketch on a part", null, "Pick a flat face of a part in the build and sketch right on it, with the robot around it to draw from (as sketching on a face in SolidWorks).");
        }

        // ------------------------------------------------------------------ picking the face

        private void StartFacePick() {
            if (stage == Stage.Piece) SetStage(Stage.Sketch);
            CloseEditField();
            pickingFace = true;
            HideChrome();
            ShowEditorPanels(false);
            SaveCamera();
            SetBackdrop(false);
            pickDrawing = Ui.Rect("Pick drawing", transform).Fill();
            pickShapes = ShapeGraphic.Create(pickDrawing);
            pickLabel = Ui.Text(pickDrawing, "Label", "", Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            pickLabel.rectTransform.anchorMin = pickLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            pickLabel.rectTransform.sizeDelta = new Vector2(520f, 30f);
            pickBanner = Ui.Panel(transform, "Pick a face", Theme.Panel, 10f).rectTransform;
            pickBanner.anchorMin = pickBanner.anchorMax = new Vector2(0.5f, 1f);
            pickBanner.pivot = new Vector2(0.5f, 1f);
            pickBanner.anchoredPosition = new Vector2(0f, -14f);
            pickBanner.sizeDelta = new Vector2(560f, 64f);
            var words = Ui.Text(pickBanner, "Text", "Click a flat face of a part to sketch on it\nTurn and pan the view with the scene's buttons; the wheel zooms", Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            words.textWrappingMode = TextWrappingModes.Normal;
            words.rectTransform.Fill(14, 6, 14, 6);
            var exit = Ui.Button(transform, "Exit", "Exit", () => {
                EndFacePick();
                if (!inPlace) {
                    RestoreScene();
                    Interface.Interface.Say("Back to the sheet", true);
                }
            }, true, 8f);
            ((RectTransform)exit.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(96f, 40f));
            exit.name = "Exit Pick";
            Ui.Tip(exit, "Exit", "Esc", "Back to the sketch without picking a face.");
            Interface.Interface.Say("Click a flat face of a part to sketch on it", true);
        }

        private void EndFacePick() {
            pickingFace = false;
            if (pickBanner != null) Destroy(pickBanner.gameObject);
            if (pickDrawing != null) Destroy(pickDrawing.gameObject);
            var exit = transform.Find("Exit Pick");
            if (exit != null) Destroy(exit.gameObject);
            ShowEditorPanels(true);
            if (!inPlace) SetBackdrop(true);
        }

        private Vector2 PickLocal(Vector3 world) {
            var screen = Overlay.ViewCamera.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(pickDrawing, screen, null, out var local);
            return local;
        }

        private void PickText(Vector3 world, string text, Color color) {
            pickLabel.text = text;
            pickLabel.color = color;
            pickLabel.rectTransform.anchoredPosition = PickLocal(world) + new Vector2(0f, 30f);
        }

        private void UpdateFacePick() {
            pickShapes.Clear();
            pickLabel.text = "";
            SceneCamera();
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) {
                EndFacePick();
                if (!inPlace) RestoreScene();
                return;
            }
            // The part's own surface, through its hole markers (they cover much of a plate).
            var ray = Overlay.ViewCamera.ScreenPointToRay(MouseInput.Position);
            if (!Physics.Raycast(ray, out var hit, Mathf.Infinity, LayerMask.GetMask("Default"))) return;
            var part = FeaturePicker.PartOf(hit.collider.gameObject);
            if (part == null) return;
            var n = hit.normal.normalized;
            var face = FaceEdges(part, hit.point, n);
            var flat = face.Count >= 3;
            foreach (var (a, b) in face) pickShapes.Line(PickLocal(a), PickLocal(b), flat ? Theme.Accent : Theme.Danger, 2.5f);
            PickText(hit.point, flat ? "Sketch on this face of the " + PartLabels.Short(part) : "Not a flat face: pick a flat one", flat ? Theme.Text : Theme.Danger);
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            if (!flat) {
                Interface.Interface.Say("Pick a flat face: a sketch lies on a flat face", false);
                return;
            }
            EnterPlane(part, hit.point, n);
        }

        // The part's edges lying on a plane (the face's outline, and any other face on the same plane).
        private static List<(Vector3 a, Vector3 b)> FaceEdges(GameObject part, Vector3 point, Vector3 normal) =>
            FeaturePicker.SharpEdges(part).Where(e => Mathf.Abs(Vector3.Dot(e.a - point, normal)) < 0.01f && Mathf.Abs(Vector3.Dot(e.b - point, normal)) < 0.01f).ToList();

        private void EnterPlane(GameObject part, Vector3 point, Vector3 normal) {
            // The origin: the part's middle brought onto the face; x along the part's own axis that lies flattest on
            // it (so horizontal and vertical follow the part).
            var renderer = part.GetComponent<Renderer>();
            var middle = renderer != null ? renderer.bounds.center : part.transform.position;
            var t = part.transform;
            var axis = new[] { t.right, t.up, t.forward }.OrderBy(a => Mathf.Abs(Vector3.Dot(a, normal))).First();
            var u = Vector3.ProjectOnPlane(axis, normal).normalized;
            if (u.sqrMagnitude < 0.5f) u = Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
            plane = new SketchPlane {
                origin = middle - normal * Vector3.Dot(middle - point, normal), u = u, n = normal, v = Vector3.Cross(u, normal).normalized,
            };
            planePart = part;
            EndFacePick();
            BeginInPlace(part);
            Interface.Interface.Say("Sketching on the " + PartLabels.Short(part) + ": Convert entities brings its edges and holes in to draw from", true);
        }

        private void BeginInPlace(GameObject part) {
            inPlace = true;
            HideChrome();
            SaveCamera();
            SetBackdrop(false);
            var switcher = PivotCamera.Main != null ? PivotCamera.Main.GetComponent<ProjectionSwitcher>() : null;
            if (switcher != null && !switcher.isOrtho) switcher.SwitchToOrtho(0.3f);
            if (PivotCamera.Main != null) PivotCamera.Main.snapSensitivity = 0f;
            NormalTo();
            if (placeButton != null) placeButton.GetComponentInChildren<TextMeshProUGUI>().text = "Back to the sheet";
            SetTool(part != null ? Tool.Project : Tool.Select);
            Changed();
        }

        /// <summary>Leaves the build view: the sketch goes back to the flat sheet (kept as it is).</summary>
        private void LeaveInPlace(bool say) {
            inPlace = false;
            planePart = null;
            RestoreScene();
            SetBackdrop(true);
            if (placeButton != null) placeButton.GetComponentInChildren<TextMeshProUGUI>().text = "Sketch on a part";
            if (tool == Tool.Project) SetTool(Tool.Select);
            Fit();
            if (say) Interface.Interface.Say("Back to the flat sheet (the sketch is kept)", true);
        }

        // ------------------------------------------------------------------ the scene around

        // The parts list, tool rail, top bar and the rest go while sketching in the build (put back after).
        private void HideChrome() {
            if (hiddenChrome.Count > 0) return;
            var root = Interface.Interface.Root;
            if (root == null) return;
            foreach (Transform child in root) {
                if (child == transform || !child.gameObject.activeSelf) continue;
                var name = child.name;
                // The toasts and the drawing overlay stay.
                if (name.Contains("Overlay") || name.Contains("Toast") || name.Contains("Feedback") || name.Contains("Tooltip") || name.Contains("Popup")) continue;
                child.gameObject.SetActive(false);
                hiddenChrome.Add(child.gameObject);
            }
        }

        private void ShowChrome() {
            foreach (var go in hiddenChrome)
                if (go != null && go.scene.isLoaded) go.SetActive(true);
            hiddenChrome.Clear();
        }

        private void ShowEditorPanels(bool show) {
            if (!show) {
                editorPanels.Clear();
                foreach (Transform child in transform) {
                    if (child.name is "Pick a face" or "Exit Pick" or "Pick drawing") continue;
                    editorPanels[child.gameObject] = child.gameObject.activeSelf;
                    child.gameObject.SetActive(false);
                }
                return;
            }
            foreach (var (go, active) in editorPanels)
                if (go != null) go.SetActive(active);
            editorPanels.Clear();
        }

        // The dark backdrop of the flat sheet, or none (the build shows through) while sketching in it.
        private void SetBackdrop(bool on) {
            var background = GetComponent<Image>();
            if (background != null) background.color = on ? new Color(0.07f, 0.075f, 0.09f, 1f) : new Color(0f, 0f, 0f, 0f);
            var canvasBackground = canvasRect != null ? canvasRect.GetComponent<Image>() : null;
            if (canvasBackground != null) canvasBackground.color = on ? new Color(0.1f, 0.11f, 0.13f, 1f) : new Color(0f, 0f, 0f, 0f);
        }

        private void SaveCamera() {
            if (savedCamera.HasValue || PivotCamera.Main == null) return;
            var cam = PivotCamera.Main;
            var switcher = cam.GetComponent<ProjectionSwitcher>();
            savedCamera = (cam.focusPosition, cam.lookAngle, cam.focusDistance, switcher != null && switcher.isOrtho, cam.snapSensitivity);
        }

        private void RestoreScene() {
            ShowChrome();
            if (!savedCamera.HasValue || PivotCamera.Main == null) {
                savedCamera = null;
                return;
            }
            var (position, angle, distance, ortho, snap) = savedCamera.Value;
            var cam = PivotCamera.Main;
            cam.snapSensitivity = snap;
            cam.SetTransform(position, angle, distance);
            var switcher = cam.GetComponent<ProjectionSwitcher>();
            if (switcher != null && switcher.isOrtho != ortho) {
                if (ortho) switcher.SwitchToOrtho(0.3f);
                else switcher.SwitchToPers(0.3f);
            }
            savedCamera = null;
        }

        // Turn, pan and zoom with the scene's own buttons (the scene's camera input is paused while the editor is open).
        private void SceneCamera() {
            var cam = PivotCamera.Main;
            var mouse = Mouse.current;
            if (cam == null || mouse == null) return;
            var overPanel = !pickingFace && OverPanel(mouse.position.ReadValue());
            var orbit = MousePivotCameraInput.OrbitPressed;
            var pan = MousePivotCameraInput.PanPressed;
            if (orbit && pan) orbit = false;
            if (orbit && (MouseInput.xAxis != 0f || MouseInput.yAxis != 0f))
                cam.OrbitControl(new Vector2(MouseInput.yAxis * 2f, MouseInput.xAxis * 2f));
            if (pan && (MouseInput.xAxis != 0f || MouseInput.yAxis != 0f))
                cam.PanControl(new Vector2(MouseInput.xAxis, MouseInput.yAxis) * 0.15f);
            var wheel = MouseInput.scrollAxis;
            if (Mathf.Abs(wheel) > 0.001f && !overPanel && !Popup.IsOpen && Dialog.OpenCount == 0) cam.ZoomControl(wheel * 2f);
        }

        /// <summary>Looks straight at the sketch, fitting it (SolidWorks' Normal To).</summary>
        private void NormalTo() {
            if (!inPlace) {
                Interface.Interface.Say("Normal to turns the view to face the sketch when you're sketching on a part", false);
                return;
            }
            var cam = PivotCamera.Main;
            if (cam == null) return;
            var points = sketch.entities.SelectMany(sketch.PointsOf).Distinct().Select(sketch.P).ToList();
            Vector2 min = new(-2f, -2f), max = new(2f, 2f);
            if (points.Count > 1) {
                min = points.Aggregate(Vector2.Min);
                max = points.Aggregate(Vector2.Max);
            }
            if (planePart != null) {
                foreach (var c in FeaturePicker.Corners(planePart)) {
                    var q = plane.To2(c);
                    min = Vector2.Min(min, q);
                    max = Vector2.Max(max, q);
                }
            }
            var size = max - min;
            var aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.6f;
            var distance = Mathf.Clamp(Mathf.Max(size.y, size.x / aspect) * 1.5f + 1f, 2f, 300f);
            cam.SetTransform(plane.To3((min + max) / 2f), Quaternion.LookRotation(-plane.n, plane.v).eulerAngles, -distance);
        }

        // ------------------------------------------------------------------ the sketch on the plane, on screen

        private Vector2 PlaneToCanvas(Vector2 inches, float height) {
            var cam = Overlay.ViewCamera;
            var screen = cam.WorldToScreenPoint(plane.To3(inches, height));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
            return local;
        }

        private Vector2 CanvasToPlane(Vector2 canvas) {
            var cam = Overlay.ViewCamera;
            var screen = RectTransformUtility.WorldToScreenPoint(null, canvasRect.TransformPoint(canvas));
            var ray = cam.ScreenPointToRay(screen);
            var denominator = Vector3.Dot(ray.direction, plane.n);
            if (Mathf.Abs(denominator) < 1e-6f) return plane.To2(ray.origin);
            var t = Vector3.Dot(plane.origin - ray.origin, plane.n) / denominator;
            return plane.To2(ray.GetPoint(t));
        }

        private Vector2 WorldToCanvas(Vector3 world) {
            var screen = Overlay.ViewCamera.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
            return local;
        }

        // Canvas units per inch where the view is (the tolerances and sizes that use zoom follow the camera).
        private void UpdatePlaneZoom() {
            var center = CanvasToPlane(canvasRect.rect.center);
            var pixels = Vector2.Distance(PlaneToCanvas(center, 0f), PlaneToCanvas(center + Vector2.right, 0f));
            zoom = Mathf.Clamp(pixels, 0.5f, 20000f);
        }

        // ------------------------------------------------------------------ Convert Entities

        private enum ProjectMode { Edge, Face, Part }
        private ProjectMode projectMode = ProjectMode.Edge;
        private bool projectAsCut;
        private bool holesAsReference;
        private float projectDepth = 0.25f;
        private SketchProject.Shape projectHover;
        private List<(Vector3 a, Vector3 b)> projectHover3D = new();

        private void HandleProject(bool pressed) {
            projectHover = null;
            projectHover3D.Clear();
            if (!inPlace) return;
            var part = FeaturePicker.PartUnderMouse(out var hit);
            if (part == null) return;
            var mouse = Overlay.Mouse;
            switch (projectMode) {
                case ProjectMode.Edge: {
                    // A hole under the mouse, else the edge nearest it.
                    if (hit.collider != null && hit.collider.TryGetComponent(out HoleCollider hole)) {
                        projectHover = SketchProject.Hole(hole, plane);
                        break;
                    }
                    var best = 10f;
                    (Vector3 a, Vector3 b) edge = default;
                    foreach (var e in FeaturePicker.SharpEdges(part)) {
                        if (!Overlay.ToScreen(e.a, out var sa) || !Overlay.ToScreen(e.b, out var sb)) continue;
                        var d = Overlay.DistanceToSegment(mouse, sa, sb);
                        if (d < best) {
                            best = d;
                            edge = e;
                        }
                    }
                    if (best < 10f) {
                        projectHover = SketchProject.Edge(edge.a, edge.b, plane);
                        projectHover3D.Add(edge);
                    }
                    break;
                }
                case ProjectMode.Face: {
                    if (hit.collider == null) break;
                    var n = hit.normal.normalized;
                    var facePlane = new SketchPlane { origin = hit.point, n = n };
                    projectHover3D.AddRange(FaceEdges(part, hit.point, n));
                    projectHover = SketchProject.Part(part, plane, faceHeight: plane.Height(hit.point));
                    // A face not parallel to the sketch: just its own edges.
                    if (Mathf.Abs(Vector3.Dot(n, plane.n)) < 0.99f) {
                        projectHover = new SketchProject.Shape();
                        var raw = projectHover3D.Select(e => (plane.To2(e.a), plane.To2(e.b))).Where(s => Vector2.Distance(s.Item1, s.Item2) > 1e-3f).ToList();
                        projectHover.segments.AddRange(SketchProject.Merge(raw));
                    }
                    break;
                }
                case ProjectMode.Part:
                    projectHover = SketchProject.Part(part, plane);
                    break;
            }
            if (!pressed) return;
            if (projectHover == null || projectHover.Empty) {
                Interface.Interface.Say("Nothing there to bring in: point at an edge, a hole or a face", false);
                return;
            }
            AddProjected(projectHover, projectMode switch {
                ProjectMode.Edge => "the edge",
                ProjectMode.Face => "the face",
                _ => "the " + PartLabels.Short(part),
            });
        }

        private void AddProjected(SketchProject.Shape shape, string what) {
            Remember();
            var added = SketchProject.Add(sketch, shape, !projectAsCut, holesAsReference);
            if (added == 0) {
                undo.RemoveAt(undo.Count - 1);
                Interface.Interface.Say("That's in the sketch already", false);
                return;
            }
            Changed();
            Interface.Interface.Say("Brought in " + what + " (" + added + (added == 1 ? " curve" : " curves") + ")");
        }

        private void ProjectFaceSketchedOn() {
            if (!inPlace || planePart == null) {
                Interface.Interface.Say("Sketch on a part first", false);
                return;
            }
            AddProjected(SketchProject.Part(planePart, plane, faceHeight: 0f), "the face you're sketching on");
        }

        private void ProjectEverythingNear() {
            if (!inPlace) return;
            var shape = new SketchProject.Shape();
            foreach (var part in PartsManager.FindLoadedObjects()) {
                if (part == null || part.IsDeleted() || part == placedPiece) continue;
                var one = SketchProject.Part(part, plane, projectDepth);
                shape.AddRange(one);
            }
            var merged = SketchProject.Merge(shape.segments.ToList());
            shape.segments.Clear();
            shape.segments.AddRange(merged);
            AddProjected(shape, "every edge and hole within " + Inch.Format(projectDepth) + " of the sketch");
        }

        private void DrawProjectHover() {
            if (projectHover3D.Count > 0)
                foreach (var (a, b) in projectHover3D)
                    shapes.Line(WorldToCanvas(a), WorldToCanvas(b), new Color(1f, 0.85f, 0.3f, 0.9f), 2.5f);
            if (projectHover == null) return;
            var color = new Color(Projected.r, Projected.g, Projected.b, 0.85f);
            foreach (var (a, b) in projectHover.segments.Concat(projectHover.holeSides)) shapes.Line(ToCanvas(a), ToCanvas(b), color, 2f);
            foreach (var (c, r) in projectHover.circles) shapes.Polyline(CircleOnCanvas(c, r), color, 2f, true);
        }

        // ------------------------------------------------------------------ the piece, on the face

        /// <summary>Puts the piece made from the sketch on the face it was sketched on (or moves the one being edited
        /// there), one undo step.</summary>
        private void PlaceOnFace(CustomPartData part, List<List<Vector2>> loops) {
            var outers = loops.Where(l => Regions.SignedArea(l) > 0f).SelectMany(l => l).ToList();
            if (outers.Count == 0) return;
            var center = (outers.Aggregate(Vector2.Min) + outers.Aggregate(Vector2.Max)) / 2f;
            var position = plane.To3(center, thickness / 2f + PieceGap);
            var rotation = plane.Rotation;
            if (placedPiece != null && !placedPiece.IsDeleted()) {
                StateSystem.AddElement(new ObjectElement(placedPiece));
                placedPiece.transform.SetPositionAndRotation(position, rotation);
                StateSystem.AddState(new ObjectElement(placedPiece));
                return;
            }
            var made = PartsManager.GeneratePart(part.Id, position, rotation);
            if (made == null) return;
            StateSystem.AddElement(new ObjectElement(made) { existing = false });
            StateSystem.AddState(new ObjectElement(made));
            PartsRecent.Use(part.Id);
        }
    }
}
