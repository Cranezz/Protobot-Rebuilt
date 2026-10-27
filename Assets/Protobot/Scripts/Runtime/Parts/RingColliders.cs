using System;
using System.Collections.Generic;
using Protobot.Geometry;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// Colliders for round parts with a hole through the middle (spacers, collars, bushings, bearings, gears,
    /// tyres): a ring of convex segments, so the hole stays open — a shaft fits through and nothing rests on
    /// air. A single convex hull would fill the hole in. Built at load from a few numbers per ring (part space).
    /// </summary>
    public class RingColliders : MonoBehaviour
    {
        const int Segments = 12;
        const string RootName = "Ring Colliders";

        [Serializable]
        public struct Ring
        {
            public Vector3 centre;
            public Vector3 axis;
            public float inner;
            public float outer;
            public float from;     // along the axis, relative to the centre
            public float to;
            public PhysicsMaterial material;
        }

        [SerializeField] List<Ring> rings = new();

        public void Add(Ring ring) => rings.Add(ring);

        void Awake()
        {
            var root = new GameObject(RootName) { layer = gameObject.layer };
            root.transform.SetParent(transform, false);
            foreach (var ring in rings)
            {
                var axis = ring.axis.normalized;
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var v = Vector3.Cross(axis, u);
                for (var k = 0; k < Segments; k++)
                {
                    // One segment: a straight inner edge (keeps it convex) and a slightly curved outer edge.
                    var a0 = k * Mathf.PI * 2f / Segments;
                    var a1 = (k + 1) * Mathf.PI * 2f / Segments;
                    var polygon = new List<Vector2>
                    {
                        Point(a0, ring.inner), Point(a0, ring.outer), Point((a0 + a1) / 2f, ring.outer / Mathf.Cos(Mathf.PI / Segments / 2f)),
                        Point(a1, ring.outer), Point(a1, ring.inner),
                    };
                    var collider = root.AddComponent<MeshCollider>();
                    collider.sharedMesh = ConvexPieces.Extrude(polygon, ring.centre, u, v, axis, ring.from, ring.to);
                    collider.convex = true;
                    collider.sharedMaterial = ring.material;
                }
            }
        }

        static Vector2 Point(float angle, float radius) => new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
    }
}
