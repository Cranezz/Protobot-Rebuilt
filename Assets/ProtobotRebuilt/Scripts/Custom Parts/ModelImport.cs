using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEngine;

namespace Protobot.Custom {
    // V2: reads 3D model files into triangles, for importing custom parts: OBJ, STL (text or binary), PLY (text
    // or binary), OFF, 3MF and glTF/GLB. Everything is read as one mesh in the file's own units (3MF and glTF
    // say theirs; the others are guessed) with its own up axis (Y for OBJ and glTF, Z for the rest, as CAD
    // programs export them). FBX, STEP and the like need a CAD program's own reader: export STL or OBJ instead.
    public static class ModelImport {
        public enum Unit { Millimeters, Centimeters, Meters, Inches }

        public class Result {
            public List<Vector3> vertices = new();
            public List<int> triangles = new();
            public Unit unit = Unit.Millimeters;
            public bool unitKnown;
            public bool zUp;
            public string error;
        }

        public static readonly string[] Extensions = { "stl", "obj", "3mf", "glb", "gltf", "ply", "off" };

        public static float InchesPer(Unit unit) => unit switch {
            Unit.Millimeters => 1f / 25.4f,
            Unit.Centimeters => 1f / 2.54f,
            Unit.Meters => 39.3700787f,
            _ => 1f,
        };

        public static string Label(Unit unit) => unit switch {
            Unit.Millimeters => "mm",
            Unit.Centimeters => "cm",
            Unit.Meters => "m",
            _ => "in",
        };

        public static Result Read(string path) {
            var result = new Result();
            try {
                var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                switch (extension) {
                    case "stl": ReadStl(File.ReadAllBytes(path), result); result.zUp = true; break;
                    case "obj": ReadObj(File.ReadAllLines(path), result); break;
                    case "ply": ReadPly(File.ReadAllBytes(path), result); result.zUp = true; break;
                    case "off": ReadOff(File.ReadAllLines(path), result); result.zUp = true; break;
                    case "3mf": Read3mf(path, result); result.zUp = true; break;
                    case "glb": ReadGltf(path, File.ReadAllBytes(path), true, result); break;
                    case "gltf": ReadGltf(path, File.ReadAllBytes(path), false, result); break;
                    case "fbx":
                    case "step":
                    case "stp":
                    case "iges":
                    case "igs":
                    case "f3d":
                    case "ipt":
                    case "sldprt":
                        result.error = "." + extension + " files can't be read here: export the part as STL or OBJ from your CAD program";
                        return result;
                    default:
                        result.error = "." + extension + " isn't a 3D model format Protobot reads (STL, OBJ, 3MF, GLB, glTF, PLY, OFF)";
                        return result;
                }
            }
            catch (Exception e) {
                Debug.LogWarning("[Import] " + path + ": " + e);
                result.error = "Couldn't read the file: " + e.Message;
                return result;
            }
            if (result.triangles.Count < 3) {
                result.error = "The file has no triangles in it";
                return result;
            }
            if (!result.unitKnown)
                result.unit = GuessUnit(result.vertices);
            return result;
        }

        // Unitless files: the first unit that makes it a robot part's size (about 1/4" to 40"), trying what CAD
        // programs export most: millimetres (STL), centimetres (Fusion's OBJ), inches, metres (Blender).
        private static Unit GuessUnit(List<Vector3> vertices) {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var v in vertices) bounds.Encapsulate(v);
            var size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            foreach (var unit in new[] { Unit.Millimeters, Unit.Centimeters, Unit.Inches, Unit.Meters }) {
                var inches = size * InchesPer(unit);
                if (inches >= 0.3f && inches <= 40f) return unit;
            }
            return Unit.Millimeters;
        }

        private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ STL

