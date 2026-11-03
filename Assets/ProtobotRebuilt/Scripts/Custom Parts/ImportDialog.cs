using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Protobot.Geometry;
using Protobot.Interface;
using SFB;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Custom {
    // V2: importing your own part. "+" in the parts browser opens the computer's file window (STL, OBJ, 3MF, GLB,
    // glTF, PLY, OFF); the model then shows here at full detail (a warning when it has so many triangles it may
    // slow the app), turning slowly until you touch it; it turns and pans with the same mouse buttons as the scene
    // (or a left drag) and zooms with the wheel, and the scene behind holds still. Settings: name, the file's units
    // and up axis, material (weight) and "Part has holes", which finds the holes and outlines them in blue. Point
    // at any outline in a flat face (a circle, a filleted slot, any shape with an inside) and click: it becomes a
    // hole of exactly that shape (screws slide around in it when it's bigger than them); click a hole to select it,
    // Delete removes it. "Add to parts" saves it to the library and starts placing it. Edit (Select tool, or a
    // right-click on the part) opens a part again to change its holes; every copy in the build updates.
    public class ImportDialog : MonoBehaviour {
        private const int DetailWarning = 60000;
        private static readonly string[] MaterialNames = { "Aluminum", "Steel", "Plastic" };
        private const int PreviewLayer = 15;
        private const int MaxTriangles = 600000;

        private Dialog dialog;
        private RectTransform settings;
        private RawImage previewImage;
        private ModelImport.Result file;
        private string path;

        // Settings
        private string partName;
        private ModelImport.Unit unit;
        private bool zUp;
        private int material;
        private bool hasHoles;
        private int selectedHole = -1, hoverHole = -1;
        private CustomPartData editing;  // the library part being edited (null: a new import)

        // The model in inches, Y up, centred: full detail (for holes) and as it will be saved.
        private List<Vector3> fullVertices = new(), vertices = new();
        private List<int> fullTriangles = new(), triangles = new();
        private readonly List<CustomHole> holes = new();

        // Preview
        private GameObject stage, model;
        private Camera previewCamera;
        private RenderTexture texture;
        private Mesh previewMesh;
        private ShapeGraphic shapes;
        private Facets facets;
        private Facets.Loop hoverLoop;
        private TextMeshProUGUI modeHint;
        private float yaw = 30f, pitch = 25f, distance = 10f;
        private Vector3 target;  // what the preview camera turns around (panned)
        private bool spinning = true;
        private Vector2 lastMouse;
        private bool dragging, pressedOnPreview;
        private Vector2 pressMouse;

        public static void Choose() {
            var filters = new[] { new ExtensionFilter("3D models", ModelImport.Extensions), new ExtensionFilter("All files", "*") };
            var paths = StandaloneFileBrowser.OpenFilePanel("Import a part", "", filters, false);
            InputEvents.TextInput.AfterFileWindow();
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) {
                Interface.Interface.Say("Import cancelled", false);
                return;
            }
            Open(paths[0]);
        }

        public static void Open(string path) {
            var result = ModelImport.Read(path);
            if (result.error != null) {
                Interface.Interface.Say(result.error, false);
                return;
            }
            if (result.triangles.Count / 3 > MaxTriangles) {
                Interface.Interface.Say("That model has " + (result.triangles.Count / 3).ToString("N0") + " triangles: the most is " + MaxTriangles.ToString("N0"), false);
                return;
            }
            var dialog = Dialog.Open("Import part", new Vector2(1000f, 700f));
            var import = dialog.gameObject.AddComponent<ImportDialog>();
            import.dialog = dialog;
            import.file = result;
            import.path = path;
            import.Build();
            if (result.triangles.Count / 3 > DetailWarning)
                Interface.Interface.Say("This model has " + (result.triangles.Count / 3).ToString("N0") + " triangles: lots of copies of it may slow things down", false);
        }

        /// <summary>Opens a part from the library again, to change its name, material and holes.</summary>
        public static void Edit(CustomPartData part) {
            if (part == null || !CustomParts.ReadMesh(part, out var meshVertices, out var meshTriangles)) {
                Interface.Interface.Say("Couldn't open that part", false);
                return;
            }
            var result = new ModelImport.Result { unit = ModelImport.Unit.Inches, unitKnown = true };
            result.vertices.AddRange(meshVertices);
            result.triangles.AddRange(meshTriangles);
            var dialog = Dialog.Open("Edit part", new Vector2(1000f, 700f));
            var import = dialog.gameObject.AddComponent<ImportDialog>();
            import.dialog = dialog;
            import.file = result;
            import.path = part.source ?? part.name;
            import.editing = part;
            import.partName = part.name;
            import.material = Mathf.Max(0, System.Array.IndexOf(MaterialNames, part.material));
            import.hasHoles = part.holes != null && part.holes.Length > 0;
            import.holes.AddRange(part.holes ?? new CustomHole[0]);
            import.Build();
        }

        private void Build() {
            if (editing == null) partName = Path.GetFileNameWithoutExtension(path);
            unit = file.unit;
            zUp = file.zUp;
            dialog.OnClose += Cleanup;

            // Preview on the left, settings on the right.
            var previewFrame = Ui.Image(dialog.Body, "Preview", Theme.Field, 8f, true);
            previewFrame.rectTransform.anchorMin = new Vector2(0f, 0f);
            previewFrame.rectTransform.anchorMax = new Vector2(0f, 1f);
            previewFrame.rectTransform.pivot = new Vector2(0f, 0.5f);
            previewFrame.rectTransform.sizeDelta = new Vector2(540f, 0f);
            previewFrame.rectTransform.anchoredPosition = Vector2.zero;
            previewImage = Ui.Rect("Image", previewFrame.transform).Fill(2, 2, 2, 2).gameObject.AddComponent<RawImage>();
            previewImage.raycastTarget = true;
            shapes = ShapeGraphic.Create(previewImage.transform);
            var hint = Ui.Hint(previewFrame.transform, "Drag", "turn", "Middle drag", "pan", "Wheel", "zoom", "Click", "a shape: hole");
            hint.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 10f), new Vector2(520f, 22f));
            // What a click on the model does now (adding a hole, picking a shape).
            modeHint = Ui.Text(previewFrame.transform, "Mode", "", Theme.FontSmall, Theme.Text);
            modeHint.alignment = TextAlignmentOptions.Center;
            modeHint.rectTransform.anchorMin = new Vector2(0f, 1f);
            modeHint.rectTransform.anchorMax = new Vector2(1f, 1f);
            modeHint.rectTransform.pivot = new Vector2(0.5f, 1f);
            modeHint.rectTransform.sizeDelta = new Vector2(-24f, 26f);
            modeHint.rectTransform.anchoredPosition = new Vector2(0f, -10f);

            // The settings scroll when they don't fit (holes add rows).
            var scroll = Ui.Rect("Settings", dialog.Body);
            scroll.anchorMin = new Vector2(0f, 0f);
            scroll.anchorMax = new Vector2(1f, 1f);
            scroll.offsetMin = new Vector2(560f, 0f);
            scroll.offsetMax = Vector2.zero;
            scroll.gameObject.AddComponent<RectMask2D>();
            scroll.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            settings = Ui.Rect("Column", scroll);
            settings.anchorMin = new Vector2(0f, 1f);
            settings.anchorMax = new Vector2(1f, 1f);
            settings.pivot = new Vector2(0.5f, 1f);
            settings.sizeDelta = Vector2.zero;
            Ui.Column(settings, 5f, new RectOffset(0, 8, 0, 8));
            settings.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = scroll;
            scrollRect.content = settings;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            var cancel = Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, false, 6f);
            cancel.Size(-1f, 36f);
            var add = Ui.Button(dialog.Footer, "Add", editing != null ? "Save changes" : "Add to parts", Finish, true, 6f);
            add.Size(-1f, 36f);

            MakeStage();
            Process();
            FrameModel();
            BuildSettings();
        }

        // ------------------------------------------------------------------ the mesh

        private void Process() {
            if (editing != null) {
                // Already in inches, Y up and centred; its holes are kept as they were.
                fullVertices = new List<Vector3>(file.vertices);
                fullTriangles = new List<int>(file.triangles);
                Simplify();
                return;
            }
            // Units and axes.
            var scale = ModelImport.InchesPer(unit);
            fullVertices = file.vertices.Select(v => (zUp ? new Vector3(v.x, v.z, v.y) : new Vector3(v.x, v.y, -v.z)) * scale).ToList();
            fullTriangles = new List<int>(file.triangles);
            MeshTools.Weld(fullVertices, fullTriangles, 0.0005f);
            if (fullVertices.Count == 0) return;
            // Changing the up axis mirrors the model (files are right-handed, Unity is left-handed), which turns
            // its faces inside out; files saved inside out are fixed the same way.
            if (SignedVolume(fullVertices, fullTriangles) < 0.0)
                for (var t = 0; t + 2 < fullTriangles.Count; t += 3)
                    (fullTriangles[t + 1], fullTriangles[t + 2]) = (fullTriangles[t + 2], fullTriangles[t + 1]);
            // Centred on its bounds.
            var bounds = new Bounds(fullVertices[0], Vector3.zero);
            foreach (var v in fullVertices) bounds.Encapsulate(v);
            for (var i = 0; i < fullVertices.Count; i++) fullVertices[i] -= bounds.center;
            Simplify();
            if (hasHoles) FindHoles();
        }

        private static double SignedVolume(List<Vector3> v, List<int> t) {
            double volume = 0;
            for (var i = 0; i + 2 < t.Count; i += 3)
                volume += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
            return volume;
        }

        // Full detail (the quality presets lost the shapes holes are picked from); hard edges kept sharp.
        private void Simplify() {
            vertices = new List<Vector3>(fullVertices);
            triangles = new List<int>(fullTriangles);
            MeshTools.SplitHardEdges(vertices, triangles, 30f);
            facets = null;
            UpdatePreviewMesh();
        }

        private Bounds ModelBounds {
            get {
                if (fullVertices.Count == 0) return new Bounds();
                var bounds = new Bounds(fullVertices[0], Vector3.zero);
                foreach (var v in fullVertices) bounds.Encapsulate(v);
                return bounds;
            }
        }

        private void FindHoles() {
            holes.Clear();
            selectedHole = -1;
            const float m = 0.0254f;
            var found = HoleScanner.Scan(fullVertices.Select(v => v * m).ToList(), fullTriangles);
            foreach (var hole in found) {
                var size = hole.size / m;
                if (size < 0.1f) continue;  // smaller than an 8-32 screw: nothing goes in it
                var found1 = new CustomHole {
                    center = (hole.entry + hole.axis * (hole.depth / 2f)) / m,
                    axis = hole.axis.normalized, size = size, depth = hole.depth / m, square = hole.square,
                    threaded = !hole.square && size <= 0.168f && size >= 0.125f && !hole.through,
                    up = hole.side.normalized, size2 = hole.size2 / m,
                };
                // V2: a shape that isn't a circle or a plain square/slot (a hexagon, a D) gets its exact outline,
                // as clicking it would.
                var exact = ExactShape(found1);
                if (exact != null) {
                    exact.threaded = false;
                    found1 = exact;
                }
                holes.Add(found1);
            }
        }

        // The outline in a flat face around a found hole, as an exact hole, when it isn't round or a plain box.
        private CustomHole ExactShape(CustomHole found) {
            if (vertices.Count == 0) return null;
            facets ??= new Facets(vertices, triangles);
            foreach (var loop in facets.innerLoops) {
                if (Mathf.Abs(Vector3.Dot(loop.normal.normalized, found.axis)) < 0.98f) continue;
                if (Vector3.ProjectOnPlane(loop.center - found.center, found.axis).magnitude > Mathf.Max(0.05f, found.size * 0.25f)) continue;
                var hole = HoleFrom(loop, out var fit);
                if (hole == null || hole.outline == null) return null;          // round
                if (loop.points.Count == 4) return null;                        // a plain square or slot: the scanner's box is it
                return hole;
            }
            return null;
        }

        // ------------------------------------------------------------------ settings column

        private void BuildSettings() {
            foreach (Transform child in settings) Destroy(child.gameObject);
            settings.DetachChildren();

            Dialog.Heading(settings, "Part");
            var nameRow = Dialog.Row(settings, "Name");
            var nameField = Ui.Field(nameRow, "Name", partName, value => partName = value.Trim().Length > 0 ? value.Trim() : partName);
            nameField.Size(-1, 32f, 1f);
            var fileRow = Dialog.Row(settings, "File", 24f);
            var fileText = Ui.Text(fileRow, "File", Path.GetFileName(path), Theme.FontSmall, Theme.TextFaint);
            fileText.Size(-1, 24f, 1f);

            Dialog.Heading(settings, "Size");
            if (editing == null) {
            var unitRow = Dialog.Row(settings, "Units");
            ToolOptions.Segmented(unitRow, new[] { "mm", "cm", "m", "in" }, (int)unit, i => {
                unit = (ModelImport.Unit)i;
                Process();
                BuildSettings();
                FrameModel();
            }, 52f);
            var axisRow = Dialog.Row(settings, "Up axis");
            ToolOptions.Segmented(axisRow, new[] { "Y up", "Z up" }, zUp ? 1 : 0, i => {
                zUp = i == 1;
                Process();
                BuildSettings();
            }, 70f);
            }
            var size = ModelBounds.size;
            var sizeRow = Dialog.Row(settings, "Size", 24f);
            Ui.Text(sizeRow, "Size", Inch.Format(size.x) + " × " + Inch.Format(size.y) + " × " + Inch.Format(size.z), Theme.FontBody, Theme.Text)
                .Size(-1, 24f, 1f);
            if (Mathf.Max(size.x, size.y, size.z) > 30f || Mathf.Max(size.x, size.y, size.z) < 0.05f) {
                var warn = Ui.Text(settings, "Warning", "That's an odd size for a robot part: check the units", Theme.FontSmall, Theme.Warning);
                warn.Size(-1, 20f);
            }

            var detailRow = Dialog.Row(settings, "Detail", 22f);
            var triangleCount = triangles.Count / 3;
            Ui.Text(detailRow, "Triangles", triangleCount.ToString("N0") + " triangles (full detail)", Theme.FontSmall, Theme.TextFaint)
                .Size(-1, 22f, 1f);
            if (triangleCount > DetailWarning) {
                var heavy = Ui.Text(settings, "Detail Warning", "Lots of detail: many copies of this part may make the app lag. A simpler model (fewer triangles) from your CAD program helps.", Theme.FontSmall, Theme.Warning);
                heavy.textWrappingMode = TextWrappingModes.Normal;
                heavy.Size(-1, 36f);
            }

            Dialog.Heading(settings, "Material");
            var materialRow = Dialog.Row(settings, "Made of");
            ToolOptions.Segmented(materialRow, MaterialNames, material, i => {
                material = i;
                UpdatePreviewMaterial();
                BuildSettings();
            }, 72f);
            var weightRow = Dialog.Row(settings, "Weight", 22f);
            var grams = CustomParts.Weight(fullVertices, fullTriangles, MaterialNames[material]);
            Ui.Text(weightRow, "Weight", grams.ToString("0.#", CultureInfo.InvariantCulture) + " g (" + (grams / 453.592f).ToString("0.###", CultureInfo.InvariantCulture) + " lb)", Theme.FontSmall, Theme.TextFaint)
                .Size(-1, 22f, 1f);

            Dialog.Heading(settings, "Holes");
            ToolOptions.Switch(settings, "Part has holes", hasHoles, on => {
                hasHoles = on;
                if (on) {
                    FindHoles();
                    Interface.Interface.Say(holes.Count == 0 ? "No holes found: click a shape on the model to make it a hole" : "Found " + holes.Count + (holes.Count == 1 ? " hole" : " holes"), holes.Count > 0);
                }
                else
                    holes.Clear();
                BuildSettings();
            });
            if (hasHoles) {
                var count = Ui.Text(settings, "Count", holes.Count + (holes.Count == 1 ? " hole" : " holes") + (selectedHole >= 0 ? " · one selected" : ""), Theme.FontSmall, Theme.TextDim);
                count.Size(-1, 20f);
                var how = Ui.Text(settings, "How", "Point at any shape in a face (a circle, a slot, a filleted rectangle...) and click it to make it a hole of that exact shape. Click a hole to select it.", Theme.FontSmall, Theme.TextFaint);
                how.textWrappingMode = TextWrappingModes.Normal;
                how.Size(-1, 54f);
                var editRow = Dialog.Row(settings, "");
                var delete = Ui.Button(editRow, "Delete", "Delete selected", DeleteSelected, false, 5f, Theme.FontSmall);
                delete.Size(-1, 30f);
                delete.interactable = selectedHole >= 0;
                Ui.Tip(delete, "Delete the selected hole", "Delete");
                var clear = Ui.Button(editRow, "Clear", "Remove all", () => {
                    holes.Clear();
                    selectedHole = -1;
                    BuildSettings();
                    Interface.Interface.Say("Removed all holes", false);
                }, false, 5f, Theme.FontSmall);
                clear.Size(-1, 30f);
            }
        }

        private void DeleteSelected() {
            if (selectedHole < 0 || selectedHole >= holes.Count) return;
            holes.RemoveAt(selectedHole);
            selectedHole = -1;
            BuildSettings();
            Interface.Interface.Say("Deleted the hole");
        }

        // ------------------------------------------------------------------ finishing

        private void Finish() {
            if (vertices.Count == 0) {
                Interface.Interface.Say("There's nothing to import", false);
                return;
            }
            if (editing != null) {
                editing.name = partName;
                editing.material = MaterialNames[material];
                editing.holes = holes.ToArray();
                editing.weightGrams = CustomParts.Weight(fullVertices, fullTriangles, MaterialNames[material]);
                CustomParts.Add(editing);
                Thumbnails.Forget(editing.key);
                var copies = CustomParts.UpdateInScene(editing);
                dialog.Close();
                Interface.Interface.Say("Saved " + editing.name + (copies > 0 ? ": " + copies + (copies == 1 ? " copy" : " copies") + " in the build updated" : ""));
                return;
            }
            var part = new CustomPartData {
                key = CustomParts.NewKey(), kind = CustomParts.Imported, name = partName, source = Path.GetFileName(path),
                material = MaterialNames[material], holes = holes.ToArray(),
                weightGrams = CustomParts.Weight(fullVertices, fullTriangles, MaterialNames[material]),
                created = System.DateTime.Now.Ticks,
            };
            CustomParts.SetMesh(part, vertices, triangles);
            CustomParts.Add(part);
            PartsRecent.Use(part.Id);
            dialog.Close();
            Interface.Interface.Say("Added " + part.name + " to your parts");
            PartsBrowser.Instance?.PlaceCustom(part);
        }

        // ------------------------------------------------------------------ preview

        private void MakeStage() {
            stage = new GameObject("Import Preview");
            stage.transform.position = new Vector3(0f, -5000f, 0f);
            model = new GameObject("Model");
            model.layer = PreviewLayer;
            model.transform.SetParent(stage.transform, false);
            previewMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            model.AddComponent<MeshFilter>().sharedMesh = previewMesh;
            model.AddComponent<MeshRenderer>();
            model.AddComponent<MeshCollider>();
            UpdatePreviewMaterial();

            var cameraObject = new GameObject("Preview Camera");
            cameraObject.transform.SetParent(stage.transform, false);
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.09f, 0.1f, 0.12f, 1f);
            previewCamera.fieldOfView = 30f;
            previewCamera.nearClipPlane = 0.05f;
            previewCamera.farClipPlane = 500f;
            texture = new RenderTexture(1080, 1180, 24) { antiAliasing = 4, name = "Import Preview" };
            previewCamera.targetTexture = texture;
            previewImage.texture = texture;

            var light = new GameObject("Preview Light").AddComponent<Light>();
            light.transform.SetParent(stage.transform, false);
            light.type = LightType.Directional;
            light.cullingMask = 1 << PreviewLayer;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            FrameModel();
        }

        private void UpdatePreviewMaterial() {
            if (model == null) return;
            var source = material == 2 ? Interface.Interface.Instance.plasticMaterial : Interface.Interface.Instance.metalMaterial;
            var renderer = model.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = source;
        }

        private void UpdatePreviewMesh() {
            if (previewMesh == null) return;
            previewMesh.Clear();
            previewMesh.SetVertices(vertices);
            previewMesh.SetTriangles(triangles, 0);
            previewMesh.RecalculateNormals();
            previewMesh.RecalculateBounds();
            var collider = model.GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = previewMesh;
        }

        private void FrameModel() {
            var size = ModelBounds.size.magnitude;
            distance = Mathf.Max(size * 1.9f, 0.5f);
        }

        private void Update() {
            if (previewCamera == null) return;
            var mouse = Mouse.current;
            var over = mouse != null && RectTransformUtility.RectangleContainsScreenPoint(previewImage.rectTransform, mouse.position.ReadValue());
            if (mouse != null) {
                var position = mouse.position.ReadValue();
                var delta = mouse.delta.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame && over && !Popup.IsOpen) {
                    pressedOnPreview = true;
                    dragging = false;
                    pressMouse = lastMouse = position;
                }
                if (pressedOnPreview && mouse.leftButton.isPressed) {
                    if (Vector2.Distance(position, pressMouse) > 4f) dragging = true;
                    if (dragging) Orbit(position - lastMouse);
                    lastMouse = position;
                }
                if (pressedOnPreview && mouse.leftButton.wasReleasedThisFrame) {
                    pressedOnPreview = false;
                    if (!dragging) ClickPreview(position);
                    dragging = false;
                }
                // The scene's own buttons (turn, pan) work here too; the scene behind holds still meanwhile.
                if (over || orbiting || panning) {
                    orbiting = MousePivotCameraInput.OrbitPressed && (orbiting || over);
                    panning = MousePivotCameraInput.PanPressed && (panning || over);
                    if (orbiting) Orbit(delta);
                    if (panning) {
                        var scale = distance * 0.0015f;
                        target -= (previewCamera.transform.right * delta.x + previewCamera.transform.up * delta.y) * scale;
                        spinning = false;
                    }
                }
                if (over) {
                    var wheel = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(wheel) > 0.01f) {
                        distance *= wheel > 0f ? 0.88f : 1.12f;
                        spinning = false;
                    }
                }
            }
            if (Keyboard.current != null && !InputEvents.TextInput.IsTyping
                && (Keyboard.current.deleteKey.wasPressedThisFrame || Keyboard.current.backspaceKey.wasPressedThisFrame))
                DeleteSelected();
            // It turns slowly on its own until you turn it, and holds still under the mouse (to click holes).
            if (spinning && !over) yaw += Time.unscaledDeltaTime * 18f;

            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            previewCamera.transform.position = stage.transform.position + target + rotation * new Vector3(0f, 0f, -distance);
            previewCamera.transform.rotation = rotation;
            previewCamera.Render();
            DrawHoles(mouse != null && over && !dragging ? mouse.position.ReadValue() : (Vector2?)null);
        }

        private bool orbiting, panning;

        private void Orbit(Vector2 delta) {
            yaw += delta.x * 0.4f;
            pitch = Mathf.Clamp(pitch - delta.y * 0.4f, -85f, 85f);
            spinning = false;
        }

        private void ClickPreview(Vector2 screen) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(previewImage.rectTransform, screen, null, out var local);
            var rect = previewImage.rectTransform.rect;
            var viewport = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
            var ray = previewCamera.ViewportPointToRay(viewport);

            // A hole under the mouse: select it. Otherwise the shape under it becomes a hole.
            var best = HoleAt(screen);
            if (best >= 0) {
                if (best != selectedHole) {
                    selectedHole = best;
                    BuildSettings();
                    Interface.Interface.Say("Selected a hole: Delete removes it");
                }
                return;
            }
            var loop = LoopAt(MouseLocal(screen));
            if (loop != null) {
                if (!hasHoles) hasHoles = true;
                MakeHole(loop);
                return;
            }
            if (selectedHole >= 0) {
                selectedHole = -1;
                BuildSettings();
            }
            else
                Interface.Interface.Say("Point at a shape with an inside (a circle, a slot...) in a flat face, then click it", false);
        }

        // How far the mesh goes from a surface point inwards (both sides of every triangle count).
        private float WallThickness(Vector3 point, Vector3 direction) {
            var origin = point + direction * 0.002f;
            var best = float.MaxValue;
            for (var t = 0; t + 2 < triangles.Count; t += 3) {
                if (RayTriangle(origin, direction, vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]], out var d) && d < best)
                    best = d;
            }
            return best < 10f ? best + 0.002f : 0.125f;
        }

        private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance) {
            distance = 0f;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(direction, e2);
            var det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            var inv = 1f / det;
            var s = origin - a;
            var u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            var v = Vector3.Dot(direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance > 0f;
        }

        private void Cleanup() {
            if (stage != null) Destroy(stage);
            if (texture != null) texture.Release();
            if (previewMesh != null) Destroy(previewMesh);
        }

        // ------------------------------------------------------------------ holes drawn over the preview

        private static readonly Color HoleBlue = new(0.25f, 0.62f, 1f, 1f);
        private static readonly Color PickBlue = new(0.45f, 0.78f, 1f, 1f);

        private Vector2? ToPreview(Vector3 local) {
            var world = model.transform.TransformPoint(local);
            var viewport = previewCamera.WorldToViewportPoint(world);
            if (viewport.z <= 0f) return null;
            var rect = shapes.rectTransform.rect;
            return rect.min + new Vector2(viewport.x * rect.width, viewport.y * rect.height);
        }

        private Vector2 MouseLocal(Vector2 screen) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(shapes.rectTransform, screen, null, out var local);
            return local;
        }

        /// <summary>The outline of one end of a hole (end -1 or 1), in the model's space.</summary>
        private static List<Vector3> Rim(CustomHole hole, float end) {
            var axis = hole.axis.normalized;
            var up = Vector3.ProjectOnPlane(hole.up, axis);
            if (up.sqrMagnitude < 0.25f) up = Vector3.ProjectOnPlane(Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right, axis);
            up.Normalize();
            var right = Vector3.Cross(up, axis).normalized;
            var center = hole.center + axis * (end * hole.depth / 2f);
            var points = new List<Vector3>();
            if (hole.outline != null && hole.outline.Length >= 3) {
                foreach (var p in hole.outline) points.Add(center + right * p.x + up * p.y);
            }
            else if (hole.square) {
                var hx = hole.size / 2f;
                var hy = (hole.size2 > 0f ? hole.size2 : hole.size) / 2f;
                points.Add(center - right * hx - up * hy);
                points.Add(center + right * hx - up * hy);
                points.Add(center + right * hx + up * hy);
                points.Add(center - right * hx + up * hy);
            }
            else
                for (var i = 0; i < 32; i++) {
                    var a = i * Mathf.PI * 2f / 32f;
                    points.Add(center + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * (hole.size / 2f));
                }
            return points;
        }

        private static Vector3 End(CustomHole hole, float end) => hole.center + hole.axis.normalized * (end * hole.depth / 2f);

        // Whether the model hides a point (in its space) from the preview camera.
        private bool Hidden(Vector3 local) {
            var world = model.transform.TransformPoint(local);
            var from = previewCamera.transform.position;
            var toPoint = world - from;
            return Physics.Raycast(from, toPoint.normalized, toPoint.magnitude - 0.01f, 1 << PreviewLayer);
        }

        private List<Vector2> Project(List<Vector3> points) {
            var result = new List<Vector2>(points.Count);
            foreach (var p in points) {
                var q = ToPreview(p);
                if (!q.HasValue) return null;
                result.Add(q.Value);
            }
            return result;
        }

        // Each hole: a blue outline at both ends with a see-through blue fill (fainter where the model hides it);
        // the selected one orange, the one under the mouse lighter. While picking a shape, the outline under the
        // mouse.
        private void DrawHoles(Vector2? mouseScreen) {
            shapes.Clear();
            hoverLoop = null;
            hoverHole = -1;
            if (mouseScreen.HasValue) {
                hoverHole = HoleAt(mouseScreen.Value);
                if (hoverHole < 0) hoverLoop = LoopAt(MouseLocal(mouseScreen.Value));
            }

            for (var i = 0; i < holes.Count; i++) {
                var color = i == selectedHole ? Theme.Warning : i == hoverHole ? PickBlue : HoleBlue;
                for (var end = -1; end <= 1; end += 2) {
                    var points = Project(Rim(holes[i], end));
                    if (points == null) continue;
                    var hidden = Hidden(End(holes[i], end));
                    if (!hidden) Fan(points, new Color(color.r, color.g, color.b, 0.3f));
                    shapes.Polyline(points, hidden ? new Color(color.r, color.g, color.b, 0.35f) : color, hidden ? 1.2f : 2.2f, true);
                }
            }

            if (hoverLoop != null) {
                var points = Project(hoverLoop.points);
                if (points != null && points.Count >= 3) {
                    Fan(points, new Color(PickBlue.r, PickBlue.g, PickBlue.b, 0.35f));
                    shapes.Polyline(points, PickBlue, 3f, true);
                }
            }

            modeHint.text = hoverLoop != null ? "Click to make this shape a hole" : hoverHole >= 0 ? "Click to select this hole" : "";
        }

        // A filled outline, as a fan from its middle (a hole's end is convex; a picked shape is close enough).
        private void Fan(List<Vector2> points, Color color) {
            var center = Vector2.zero;
            foreach (var p in points) center += p;
            center /= points.Count;
            for (var i = 0; i < points.Count; i++)
                shapes.Triangle(center, points[i], points[(i + 1) % points.Count], color);
        }

        /// <summary>The hole whose outline is under the mouse (the nearest to the camera), or -1.</summary>
        private int HoleAt(Vector2 screen) {
            var mouse = MouseLocal(screen);
            var best = -1;
            var bestDepth = float.MaxValue;
            for (var i = 0; i < holes.Count; i++) {
                for (var end = -1; end <= 1; end += 2) {
                    var points = Project(Rim(holes[i], end));
                    if (points == null) continue;
                    if (!Geometry.MeshSlicer.Contains(points, mouse) && !NearOutline(points, mouse, 6f)) continue;
                    var depth = Vector3.Distance(previewCamera.transform.position, model.transform.TransformPoint(End(holes[i], end)));
                    if (depth < bestDepth) {
                        bestDepth = depth;
                        best = i;
                    }
                }
            }
            return best;
        }

        private static bool NearOutline(List<Vector2> points, Vector2 p, float pixels) {
            for (var i = 0; i < points.Count; i++) {
                var a = points[i];
                var ab = points[(i + 1) % points.Count] - a;
                var t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                if ((a + ab * t - p).sqrMagnitude < pixels * pixels) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ picking a shape as a hole

        /// <summary>The outline in a flat face (facing the camera) under the mouse, or null.</summary>
        private Facets.Loop LoopAt(Vector2 mouse) {
            facets ??= new Facets(vertices, triangles);
            var view = model.transform.InverseTransformPoint(previewCamera.transform.position);
            Facets.Loop best = null;
            var bestScore = float.MaxValue;
            foreach (var loop in facets.innerLoops) {
                if (Vector3.Dot(loop.normal, view - loop.center) <= 0f) continue;  // its face looks away
                var points = Project(loop.points);
                if (points == null) continue;
                var depth = (view - loop.center).magnitude;
                // Inside it (the nearest to the camera wins), or else right on its edge.
                float score;
                if (Geometry.MeshSlicer.Contains(points, mouse)) score = depth;
                else if (NearOutline(points, mouse, 8f)) score = 1e4f + depth;
                else continue;
                if (score < bestScore) {
                    bestScore = score;
                    best = loop;
                }
            }
            return best;
        }

        // The picked outline as a hole: round when it's a circle, otherwise the smallest box around it (a square,
        // or a slot the screw can slide along), through the wall the face is on.
        private void MakeHole(Facets.Loop loop) {
            var hole = HoleFrom(loop, out var fit);
            if (hole == null) {
                Interface.Interface.Say("That shape is too small to be a hole", false);
                return;
            }
            var round = hole.outline == null;
            foreach (var other in holes)
                if (Vector3.Distance(other.center, hole.center) < 0.03f && Mathf.Abs(Vector3.Dot(other.axis.normalized, hole.axis)) > 0.98f) {
                    Interface.Interface.Say("That shape is already a hole", false);
                    return;
                }
            holes.Add(hole);
            selectedHole = holes.Count - 1;
            BuildSettings();
            Interface.Interface.Say((round ? "Made a " + fit.Describe() + " hole" : "Made a hole of that exact shape (" + Inch.Format(fit.size) + " × " + Inch.Format(fit.size2 > 0f ? fit.size2 : fit.size) + ")") + (fit.Slides ? ": a screw slides around in it" : ""));
        }

        // A hole from an outline in a flat face (null: too small).
        private CustomHole HoleFrom(Facets.Loop loop, out Polycarb.PolyPart.HoleFit fit) {
            var n = loop.normal.normalized;
            var axis = -n;
            var u = Vector3.ProjectOnPlane(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right, n).normalized;
            var v = Vector3.Cross(n, u);
            var origin = loop.center;
            var flat = loop.points.Select(p => new Vector2(Vector3.Dot(p - origin, u), Vector3.Dot(p - origin, v))).ToList();
            fit = Polycarb.PolyPart.FitHole(flat);
            if (!fit.valid) return null;
            var middle = origin + u * fit.center.x + v * fit.center.y;
            var sideY = (u * fit.side.x + v * fit.side.y).normalized;

            // How thick the wall is: just outside the shape, into the material.
            var edge = loop.points[0];
            var outward = Vector3.ProjectOnPlane(edge - middle, n).normalized;
            var depth = WallThickness(edge + outward * 0.03f, axis);
            if (depth > 4f) depth = 0.125f;

            // V2: only a true circle (drawn with many sides) is a round hole; anything else, a 12-sided hole
            // included, is the exact shape clicked.
            var round = fit.round && loop.points.Count >= 24;
            var hole = new CustomHole {
                axis = axis, depth = depth, center = middle + axis * (depth / 2f),
                square = !round, size = fit.size, size2 = round ? 0f : fit.size2, up = round ? Vector3.zero : (sideY.sqrMagnitude > 0.5f ? sideY : v),
            };
            // Its exact shape, in the hole's own right/up (as its collider is turned: right = up x axis).
            if (!round) {
                var up = hole.up;
                var right = Vector3.Cross(up, axis).normalized;
                hole.outline = loop.points.Select(p => new Vector2(Vector3.Dot(p - middle, right), Vector3.Dot(p - middle, up))).ToArray();
            }
            return hole;
        }
    }
}
