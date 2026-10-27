using System.Collections.Generic;
using Protobot.Geometry;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// For metal stock and shafts: the part's cross-section (split into convex pieces) and how far it
    /// runs along its length. Builds colliders that follow the real shape, so a C-channel is open on one
    /// side instead of a solid box. Saw cuts are kept as planes and clip those colliders, so angled and
    /// lengthwise cuts collide where the metal really is. All values are in the part's own space.
    /// </summary>
    public class StockProfile : MonoBehaviour
    {
        [SerializeField] Vector3 axis = Vector3.forward;
        [SerializeField] Vector3 u = Vector3.right;
        [SerializeField] Vector3 v = Vector3.up;
        [SerializeField] Vector3 origin;
        [Tooltip("Where the metal currently runs along the axis (shrinks as it's cut).")]
        [SerializeField] float start;
        [SerializeField] float end;
        [Tooltip("The full length it came in, which the colliders are made from before the cuts clip them.")]
        [SerializeField] float fullStart;
        [SerializeField] float fullEnd;
        [SerializeField] List<Vector2> points = new();
        [SerializeField] List<int> pieceSizes = new();
        [SerializeField] List<Vector3> cutPoints = new();
        [SerializeField] List<Vector3> cutNormals = new();
        [SerializeField] PhysicsMaterial physicsMaterial;

        /// <summary>Unit direction along the part's length (part space).</summary>
        public Vector3 Axis => axis;
        /// <summary>Across the cross-section (the profile's own X); <see cref="V"/> is its Y, <see cref="Axis"/> its Z.</summary>
        public Vector3 U => u;
        public Vector3 V => v;
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
            start = fullStart = from;
            end = fullEnd = to;
            physicsMaterial = material;
            points.Clear();
            pieceSizes.Clear();
            cutPoints.Clear();
            cutNormals.Clear();
            foreach (var piece in pieces)
            {
                points.AddRange(piece);
                pieceSizes.Add(piece.Count);
            }
        }

        void Awake()
        {
            if (fullEnd <= fullStart)
            {
                fullStart = start;
                fullEnd = end;
            }
            BuildColliders();
        }

        /// <summary>
        /// After a saw cut: what's left is on the side <paramref name="keepNormal"/> points to, and now runs from
        /// <paramref name="from"/> to <paramref name="to"/> along the axis.
        /// </summary>
        public void AddCut(Vector3 point, Vector3 keepNormal, float from, float to)
        {
            cutPoints.Add(point);
            cutNormals.Add(keepNormal.normalized);
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

            var colliderRoot = new GameObject(ColliderRootName) { layer = gameObject.layer };
            colliderRoot.transform.SetParent(transform, false);
            var index = 0;
            foreach (var size in pieceSizes)
            {
                var piece = points.GetRange(index, size);
                index += size;
                var mesh = ConvexPieces.Extrude(piece, origin, u, v, axis, fullStart, fullEnd);
                for (var i = 0; i < cutPoints.Count && mesh != null; i++)
                    mesh = MeshSlicer.Slice(mesh, new Plane(cutNormals[i], cutPoints[i]));
                if (mesh == null)
                    continue;
                // A shallow cut can leave a sliver of a piece too thin for a convex collider; it doesn't matter.
                var s = mesh.bounds.size;
                if (Mathf.Min(s.x, Mathf.Min(s.y, s.z)) < 0.0001f)
                    continue;
                var mc = colliderRoot.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
                mc.sharedMaterial = physicsMaterial;
            }
        }
    }
}
