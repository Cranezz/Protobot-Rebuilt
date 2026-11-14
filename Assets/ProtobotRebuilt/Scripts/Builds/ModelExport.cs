using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Protobot.Interface;
using SFB;
using UnityEngine;

namespace Protobot.Builds {
    // V2: File > Export 3D model…: the robot (or the selected part and what's fastened to it) as a 3D model for other
    // CAD (Fusion, Onshape, Inventor, SolidWorks import mesh), renderers (Blender, KeyShot) or a slicer: OBJ with its
    // colors (an .mtl beside it), or binary STL. Every part as it's shown (cut, drilled, bent; chains, rods, inserts),
    // in millimetres (what slicers and CAD expect) or inches. OBJ is Y-up, STL Z-up, both right-handed. STEP needs
    // the parts' solid models, which a built app doesn't have.
    public static class ModelExport {
        public enum Format { Obj, Stl }

        private static Format format = Format.Obj;
        private static bool millimetres = true;
        private static bool selectedOnly;

        public static void Open() {
            var dialog = Dialog.Open("Export 3D model", new Vector2(520f, 360f));
            Ui.Column(dialog.Body, 8f);
            var selected = Tools.SelectTool.SelectedPart;
            void Build() {
                foreach (Transform child in dialog.Body) UnityEngine.Object.Destroy(child.gameObject);
                Dialog.Heading(dialog.Body, "Format");
                ToolOptions.Segmented(Dialog.Row(dialog.Body, ""), new[] { "OBJ (with colors)", "STL" }, (int)format, i => {
                    format = (Format)i;
                    Build();
                });
                Dialog.Heading(dialog.Body, "Units");
                ToolOptions.Segmented(Dialog.Row(dialog.Body, ""), new[] { "Millimetres", "Inches" }, millimetres ? 0 : 1, i => {
                    millimetres = i == 0;
                    Build();
                });
                if (selected != null) {
                    Dialog.Heading(dialog.Body, "What");
                    ToolOptions.Segmented(Dialog.Row(dialog.Body, ""), new[] { "The whole robot", "Selected group" }, selectedOnly ? 1 : 0, i => {
                        selectedOnly = i == 1;
                        Build();
                    });
                }
                var note = Ui.Text(dialog.Body, "Note", format == Format.Obj
                    ? "OBJ keeps every part's colors (an .mtl file is saved next to it): for Blender, KeyShot, Fusion or Onshape."
                    : "STL is one solid shape with no colors: for 3D printing or CAD that only takes STL.", Theme.FontSmall, Theme.TextDim);
                note.textWrappingMode = TMPro.TextWrappingModes.Normal;
                note.Size(-1, 40f);
            }
            Build();
            var export = Ui.Button(dialog.Footer, "Export", "Export…", () => {
                dialog.Close();
                Export(selected != null && selectedOnly ? selected : null);
            }, true, 6f);
            export.Size(120f, 34f);
        }

        private static void Export(GameObject only) {
            var extension = format == Format.Obj ? "obj" : "stl";
            var name = BuildName();
            var path = StandaloneFileBrowser.SaveFilePanel("Export 3D model", "", name, extension);
            InputEvents.TextInput.AfterFileWindow();
            if (string.IsNullOrEmpty(path)) {
                Interface.Interface.Say("Export cancelled", false);
                return;
            }
            try {
                var shapes = Gather(only);
                if (shapes.Count == 0) {
                    Interface.Interface.Say("Nothing to export: the build is empty", false);
                    return;
                }
                var scale = millimetres ? 25.4f : 1f;
                var triangles = format == Format.Obj ? WriteObj(path, shapes, scale) : WriteStl(path, shapes, scale);
                Interface.Interface.Announce("Exported " + shapes.Select(s => s.part).Distinct().Count() + " parts (" + triangles.ToString("N0", CultureInfo.InvariantCulture)
                                        + " triangles) to " + Path.GetFileName(path) + (millimetres ? " in mm" : " in inches"));
            }
            catch (Exception e) {
                Debug.LogException(e);
                Interface.Interface.Say("Couldn't export: " + e.Message, false);
            }
        }

        public static string BuildName() {
            var manager = UnityEngine.Object.FindAnyObjectByType<BuildsManager>();
            var file = manager != null ? manager.GetFileName() : "";
            return string.IsNullOrEmpty(file) ? "Robot" : Path.GetFileNameWithoutExtension(file);
        }

        private struct Shape {
            public GameObject part;
            public Mesh mesh;
            public Matrix4x4 matrix;
            public Material[] materials;
        }

        // What's drawn of every part (and its children: rods, links, inserts), not hole colliders or marks.
        private static List<Shape> Gather(GameObject only) {
            var parts = only != null ? (only.GetConnectedObjects(true, true) ?? new List<GameObject> { only }) : PartsManager.FindLoadedObjects();
            var shapes = new List<Shape>();
            foreach (var part in parts.Where(p => p != null && !p.IsDeleted()).Distinct()) {
                foreach (var renderer in part.GetComponentsInChildren<MeshRenderer>()) {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.gameObject.layer == HoleCollider.HOLE_COLLISIONS_LAYER) continue;
                    if (renderer.GetComponent<TMPro.TMP_Text>() != null) continue;
                    if (renderer.sharedMaterial == Motion.Dynamics.Highlight) continue;
                    if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                    shapes.Add(new Shape { part = part, mesh = filter.sharedMesh, matrix = renderer.localToWorldMatrix, materials = renderer.sharedMaterials });
                }
            }
            return shapes;
        }

