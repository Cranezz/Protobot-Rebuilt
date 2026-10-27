using System.Collections.Generic;
using System.IO;
using Protobot.Geometry;
using Protobot.Interaction;
using Protobot.Parts;
using UnityEditor;
using UnityEngine;

namespace Protobot.Editor
{
    /// <summary>
    /// Turns every catalog part into a physical, grabbable prefab and bakes its picker thumbnail:
    ///  - mass = mesh volume × density of its material (aluminium, steel, brass, rubber, plastic);
    ///  - one convex collider per body (from the simplest LOD) with a physics material for its finish;
    ///  - Rigidbody tuned for small parts, PartBody id and a grab HoverTarget.
    /// Also sets the project's physics up for centimetre-sized objects.
    /// </summary>
    public static class PartPrefabBuilder
    {
        public const int PartsLayer = 8;
        const string PrefabFolder = "Assets/Protobot/Parts/Prefabs/";
        const string PhysicsFolder = "Assets/Protobot/Parts/Physics/";
        public const string ThumbnailFolder = "Assets/Protobot/Parts/Thumbnails/";

        enum Finish { Aluminum, Steel, Brass, Rubber, Plastic }

        // kg/m³
        static readonly Dictionary<Finish, float> Density = new()
        {
            { Finish.Aluminum, 2700f }, { Finish.Steel, 7850f }, { Finish.Brass, 8500f },
            { Finish.Rubber, 1150f }, { Finish.Plastic, 1150f },
        };

        // dynamic friction, static friction, bounciness
        static readonly Dictionary<Finish, (float dyn, float stat, float bounce)> Surface = new()
        {
            { Finish.Aluminum, (0.42f, 0.55f, 0.18f) }, { Finish.Steel, (0.38f, 0.5f, 0.2f) }, { Finish.Brass, (0.38f, 0.5f, 0.18f) },
            { Finish.Rubber, (0.9f, 1.0f, 0.35f) }, { Finish.Plastic, (0.35f, 0.45f, 0.25f) },
        };

        [MenuItem("Tools/Protobot/Configure Physics For Small Parts")]
        public static void ConfigurePhysics()
        {
            Physics.defaultContactOffset = 0.002f;     // default 1 cm is bigger than a screw
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.bounceThreshold = 0.35f;           // slower impacts settle instead of jittering
            Physics.sleepThreshold = 0.0015f;
            Physics.defaultMaxAngularSpeed = 40f;
            Time.fixedDeltaTime = 1f / 100f;

            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            layers.GetArrayElementAtIndex(PartsLayer).stringValue = "Parts";
            tags.ApplyModifiedPropertiesWithoutUndo();
            Physics.IgnoreLayerCollision(2, PartsLayer, true);  // parts pass through the player's body
            AssetDatabase.SaveAssets();
        }

