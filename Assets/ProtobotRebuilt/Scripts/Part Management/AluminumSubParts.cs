using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Protobot {
    [Serializable]
    public class AluminumSubParts : MonoBehaviour {
        [SerializeField] private GameObject start;
        [SerializeField] private GameObject mid;
        [SerializeField] private GameObject mid5Start;
        [SerializeField] private GameObject mid5End;
        [SerializeField] private GameObject end;
        [SerializeField] private Material material;
        [SerializeField] private int maxHoleCount = 35;

        // V2: a part is cut from full-length stock (maxHoleCount holes), the way the saw cuts, instead of being
        // put together from segments: every length gets a clean, capped end (the original only had the end
        // segment on full-length parts). Cut meshes are kept per length.
        [NonSerialized] private Mesh stock;
        [NonSerialized] private readonly System.Collections.Generic.Dictionary<int, Mesh> cutMeshes = new();

        public Mesh GetMesh(int holeCount) {
            holeCount = Mathf.Clamp(holeCount, 1, maxHoleCount);
            if (cutMeshes.TryGetValue(holeCount, out var cached) && cached != null)
                return cached;
            var full = Stock();
            if (holeCount == maxHoleCount)
                return full;

            // Keep the first holeCount holes: cut in the middle of the gap after the last one, then centre them.
            var cutAt = GetXPos(holeCount - 1, maxHoleCount) + 0.25f;
            var mesh = Tools.PartCutting.Slice(full, new Vector3(cutAt, 0f, 0f), Vector3.left);
            if (mesh == null)
                return Segments(holeCount);
            var shift = 0.25f * (maxHoleCount - holeCount);
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i].x += shift;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.name = name + " (" + holeCount + ")";
            cutMeshes[holeCount] = mesh;
            return mesh;
        }

        private Mesh Stock() {
            if (stock == null) {
                stock = Segments(maxHoleCount);
                stock.name = name + " (" + maxHoleCount + ")";
            }
            return stock;
        }

        // The original's way: one segment per hole.
        private Mesh Segments(int holeCount) {
            CombineInstance[] combine = new CombineInstance[holeCount];

            for (int i = 0; i < holeCount; i++) {
                var subPart = GetSubPart(i + 1);
                var meshFilter = subPart.GetComponent<MeshFilter>();

                combine[i].mesh = meshFilter.sharedMesh;

                var tMatrix = meshFilter.transform.localToWorldMatrix;
                tMatrix[0, 3] = GetXPos(i, holeCount);
                tMatrix[1, 3] = 0; // resets Y pos
                tMatrix[2, 3] = 0; // resets Z pos

                combine[i].transform = tMatrix;
            }

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = IndexFormat.UInt32;
            newMesh.CombineMeshes(combine);
            newMesh.RecalculateNormals();

            return newMesh;
        }

        public float GetXPos(int holeIndex, int holeCount) => 0.5f * ((-holeCount + 1) / 2f + holeIndex);

        public GameObject GeneratePart(int holeCount) {
            // make a new game object with a mesh from GetMesh(holeCount)
            // iterate thru all sub parts and add their holes with their LOCAL POSITIONS shifted by x values

            var newPart = new GameObject("Aluminum Part (" + holeCount + ")");
            Mesh mesh = GetMesh(holeCount);
            newPart.AddComponent<MeshFilter>().sharedMesh = mesh;
            newPart.AddComponent<MeshCollider>().sharedMesh = mesh;
            newPart.AddComponent<MeshRenderer>().material = material;
            
            GenerateHoles(newPart, holeCount);
            
            return newPart;
        }
        
        public void GenerateHoles(GameObject obj, int holeCount) {
            // iterate thru each subpart
            for (int i = 1; i <= holeCount; i++) {
                GameObject subPart = GetSubPart(i);

                foreach (var holeCollider in subPart.GetComponentsInChildren<HoleCollider>()) {
                    var pos = holeCollider.transform.localPosition;
                    pos.x += GetXPos(i - 1, holeCount); // correctly positions hole

                    var rot = holeCollider.transform.rotation;
                    
                    Instantiate(holeCollider.gameObject, pos, rot, obj.transform);
                }
            }
        }

        public GameObject GetSubPart(int curHole) {
            if (curHole == 1) 
                return start;
            
            if (curHole == maxHoleCount)
                return end;

            if (curHole % 5 == 0)
                return mid5Start;

            if ((curHole - 1) % 5 == 0)
                return mid5End;

            return mid;
        }
    }
}