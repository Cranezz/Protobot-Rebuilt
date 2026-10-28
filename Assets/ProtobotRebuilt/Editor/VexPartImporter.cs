using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Parts_List;
using Protobot;
using Protobot.Geometry;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// V2: turns the VEX-CAD library (Assets/Protobot/Parts/Models, FBX in metres) into part types for the original
// Protobot parts menu, built like its own: a PartType + ChildPartGenerator prefab per family, one child per part
// with an inch-scaled mesh, a MeshCollider, PartData, PartName and HoleColliders found by HoleScanner.
//
// Holes are typed by what they really are: round 8-32 tapped holes (0.10"-0.152") are Threaded, so the original
// screws bite into them like into a nut. That gives the tricks the community uses: a screw into a shaft collar's
// set screw hole (collar as a bracket or a stop), into standoffs and couplers, into motor cases, into threaded
// bumpers. A collar's bore is Clamp. Everything else (square structure holes, gear and sprocket screw holes) is
// Normal, so screws pass through it.
//
// Pre-cut or pre-modified parts are left out (half-cut channels, drilled or shaved parts): the saw and drill
// make those. Screws and nuts stay the original's (they carry its Screw/Nut behaviour). Runs a family per editor
// update; progress in Temp/VexImport.txt.
public static class VexPartImporter {
    private const string ModelsFolder = "Assets/Protobot/Parts/Models/";
    private const string ManifestPath = ModelsFolder + "parts_manifest.json";
    private const string PrefabFolder = "Assets/ProtobotRebuilt/Resources/Part Prefabs/VEX/";
    private const string MeshFolder = "Assets/ProtobotRebuilt/VEX Parts/Meshes/";
    private const string ProgressPath = "Temp/VexImport.txt";
    private const float Inch = 39.3701f;          // inches per metre
    private const int TriangleBudget = 30000;    // per part: the most detailed LOD under this

    private static readonly string[] OriginalPrefabs = {
        "Assets/ProtobotRebuilt/Resources/Part Prefabs/5Structure/Gussets.prefab",
        "Assets/ProtobotRebuilt/Resources/Part Prefabs/5Structure/Standoff.prefab",
    };

    // ---------------------------------------------------------------- which parts

    public static bool Excluded(string path) {
        var lower = path.ToLowerInvariant();
        var name = lower.Substring(lower.LastIndexOf('/') + 1);
        // "Reference Files" folders deeper down hold real parts (HS gears, sprockets); the top-level structure one
        // holds cross-section references.
        if (lower.StartsWith("game objects/") || lower.StartsWith("structure/reference files/") || lower.Contains("sizing tool"))
            return true;
        if (Regex.IsMatch(name, @"\bhalf\b") || name.Contains("drilled") || name.Contains("shaved"))
            return true; // pre-cut / pre-modified: the saw and drill do this now
        if (lower.StartsWith("structure/hardware/")) {
            // The original's screws and nuts keep their own behaviour.
            if (name.StartsWith("nut") || (name.Contains("screw") && !name.Contains("set screw") && !name.StartsWith("sscrew")))
                return true;
        }
        return false;
    }

    private class Family {
        public string key, name, id;
        public PartType.PartGroup group;
        public List<string> paths = new();
    }

