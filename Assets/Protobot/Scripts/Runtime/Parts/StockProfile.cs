using System.Collections.Generic;
using Protobot.Geometry;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// For metal stock and shafts: the part's cross-section (split into convex pieces) and how far it
    /// runs along its length. Builds colliders that follow the real shape, so a C-channel is open on one
    /// side instead of a solid box, and lets the saw shorten the part. All values are in the part's own space.
    /// </summary>
    public class StockProfile : MonoBehaviour
    {
        [SerializeField] Vector3 axis = Vector3.forward;
        [SerializeField] Vector3 u = Vector3.right;
        [SerializeField] Vector3 v = Vector3.up;
        [SerializeField] Vector3 origin;
        [SerializeField] float start;
        [SerializeField] float end;
        [SerializeField] List<Vector2> points = new();
        [SerializeField] List<int> pieceSizes = new();
        [SerializeField] PhysicsMaterial physicsMaterial;

        GameObject colliderRoot;

        /// <summary>Unit direction along the part's length (part space).</summary>
        public Vector3 Axis => axis;
        public Vector3 Origin => origin;
        public float Start => start;
        public float End => end;
        public float Length => end - start;
        public int PieceCount => pieceSizes.Count;

        public void Configure(Vector3 lengthAxis, Vector3 sideU, Vector3 sideV, Vector3 profileOrigin, float from, float to,
            List<List<Vector2>> pieces, PhysicsMaterial material)
        {
            axis = lengthAxis;
            u = sideU;
            v = sideV;
            origin = profileOrigin;
            start = from;
            end = to;
            physicsMaterial = material;
            points.Clear();
            pieceSizes.Clear();
            foreach (var piece in pieces)
            {
                points.AddRange(piece);
                pieceSizes.Add(piece.Count);
            }
        }

        void Awake() => BuildColliders();

        /// <summary>After a cut: the part now runs from <paramref name="from"/> to <paramref name="to"/> along its axis.</summary>
        public void SetExtent(float from, float to)
        {
            start = from;
            end = to;
            BuildColliders();
        }

        /// <summary>Position of a part-space point along the length.</summary>
        public float AlongAxis(Vector3 partPoint) => Vector3.Dot(partPoint - origin, axis);

        /// <summary>How far the cross-section reaches from the centre line (m).</summary>
        public float CrossSectionRadius
        {
            get
            {
                var r = 0f;
                foreach (var p in points)
                    r = Mathf.Max(r, p.magnitude);
                return r;
            }
        }

        const string ColliderRootName = "Stock Colliders";

        void BuildColliders()
        {
            if (pieceSizes.Count == 0)
                return;

            // The profile colliders replace the convex-hull ones the prefab was built with (and any left
            // over from the part this one was copied from, e.g. a saw offcut).
            foreach (var c in GetComponentsInChildren<MeshCollider>())
                Destroy(c);
            var stale = new List<Transform>();
            foreach (Transform child in transform)
                if (child.name == ColliderRootName)
                    stale.Add(child);
            foreach (var child in stale)
            {
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            colliderRoot = new GameObject(ColliderRootName) { layer = gameObject.layer };
            colliderRoot.transform.SetParent(transform, false);
            var index = 0;
            foreach (var size in pieceSizes)
            {
                var piece = points.GetRange(index, size);
                index += size;
                var mc = colliderRoot.AddComponent<MeshCollider>();
                mc.sharedMesh = ConvexPieces.Extrude(piece, origin, u, v, axis, start, end);
                mc.convex = true;
                mc.sharedMaterial = physicsMaterial;
            }
        }
    }
}