        public static void BuildAll(PartCatalog catalog, bool rebakeThumbnails)
        {
            ConfigurePhysics();
            var materials = PhysicsMaterials();
            Directory.CreateDirectory(PrefabFolder);
            try
            {
                for (var i = 0; i < catalog.parts.Count; i++)
                {
                    var entry = catalog.parts[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Building part prefabs", entry.id, i / (float)catalog.parts.Count))
                        break;
                    if (entry.model != null)
                        BuildPrefab(entry, materials);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            BakeThumbnails(catalog, rebakeThumbnails);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        static Dictionary<Finish, PhysicsMaterial> PhysicsMaterials()
        {
            Directory.CreateDirectory(PhysicsFolder);
            var result = new Dictionary<Finish, PhysicsMaterial>();
            foreach (var (finish, (dyn, stat, bounce)) in Surface)
            {
                var path = PhysicsFolder + finish + ".physicMaterial";
                var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
                if (mat == null)
                {
                    mat = new PhysicsMaterial(finish.ToString());
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.dynamicFriction = dyn;
                mat.staticFriction = stat;
                mat.bounciness = bounce;
                mat.frictionCombine = PhysicsMaterialCombine.Average;
                mat.bounceCombine = PhysicsMaterialCombine.Average;
                EditorUtility.SetDirty(mat);
                result[finish] = mat;
            }
            return result;
        }

        static void BuildPrefab(PartEntry entry, Dictionary<Finish, PhysicsMaterial> physicsMaterials)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.model);
            try
            {
                instance.name = entry.displayName;
                SetLayer(instance, PartsLayer);

                // Group renderers by body ("Solid3_LOD0" -> "Solid3").
                var bodies = new Dictionary<string, List<MeshRenderer>>();
                foreach (var r in instance.GetComponentsInChildren<MeshRenderer>())
                {
                    var name = r.name;
                    var lod = name.LastIndexOf("_LOD", System.StringComparison.Ordinal);
                    var key = lod >= 0 ? name.Substring(0, lod) : name;
                    if (!bodies.TryGetValue(key, out var list))
                        bodies[key] = list = new List<MeshRenderer>();
                    list.Add(r);
                }

                // Metal stock and shafts get colliders that follow their cross-section (open C-channels etc.).
                var useProfile = entry.cuttable && AddStockProfile(instance, bodies, physicsMaterials);

                var mass = 0f;
                foreach (var list in bodies.Values)
                {
                    list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    var detailed = list[0];               // _LOD0
                    var finish = FinishOf(detailed.sharedMaterials);
                    var detailedMesh = detailed.GetComponent<MeshFilter>().sharedMesh;
                    mass += Volume(detailedMesh, detailed.transform) * Density[finish];
                    if (useProfile)
                        continue;

                    // Collider from the simplest LOD that still has a real shape (tiny bodies can be
                    // decimated down to a few vertices, which can't form a convex hull).
                    var simple = detailed;
                    for (var i = list.Count - 1; i > 0; i--)
                    {
                        var m = list[i].GetComponent<MeshFilter>().sharedMesh;
                        if (m.vertexCount >= 16 && MinSize(m.bounds.size) > 0.0002f)
                        {
                            simple = list[i];
                            break;
                        }
                    }
                    var colliderMesh = simple.GetComponent<MeshFilter>().sharedMesh;
                    if (MinSize(colliderMesh.bounds.size) < 0.0002f && bodies.Count > 1)
                        continue;  // paper-thin detail (labels, decals): not worth a collider

                    var collider = simple.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = colliderMesh;
                    collider.convex = true;
                    collider.sharedMaterial = physicsMaterials[finish];
                }

                entry.massKg = Mathf.Max(0.0005f, RealisticMass(entry, mass));
                var body = instance.AddComponent<Rigidbody>();
                body.mass = entry.massKg;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearDamping = 0.05f;
                body.angularDamping = 0.15f;

                instance.AddComponent<PartBody>().Configure(entry.id);
                // How to pick it up is on the hands' controls line; the tooltip just describes the part.
                instance.AddComponent<HoverTarget>().Configure(CursorKind.Grab, entry.displayName, $"{entry.SizeLabel}  ·  {entry.MassLabel}");

                var path = PrefabFolder + entry.id + ".prefab";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                entry.prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------------------------------------------------------------- stock profile

        /// <summary>
        /// Finds the part's cross-section (sampled along its length, keeping the slice that misses the holes),
        /// splits it into convex pieces and stores it in a StockProfile, which builds the colliders at runtime.
        /// </summary>
        static bool AddStockProfile(GameObject root, Dictionary<string, List<MeshRenderer>> bodies, Dictionary<Finish, PhysicsMaterial> materials)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Material[] firstMaterials = null;
            var toRoot = root.transform.worldToLocalMatrix;
            foreach (var list in bodies.Values)
            {
                var r = list[0];
                firstMaterials ??= r.sharedMaterials;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var m = toRoot * r.transform.localToWorldMatrix;
                var start = verts.Count;
                foreach (var v in mesh.vertices)
                    verts.Add(m.MultiplyPoint3x4(v));
                for (var s = 0; s < mesh.subMeshCount; s++)
                    foreach (var i in mesh.GetTriangles(s))
                        tris.Add(start + i);
            }
            if (verts.Count == 0)
                return false;

            var bounds = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts)
                bounds.Encapsulate(v);
            var size = bounds.size;
            var axisIndex = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var axis = Vector3.zero;
            axis[axisIndex] = 1f;
            var u = Vector3.zero;
            u[(axisIndex + 1) % 3] = 1f;
            var w = Vector3.Cross(axis, u);
            var half = size[axisIndex] / 2f;

            List<MeshSlicer.Loop> best = null;
            var bestArea = 0f;
            for (var k = 0; k <= 16; k++)
            {
                var t = Mathf.Lerp(-0.4f, 0.4f, k / 16f) * size[axisIndex];
                var loops = MeshSlicer.Section(verts, tris, new Plane(axis, bounds.center + axis * t));
                var area = 0f;
                foreach (var (outer, holes) in MeshSlicer.Nest(loops))
                {
                    area += Mathf.Abs(outer.signedArea);
                    foreach (var h in holes)
                        area -= Mathf.Abs(h.signedArea);
                }
                if (area > bestArea)
                {
                    bestArea = area;
                    best = loops;
                }
            }
            if (best == null)
                return false;

            // Express the outline in the profile's own u/w axes around the part's centre line, with rounded
            // bends straightened out (a tenth of a millimetre is plenty for collisions).
            foreach (var loop in best)
            {
                loop.points2D.Clear();
                foreach (var p in loop.points3D)
                    loop.points2D.Add(new Vector2(Vector3.Dot(p - bounds.center, u), Vector3.Dot(p - bounds.center, w)));
                MeshSlicer.Simplify(loop, 0.0001f);
            }
            var (points, _, triangles) = MeshSlicer.Triangulate(best);
            if (triangles.Count < 3)
                return false;
            var pieces = ConvexPieces.FromTriangles(points, triangles);
            if (pieces.Count == 0 || pieces.Count > 48)
                return false;

            var profile = root.AddComponent<StockProfile>();
            profile.Configure(axis, u, w, bounds.center, -half, half, pieces, materials[FinishOf(firstMaterials)]);
            return true;
        }

        /// <summary>
        /// Electronics are modelled as hollow shells (no circuit boards or motor inside), so their mesh volume
        /// gives a far too low weight. Use the known weight where we have it, otherwise a typical density for
        /// packed electronics applied to the outer size.
        /// </summary>
        static float RealisticMass(PartEntry entry, float meshMass)
        {
            if (entry.displayName == "V5 Motor")
                return 0.155f;  // V5 Smart Motor (11 W), VEX spec
            if (entry.binId is "motors" or "brain" or "sensors")
            {
                var s = entry.size;
                return Mathf.Max(meshMass, s.x * s.y * s.z * 650f);
            }
            return meshMass;
        }

        /// <summary>Enclosed volume of a closed mesh (m³), via signed tetrahedra; falls back to a box estimate for open meshes.</summary>
        static float Volume(Mesh mesh, Transform t)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var m = t.localToWorldMatrix;
            double volume = 0;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = m.MultiplyPoint3x4(vertices[triangles[i]]);
                var b = m.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                var c = m.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }
            var v = Mathf.Abs((float)volume);
            var box = mesh.bounds.size;
            var boxVolume = box.x * box.y * box.z;
            return v > 1e-10f && v <= boxVolume * 1.01f ? v : boxVolume * 0.3f;
        }

        static float MinSize(Vector3 v) => Mathf.Min(v.x, Mathf.Min(v.y, v.z));

        static Finish FinishOf(Material[] materials)
        {
            foreach (var mat in materials)
            {
                if (mat == null)
                    continue;
                var n = mat.name.ToLowerInvariant();
                if (n.Contains("rubber") || n.Contains("flex_wheel") || n.Contains("tire") || n.Contains("tread")) return Finish.Rubber;
                if (n.Contains("brass") || n.Contains("bronze") || n.Contains("copper")) return Finish.Brass;
                if (n.Contains("aluminum") || n.Contains("aluminium") || n == "alu" || n.StartsWith("alu_") || n.Contains("_alu")
                    || n.Contains("anodized") || n.Contains("machined") || n.Contains("standoff")) return Finish.Aluminum;
                if (n.Contains("steel") || n.Contains("stainless") || n.Contains("chrome") || n.Contains("zinc") || n.Contains("titanium")
                    || n.Contains("screw") || n.Contains("vex_metal")) return Finish.Steel;
            }
            return Finish.Plastic;
        }

        static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        // ---------------------------------------------------------------- thumbnails

        /// <summary>Renders each part from a three-quarter view onto the card colour (only missing ones unless forced).</summary>
        public static void BakeThumbnails(PartCatalog catalog, bool force)
        {
            var preview = new PreviewRenderUtility();
            var written = new List<(PartEntry entry, string path)>();
            try
            {
                var cam = preview.camera;
                cam.orthographic = true;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.93f, 0.91f, 0.86f, 1f);
                preview.lights[0].intensity = 1.25f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                preview.lights[1].intensity = 0.7f;
                preview.lights[1].transform.rotation = Quaternion.Euler(330f, 160f, 0f);
                preview.ambientColor = new Color(0.42f, 0.42f, 0.45f);

                for (var i = 0; i < catalog.parts.Count; i++)
                {
                    var entry = catalog.parts[i];
                    var path = ThumbnailFolder + entry.id + ".png";
                    if (entry.model == null || (!force && File.Exists(path)))
                    {
                        written.Add((entry, path));
                        continue;
                    }
                    if (EditorUtility.DisplayCancelableProgressBar("Baking part thumbnails", entry.id, i / (float)catalog.parts.Count))
                        break;

                    var go = preview.InstantiatePrefabInScene(entry.model);
                    Frame(cam, go);
                    preview.BeginStaticPreview(new Rect(0, 0, 256, 256));
                    preview.Render(true);
                    var tex = preview.EndStaticPreview();
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    Object.DestroyImmediate(go);
                    written.Add((entry, path));
                }
            }
            finally
            {
                preview.Cleanup();
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            foreach (var (entry, path) in written)
                entry.thumbnail = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void Frame(Camera cam, GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            var direction = new Vector3(1f, 0.85f, -1.15f).normalized;
            cam.transform.position = bounds.center + direction * (bounds.extents.magnitude * 3f + 0.1f);
            cam.transform.LookAt(bounds.center);

            // Orthographic size that fits all 8 corners of the bounds, with a small margin.
            var extent = 0f;
            for (var i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var local = cam.transform.InverseTransformPoint(corner);
                extent = Mathf.Max(extent, Mathf.Abs(local.x), Mathf.Abs(local.y));
            }
            cam.orthographicSize = extent * 1.12f;
        }
    }
}
