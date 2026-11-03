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
    // glTF, PLY, OFF); the model then shows here turning slowly (drag to turn it, scroll to zoom) with its
    // settings: name, the file's units and up axis, a quality preset (fewer triangles keeps big models fast),
    // material (weight) and "Part has holes", which finds the holes (round and square, square to the part's
    // sides) and marks them; click a marker to select it and delete it, or "Add hole" and click the model to put
    // one there. "Add to parts" saves it to the library, puts it in the parts browser and starts placing it.
    public class ImportDialog : MonoBehaviour {
        private static readonly string[] Qualities = { "Original", "High", "Medium", "Low" };
        private static readonly float[] QualityShare = { 1f, 0.5f, 0.2f, 0.08f };
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
        private int quality = 1;
        private int material;
        private bool hasHoles;
        private int holeSize;
        private bool addingHole;
        private int selectedHole = -1;

        // The model in inches, Y up, centred: full detail (for holes) and as it will be saved.
        private List<Vector3> fullVertices = new(), vertices = new();
        private List<int> fullTriangles = new(), triangles = new();
        private readonly List<CustomHole> holes = new();

        // Preview
        private GameObject stage, model;
        private Camera previewCamera;
        private RenderTexture texture;
        private Mesh previewMesh;
        private readonly List<GameObject> markers = new();
        private Material markerMaterial, markerSelected;
        private float yaw = 30f, pitch = 25f, distance = 10f;
        private bool spinning = true;
        private Vector2 lastMouse;
        private bool dragging, pressedOnPreview;
        private Vector2 pressMouse;

        public static void Choose() {
            var filters = new[] { new ExtensionFilter("3D models", ModelImport.Extensions), new ExtensionFilter("All files", "*") };
            var paths = StandaloneFileBrowser.OpenFilePanel("Import a part", "", filters, false);
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
        }

        private void Build() {
            partName = Path.GetFileNameWithoutExtension(path);
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
            var hint = Ui.Hint(previewFrame.transform, "Drag", "turn", "Scroll", "zoom");
            hint.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 10f), new Vector2(300f, 22f));

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
            var add = Ui.Button(dialog.Footer, "Add", "Add to parts", Finish, true, 6f);
            add.Size(-1f, 36f);

            MakeStage();
            Process();
            FrameModel();
            BuildSettings();
        }

        // ------------------------------------------------------------------ the mesh

        private void Process() {
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

        private void Simplify() {
            vertices = new List<Vector3>(fullVertices);
            triangles = new List<int>(fullTriangles);
            var count = triangles.Count / 3;
            var target = Mathf.Max(300, Mathf.RoundToInt(count * QualityShare[quality]));
            if (target < count)
                MeshTools.Simplify(vertices, triangles, target);
            MeshTools.SplitHardEdges(vertices, triangles, 30f);
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
                holes.Add(new CustomHole {
                    center = (hole.entry + hole.axis * (hole.depth / 2f)) / m,
                    axis = hole.axis.normalized, size = size, depth = hole.depth / m, square = hole.square,
                    threaded = !hole.square && size <= 0.168f && size >= 0.125f && !hole.through,
                });
            }
            UpdateMarkers();
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
            var size = ModelBounds.size;
            var sizeRow = Dialog.Row(settings, "Size", 24f);
            Ui.Text(sizeRow, "Size", Inch.Format(size.x) + " × " + Inch.Format(size.y) + " × " + Inch.Format(size.z), Theme.FontBody, Theme.Text)
                .Size(-1, 24f, 1f);
            if (Mathf.Max(size.x, size.y, size.z) > 30f || Mathf.Max(size.x, size.y, size.z) < 0.05f) {
                var warn = Ui.Text(settings, "Warning", "That's an odd size for a robot part: check the units", Theme.FontSmall, Theme.Warning);
                warn.Size(-1, 20f);
            }

            Dialog.Heading(settings, "Quality");
            var qualityRow = Dialog.Row(settings, "Detail");
            ToolOptions.Segmented(qualityRow, Qualities, quality, i => {
                quality = i;
                Simplify();
                BuildSettings();
            }, 60f);
            var trianglesRow = Dialog.Row(settings, "", 22f);
            Ui.Text(trianglesRow, "Triangles", (triangles.Count / 3).ToString("N0") + " triangles (file: " + (file.triangles.Count / 3).ToString("N0") + ")", Theme.FontSmall, Theme.TextFaint)
                .Size(-1, 22f, 1f);

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
                    Interface.Interface.Say(holes.Count == 0 ? "No holes found: add them with Add hole" : "Found " + holes.Count + (holes.Count == 1 ? " hole" : " holes"), holes.Count > 0);
                }
                else {
                    holes.Clear();
                    addingHole = false;
                    UpdateMarkers();
                }
                BuildSettings();
            });
            if (hasHoles) {
                var count = Ui.Text(settings, "Count", holes.Count + (holes.Count == 1 ? " hole" : " holes") + (selectedHole >= 0 ? " · one selected" : ""), Theme.FontSmall, Theme.TextDim);
                count.Size(-1, 20f);
                var addRow = Dialog.Row(settings, "");
                var add = Ui.Button(addRow, "Add Hole", addingHole ? "Click the model…" : "Add hole", () => {
                    addingHole = !addingHole;
                    BuildSettings();
                }, addingHole, 5f, Theme.FontSmall);
                add.Size(-1, 30f);
                Ui.Tip(add, "Add hole", null, "Then click the model where the hole goes: it goes through the wall there, square to the face.");
                var sizes = Popup.Choice(addRow, HoleSizes.Labels, holeSize, i => holeSize = i, 196f);
                sizes.Size(196f, 30f);
                var editRow = Dialog.Row(settings, "");
                var delete = Ui.Button(editRow, "Delete", "Delete selected", DeleteSelected, false, 5f, Theme.FontSmall);
                delete.Size(-1, 30f);
                delete.interactable = selectedHole >= 0;
                Ui.Tip(delete, "Delete the selected hole", "Delete");
                var clear = Ui.Button(editRow, "Clear", "Remove all", () => {
                    holes.Clear();
                    selectedHole = -1;
                    UpdateMarkers();
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
            UpdateMarkers();
            BuildSettings();
            Interface.Interface.Say("Deleted the hole");
        }

        // ------------------------------------------------------------------ finishing

        private void Finish() {
            if (vertices.Count == 0) {
                Interface.Interface.Say("There's nothing to import", false);
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

            var holeBase = Interface.Interface.Instance.polycarbMaterial;
            markerMaterial = new Material(holeBase) { color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.6f) };
            if (markerMaterial.HasProperty("_BaseColor")) markerMaterial.SetColor("_BaseColor", markerMaterial.color);
            markerSelected = new Material(holeBase) { color = new Color(Theme.Warning.r, Theme.Warning.g, Theme.Warning.b, 0.8f) };
            if (markerSelected.HasProperty("_BaseColor")) markerSelected.SetColor("_BaseColor", markerSelected.color);
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
            UpdateMarkers();
        }

        private void FrameModel() {
            var size = ModelBounds.size.magnitude;
            distance = Mathf.Max(size * 1.9f, 0.5f);
        }

        private void UpdateMarkers() {
            foreach (var marker in markers) Destroy(marker);
            markers.Clear();
            if (model == null) return;
            for (var i = 0; i < holes.Count; i++) {
                var hole = holes[i];
                var marker = GameObject.CreatePrimitive(hole.square ? PrimitiveType.Cube : PrimitiveType.Cylinder);
                Destroy(marker.GetComponent<Collider>());
                marker.layer = PreviewLayer;
                marker.transform.SetParent(model.transform, false);
                marker.transform.localPosition = hole.center;
                // Cylinders stand along their y; cubes are a unit across.
                var depth = Mathf.Max(hole.depth, 0.05f) + 0.04f;
                marker.transform.localRotation = Quaternion.FromToRotation(Vector3.up, hole.axis);
                marker.transform.localScale = hole.square ? new Vector3(hole.size, depth, hole.size) : new Vector3(hole.size, depth / 2f, hole.size);
                marker.GetComponent<Renderer>().sharedMaterial = i == selectedHole ? markerSelected : markerMaterial;
                markers.Add(marker);
            }
        }

        private void Update() {
            if (previewCamera == null) return;
            var mouse = Mouse.current;
            var over = mouse != null && RectTransformUtility.RectangleContainsScreenPoint(previewImage.rectTransform, mouse.position.ReadValue());
            if (mouse != null) {
                var position = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame && over && !Popup.IsOpen) {
                    pressedOnPreview = true;
                    dragging = false;
                    pressMouse = lastMouse = position;
                }
                if (pressedOnPreview && mouse.leftButton.isPressed) {
                    if (Vector2.Distance(position, pressMouse) > 4f) dragging = true;
                    if (dragging) {
                        var delta = position - lastMouse;
                        yaw += delta.x * 0.4f;
                        pitch = Mathf.Clamp(pitch - delta.y * 0.4f, -85f, 85f);
                        spinning = false;
                    }
                    lastMouse = position;
                }
                if (pressedOnPreview && mouse.leftButton.wasReleasedThisFrame) {
                    pressedOnPreview = false;
                    if (!dragging) ClickPreview(position);
                    dragging = false;
                }
                if (over) {
                    var wheel = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(wheel) > 0.01f) distance *= wheel > 0f ? 0.88f : 1.12f;
                }
            }
            if (Keyboard.current != null && !InputEvents.TextInput.IsTyping
                && (Keyboard.current.deleteKey.wasPressedThisFrame || Keyboard.current.backspaceKey.wasPressedThisFrame))
                DeleteSelected();
            if (spinning) yaw += Time.unscaledDeltaTime * 18f;

            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            previewCamera.transform.position = stage.transform.position + rotation * new Vector3(0f, 0f, -distance);
            previewCamera.transform.rotation = rotation;
            previewCamera.Render();
        }

        private void ClickPreview(Vector2 screen) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(previewImage.rectTransform, screen, null, out var local);
            var rect = previewImage.rectTransform.rect;
            var viewport = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
            var ray = previewCamera.ViewportPointToRay(viewport);

            if (addingHole) {
                if (!Physics.Raycast(ray, out var hit, 1000f, 1 << PreviewLayer)) {
                    Interface.Interface.Say("Click on the model to put the hole there", false);
                    return;
                }
                var localPoint = model.transform.InverseTransformPoint(hit.point);
                var normal = model.transform.InverseTransformDirection(hit.normal).normalized;
                var depth = WallThickness(localPoint, -normal);
                var choice = HoleSizes.Common[holeSize];
                holes.Add(new CustomHole {
                    center = localPoint - normal * (depth / 2f), axis = -normal, size = choice.inches, depth = depth,
                    square = choice.square, threaded = choice.threaded,
                });
                selectedHole = holes.Count - 1;
                addingHole = false;
                UpdateMarkers();
                BuildSettings();
                Interface.Interface.Say("Added a " + choice.label.Split('(')[0].Trim() + " hole");
                return;
            }

            // Select the marker nearest the click.
            var best = -1;
            var bestDistance = 0.04f;
            for (var i = 0; i < holes.Count; i++) {
                var center = model.transform.TransformPoint(holes[i].center);
                var along = Vector3.Dot(center - ray.origin, ray.direction);
                var distanceToRay = Vector3.Distance(center, ray.origin + ray.direction * along);
                var allowed = Mathf.Max(holes[i].size * 0.75f, holes[i].depth * 0.6f);
                if (distanceToRay < allowed && (best < 0 || distanceToRay / allowed < bestDistance)) {
                    best = i;
                    bestDistance = distanceToRay / allowed;
                }
            }
            if (best != selectedHole) {
                selectedHole = best;
                UpdateMarkers();
                BuildSettings();
            }
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
            foreach (var marker in markers) if (marker != null) Destroy(marker);
            if (stage != null) Destroy(stage);
            if (texture != null) texture.Release();
            if (previewMesh != null) Destroy(previewMesh);
            if (markerMaterial != null) Destroy(markerMaterial);
            if (markerSelected != null) Destroy(markerSelected);
        }
    }
}