    private static List<Family> Families() {
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        var byKey = new Dictionary<string, Family>();
        var originalNames = new HashSet<string>(Resources.LoadAll<GameObject>("Part Prefabs")
            .Where(p => p.GetComponent<PartType>() != null && !AssetDatabase.GetAssetPath(p).StartsWith(PrefabFolder))
            .Select(p => Normalize(p.name)));

        foreach (var part in manifest.parts) {
            if (Excluded(part.path) || !File.Exists(ModelsFolder + part.path + ".fbx"))
                continue;
            var segments = part.path.Split('/');
            var top = segments[0];
            // Family = the folder under the top one; a folder holding a single part joins its top's "Parts".
            var key = segments.Length >= 3 ? segments[0] + "/" + segments[1] : top + "/*";
            if (!byKey.TryGetValue(key, out var family)) {
                family = new Family { key = key };
                byKey[key] = family;
            }
            family.paths.Add(part.path);
        }

        // Singles: folders with one part also go to their top's "Parts".
        foreach (var family in byKey.Values.ToList()) {
            if (family.paths.Count != 1 || family.key.EndsWith("/*"))
                continue;
            var top = family.key.Split('/')[0];
            var key = top + "/*";
            if (!byKey.TryGetValue(key, out var shared))
                byKey[key] = shared = new Family { key = key };
            shared.paths.AddRange(family.paths);
            byKey.Remove(family.key);
        }

        var families = byKey.Values.OrderBy(f => f.key).ToList();
        var usedIds = new HashSet<string>();
        foreach (var family in families) {
            var top = family.key.Split('/')[0];
            var folder = family.key.Split('/')[1];
            family.name = folder == "*" ? TopName(top) : FamilyName(folder);
            if (originalNames.Contains(Normalize(family.name)))
                family.name += " (VEX)";
            family.group = top switch {
                "Motion" => PartType.PartGroup.Motion,
                "Electronics" => PartType.PartGroup.Electronics,
                _ => PartType.PartGroup.Structure,
            };
            var id = "VX" + Regex.Replace(family.name.ToUpperInvariant(), "[^A-Z0-9]", "");
            if (id.Length > 16) id = id.Substring(0, 16);
            while (!usedIds.Add(id)) id += "X";
            family.id = id;
        }
        return families;
    }

    private static string TopName(string top) => top switch {
        "Electronics" => "V5 Electronics",
        "Motion" => "Motion Parts",
        "Structure" => "Structure Parts",
        _ => "Miscellaneous",
    };

    private static string FamilyName(string folder) => folder switch {
        "C Channel" => "C-Channel Stock",
        "Angle" => "Angle Stock",
        "Plate" => "Plate Stock",
        "Shaft HS" => "High Strength Shaft Parts",
        "Shaft LS" => "Shaft Parts",
        "Hardware" => "Hardware",
        "Reference Files" => "Ball Bearings",
        _ => folder,
    };

