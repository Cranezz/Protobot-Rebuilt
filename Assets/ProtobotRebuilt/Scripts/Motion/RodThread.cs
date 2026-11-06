using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Motion {
    // V2: the threaded end of a pneumatic cylinder's rod (8-32), which works like a screw: a nut threads onto it
    // (nuts snap to it like to any screw), and what it passes through to that nut is fastened to the rod, and so to
    // the cylinder. Its transform is the screw's head end (where the thread starts); the thread runs back along
    // -forward to the rod's tip, as a screw's shank does.
    public class RodThread : Screw {
        public GameObject cylinder;

        public override List<GameObject> GetAttachedParts() {
            var parts = base.GetAttachedParts();
            if (parts != null && cylinder != null && !parts.Contains(cylinder)) parts.Add(cylinder);
            return parts;
        }

        /// <summary>A plain cylinder along z from -length to +head (a screw's length is its mesh minus 0.087").</summary>
        private static readonly Dictionary<(float, float), Mesh> Made = new();

        /// <summary>The thread's see-through mark (one per size, shared).</summary>
        public static Mesh ThreadMesh(float length, float radius) {
            if (Made.TryGetValue((length, radius), out var made) && made != null) return made;
            return Made[(length, radius)] = MakeThread(length, radius);
        }

        private static Mesh MakeThread(float length, float radius) {
            const int Sides = 20;
            const float Head = 0.087f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i <= Sides; i++) {
                var angle = i * Mathf.PI * 2f / Sides;
                var p = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                vertices.Add(p + Vector3.back * length);
                vertices.Add(p + Vector3.forward * Head);
            }
            for (var i = 0; i < Sides; i++) {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            var mesh = new Mesh { name = "Rod Thread" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
