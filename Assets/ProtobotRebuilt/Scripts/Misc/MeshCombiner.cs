using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot {
    public static class MeshCombiner {
        private static Pivot pivot;

        [RuntimeInitializeOnLoadMethod]
        private static void Init() {
            pivot = Pivot.Create("MeshCombiner Pivot");
        }

        public static Mesh CombineMeshes(List<GameObject> objs, Vector3 position, Quaternion rotation) {
            pivot.SetPosition(position, false);
            pivot.transform.rotation = rotation;

            pivot.AddObjects(objs);

            pivot.SetPosition(new Vector3(0, 0, 0), true);
            pivot.transform.rotation = Quaternion.identity;

            List<MeshFilter> meshFilters = new List<MeshFilter>();
            objs.ForEach(x => {
                if (x.TryGetComponent(out MeshFilter meshFilter))
                    meshFilters.Add(meshFilter);
                // V2: a cylinder's rod is part of it (the moved cylinder's preview showed no rod).
                if (x.TryGetComponent(out Motion.PneumaticCylinder cylinder) && cylinder.Rod != null && cylinder.Rod.TryGetComponent(out MeshFilter rodFilter) && rodFilter.sharedMesh != null)
                    meshFilters.Add(rodFilter);
                // V2: a wheel's or gear's inserts showing in its holes are part of it (the preview of a moved part too).
                foreach (var insert in x.GetComponentsInChildren<HoleInsert>())
                    foreach (var filter in insert.GetComponentsInChildren<MeshFilter>())
                        if (filter.gameObject.activeInHierarchy && filter.sharedMesh != null && filter.GetComponent<HoleCollider>() == null)
                            meshFilters.Add(filter);
            });

            CombineInstance[] combine = new CombineInstance[meshFilters.Count];

            for (int i = 0; i < meshFilters.Count; i++) {
                combine[i].mesh = meshFilters[i].sharedMesh.CombineSubmeshes();
                combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
            }

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = IndexFormat.UInt32;
            newMesh.CombineMeshes(combine);

            pivot.SetPosition(position, true);
            pivot.transform.rotation = rotation;

            pivot.SetEmpty();

            return newMesh;
        }
    }
}