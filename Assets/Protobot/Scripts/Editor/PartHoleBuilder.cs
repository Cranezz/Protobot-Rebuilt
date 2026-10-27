using System.Collections.Generic;
using Protobot.Geometry;
using Protobot.Parts;
using UnityEditor;
using UnityEngine;

namespace Protobot.Editor
{
    /// <summary>
    /// Finds every part's holes from its model (<see cref="HoleScanner"/>), decides what each hole does from the
    /// part's role (a nut's hole is threaded, a gear's square bore locks onto a shaft...), and measures screws
    /// and nuts. The results are stored on the part prefabs (<see cref="PartHoles"/>, <see cref="Fastener"/>).
    /// Run after Build Part Library (which calls it too).
    /// </summary>
    public static class PartHoleBuilder
    {
        const string PrefabRoot = "Assets/Protobot/Parts/Prefabs";

        [MenuItem("Tools/Protobot/Scan Part Holes")]
        public static void ScanAll()
        {
            var paths = PrefabPaths();
            var holes = 0;
            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Scanning part holes", paths[i], i / (float)paths.Count))
                        break;
                    holes += ScanPrefab(paths[i]);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[Parts] Found {holes} holes on {paths.Count} parts.");
        }

        const string StatusFile = "Temp/PartHoleScan.txt";
        static List<string> pending;
        static int pendingIndex, pendingHoles;

        /// <summary>
        /// Scans everything a few parts per editor frame, so a script that starts it isn't blocked for minutes.
        /// Progress goes to Temp/PartHoleScan.txt ("done" on the last line when finished).
        /// </summary>
        public static void ScanAllInBackground()
        {
            pending = PrefabPaths();
            pendingIndex = pendingHoles = 0;
            EditorApplication.update -= ScanStep;
            EditorApplication.update += ScanStep;
        }

        static void ScanStep()
        {
            var until = EditorApplication.timeSinceStartup + 0.5;
            while (pendingIndex < pending.Count && EditorApplication.timeSinceStartup < until)
                pendingHoles += ScanPrefab(pending[pendingIndex++]);
            var finished = pendingIndex >= pending.Count;
            System.IO.File.WriteAllText(StatusFile, $"{pendingIndex}/{pending.Count} parts, {pendingHoles} holes\n{(finished ? "done" : "")}");
            if (!finished)
                return;
            EditorApplication.update -= ScanStep;
            AssetDatabase.SaveAssets();
            Debug.Log($"[Parts] Found {pendingHoles} holes on {pending.Count} parts.");
        }

        /// <summary>Scans part of the list (for scripted runs that must stay short). Returns the holes found.</summary>
        public static int ScanRange(int start, int count)
        {
            var paths = PrefabPaths();
            var holes = 0;
            for (var i = start; i < Mathf.Min(paths.Count, start + count); i++)
                holes += ScanPrefab(paths[i]);
            AssetDatabase.SaveAssets();
            return holes;
        }