        private static void ReadStl(byte[] bytes, Result result) {
            // Binary: 80-byte header, count, 50 bytes per triangle (some binary files start with "solid" too).
            if (bytes.Length >= 84) {
                var count = BitConverter.ToUInt32(bytes, 80);
                if (84 + count * 50L == bytes.Length) {
                    for (var i = 0; i < count; i++) {
                        var offset = 84 + i * 50 + 12;
                        for (var k = 0; k < 3; k++) {
                            result.triangles.Add(result.vertices.Count);
                            result.vertices.Add(new Vector3(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8)));
                            offset += 12;
                        }
                    }
                    return;
                }
            }
            var text = Encoding.ASCII.GetString(bytes);
            var corners = new List<Vector3>();
            foreach (var raw in text.Split('\n')) {
                var line = raw.Trim();
                if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) {
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    corners.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                }
                else if (line.StartsWith("endloop", StringComparison.OrdinalIgnoreCase)) {
                    AddPolygon(result, corners);
                    corners.Clear();
                }
            }
        }

        private static void AddPolygon(Result result, List<Vector3> corners) {
            if (corners.Count < 3) return;
            var start = result.vertices.Count;
            result.vertices.AddRange(corners);
            for (var i = 1; i + 1 < corners.Count; i++) {
                result.triangles.Add(start);
                result.triangles.Add(start + i);
                result.triangles.Add(start + i + 1);
            }
        }

        // ------------------------------------------------------------------ OBJ

        private static void ReadObj(string[] lines, Result result) {
            var positions = new List<Vector3>();
            foreach (var raw in lines) {
                var line = raw.Trim();
                if (line.StartsWith("v ")) {
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    positions.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                }
            }
            result.vertices.AddRange(positions);
            var face = new List<int>();
            foreach (var raw in lines) {
                var line = raw.Trim();
                if (!line.StartsWith("f ")) continue;
                face.Clear();
                var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 1; i < p.Length; i++) {
                    var index = int.Parse(p[i].Split('/')[0], CultureInfo.InvariantCulture);
                    face.Add(index < 0 ? positions.Count + index : index - 1);
                }
                for (var i = 1; i + 1 < face.Count; i++) {
                    result.triangles.Add(face[0]);
                    result.triangles.Add(face[i]);
                    result.triangles.Add(face[i + 1]);
                }
            }
        }

        // ------------------------------------------------------------------ OFF

        private static void ReadOff(string[] lines, Result result) {
            var data = lines.Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToList();
            var first = 0;
            if (data[0].StartsWith("OFF")) {
                var rest = data[0].Substring(3).Trim();
                if (rest.Length > 0) data[0] = rest; else first = 1;
            }
            var counts = data[first].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            int vertexCount = int.Parse(counts[0]), faceCount = int.Parse(counts[1]);
            for (var i = 0; i < vertexCount; i++) {
                var p = data[first + 1 + i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                result.vertices.Add(new Vector3(F(p[0]), F(p[1]), F(p[2])));
            }
            for (var i = 0; i < faceCount; i++) {
                var p = data[first + 1 + vertexCount + i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                var n = int.Parse(p[0]);
                for (var k = 2; k < n; k++) {
                    result.triangles.Add(int.Parse(p[1]));
                    result.triangles.Add(int.Parse(p[k]));
                    result.triangles.Add(int.Parse(p[k + 1]));
                }
            }
        }

        // ------------------------------------------------------------------ PLY

        private static void ReadPly(byte[] bytes, Result result) {
            // Header (text), then the data as text or binary.
            var headerEnd = IndexOf(bytes, Encoding.ASCII.GetBytes("end_header"));
            if (headerEnd < 0) throw new Exception("not a PLY file");
            var dataStart = headerEnd + "end_header".Length;
            while (dataStart < bytes.Length && bytes[dataStart] != '\n') dataStart++;
            dataStart++;
            var header = Encoding.ASCII.GetString(bytes, 0, headerEnd).Split('\n').Select(l => l.Trim()).ToList();
            var format = header.First(l => l.StartsWith("format")).Split(' ')[1];

            var elements = new List<(string name, int count, List<(string type, string listCount, string name)> props)>();
            foreach (var line in header) {
                var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "element") elements.Add((p[1], int.Parse(p[2]), new List<(string, string, string)>()));
                else if (p[0] == "property" && elements.Count > 0) {
                    if (p[1] == "list") elements[^1].props.Add((p[3], p[2], p[4]));
                    else elements[^1].props.Add((p[1], null, p[2]));
                }
            }

            if (format == "ascii") {
                var lines = Encoding.ASCII.GetString(bytes, dataStart, bytes.Length - dataStart).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                var row = 0;
                foreach (var element in elements) {
                    for (var i = 0; i < element.count; i++, row++) {
                        var values = lines[row].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        var at = 0;
                        var position = Vector3.zero;
                        foreach (var prop in element.props) {
                            if (prop.listCount != null) {
                                var n = int.Parse(values[at++]);
                                var face = new List<int>();
                                for (var k = 0; k < n; k++) face.Add(int.Parse(values[at++]));
                                if (element.name == "face" && (prop.name == "vertex_indices" || prop.name == "vertex_index")) AddFace(result, face);
                            }
                            else {
                                var value = F(values[at++]);
                                if (element.name == "vertex") {
                                    if (prop.name == "x") position.x = value;
                                    else if (prop.name == "y") position.y = value;
                                    else if (prop.name == "z") position.z = value;
                                }
                            }
                        }
                        if (element.name == "vertex") result.vertices.Add(position);
                    }
                }
                return;
            }

            var bigEndian = format == "binary_big_endian";
            var offset = dataStart;
            foreach (var element in elements) {
                for (var i = 0; i < element.count; i++) {
                    var position = Vector3.zero;
                    foreach (var prop in element.props) {
                        if (prop.listCount != null) {
                            var n = (int)ReadBinary(bytes, ref offset, prop.listCount, bigEndian);
                            var face = new List<int>();
                            for (var k = 0; k < n; k++) face.Add((int)ReadBinary(bytes, ref offset, prop.type, bigEndian));
                            if (element.name == "face" && (prop.name == "vertex_indices" || prop.name == "vertex_index")) AddFace(result, face);
                        }
                        else {
                            var value = (float)ReadBinary(bytes, ref offset, prop.type, bigEndian);
                            if (element.name == "vertex") {
                                if (prop.name == "x") position.x = value;
                                else if (prop.name == "y") position.y = value;
                                else if (prop.name == "z") position.z = value;
                            }
                        }
                    }
                    if (element.name == "vertex") result.vertices.Add(position);
                }
            }
        }

        private static void AddFace(Result result, List<int> face) {
            for (var k = 1; k + 1 < face.Count; k++) {
                result.triangles.Add(face[0]);
                result.triangles.Add(face[k]);
                result.triangles.Add(face[k + 1]);
            }
        }

        private static double ReadBinary(byte[] bytes, ref int offset, string type, bool bigEndian) {
            int size = type switch {
                "char" or "int8" or "uchar" or "uint8" => 1,
                "short" or "int16" or "ushort" or "uint16" => 2,
                "double" or "float64" => 8,
                _ => 4,
            };
            var chunk = new byte[size];
            Array.Copy(bytes, offset, chunk, 0, size);
            offset += size;
            if (bigEndian == BitConverter.IsLittleEndian) Array.Reverse(chunk);
            return type switch {
                "char" or "int8" => (sbyte)chunk[0],
                "uchar" or "uint8" => chunk[0],
                "short" or "int16" => BitConverter.ToInt16(chunk, 0),
                "ushort" or "uint16" => BitConverter.ToUInt16(chunk, 0),
                "int" or "int32" => BitConverter.ToInt32(chunk, 0),
                "uint" or "uint32" => BitConverter.ToUInt32(chunk, 0),
                "double" or "float64" => BitConverter.ToDouble(chunk, 0),
                _ => BitConverter.ToSingle(chunk, 0),
            };
        }

        private static int IndexOf(byte[] haystack, byte[] needle) {
            for (var i = 0; i + needle.Length <= haystack.Length && i < 65536; i++) {
                var match = true;
                for (var k = 0; k < needle.Length && match; k++) match = haystack[i + k] == needle[k];
                if (match) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------ 3MF

        private static void Read3mf(string path, Result result) {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                        ?? throw new Exception("no 3D model inside the 3MF file");
            var document = new XmlDocument();
            using (var stream = entry.Open()) document.Load(stream);
            var model = document.DocumentElement;
            var unit = model.GetAttribute("unit");
            result.unitKnown = true;
            result.unit = unit switch {
                "micron" => Unit.Millimeters,
                "centimeter" => Unit.Centimeters,
                "meter" => Unit.Meters,
                "inch" => Unit.Inches,
                "foot" => Unit.Inches,
                _ => Unit.Millimeters,
            };
            var scale = unit == "micron" ? 0.001f : unit == "foot" ? 12f : 1f;

            var objects = new Dictionary<string, XmlElement>();
            foreach (XmlElement obj in document.GetElementsByTagName("object")) objects[obj.GetAttribute("id")] = obj;
            var items = document.GetElementsByTagName("item").Cast<XmlElement>().ToList();
            if (items.Count == 0)
                foreach (var obj in objects.Values) AddObject(obj, Matrix4x4.identity, objects, result, scale, 0);
            foreach (var item in items)
                if (objects.TryGetValue(item.GetAttribute("objectid"), out var obj))
                    AddObject(obj, Transform3mf(item.GetAttribute("transform")), objects, result, scale, 0);
        }

        private static void AddObject(XmlElement obj, Matrix4x4 matrix, Dictionary<string, XmlElement> objects, Result result, float scale, int depth) {
            if (depth > 8) return;
            foreach (XmlElement mesh in obj.GetElementsByTagName("mesh")) {
                if (mesh.ParentNode != obj) continue;
                var start = result.vertices.Count;
                foreach (XmlElement vertex in mesh.GetElementsByTagName("vertex")) {
                    var p = new Vector3(F(vertex.GetAttribute("x")), F(vertex.GetAttribute("y")), F(vertex.GetAttribute("z")));
                    result.vertices.Add(matrix.MultiplyPoint3x4(p) * scale);
                }
                foreach (XmlElement triangle in mesh.GetElementsByTagName("triangle")) {
                    result.triangles.Add(start + int.Parse(triangle.GetAttribute("v1")));
                    result.triangles.Add(start + int.Parse(triangle.GetAttribute("v2")));
                    result.triangles.Add(start + int.Parse(triangle.GetAttribute("v3")));
                }
            }
            foreach (XmlElement component in obj.GetElementsByTagName("component"))
                if (objects.TryGetValue(component.GetAttribute("objectid"), out var child))
                    AddObject(child, matrix * Transform3mf(component.GetAttribute("transform")), objects, result, scale, depth + 1);
        }

        // "m00 m01 m02 m10 m11 m12 m20 m21 m22 m30 m31 m32" (row vectors).
        private static Matrix4x4 Transform3mf(string text) {
            if (string.IsNullOrWhiteSpace(text)) return Matrix4x4.identity;
            var v = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
            if (v.Length != 12) return Matrix4x4.identity;
            // Row vectors (p' = p M) to Unity's column vectors (p' = M p).
            var m = Matrix4x4.identity;
            for (var row = 0; row < 3; row++) {
                m[row, 0] = v[row];
                m[row, 1] = v[3 + row];
                m[row, 2] = v[6 + row];
                m[row, 3] = v[9 + row];
            }
            return m;
        }

        // ------------------------------------------------------------------ glTF / GLB

        private static void ReadGltf(string path, byte[] bytes, bool binary, Result result) {
            string json;
            byte[] glbBuffer = null;
            if (binary) {
                if (BitConverter.ToUInt32(bytes, 0) != 0x46546C67) throw new Exception("not a GLB file");
                var offset = 12;
                json = null;
                while (offset + 8 <= bytes.Length) {
                    var length = (int)BitConverter.ToUInt32(bytes, offset);
                    var type = BitConverter.ToUInt32(bytes, offset + 4);
                    if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(bytes, offset + 8, length);
                    else if (type == 0x004E4942) {
                        glbBuffer = new byte[length];
                        Array.Copy(bytes, offset + 8, glbBuffer, 0, length);
                    }
                    offset += 8 + length;
                }
                if (json == null) throw new Exception("the GLB file has no JSON");
            }
            else json = Encoding.UTF8.GetString(bytes);

            var root = (Dictionary<string, object>)MiniJson.Parse(json);
            var buffers = new List<byte[]>();
            foreach (Dictionary<string, object> buffer in List(root, "buffers")) {
                if (buffer.TryGetValue("uri", out var uriObject) && uriObject is string uri) {
                    if (uri.StartsWith("data:")) buffers.Add(Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1)));
                    else buffers.Add(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path) ?? "", Uri.UnescapeDataString(uri))));
                }
                else buffers.Add(glbBuffer);
            }
            var views = List(root, "bufferViews").Cast<Dictionary<string, object>>().ToList();
            var accessors = List(root, "accessors").Cast<Dictionary<string, object>>().ToList();
            var meshes = List(root, "meshes").Cast<Dictionary<string, object>>().ToList();
            var nodes = List(root, "nodes").Cast<Dictionary<string, object>>().ToList();
            result.unitKnown = true;
            result.unit = Unit.Meters;

            void AddMesh(int meshIndex, Matrix4x4 matrix) {
                foreach (Dictionary<string, object> primitive in List(meshes[meshIndex], "primitives")) {
                    var mode = primitive.TryGetValue("mode", out var m) ? Convert.ToInt32(m) : 4;
                    if (mode != 4) continue;
                    var attributes = (Dictionary<string, object>)primitive["attributes"];
                    if (!attributes.TryGetValue("POSITION", out var positionIndex)) continue;
                    var positions = ReadAccessor(accessors[Convert.ToInt32(positionIndex)], views, buffers);
                    var start = result.vertices.Count;
                    for (var i = 0; i + 2 < positions.Length; i += 3)
                        result.vertices.Add(matrix.MultiplyPoint3x4(new Vector3((float)positions[i], (float)positions[i + 1], (float)positions[i + 2])));
                    var count = positions.Length / 3;
                    if (primitive.TryGetValue("indices", out var indexAccessor)) {
                        var indices = ReadAccessor(accessors[Convert.ToInt32(indexAccessor)], views, buffers);
                        foreach (var index in indices) result.triangles.Add(start + (int)index);
                    }
                    else
                        for (var i = 0; i < count; i++) result.triangles.Add(start + i);
                }
            }

            void AddNode(int index, Matrix4x4 parent, int depth) {
                if (depth > 32) return;
                var node = nodes[index];
                var matrix = parent * NodeMatrix(node);
                if (node.TryGetValue("mesh", out var mesh)) AddMesh(Convert.ToInt32(mesh), matrix);
                if (node.TryGetValue("children", out var children))
                    foreach (var child in (List<object>)children) AddNode(Convert.ToInt32(child), matrix, depth + 1);
            }

            var scenes = List(root, "scenes").Cast<Dictionary<string, object>>().ToList();
            if (scenes.Count > 0) {
                var scene = root.TryGetValue("scene", out var s) ? Convert.ToInt32(s) : 0;
                foreach (var node in List(scenes[Mathf.Clamp(scene, 0, scenes.Count - 1)], "nodes")) AddNode(Convert.ToInt32(node), Matrix4x4.identity, 0);
            }
            else
                for (var i = 0; i < meshes.Count; i++) AddMesh(i, Matrix4x4.identity);
        }

        private static List<object> List(Dictionary<string, object> obj, string key) =>
            obj.TryGetValue(key, out var value) && value is List<object> list ? list : new List<object>();

        private static Matrix4x4 NodeMatrix(Dictionary<string, object> node) {
            if (node.TryGetValue("matrix", out var m) && m is List<object> values && values.Count == 16) {
                var matrix = new Matrix4x4();
                for (var i = 0; i < 16; i++) matrix[i % 4, i / 4] = Convert.ToSingle(values[i]);  // column-major
                return matrix;
            }
            var t = Vector3.zero;
            var r = Quaternion.identity;
            var sc = Vector3.one;
            if (node.TryGetValue("translation", out var tv) && tv is List<object> tl) t = new Vector3(Convert.ToSingle(tl[0]), Convert.ToSingle(tl[1]), Convert.ToSingle(tl[2]));
            if (node.TryGetValue("rotation", out var rv) && rv is List<object> rl) r = new Quaternion(Convert.ToSingle(rl[0]), Convert.ToSingle(rl[1]), Convert.ToSingle(rl[2]), Convert.ToSingle(rl[3]));
            if (node.TryGetValue("scale", out var sv) && sv is List<object> sl) sc = new Vector3(Convert.ToSingle(sl[0]), Convert.ToSingle(sl[1]), Convert.ToSingle(sl[2]));
            return Matrix4x4.TRS(t, r, sc);
        }

        private static double[] ReadAccessor(Dictionary<string, object> accessor, List<Dictionary<string, object>> views, List<byte[]> buffers) {
            var count = Convert.ToInt32(accessor["count"]);
            var componentType = Convert.ToInt32(accessor["componentType"]);
            var components = (string)accessor["type"] switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => 1 };
            var values = new double[count * components];
            if (!accessor.TryGetValue("bufferView", out var viewIndex)) return values;
            var view = views[Convert.ToInt32(viewIndex)];
            var buffer = buffers[Convert.ToInt32(view["buffer"])];
            var size = componentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, _ => 4 };
            var stride = view.TryGetValue("byteStride", out var st) ? Convert.ToInt32(st) : size * components;
            var start = (view.TryGetValue("byteOffset", out var vo) ? Convert.ToInt32(vo) : 0) + (accessor.TryGetValue("byteOffset", out var ao) ? Convert.ToInt32(ao) : 0);
            for (var i = 0; i < count; i++)
                for (var c = 0; c < components; c++) {
                    var at = start + i * stride + c * size;
                    values[i * components + c] = componentType switch {
                        5120 => (sbyte)buffer[at],
                        5121 => buffer[at],
                        5122 => BitConverter.ToInt16(buffer, at),
                        5123 => BitConverter.ToUInt16(buffer, at),
                        5125 => BitConverter.ToUInt32(buffer, at),
                        _ => BitConverter.ToSingle(buffer, at),
                    };
                }
            return values;
        }
    }

    // A small JSON reader for glTF (objects, arrays, numbers, strings, true/false/null).
    public static class MiniJson {
        public static object Parse(string json) {
            var i = 0;
            return Value(json, ref i);
        }

        private static void Skip(string s, ref int i) {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object Value(string s, ref int i) {
            Skip(s, ref i);
            switch (s[i]) {
                case '{': {
                    var obj = new Dictionary<string, object>();
                    i++;
                    Skip(s, ref i);
                    if (s[i] == '}') { i++; return obj; }
                    while (true) {
                        Skip(s, ref i);
                        var key = (string)Value(s, ref i);
                        Skip(s, ref i);
                        i++;  // ':'
                        obj[key] = Value(s, ref i);
                        Skip(s, ref i);
                        if (s[i++] == '}') return obj;
                    }
                }
                case '[': {
                    var list = new List<object>();
                    i++;
                    Skip(s, ref i);
                    if (s[i] == ']') { i++; return list; }
                    while (true) {
                        list.Add(Value(s, ref i));
                        Skip(s, ref i);
                        if (s[i++] == ']') return list;
                    }
                }
                case '"': {
                    var builder = new StringBuilder();
                    i++;
                    while (s[i] != '"') {
                        if (s[i] == '\\') {
                            i++;
                            switch (s[i]) {
                                case 'n': builder.Append('\n'); break;
                                case 't': builder.Append('\t'); break;
                                case 'r': builder.Append('\r'); break;
                                case 'b': builder.Append('\b'); break;
                                case 'f': builder.Append('\f'); break;
                                case 'u': builder.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                                default: builder.Append(s[i]); break;
                            }
                        }
                        else builder.Append(s[i]);
                        i++;
                    }
                    i++;
                    return builder.ToString();
                }
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default: {
                    var start = i;
                    while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                    return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                }
            }
        }
    }
}
