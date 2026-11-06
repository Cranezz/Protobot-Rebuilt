using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Parts_List;
using UnityEngine;

namespace Protobot.Custom {
    // V2: parts the user makes: imported 3D models ("CUST-<key>") and polycarbonate pieces cut in the sheet
    // editor ("POLY-<key>"). Each is a mesh in inches with its holes. They live in a library on this computer
    // (persistentDataPath/Custom Parts, one JSON file each) so they're in the parts browser in every build, and
    // a build file carries the ones it uses (BuildFile), so a build sent to someone else opens with them.
    [Serializable]
    public class CustomHole {
        public Vector3 center;   // middle of the hole, part space (inches)
        public Vector3 axis;     // along the hole
        public float size;       // round diameter or square side (inches)
        public float depth;
        public bool square;      // square or rectangular (a slot); otherwise round
        public bool threaded;
        public Vector3 up;       // along the hole's second side (zero: any way round)
        public float size2;      // the second side of a rectangular hole (0: the same as size)
        public Vector2[] outline; // its exact shape, in inches along (right, up) around the centre (null: plain)
    }

    [Serializable]
    public class CustomPartData {
        public string key;          // 8 hex digits
        public string kind;         // "CUST" (imported) or "POLY" (polycarbonate)
        public string name;
        public string source;       // the file it came from (imported)
        public string material;     // Aluminum, Steel, Plastic, Polycarbonate
        public string mesh;         // vertices (float x,y,z) then triangles (int), deflated, base64
        public CustomHole[] holes = Array.Empty<CustomHole>();
        public float weightGrams;
        public float thickness;     // polycarbonate
        public string sketch;       // polycarbonate: the sketch, to edit it again
        public string finish;       // plastic pieces: what they're cut from (a PlasticMaterials id)
        public long created;

        public string Id => kind + "-" + key;
    }

    public static class CustomParts {
        public const string Imported = "CUST", Polycarbonate = "POLY";

        private static readonly Dictionary<string, CustomPartData> parts = new();
        // Deleted from the library (their files in the Deleted folder, to restore): still found by id, so copies
        // in the open build keep their model and are saved in it, but not listed in the parts browser.
        private static readonly Dictionary<string, CustomPartData> deleted = new();
        private static readonly Dictionary<string, Mesh> meshes = new();
        private static bool loaded;

        /// <summary>Raised when a part is added, changed or removed (the parts browser refreshes).</summary>
        public static event Action Changed;

        public static string LibraryFolder => Path.Combine(Application.persistentDataPath, "Custom Parts");
        public static string DeletedFolder => Path.Combine(LibraryFolder, "Deleted");

        /// <summary>Parts deleted from the library that can still be restored (newest first).</summary>
        public static IEnumerable<CustomPartData> Deleted {
            get {
                EnsureLoaded();
                return deleted.Values.Where(p => File.Exists(DeletedFile(p.key))).OrderByDescending(p => File.GetLastWriteTimeUtc(DeletedFile(p.key))).ToList();
            }
        }

        public static bool IsDeleted(string key) {
            EnsureLoaded();
            return !parts.ContainsKey(key) && deleted.ContainsKey(key);
        }

        private static string LibraryFile(string key) => Path.Combine(LibraryFolder, key + ".json");
        private static string DeletedFile(string key) => Path.Combine(DeletedFolder, key + ".json");

        /// <summary>The size of a part's file on this computer (bytes; 0 if it has none).</summary>
        public static long FileSize(CustomPartData part) {
            try {
                var file = new FileInfo(parts.ContainsKey(part.key) ? LibraryFile(part.key) : DeletedFile(part.key));
                return file.Exists ? file.Length : 0;
            }
            catch {
                return 0;
            }
        }

        public static IEnumerable<CustomPartData> All {
            get {
                EnsureLoaded();
                return parts.Values;
            }
        }

        public static IEnumerable<CustomPartData> OfKind(string kind) => All.Where(p => p.kind == kind);

        /// <summary>The custom part with this part id ("CUST-1a2b3c4d"), or null.</summary>
        public static CustomPartData Find(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            var split = id.Split('-');
            if (split.Length < 2 || (split[0] != Imported && split[0] != Polycarbonate)) return null;
            EnsureLoaded();
            return parts.TryGetValue(split[1], out var part) || deleted.TryGetValue(split[1], out part) ? part : null;
        }