    private static string Normalize(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");

    [Serializable] private class Manifest { public List<ManifestPart> parts; }
    [Serializable] private class ManifestPart { public string path; }

    // ---------------------------------------------------------------- running

    private static Queue<Family> queue;
    private static int total, done, partsMade;
    private static GameObject squareTemplate, roundTemplate;
    private static Dictionary<string, Material> palette;

    [MenuItem("Tools/Protobot/Import VEX Parts")]
    public static void ImportAll() => Start(Families());

    /// <summary>Imports only the families whose name contains <paramref name="filter"/> (for testing).</summary>
    public static void ImportMatching(string filter) =>
        Start(Families().Where(f => f.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList());

    private static void Start(List<Family> families) {
        LoadTemplates();
        Directory.CreateDirectory(PrefabFolder);
        Directory.CreateDirectory(MeshFolder);
        queue = new Queue<Family>(families);
        total = families.Count;
        done = 0;
        partsMade = 0;
        WriteProgress("started " + total + " families");
        EditorApplication.update -= Step;
        EditorApplication.update += Step;
    }

    public static bool Running => queue != null && queue.Count > 0;

    private static void Step() {
        if (queue == null || queue.Count == 0) {
            EditorApplication.update -= Step;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            WriteProgress("done " + done + "/" + total + " families, " + partsMade + " parts");
            return;
        }
        var family = queue.Dequeue();
        try {
            partsMade += Build(family);
            done++;
            WriteProgress("working " + done + "/" + total + " " + family.name);
        }
        catch (Exception e) {
            done++;
            Debug.LogError("[VEX import] " + family.name + ": " + e);
            WriteProgress("error in " + family.name + ": " + e.Message);
        }
    }

    private static void WriteProgress(string line) {
        File.AppendAllText(ProgressPath, DateTime.Now.ToString("HH:mm:ss ") + line + "\n");
    }

    private static void LoadTemplates() {
        var gusset = AssetDatabase.LoadAssetAtPath<GameObject>(OriginalPrefabs[0]);
        var standoff = AssetDatabase.LoadAssetAtPath<GameObject>(OriginalPrefabs[1]);
        squareTemplate = gusset.GetComponentInChildren<HoleCollider>(true).gameObject;
        roundTemplate = standoff.GetComponentInChildren<HoleCollider>(true).gameObject;

        palette = new Dictionary<string, Material>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/ProtobotRebuilt/Materials" })) {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Override/") || path.Contains("/UI/"))
                continue;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            palette[material.name] = material;
        }
    }

    // ---------------------------------------------------------------- one family

    private static int Build(Family family) {
        var root = new GameObject(family.name);
        var meshAssetPath = MeshFolder + family.id + ".asset";
        var meshes = new List<Mesh>();
        var usedNames = new HashSet<string>();
        try {
            var type = root.AddComponent<PartType>();
            type.id = family.id;
            type.group = family.group;
            type.icon = IconFor(family);
            var generator = root.AddComponent<ChildPartGenerator>();
            generator.param1 = new Parameter { name = "Type", value = "", customUnit = "", customDefault = "" };
            generator.param2 = new Parameter { name = "", value = "", customUnit = "", customDefault = "" };

            foreach (var path in family.paths.OrderBy(p => p)) {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelsFolder + path + ".fbx");
                if (model == null)
                    continue;
                var display = DisplayName(path);
                var option = Regex.Replace(display, "-", " ").Trim();
                while (!usedNames.Add(option))
                    option += " ";
                var part = MakePart(model, path, display, option, family, out var mesh);
                if (part == null)
                    continue;
                part.transform.SetParent(root.transform, false);
                meshes.Add(mesh);
            }

            // The meshes are kept as sub-assets of one file per family.
            var store = new Mesh { name = family.name + " (meshes)" };
            AssetDatabase.DeleteAsset(meshAssetPath);
            AssetDatabase.CreateAsset(store, meshAssetPath);
            foreach (var mesh in meshes)
                AssetDatabase.AddObjectToAsset(mesh, store);
            AssetDatabase.SaveAssets();

            var prefabPath = PrefabFolder + Regex.Replace(family.name, @"[\\/:*?""<>|]", "") + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return meshes.Count;
        }
        finally {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject MakePart(GameObject model, string path, string display, string option, Family family, out Mesh mesh) {
        mesh = null;
        var lods = Lods(model);
        if (lods.Count == 0)
            return null;

        // Holes from the most detailed level, in metres (the scanner's units).
        Gather(lods[0], model.transform, 1f, out var fineVerts, out var fineTris, out _);
        var found = fineTris.Count > 0 ? HoleScanner.Scan(fineVerts, fineTris) : new List<HoleScanner.Found>();
        // Smaller than an 8-32 screw (set screw sockets, vents): nothing goes in them.
        found.RemoveAll(h => h.size * Inch < MinUsableHole);

        // The render mesh: the most detailed level under the budget.
        var chosen = lods.FirstOrDefault(l => TriangleCount(l) <= TriangleBudget) ?? lods[lods.Count - 1];
        mesh = BakeMesh(chosen, model.transform, out var materials);
        mesh.name = option.Trim();

        var part = new GameObject(option.Trim());
        part.tag = family.group == PartType.PartGroup.Motion ? "Motion" : "Object";
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterials = materials.Select(m => MapMaterial(m, family)).ToArray();
        part.AddComponent<MeshCollider>().sharedMesh = mesh;
        part.AddComponent<SavedObject>();
        var data = part.AddComponent<PartData>();
        data.param1Value = option;
        data.param2Value = "";
        data.holeData = "";
        var partName = part.AddComponent<PartName>();
        partName.name = display;
        Gather(lods[0], model.transform, 1f, out var metreVerts, out var metreTris, out _);
        partName.weightInGrams = Mathf.Round(VolumeCm3(metreVerts, metreTris) * Density(path, materials) * 100f) / 100f;

        // Where 8-32 tapped holes are real: electronics (threaded inserts) and pneumatics (mounting nuts, ports).
        // Elsewhere small round holes are pins, rivets or clearance, so they stay Normal.
        var lowerPath = path.ToLowerInvariant();
        var threadedParts = lowerPath.StartsWith("electronics") || lowerPath.Contains("pneumatic");
        // Inserts are often drawn at the screw's own size (0.164"), still under an 8-32 clearance hole (0.17").
        const float MaxTapped = 0.168f;

        // Standoffs and couplers are tapped through their length: one threaded hole end to end, like the
        // original's standoff (screws go in from both ends).
        var isThreadedBar = (lowerPath.Contains("standoff") && !lowerPath.Contains("retainer"))
                            || (lowerPath.Contains("/hardware/") && lowerPath.Contains("coupler"));
        if (isThreadedBar && fineVerts.Count > 0) {
            var bounds = BoundsOf(fineVerts);
            var longest = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0 : bounds.size.y >= bounds.size.z ? 1 : 2;
            var along = Vector3.zero;
            along[longest] = 1f;
            found.RemoveAll(h => Mathf.Abs(Vector3.Dot(h.axis.normalized, along)) > 0.9f);
            var bar = Object.Instantiate(roundTemplate, part.transform);
            bar.name = "HoleCollider";
            bar.transform.localPosition = bounds.center * Inch;
            bar.transform.localRotation = Quaternion.LookRotation(along);
            bar.transform.localScale = new Vector3(SetScrewTap, SetScrewTap, bounds.size[longest] * Inch);
            var barHole = bar.GetComponent<HoleCollider>();
            barHole.holeType = HoleCollider.HoleType.Threaded;
            barHole.twoSided = true;
            data.primaryHole = barHole;
        }

        HoleCollider primary = null;
        var bestDistance = float.MaxValue;
        var isCollar = path.ToLowerInvariant().Contains("collar");
        // A collar's bore: the biggest hole through its middle. Clamping collars are split, so the scanner may
        // not see theirs: then it's a square bore (1/8", 1/4" for high strength) across the collar's thin side.
        var collarBore = default(HoleScanner.Found);
        if (isCollar) {
            var middle = fineVerts.Count > 0 ? BoundsOf(fineVerts).center : Vector3.zero;
            collarBore = found.Where(h => DistanceToLine(middle, h.entry, h.axis) < 0.0015f).OrderByDescending(h => h.size).FirstOrDefault();
            if (collarBore.size <= 0f && fineVerts.Count > 0) {
                var bounds = BoundsOf(fineVerts);
                var thin = bounds.size.x <= bounds.size.y && bounds.size.x <= bounds.size.z ? 0 : bounds.size.y <= bounds.size.z ? 1 : 2;
                var axisVector = Vector3.zero;
                axisVector[thin] = 1f;
                var side = path.Contains("HS ") ? 0.25f / Inch : 0.125f / Inch;
                collarBore = new HoleScanner.Found {
                    entry = bounds.center - axisVector * (bounds.size[thin] / 2f), axis = axisVector, size = side,
                    square = true, depth = bounds.size[thin], through = true,
                };
                found.Add(collarBore);
            }
        }
        foreach (var hole in found) {
            var size = hole.size * Inch;
            var depth = hole.depth * Inch;
            var center = (hole.entry + hole.axis * (hole.depth / 2f)) * Inch;
            var type = HoleCollider.HoleType.Normal;
            if (isCollar && hole.Equals(collarBore))
                type = HoleCollider.HoleType.Clamp;
            else if (threadedParts && !hole.square && size >= 0.125f && size <= MaxTapped)
                type = HoleCollider.HoleType.Threaded;  // 8-32 tapped (inserts in motors, sensors, the brain...)

            var collider = Object.Instantiate(hole.square ? squareTemplate : roundTemplate, part.transform);
            collider.name = "HoleCollider";
            collider.transform.localPosition = center;
            collider.transform.localRotation = Quaternion.LookRotation(hole.axis);
            collider.transform.localScale = new Vector3(size, size, depth);
            var holeCollider = collider.GetComponent<HoleCollider>();
            holeCollider.holeType = type;
            holeCollider.twoSided = type == HoleCollider.HoleType.Threaded && hole.through;

            if (center.magnitude < bestDistance) {
                bestDistance = center.magnitude;
                primary = holeCollider;
            }
        }
        // A collar's set screw hole goes into its curved side, which the scanner (flat faces) doesn't see.
        if (isCollar && collarBore.size > 0f && FindSetScrewHole(fineVerts, fineTris, collarBore, out var screwCenter, out var screwAxis, out var screwDepth)) {
            var collider = Object.Instantiate(roundTemplate, part.transform);
            collider.name = "HoleCollider";
            collider.transform.localPosition = screwCenter * Inch;
            collider.transform.localRotation = Quaternion.LookRotation(screwAxis);
            collider.transform.localScale = new Vector3(SetScrewTap, SetScrewTap, screwDepth * Inch);
            var holeCollider = collider.GetComponent<HoleCollider>();
            holeCollider.holeType = HoleCollider.HoleType.Threaded;
            holeCollider.twoSided = false;
        }

        // The middle hole is the one inserts and shafts go through (gears, wheels, collars, spacers).
        if (family.group == PartType.PartGroup.Motion && primary != null && bestDistance < 0.1f) {
            data.primaryHole = primary;
            if (primary.holeType == HoleCollider.HoleType.Threaded)
                primary.holeType = HoleCollider.HoleType.Normal;  // a bore for a shaft, never a tapped hole
        }
        return part;
    }

    private const float SetScrewTap = 0.136f; // 8-32 tap size, inches
    private const float MinUsableHole = 0.10f; // inches

    private static Bounds BoundsOf(List<Vector3> points) {
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var point in points)
            bounds.Encapsulate(point);
        return bounds;
    }

    private static float DistanceToLine(Vector3 point, Vector3 linePoint, Vector3 direction) =>
        Vector3.Cross(point - linePoint, direction.normalized).magnitude;

    /// <summary>
    /// The open, threaded set screw hole in a collar's side: rays from the bore outwards all around (and along
    /// its height) that meet no metal on the way out. Metres, part space; the axis points inwards.
    /// </summary>
    private static bool FindSetScrewHole(List<Vector3> vertices, List<int> triangles, HoleScanner.Found bore,
                                         out Vector3 center, out Vector3 axis, out float depth) {
        center = axis = Vector3.zero;
        depth = 0f;
        var boreAxis = bore.axis.normalized;
        var boreCenter = bore.entry + boreAxis * (bore.depth / 2f);
        var u = Vector3.Cross(boreAxis, Mathf.Abs(boreAxis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        var v = Vector3.Cross(boreAxis, u);
        var start = bore.size / 2f + 0.0003f;
        const float reach = 0.02f;
        const int angles = 120;
        var heights = new List<float>();
        for (var h = -bore.depth / 2f + 0.0008f; h <= bore.depth / 2f - 0.0008f; h += 0.0005f)
            heights.Add(h);

        var misses = new List<(float angle, float height)>();
        var outer = 0f;
        var outerCount = 0;
        foreach (var height in heights) {
            for (var a = 0; a < angles; a++) {
                var angle = a * Mathf.PI * 2f / angles;
                var direction = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                var origin = boreCenter + boreAxis * height + direction * start;
                if (FarthestHit(vertices, triangles, origin, direction, reach, out var distance)) {
                    outer += start + distance;
                    outerCount++;
                }
                else
                    misses.Add((angle, height));
            }
        }
        // A real hole is a small patch of misses; a part with no wall there at all (open sides) misses a lot.
        if (misses.Count == 0 || misses.Count > angles * heights.Count / 4 || outerCount == 0)
            return false;

        // The biggest patch, by angle (around wrap-around) and height.
        var sx = 0f; var sy = 0f; var sh = 0f;
        foreach (var (angle, height) in misses) {
            sx += Mathf.Cos(angle);
            sy += Mathf.Sin(angle);
            sh += height;
        }
        var meanAngle = Mathf.Atan2(sy, sx);
        var meanHeight = sh / misses.Count;
        var outward = (u * Mathf.Cos(meanAngle) + v * Mathf.Sin(meanAngle)).normalized;
        var outerRadius = outer / outerCount;
        depth = Mathf.Max(outerRadius - bore.size / 2f, 0.001f);
        center = boreCenter + boreAxis * meanHeight + outward * (bore.size / 2f + depth / 2f);
        axis = -outward;
        return true;
    }

    // The farthest point where the ray passes a surface within maxDistance (Möller–Trumbore, both faces).
    private static bool FarthestHit(List<Vector3> vertices, List<int> triangles, Vector3 origin, Vector3 direction, float maxDistance, out float distance) {
        distance = 0f;
        var hit = false;
        for (var i = 0; i + 2 < triangles.Count; i += 3) {
            var a = vertices[triangles[i]];
            var e1 = vertices[triangles[i + 1]] - a;
            var e2 = vertices[triangles[i + 2]] - a;
            var p = Vector3.Cross(direction, e2);
            var det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f)
                continue;
            var inv = 1f / det;
            var t = origin - a;
            var b1 = Vector3.Dot(t, p) * inv;
            if (b1 < 0f || b1 > 1f)
                continue;
            var q = Vector3.Cross(t, e1);
            var b2 = Vector3.Dot(direction, q) * inv;
            if (b2 < 0f || b1 + b2 > 1f)
                continue;
            var d = Vector3.Dot(e2, q) * inv;
            if (d > 0f && d <= maxDistance && d > distance) {
                distance = d;
                hit = true;
            }
        }
        return hit;
    }

    private static string DisplayName(string path) {
        var naming = Type.GetType("Protobot.Editor.PartNaming, Protobot.Editor");
        var method = naming?.GetMethod("DisplayName");
        return method != null ? (string)method.Invoke(null, new object[] { path }) : path.Substring(path.LastIndexOf('/') + 1);
    }

    // ---------------------------------------------------------------- meshes

    private static List<Renderer[]> Lods(GameObject model) {
        var result = new List<Renderer[]>();
        if (model.TryGetComponent(out LODGroup group)) {
            foreach (var lod in group.GetLODs())
                if (lod.renderers.Length > 0)
                    result.Add(lod.renderers.Where(r => r != null).ToArray());
        }
        else
            result.Add(model.GetComponentsInChildren<MeshRenderer>(true));
        return result;
    }

    private static int TriangleCount(Renderer[] renderers) {
        var count = 0;
        foreach (var renderer in renderers)
            if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                for (var s = 0; s < filter.sharedMesh.subMeshCount; s++)
                    count += (int)filter.sharedMesh.GetIndexCount(s) / 3;
        return count;
    }

    private static void Gather(Renderer[] renderers, Transform root, float scale, out List<Vector3> vertices, out List<int> triangles, out List<Material> materials) {
        vertices = new List<Vector3>();
        triangles = new List<int>();
        materials = new List<Material>();
        foreach (var renderer in renderers) {
            if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                continue;
            var source = filter.sharedMesh;
            var matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            var start = vertices.Count;
            foreach (var v in source.vertices)
                vertices.Add(matrix.MultiplyPoint3x4(v) * scale);
            for (var s = 0; s < source.subMeshCount; s++)
                foreach (var index in source.GetTriangles(s))
                    triangles.Add(start + index);
            materials.AddRange(renderer.sharedMaterials);
        }
    }

    // One submesh per distinct material, inches.
    private static Mesh BakeMesh(Renderer[] renderers, Transform root, out List<Material> materials) {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var bySubmesh = new List<List<int>>();
        materials = new List<Material>();
        foreach (var renderer in renderers) {
            if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                continue;
            var source = filter.sharedMesh;
            var matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            var start = vertices.Count;
            var sourceNormals = source.normals;
            var sourceVertices = source.vertices;
            for (var i = 0; i < sourceVertices.Length; i++) {
                vertices.Add(matrix.MultiplyPoint3x4(sourceVertices[i]) * Inch);
                normals.Add(i < sourceNormals.Length ? matrix.MultiplyVector(sourceNormals[i]).normalized : Vector3.up);
            }
            var shared = renderer.sharedMaterials;
            for (var s = 0; s < source.subMeshCount; s++) {
                var material = s < shared.Length ? shared[s] : null;
                var slot = materials.IndexOf(material);
                if (slot < 0) {
                    materials.Add(material);
                    bySubmesh.Add(new List<int>());
                    slot = materials.Count - 1;
                }
                foreach (var index in source.GetTriangles(s))
                    bySubmesh[slot].Add(start + index);
            }
        }
        var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = bySubmesh.Count;
        for (var s = 0; s < bySubmesh.Count; s++)
            mesh.SetTriangles(bySubmesh[s], s);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float VolumeCm3(List<Vector3> vertices, List<int> triangles) {
        double volume = 0;
        for (var i = 0; i + 2 < triangles.Count; i += 3)
            volume += Vector3.Dot(vertices[triangles[i]], Vector3.Cross(vertices[triangles[i + 1]], vertices[triangles[i + 2]])) / 6.0;
        return Mathf.Abs((float)volume) * 1e6f;
    }

    private static float Density(string path, List<Material> materials) {
        var text = (path + " " + string.Join(" ", materials.Where(m => m != null).Select(m => m.name))).ToLowerInvariant();
        if (text.Contains("steel")) return 7.85f;
        if (text.Contains("alu")) return 2.7f;
        if (text.Contains("rubber") || text.Contains("flex")) return 1.1f;
        if (path.StartsWith("Electronics")) return 1.6f;
        return 1.2f;
    }

    // ---------------------------------------------------------------- look

    // The original's flat palette: aluminium and steel stock is its paintable "Metal Color", the rest goes to
    // the palette color nearest the model's own.
    private static Material MapMaterial(Material source, Family family) {
        var name = source != null ? source.name.ToLowerInvariant() : "";
        if (family.group == PartType.PartGroup.Structure && (name.Contains("alu") || name.Contains("steel")))
            return palette["Metal Color"];
        if (name.Contains("alu") || name.Contains("steel") || name.Contains("titanium") || name.Contains("polished"))
            return palette.TryGetValue("Silver Steel", out var steel) ? steel : palette["Metal Color"];
        var color = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source != null ? source.color : Color.gray;
        Material best = null;
        var bestDistance = float.MaxValue;
        foreach (var candidate in new[] { "Black Plastic", "Dark Gray Plastic", "Gray Plastic", "White Plastic", "Vex Red",
                     "Vex Blue", "Green Plastic", "Yellow Plastic", "Orange Plastic", "Silver Steel", "Gold Steel" }) {
            if (!palette.TryGetValue(candidate, out var material))
                continue;
            var c = material.GetColor("_BaseColor");
            var distance = new Vector3(c.r - color.r, c.g - color.g, c.b - color.b).sqrMagnitude;
            if (distance < bestDistance) {
                bestDistance = distance;
                best = material;
            }
        }
        return best != null ? best : palette["Metal Color"];
    }

    private static Dictionary<string, Sprite> originalIcons;

    // The original's white icons, picked by what the family is.
    private static Sprite IconFor(Family family) {
        if (originalIcons == null) {
            originalIcons = new Dictionary<string, Sprite>();
            foreach (var prefab in Resources.LoadAll<GameObject>("Part Prefabs")) {
                var type = prefab.GetComponent<PartType>();
                if (type != null && type.icon != null && !AssetDatabase.GetAssetPath(prefab).StartsWith(PrefabFolder))
                    originalIcons[Normalize(prefab.name)] = type.icon;
            }
        }
        var name = family.name.ToLowerInvariant();
        string pick =
            name.Contains("c-channel") || name.Contains("rail") ? "cchannel" :
            name.Contains("angle") ? "angle" :
            name.Contains("plate") ? "plate" :
            name.Contains("gusset") || name.Contains("bracket") || name.Contains("hinge") ? "gussets" :
            name.Contains("sprocket") || name.Contains("chain") ? "sprocket" :
            name.Contains("gear") || name.Contains("pulley") ? "gear" :
            name.Contains("wheel") ? "tractionwheel" :
            name.Contains("spacer") ? "spacer" :
            name.Contains("shaft") || name.Contains("insert") ? "shaftcollar" :
            name.Contains("standoff") || name.Contains("retainer") ? "standoff" :
            name.Contains("hardware") ? "screw" :
            name.Contains("pneumatic") ? "cylinder" :
            name.Contains("sensor") ? "sensor" :
            family.group == PartType.PartGroup.Electronics ? "motor" :
            "mechanisms";
        return originalIcons.TryGetValue(pick, out var icon) ? icon : originalIcons.Values.FirstOrDefault();
    }
}