        public static List<string> PrefabPaths()
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("/Game Objects/"))  // field elements, not robot parts
                    paths.Add(path);
            }
            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }

        /// <summary>Scans one part prefab and stores its holes (or, for a screw, its measurements).</summary>
        public static int ScanPrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var body = root.GetComponent<PartBody>();
                if (body == null)
                    return 0;
                var role = PartRoles.Of(body.PartId);
                Gather(root, out var vertices, out var indices);
                var count = 0;
                if (PartRoles.IsScrew(role))
                {
                    RemoveIfPresent<PartHoles>(root);
                    if (MeasureScrew(vertices, out var seat, out var axis, out var length, out var diameter, out var head))
                        GetOrAdd<Fastener>(root).Configure(role, seat, axis, length, diameter, head);
                }
                else
                {
                    var holes = new List<Hole>();
                    foreach (var found in HoleScanner.Scan(vertices, indices))
                    {
                        holes.Add(new Hole
                        {
                            center = found.entry + found.axis * (found.depth / 2f), axis = found.axis, size = found.size,
                            depth = found.depth, square = found.square, through = found.through, kind = KindOf(role, found),
                        });
                    }
                    GetOrAdd<PartHoles>(root).Set(holes);
                    count = holes.Count;

                    if (role == PartRole.Nut && TryNutHole(holes, out var hole))
                        GetOrAdd<Fastener>(root).Configure(role, hole.Entry, hole.axis, hole.depth, hole.size, 0f);
                    else
                        RemoveIfPresent<Fastener>(root);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static HoleKind KindOf(PartRole role, HoleScanner.Found hole)
        {
            switch (role)
            {
                case PartRole.Nut:
                case PartRole.Standoff:
                    return HoleKind.Threaded;
                case PartRole.Motor:
                    return hole.square ? HoleKind.ShaftLock : HoleKind.Threaded;
                case PartRole.Collar:
                    return HoleKind.ShaftLock;  // the set screw locks it whatever the bore's shape
                case PartRole.Gear:
                case PartRole.Wheel:
                    return hole.square ? HoleKind.ShaftLock : HoleKind.Clearance;
                case PartRole.Bearing:
                    return hole.square ? HoleKind.Clearance : HoleKind.Bearing;
                default:
                    return HoleKind.Clearance;
            }
        }

        /// <summary>A nut's threaded hole: the through hole nearest the middle.</summary>
        static bool TryNutHole(List<Hole> holes, out Hole nut)
        {
            nut = default;
            var best = float.MaxValue;
            foreach (var hole in holes)
            {
                if (!hole.through || hole.center.magnitude >= best)
                    continue;
                best = hole.center.magnitude;
                nut = hole;
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// A screw lies along its longest side; the wider end is the head. The seat is the head's underside
        /// (where the head stops being wider than the thread) and the length runs from there to the tip.
        /// </summary>
        static bool MeasureScrew(List<Vector3> vertices, out Vector3 seat, out Vector3 axis, out float length, out float diameter, out float headDiameter)
        {
            seat = axis = Vector3.zero;
            length = diameter = headDiameter = 0f;
            if (vertices.Count == 0)
                return false;
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var v in vertices)
                bounds.Encapsulate(v);
            var k = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0 : bounds.size.y >= bounds.size.z ? 1 : 2;
            var centre = bounds.center;
            float Radial(Vector3 v)
            {
                var d = v - centre;
                d[k] = 0f;
                return d.magnitude;
            }
            float lowR = 0f, highR = 0f;
            foreach (var v in vertices)
            {
                if (v[k] - bounds.min[k] < 0.001f)
                    lowR = Mathf.Max(lowR, Radial(v));
                if (bounds.max[k] - v[k] < 0.001f)
                    highR = Mathf.Max(highR, Radial(v));
            }
            var headAtLow = lowR >= highR;
            axis = Vector3.zero;
            axis[k] = headAtLow ? 1f : -1f;
            var headEnd = headAtLow ? bounds.min[k] : bounds.max[k];
            var tipEnd = headAtLow ? bounds.max[k] : bounds.min[k];

            float shank = 0f, head = 0f;
            foreach (var v in vertices)
            {
                if (Mathf.Abs(v[k] - tipEnd) < 0.002f)
                    shank = Mathf.Max(shank, Radial(v));
                head = Mathf.Max(head, Radial(v));
            }
            // The head: everything clearly wider than the thread and close to the head's own width.
            var threshold = Mathf.Max(shank * 1.3f, head * 0.8f);
            var headHeight = 0f;
            foreach (var v in vertices)
                if (Radial(v) > threshold)
                    headHeight = Mathf.Max(headHeight, Mathf.Abs(v[k] - headEnd));

            seat = centre;
            seat[k] = headEnd + axis[k] * headHeight;
            length = Mathf.Abs(tipEnd - seat[k]);
            diameter = shank * 2f;
            headDiameter = head * 2f;
            return length > 0f;
        }

        static void Gather(GameObject root, out List<Vector3> vertices, out List<int> indices)
        {
            vertices = new List<Vector3>();
            indices = new List<int>();
            foreach (var r in PartShape.DetailRenderers(root))
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var toPart = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var start = vertices.Count;
                foreach (var v in mesh.vertices)
                    vertices.Add(toPart.MultiplyPoint3x4(v));
                foreach (var t in mesh.triangles)
                    indices.Add(start + t);
            }
        }

        static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        static void RemoveIfPresent<T>(GameObject go) where T : Component
        {
            if (go.TryGetComponent(out T existing))
                Object.DestroyImmediate(existing, true);
        }
    }
}