        public static string NewKey() => Guid.NewGuid().ToString("N").Substring(0, 8);

        // ------------------------------------------------------------------ library

        private static void EnsureLoaded() {
            if (loaded) return;
            loaded = true;
            try {
                void Read(string folder, Dictionary<string, CustomPartData> into) {
                    if (!Directory.Exists(folder)) return;
                    foreach (var file in Directory.GetFiles(folder, "*.json")) {
                        try {
                            var part = JsonUtility.FromJson<CustomPartData>(File.ReadAllText(file));
                            if (part != null && !string.IsNullOrEmpty(part.key))
                                into[part.key] = part;
                        }
                        catch (Exception e) {
                            Debug.LogWarning("[Custom parts] Couldn't read " + file + ": " + e.Message);
                        }
                    }
                }
                Read(LibraryFolder, parts);
                Read(DeletedFolder, deleted);
            }
            catch (Exception e) {
                Debug.LogWarning("[Custom parts] Couldn't read the library: " + e.Message);
            }
        }

        /// <summary>Adds (or replaces) a part in the library and saves it.</summary>
        public static void Add(CustomPartData part, bool save = true) {
            EnsureLoaded();
            parts[part.key] = part;
            deleted.Remove(part.key);
            meshes.Remove(part.key);
            if (save) {
                try {
                    Directory.CreateDirectory(LibraryFolder);
                    File.WriteAllText(LibraryFile(part.key), JsonUtility.ToJson(part));
                    if (File.Exists(DeletedFile(part.key))) File.Delete(DeletedFile(part.key));
                }
                catch (Exception e) {
                    Debug.LogWarning("[Custom parts] Couldn't save " + part.name + ": " + e.Message);
                }
            }
            CustomPartGenerator.Refresh();
            Changed?.Invoke();
        }

        /// <summary>A part carried in a build being opened: into the library, unless it was deleted from it (then it
        /// only serves this build, and stays restorable).</summary>
        public static void AddFromBuild(CustomPartData part) {
            EnsureLoaded();
            if (!parts.ContainsKey(part.key) && deleted.ContainsKey(part.key)) {
                deleted[part.key] = part;
                meshes.Remove(part.key);
                CustomPartGenerator.Refresh();
                return;
            }
            Add(part);
        }

