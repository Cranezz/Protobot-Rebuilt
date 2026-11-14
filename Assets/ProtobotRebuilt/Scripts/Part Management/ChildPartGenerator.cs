using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Protobot {
    /// <summary>
    /// A type of Part Generator that involves a Part Type where all children's PartData can directly be considered a part on their own
    /// </summary>
    public class ChildPartGenerator : PartGenerator {
        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var child = GetChildObj();
            GameObject newObj = Instantiate(child, position, rotation);
            RemoveDataScripts(newObj);
            // V2: models fixed at runtime (a turned head, a shoulder screw facing the app's screws' way).
            if (newObj.TryGetComponent(out MeshFilter filter) && Fixed(child.name, filter.sharedMesh) is Mesh fixedMesh) {
                filter.sharedMesh = fixedMesh;
                if (newObj.TryGetComponent(out MeshCollider meshCollider)) meshCollider.sharedMesh = fixedMesh;
                // V2: the model's holes are turned and moved with it (a V5 fitting's tube socket).
                if (fixedMoves.TryGetValue(fixedMesh, out var move))
                    foreach (Transform hole in newObj.transform)
                        if (hole.GetComponent<HoleCollider>() != null) {
                            hole.localPosition = move.turn * hole.localPosition + move.shift;
                            hole.localRotation = move.turn * hole.localRotation;
                        }
            }
            // V2: VEX shoulder screws are screws: they fasten what they go through into a nut or a threaded hole;
            // couplers and the rubber link are threaded studs, fastening like a screw along all their length.
            var stud = IsStud(GetId(), child.name);
            // V2: a pneumatic fitting threads into a port like a screw: its thread is the shank, the rest the head.
            if (IsFitting(GetId(), child.name) && newObj.GetComponent<Screw>() == null) {
                var mesh = newObj.GetComponent<MeshFilter>().sharedMesh;
                if (fittingThreads.TryGetValue(mesh, out var thread)) Screw.Lengths[mesh] = thread;
                newObj.tag = "Screw";
                newObj.AddComponent<Screw>();
            }
            // V2: a shoulder screw's head is its own material (RoboSource colors it by length: PartColors).
            if (IsShoulderScrew(child.name) && newObj.TryGetComponent(out MeshRenderer headRenderer) && newObj.TryGetComponent(out MeshFilter headFilter)
                && headFilter.sharedMesh != null && headFilter.sharedMesh.subMeshCount == 2 && headRenderer.sharedMaterials.Length == 1)
                headRenderer.sharedMaterials = new[] { headRenderer.sharedMaterial, headRenderer.sharedMaterial };
            if ((IsShoulderScrew(child.name) || stud) && newObj.GetComponent<Screw>() == null) {
                var mesh = newObj.GetComponent<MeshFilter>().sharedMesh;
                if (stud) Screw.Studs.Add(mesh);
                else Screw.Shoulders[mesh] = ShoulderLength(child.name);
                newObj.tag = "Screw";
                newObj.AddComponent<Screw>();
            }
            SetId(newObj);

            return newObj;
        }

        public override Mesh GetMesh() {
            var child = GetChildObj();
            var mesh = child.GetComponent<MeshFilter>().sharedMesh;
            return Fixed(child.name, mesh) ?? mesh;  // V2
        }

        /// <summary>V2: a VEX-CAD pneumatic fitting that threads into a part (cylinder, valve or reservoir port).</summary>
        public static bool IsFitting(string id, string name) => id != null && id.StartsWith("VXPNEUMATICS") && IsFittingName(name);

        // V2: the legacy "Fitting for…" parts, V5's straight, elbow and flow valve fittings (an M5 stud on top) and the
        // 4mm plug (its tube pushes into a fitting's socket).
        private static bool IsFittingName(string name) => name != null && (name.StartsWith("Fitting for") || name == "Straight Pneumatic Fitting"
            || name == "Elbow Fitting" || name == "Air Flow Valve Fitting" || name == PlugName);

        public const string PlugName = "Pneumatic Fitting Plug";

        /// <summary>V2: the legacy fittings (their own ports), not V5's: V5's keep their tube sockets.</summary>
        public static bool IsLegacyFitting(string name) => name != null && name.StartsWith("Fitting for");

        private static readonly Dictionary<Mesh, (Quaternion turn, Vector3 shift)> fixedMoves = new();

        /// <summary>V2: a fitting's (fixed) model -> its thread's length and diameter.</summary>
        public static readonly Dictionary<Mesh, float> fittingThreads = new();
        public static readonly Dictionary<Mesh, float> fittingDiameters = new();

        /// <summary>V2: a pneumatic fitting or plug (threads or pushes into a port).</summary>
        public static bool IsFittingPart(GameObject part) =>
            part != null && part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && fittingDiameters.ContainsKey(filter.sharedMesh);

        /// <summary>V2: a VEX-CAD threaded stud: a coupler (plain, hex, star) or the straight rubber link.</summary>
        public static bool IsStud(string id, string name) => id != null && id.StartsWith("VXHARDWARE") && (name.Contains("Coupler") || name == "Rubber Link");

        /// <summary>V2: a VEX-CAD shoulder screw (not the "threads shown" copy).</summary>
        public static bool IsShoulderScrew(string name) => name.Contains("Shoulder Screw") && !name.Contains("threads shown");

        /// <summary>V2: a shoulder screw's shoulder length, from its name ("1/2" Shoulder Screw (0.18" shoulder)").</summary>
        public static float ShoulderLength(string name) {
            var match = System.Text.RegularExpressions.Regex.Match(name ?? "", @"\(([\d.]+)"" shoulder\)");
            return match.Success && float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var length) ? length : 0f;
        }

        // V2: one shared, fixed copy per model (made once):
        // - the original's 1/2" screw has its hex head turned 30° from every other screw's;
        // - VEX's shoulder screws are modelled head at -z, tip at the origin; the app's screws have the head's underside
        //   at the origin and the shank along -z (their hole detector, length and placing rely on it): turned round.
        private static readonly Dictionary<string, float> turnedModels = new() { { "SCRW 0.5in", 30f } };
        private static readonly Dictionary<Mesh, Mesh> fixedCopies = new();
        private const float HeadHeight = 0.087f;   // as Screw.ScrewLength takes it

        private static Mesh Fixed(string name, Mesh mesh) {
            if (mesh == null || !mesh.isReadable || fixedCopies.ContainsValue(mesh)) return null;
            var shoulder = IsShoulderScrew(name);
            var link = name == "Rubber Link";
            var plug = name == PlugName;
            var fitting = IsFittingName(name) && !plug && mesh.name.Length > 0;
            if (!shoulder && !link && !fitting && !plug && !turnedModels.ContainsKey(mesh.name)) return null;
            if (fixedCopies.TryGetValue(mesh, out var copy) && copy != null) return copy;
            copy = Instantiate(mesh);
            copy.name = mesh.name;
            var turn = shoulder || fitting || plug ? Quaternion.AngleAxis(180f, Vector3.right) : link ? Quaternion.identity : Quaternion.AngleAxis(turnedModels[mesh.name], Vector3.forward);
            var threadLength = 0f;
            var threadRadius = 0f;
            if (fitting) {
                // VEX's fittings: the tube end at z = 0, the thread at the top (+z). The thread's radius: the widest
                // point within 0.03" of the top; it starts above the last point wider than it (the hex).
                var top = mesh.bounds.max.z;
                foreach (var v in mesh.vertices)
                    if (top - v.z < 0.03f) threadRadius = Mathf.Max(threadRadius, new Vector2(v.x, v.y).magnitude);
                var start = mesh.bounds.min.z;
                foreach (var v in mesh.vertices)
                    if (new Vector2(v.x, v.y).magnitude > threadRadius + 0.02f) start = Mathf.Max(start, v.z);
                threadLength = top - start;
            }
            var shift = shoulder ? new Vector3(0f, 0f, mesh.bounds.min.z + HeadHeight) : Vector3.zero;   // -z top -> +z, then underside to 0
            if (fitting) shift = new Vector3(0f, 0f, mesh.bounds.max.z - threadLength);   // thread along -z, body +z
            if (link) {   // the rubber link: one stud's tip at the origin, the rest along -z (as the couplers are)
                shift = new Vector3(0f, 0f, -mesh.bounds.max.z);
            }
            if (plug) {
                // VEX's plug: its handle at -z, its 4mm tube up to +z. Turned round, the tube runs from the origin
                // along -z (as a screw's shank), the handle above.
                var tubeStart = float.MaxValue;
                foreach (var v in mesh.vertices) {
                    var r = new Vector2(v.x, v.y).magnitude;
                    if (r > 0.07f && r < 0.085f) tubeStart = Mathf.Min(tubeStart, v.z);
                }
                if (tubeStart == float.MaxValue) tubeStart = mesh.bounds.center.z;
                threadLength = mesh.bounds.max.z - tubeStart;
                threadRadius = 0.157f / 2f;
                shift = new Vector3(0f, 0f, tubeStart);
            }
            var vertices = copy.vertices;
            var normals = copy.normals;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = turn * vertices[i] + shift;
            for (int i = 0; i < normals.Length; i++) normals[i] = turn * normals[i];
            copy.vertices = vertices;
            copy.normals = normals;
            if (shoulder && copy.subMeshCount == 1) {
                // V2: the head (above its underside, now z = 0) as a second sub-mesh, so it can be colored.
                var triangles = copy.triangles;
                var shank = new List<int>();
                var head = new List<int>();
                for (var t = 0; t + 2 < triangles.Length; t += 3) {
                    var z = (vertices[triangles[t]].z + vertices[triangles[t + 1]].z + vertices[triangles[t + 2]].z) / 3f;
                    (z > -0.0005f ? head : shank).AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                }
                if (head.Count > 0 && shank.Count > 0) {
                    copy.subMeshCount = 2;
                    copy.SetTriangles(shank, 0);
                    copy.SetTriangles(head, 1);
                }
            }
            copy.RecalculateTangents();
            copy.RecalculateBounds();
            fixedCopies[mesh] = copy;
            fixedMoves[copy] = (turn, shift);
            if (fitting || plug) {
                fittingThreads[copy] = threadLength;
                fittingDiameters[copy] = threadRadius * 2f;
            }
            return copy;
        }

        /// <summary>V2: a shoulder screw's model as parts use it (its head a second sub-mesh: the parts list's
        /// pictures show the head's color too), with the materials it needs; anything else as it is.</summary>
        public static (Mesh, Material[]) ShoulderShown(string name, Mesh mesh, Material[] materials) {
            if (!IsShoulderScrew(name) || !(Fixed(name, mesh) is Mesh shown)) return (mesh, materials);
            if (shown.subMeshCount == 2 && materials != null && materials.Length == 1) materials = new[] { materials[0], materials[0] };
            return (shown, materials);
        }

        private GameObject GetChildObj() {
            var childParts = GetComponentsInChildren<PartData>();

            foreach (PartData partData in childParts) {
                if (partData.param1Value != param1.value) continue;
                
                if (!UsesTwoParams || (UsesTwoParams && partData.param2Value == param2.value))
                    return partData.gameObject;
            }

            throw new IndexOutOfRangeException("No matching parameters found in children for " + gameObject.name);
        }
        
        public override bool TryGetPartData(out PartData partData) {
            partData = GetChildObj().GetComponent<PartData>();
            return true;
        }

        public override List<string> GetParam1Options() {
            var uniqueOptions = new HashSet<string>();
            var childParts = GetComponentsInChildren<PartData>();

            foreach (var partData in childParts) {
                uniqueOptions.Add(partData.param1Value);
            }

            return uniqueOptions.ToList();
        }

        public override List<string> GetParam2Options() {
            var uniqueOptions = new HashSet<string>();
            var childParts = GetComponentsInChildren<PartData>();

            foreach (var partData in childParts) {
                if (partData.param1Value == param1.value)
                    uniqueOptions.Add(partData.param2Value);
            }

            return uniqueOptions.ToList();
        }
    }
}