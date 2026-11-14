using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    // The inserts in a high strength gear's or wheel's middle hole, shown for what goes through it.
    // V2: a silver square insert on a 1/8" (normal) shaft, VEX's bronze round-bore adapter on a screw (a screw joint: it spins),
    // nothing on a high strength shaft. They sit flush in the hole (they used to stand 0.05" out of each face, and
    // placing on a shaft left that gap); the plastic inserts are gone. VEX-CAD wheels, gears and sprockets get one
    // too (Dynamics.Attach adds it to their middle hole), with the original's metal insert.
    [RequireComponent(typeof(HoleCollider))]
    public class HoleInsert : MonoBehaviour {
        private HoleCollider holeCol;

        [SerializeField] private GameObject metalInsertPrefab;
        [SerializeField] private GameObject plasticInsertPrefab;   // V2: not used

        private GameObject metalInserts;
        private GameObject brassInserts;

        private static Material brass;

        private static GameObject defaultMetal;

        // The original's metal insert, from a part type that has one (for inserts added to VEX-CAD parts).
        private static GameObject DefaultMetal() {
            if (defaultMetal != null || PartsManager.partTypes == null) return defaultMetal;
            foreach (var type in PartsManager.partTypes) {
                if (type == null) continue;
                var insert = type.GetComponentInChildren<HoleInsert>(true);
                if (insert != null && insert.metalInsertPrefab != null) {
                    defaultMetal = insert.metalInsertPrefab;
                    break;
                }
            }
            return defaultMetal;
        }

        private void Start() {
            holeCol = GetComponent<HoleCollider>();
            if (metalInsertPrefab == null) metalInsertPrefab = DefaultMetal();
            if (metalInsertPrefab == null) {
                enabled = false;
                return;
            }
            metalInserts = InstantiateInserts(metalInsertPrefab, "Metal Inserts");
            brassInserts = BronzeAdapter();

            holeCol.OnSetDetector += Show;
            // A screw or shaft can be through it already (it went on before this started).
            Show(holeCol.detectors != null && holeCol.detectors.Count > 0 ? holeCol.detectors[0] : null);
        }

        private void Show(HoleDetector detector) {
            if (detector == null && holeCol.detectors != null && holeCol.detectors.Count > 0) detector = holeCol.detectors[0];
            var through = detector != null ? detector.transform.parent : null;
            var gone = Gone;
            metalInserts.SetActive(!gone && through != null && through.name.Contains("Normal Shaft"));
            if (brassInserts != null) brassInserts.SetActive(!gone && through != null && through.GetComponent<Screw>() != null);
            shownGone = gone;
        }

        // V2: the inserts go with their part: deleted, they hide (and come back with an undo).
        private bool shownGone;
        private bool Gone => holeCol != null && holeCol.holeData != null && holeCol.holeData.part != null && holeCol.holeData.part.IsDeleted();

        // The inserts are drawn as part of the part: on its layer (hidden with it while it's dragged or deleted).
        private int shownLayer = -1;

        private void LateUpdate() {
            if (metalInserts == null) return;
            if (Gone != shownGone) Show(null);
            var part = holeCol.holeData != null ? holeCol.holeData.part : null;
            if (part == null || part.layer == shownLayer) return;
            shownLayer = part.layer;
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child != transform && child.GetComponent<HoleCollider>() == null) child.gameObject.layer = shownLayer;
        }

        // VEX's #8 round bore adapter (276-8034, "High Strength Shaft Adapter (#8 Round Bore, 1/2" Long)"): sintered
        // bronze with a round bore an 8-32 screw turns in (0.165"), 1/2" long, or the hole's depth when that's shorter
        // (flush: it adds no space). One piece through the middle, its real 1/4" square outside.
        private const float AdapterSide = 0.245f, AdapterBore = 0.165f, AdapterLength = 0.5f;
        private static readonly Dictionary<int, Mesh> adapters = new();

        private static Mesh Adapter(float length) {
            var key = Mathf.RoundToInt(length * 1000f);
            if (adapters.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            const int Sides = 32;
            var half = AdapterSide / 2f;
            var radius = AdapterBore / 2f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) {
                var i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });   // clockwise seen from outside (Unity's front)
            }
            Vector2 Outer(float angle) {
                var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                return d * (half / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)));
            }
            Vector2 Inner(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            var z = length / 2f;
            for (var k = 0; k < Sides; k++) {
                var a0 = k * Mathf.PI * 2f / Sides;
                var a1 = (k + 1) * Mathf.PI * 2f / Sides;
                Vector2 o0 = Outer(a0), o1 = Outer(a1), i0 = Inner(a0), i1 = Inner(a1);
                Quad(new Vector3(o0.x, o0.y, -z), new Vector3(o0.x, o0.y, z), new Vector3(o1.x, o1.y, z), new Vector3(o1.x, o1.y, -z));   // outside
                Quad(new Vector3(i1.x, i1.y, -z), new Vector3(i1.x, i1.y, z), new Vector3(i0.x, i0.y, z), new Vector3(i0.x, i0.y, -z));   // bore
                Quad(new Vector3(i0.x, i0.y, z), new Vector3(i1.x, i1.y, z), new Vector3(o1.x, o1.y, z), new Vector3(o0.x, o0.y, z));     // ends
                Quad(new Vector3(o0.x, o0.y, -z), new Vector3(o1.x, o1.y, -z), new Vector3(i1.x, i1.y, -z), new Vector3(i0.x, i0.y, -z));
            }
            mesh = new Mesh { name = "Bronze Adapter 276-8034" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            adapters[key] = mesh;
            return mesh;
        }

        // The adapter in the hole, centred, as long as the hole is deep (at most 1/2").
        private GameObject BronzeAdapter() {
            if (metalInsertPrefab == null) return null;
            if (brass == null) {
                var silver = metalInsertPrefab.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                brass = new Material(silver) { name = "Sintered Bronze" };
                var color = new Color(0.44f, 0.32f, 0.17f);   // the app shows a color about twice its value
                brass.color = color;
                if (brass.HasProperty("_BaseColor")) brass.SetColor("_BaseColor", color);
                if (brass.HasProperty("_Smoothness")) brass.SetFloat("_Smoothness", 0.35f);
                // Sintered (porous, matte): mostly its own color, not mirror-dark like the steel it's copied from.
                if (brass.HasProperty("_Metallic")) brass.SetFloat("_Metallic", 0.25f);
            }
            var inserts = new GameObject("Brass Inserts");
            inserts.transform.SetParent(transform, false);
            var piece = new GameObject("Bronze Adapter");
            piece.transform.SetParent(inserts.transform, false);
            // Square to the hole (its square sides along the hole's), and full size whatever the hole's scale.
            var scale = transform.lossyScale;
            piece.transform.localScale = new Vector3(1f / Mathf.Max(scale.x, 1e-4f), 1f / Mathf.Max(scale.y, 1e-4f), 1f / Mathf.Max(scale.z, 1e-4f));
            // A hair short of each face, so its ends don't flicker against the hub's faces.
            var length = Mathf.Min(AdapterLength, holeCol.holeData.depth > 0f ? holeCol.holeData.depth : AdapterLength) - 0.01f;
            piece.AddComponent<MeshFilter>().sharedMesh = Adapter(length);
            piece.AddComponent<MeshRenderer>().sharedMaterial = brass;
            inserts.SetActive(false);
            return inserts;
        }

        private GameObject InstantiateInserts(GameObject insertPrefab, string insertName) {
            GameObject inserts = new GameObject(insertName);
            inserts.transform.SetParent(transform);
            inserts.transform.localPosition = Vector3.zero;

            Vector3 holeDir = holeCol.holeData.forward;
            float holeDepth = holeCol.holeData.depth / 2;
            // V2: flush with the hole's faces: the insert's outer end on the face, the rest inside the hole.
            var filter = insertPrefab.GetComponentInChildren<MeshFilter>(true);
            var outer = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.min.z * filter.transform.lossyScale.z : -0.1f;

            var posInsert = Instantiate(insertPrefab, inserts.transform);
            posInsert.SetActive(true);
            Vector3 pos = holeDir * (holeDepth + outer);
            posInsert.transform.localPosition = pos;
            posInsert.transform.forward = -holeDir;

            var negInsert = Instantiate(insertPrefab, inserts.transform);
            negInsert.SetActive(true);
            pos = -holeDir * (holeDepth + outer);
            negInsert.transform.localPosition = pos;
            negInsert.transform.forward = holeDir;

            inserts.SetActive(false);

            return inserts;
        }
    }
}