        /// <summary>Deletes a part from the library: its file goes to the Deleted folder (Restore brings it back).
        /// Copies in the open build keep working and are saved with it.</summary>
        public static bool Remove(string key) {
            EnsureLoaded();
            if (!parts.TryGetValue(key, out var part)) return false;
            try {
                Directory.CreateDirectory(DeletedFolder);
                var file = LibraryFile(key);
                if (File.Exists(DeletedFile(key))) File.Delete(DeletedFile(key));
                if (File.Exists(file)) File.Move(file, DeletedFile(key));
                else File.WriteAllText(DeletedFile(key), JsonUtility.ToJson(part));
                File.SetLastWriteTimeUtc(DeletedFile(key), DateTime.UtcNow);  // newest deleted first
            }
            catch (Exception e) {
                Debug.LogWarning("[Custom parts] Couldn't delete " + key + ": " + e.Message);
                return false;
            }
            parts.Remove(key);
            deleted[key] = part;
            CustomPartGenerator.Refresh();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Puts a deleted part back in the library.</summary>
        public static bool Restore(string key) {
            EnsureLoaded();
            if (!deleted.TryGetValue(key, out var part)) return false;
            try {
                Directory.CreateDirectory(LibraryFolder);
                if (File.Exists(LibraryFile(key))) File.Delete(LibraryFile(key));
                if (File.Exists(DeletedFile(key))) File.Move(DeletedFile(key), LibraryFile(key));
                else File.WriteAllText(LibraryFile(key), JsonUtility.ToJson(part));
            }
            catch (Exception e) {
                Debug.LogWarning("[Custom parts] Couldn't restore " + key + ": " + e.Message);
                return false;
            }
            deleted.Remove(key);
            parts[key] = part;
            CustomPartGenerator.Refresh();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Deletes a deleted part's file for good (copies in the open build still work until it closes).</summary>
        public static bool Purge(string key) {
            EnsureLoaded();
            try {
                if (File.Exists(DeletedFile(key))) File.Delete(DeletedFile(key));
            }
            catch (Exception e) {
                Debug.LogWarning("[Custom parts] Couldn't delete " + key + " for good: " + e.Message);
                return false;
            }
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ meshes

        public static void SetMesh(CustomPartData part, IList<Vector3> vertices, IList<int> triangles) {
            using var raw = new MemoryStream();
            using (var deflate = new DeflateStream(raw, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var writer = new BinaryWriter(deflate)) {
                writer.Write(vertices.Count);
                writer.Write(triangles.Count);
                foreach (var v in vertices) {
                    writer.Write(v.x);
                    writer.Write(v.y);
                    writer.Write(v.z);
                }
                foreach (var t in triangles)
                    writer.Write(t);
            }
            part.mesh = Convert.ToBase64String(raw.ToArray());
            meshes.Remove(part.key ?? "");
        }

        public static bool ReadMesh(CustomPartData part, out Vector3[] vertices, out int[] triangles) {
            vertices = null;
            triangles = null;
            if (string.IsNullOrEmpty(part.mesh)) return false;
            try {
                using var raw = new MemoryStream(Convert.FromBase64String(part.mesh));
                using var deflate = new DeflateStream(raw, CompressionMode.Decompress);
                using var reader = new BinaryReader(deflate);
                var vertexCount = reader.ReadInt32();
                var indexCount = reader.ReadInt32();
                vertices = new Vector3[vertexCount];
                for (var i = 0; i < vertexCount; i++)
                    vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                triangles = new int[indexCount];
                for (var i = 0; i < indexCount; i++)
                    triangles[i] = reader.ReadInt32();
                return true;
            }
            catch (Exception e) {
                Debug.LogWarning("[Custom parts] Broken mesh in " + part.name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>The part's mesh (shared by every copy, readable so the saw and drill work on it).</summary>
        public static Mesh MeshOf(CustomPartData part) {
            if (meshes.TryGetValue(part.key, out var mesh) && mesh != null)
                return mesh;
            mesh = new Mesh { name = part.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            if (ReadMesh(part, out var vertices, out var triangles)) {
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                // Plastic pieces: texture coordinates in inches across the sheet (x, z), for grainy finishes.
                if (part.kind == Polycarbonate) mesh.uv = vertices.Select(v => new Vector2(v.x, v.z)).ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
            meshes[part.key] = mesh;
            return mesh;
        }

        // ------------------------------------------------------------------ parts in the scene

        private static GameObject roundHole, squareHole;

        private static void FindHoleTemplates() {
            if (roundHole != null && squareHole != null) return;
            foreach (var type in PartsManager.partTypes ?? Array.Empty<PartType>()) {
                if (type == null) continue;
                if (roundHole == null && type.id == "SNDF")
                    roundHole = type.GetComponentInChildren<HoleCollider>(true)?.gameObject;
                if (squareHole == null && type.id == "GSET")
                    squareHole = type.GetComponentInChildren<HoleCollider>(true)?.gameObject;
            }
        }

        public static Material MaterialFor(CustomPartData part) {
            var host = Interface.Interface.Instance;
            if (host == null) return null;
            if (part.kind == Polycarbonate)
                return Polycarb.PlasticMaterials.MaterialFor(part.finish ?? Polycarb.PlasticMaterials.Default) ?? host.polycarbMaterial;
            if (part.material == "Polycarbonate")
                return host.polycarbMaterial;
            return part.material == "Plastic" ? host.plasticMaterial : host.metalMaterial;
        }

        /// <summary>A copy of the part in the scene, as any part generator makes one.</summary>
        public static GameObject Build(CustomPartData part, Vector3 position, Quaternion rotation) {
            var obj = new GameObject(part.name);
            obj.tag = "Object";
            obj.transform.SetPositionAndRotation(position, rotation);
            var mesh = MeshOf(part);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            var material = MaterialFor(part);
            if (material != null) renderer.sharedMaterial = material;
            obj.AddComponent<MeshCollider>().sharedMesh = mesh;
            obj.AddComponent<SavedObject>().id = part.Id;
            if (part.kind == Polycarbonate && material != null) obj.AddComponent<KeepTranslucent>().alpha = material.color.a;
            var partName = obj.AddComponent<PartName>();
            partName.name = part.name;
            partName.weightInGrams = part.weightGrams;
            AddHoles(obj, part.holes);
            return obj;
        }

        public static void AddHoles(GameObject obj, IEnumerable<CustomHole> holes) {
            FindHoleTemplates();
            foreach (var hole in holes ?? Array.Empty<CustomHole>()) {
                var template = hole.square ? squareHole : roundHole;
                if (template == null) continue;
                var collider = UnityEngine.Object.Instantiate(template, obj.transform);
                collider.name = "HoleCollider";
                collider.transform.localPosition = hole.center;
                var axis = hole.axis.sqrMagnitude > 0.5f ? hole.axis : Vector3.forward;
                var up = Vector3.ProjectOnPlane(hole.up, axis);
                collider.transform.localRotation = up.sqrMagnitude > 0.25f ? Quaternion.LookRotation(axis, up) : Quaternion.LookRotation(axis);
                collider.transform.localScale = new Vector3(hole.size, hole.size2 > 0f ? hole.size2 : hole.size, Mathf.Max(hole.depth, 0.02f));
                var holeCollider = collider.GetComponent<HoleCollider>();
                holeCollider.holeType = hole.threaded ? HoleCollider.HoleType.Threaded : HoleCollider.HoleType.Normal;
                holeCollider.twoSided = hole.threaded;
                holeCollider.Refresh(hole.outline != null && hole.outline.Length >= 3 ? hole.outline : null);
            }
        }

        /// <summary>After a part was edited (its polycarbonate sketch): the copies in the build take the new shape.</summary>
        public static int UpdateInScene(CustomPartData part) {
            var count = 0;
            var mesh = MeshOf(part);
            foreach (var obj in PartsManager.FindLoadedObjects()) {
                if (obj.GetComponent<SavedObject>().id != part.Id) continue;
                obj.GetComponent<MeshFilter>().sharedMesh = mesh;
                var material = MaterialFor(part);
                if (material != null && obj.TryGetComponent(out MeshRenderer renderer)) {
                    renderer.sharedMaterial = material;
                    if (obj.TryGetComponent(out KeepTranslucent keep)) keep.alpha = material.color.a;
                }
                if (obj.TryGetComponent(out MeshCollider collider)) {
                    collider.sharedMesh = null;
                    collider.sharedMesh = mesh;
                }
                foreach (var hole in obj.GetComponentsInChildren<HoleCollider>(true)) {
                    if (hole.transform.parent != obj.transform) continue;
                    hole.transform.SetParent(null);
                    UnityEngine.Object.Destroy(hole.gameObject);
                }
                AddHoles(obj, part.holes);
                if (obj.TryGetComponent(out PartName partName)) {
                    partName.name = part.name;
                    partName.weightInGrams = part.weightGrams;
                }
                count++;
            }
            return count;
        }

        // ------------------------------------------------------------------ weights

        public static float Density(string material) => material switch {
            "Steel" => 7.85f,
            "Plastic" => 1.10f,
            "Polycarbonate" => 1.20f,
            "ABS" => 1.05f,
            "Acetal" => 1.41f,
            "Delrin" => 1.42f,
            _ => 2.70f,  // aluminum
        };

        /// <summary>Grams, from the mesh's volume (inches) and the material.</summary>
        public static float Weight(IList<Vector3> vertices, IList<int> triangles, string material) {
            double volume = 0;
            for (var i = 0; i + 2 < triangles.Count; i += 3)
                volume += Vector3.Dot(vertices[triangles[i]], Vector3.Cross(vertices[triangles[i + 1]], vertices[triangles[i + 2]])) / 6.0;
            var cubicCm = Math.Abs(volume) * 16.387064;
            return Mathf.Round((float)(cubicCm * Density(material)) * 100f) / 100f;
        }
    }
}