        // Unity is left-handed, Y up. OBJ: right-handed Y up (x mirrored); STL: right-handed Z up (y and z swapped).
        // Mirroring turns the triangles inside out, so their corners are taken the other way round.
        private static Vector3 ObjPoint(Vector3 p, float scale) => new(-p.x * scale, p.y * scale, p.z * scale);
        private static Vector3 StlPoint(Vector3 p, float scale) => new(p.x * scale, p.z * scale, p.y * scale);

        private static long WriteObj(string path, List<Shape> shapes, float scale) {
            var mtlPath = Path.ChangeExtension(path, "mtl");
            var materials = new Dictionary<string, Color>();
            long triangles = 0;
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20)) {
                writer.WriteLine("# Protobot " + Application.version + ": " + BuildName() + (scale > 1f ? " (millimetres)" : " (inches)"));
                writer.WriteLine("mtllib " + Path.GetFileName(mtlPath));
                var offset = 1;
                var index = 0;
                foreach (var shape in shapes) {
                    var vertices = shape.mesh.vertices;
                    writer.WriteLine("o " + Clean(shape.part.name) + "_" + index++);
                    var sb = new StringBuilder(vertices.Length * 40);
                    foreach (var v in vertices) {
                        var p = ObjPoint(shape.matrix.MultiplyPoint3x4(v), scale);
                        sb.Append("v ").Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append('\n');
                    }
                    writer.Write(sb);
                    for (var s = 0; s < shape.mesh.subMeshCount; s++) {
                        var material = s < shape.materials.Length ? shape.materials[s] : shape.materials.LastOrDefault();
                        var color = material != null ? material.color : Color.gray;
                        var key = "c" + ColorUtility.ToHtmlStringRGBA(color);
                        materials[key] = color;
                        writer.WriteLine("usemtl " + key);
                        var tris = shape.mesh.GetTriangles(s);
                        sb.Clear();
                        for (var t = 0; t + 2 < tris.Length; t += 3) {
                            sb.Append("f ").Append(tris[t] + offset).Append(' ').Append(tris[t + 2] + offset).Append(' ').Append(tris[t + 1] + offset).Append('\n');
                            triangles++;
                        }
                        writer.Write(sb);
                    }
                    offset += vertices.Length;
                }
            }
            using (var mtl = new StreamWriter(mtlPath, false, new UTF8Encoding(false))) {
                foreach (var pair in materials) {
                    // The app's colors are about half their real value (its light doubles them): brought back up here,
                    // at most to 0.9 (bright metals, already near white, stay gray rather than turning pure white).
                    var c = pair.Value;
                    var k = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    var up = k > 0.001f ? Mathf.Clamp(0.9f / k, 1f, 2f) : 1f;
                    mtl.WriteLine("newmtl " + pair.Key);
                    mtl.WriteLine("Kd " + F(Mathf.Clamp01(c.r * up)) + " " + F(Mathf.Clamp01(c.g * up)) + " " + F(Mathf.Clamp01(c.b * up)));
                    if (c.a < 0.99f) mtl.WriteLine("d " + F(c.a));
                }
            }
            return triangles;
        }

        private static long WriteStl(string path, List<Shape> shapes, float scale) {
            long count = shapes.Sum(s => (long)Enumerable.Range(0, s.mesh.subMeshCount).Sum(m => (long)s.mesh.GetIndexCount(m)) / 3);
            using var stream = new BufferedStream(File.Create(path), 1 << 20);
            using var writer = new BinaryWriter(stream);
            var header = Encoding.ASCII.GetBytes(("Protobot " + Application.version + " " + (scale > 1f ? "mm" : "inches")).PadRight(80).Substring(0, 80));
            writer.Write(header);
            writer.Write((uint)count);
            foreach (var shape in shapes) {
                var vertices = shape.mesh.vertices.Select(v => StlPoint(shape.matrix.MultiplyPoint3x4(v), scale)).ToArray();
                for (var s = 0; s < shape.mesh.subMeshCount; s++) {
                    var tris = shape.mesh.GetTriangles(s);
                    for (var t = 0; t + 2 < tris.Length; t += 3) {
                        Vector3 a = vertices[tris[t]], b = vertices[tris[t + 2]], c = vertices[tris[t + 1]];
                        var n = Vector3.Cross(b - a, c - a).normalized;
                        Write(writer, n);
                        Write(writer, a);
                        Write(writer, b);
                        Write(writer, c);
                        writer.Write((ushort)0);
                    }
                }
            }
            return count;
        }

        private static void Write(BinaryWriter writer, Vector3 v) {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }

        private static string F(float f) => f.ToString("0.#####", CultureInfo.InvariantCulture);
        private static string Clean(string s) => new string((s ?? "part").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }
}
