using System.Collections.Generic;
using System.Linq;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.Game {
    public enum PieceColor { Yellow, Red, Blue, Clear, Gray }

    // V2: an Override Pin in the simulation: two halves along its axis (each red, blue or yellow), solid, so each half's
    // collision shape is its own (light) mesh made convex. The visuals are the field's own meshes.
    public class Pin : MonoBehaviour {
        public Rigidbody body;
        /// <summary>Each half: its direction from the pin's middle (local) and its color.</summary>
        public readonly List<(Vector3 direction, PieceColor color)> halves = new();
        public float halfLength = 3.25f;

        public Vector3 Axis => transform.TransformDirection(halves.Count > 0 ? halves[0].direction : Vector3.forward);
        public Vector3 End(int half) => transform.position + transform.TransformDirection(halves[half].direction) * halfLength;
        public PieceColor ColorOf(int half) => halves[half].color;
    }

    // V2: an Override Cup: an hourglass of two halves, one opaque (dark gray), one clear. Hollow, so its collision shape
    // is two rings of thin convex wedges (a pin really goes into either end).
    public class Cup : MonoBehaviour {
        public Rigidbody body;
        /// <summary>The opaque half's direction from the middle (local); the clear half is the other way.</summary>
        public Vector3 opaqueDirection = Vector3.back;
        public float halfLength = 3.24f;

        public Vector3 OpaqueEnd => transform.position + transform.TransformDirection(opaqueDirection) * halfLength;
        public Vector3 ClearEnd => transform.position - transform.TransformDirection(opaqueDirection) * halfLength;
    }

    // V2: a Toggle: a triangular bar on the perimeter that turns on its long axis. It rests in three positions, 120°
    // apart (a face on its mounts); the color on top is the color it's set to (yellow at the start).
    public class Toggle : MonoBehaviour {
        public Rigidbody body;
        public Vector3 axisLocal;
        public Quadrant quadrant;
        /// <summary>The face colors in order round the axis (local normals), yellow first (on top at the start).</summary>
        public readonly List<(Vector3 normal, PieceColor color)> faces = new();
        private float startAngle;
        private readonly HashSet<Collider> touching = new();
        private const float Stiffness = 1500f, Damping = 25f;   // a gentle detent (kg·in²/s² per radian)

        /// <summary>V2: how far the bar can be lifted in its brackets (it drops back by its weight).</summary>
        public const float Lift = 1f;
        public float restHeight;

        /// <summary>V2: down in its brackets (SC: a Toggle counts only when fully seated).</summary>
        public bool Seated => body == null || transform.position.y - restHeight < 0.1f;

        public bool TouchedByRobot => touching.Count > 0;

        private void Start() => startAngle = Angle();

        private float Angle() {
            // The yellow face's turn about the axis, from straight up.
            var axis = transform.TransformDirection(axisLocal);
            var normal = transform.TransformDirection(faces[0].normal);
            var reference = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
            return Vector3.SignedAngle(reference, Vector3.ProjectOnPlane(normal, axis), axis);
        }

        private void FixedUpdate() {
            if (body == null || faces.Count == 0) return;
            var axis = transform.TransformDirection(axisLocal);
            var angle = Angle() - startAngle;
            var nearest = Mathf.Round(angle / 120f) * 120f;
            var error = Mathf.DeltaAngle(nearest, angle) * Mathf.Deg2Rad;
            var spin = Vector3.Dot(body.angularVelocity, axis);
            body.AddTorque(axis * (-Stiffness * error - Damping * spin), ForceMode.Force);
        }

        /// <summary>The color on top, if it rests in a set position and no robot touches it; else yellow (neutral).</summary>
        public PieceColor SetColor {
            get {
                if (faces.Count == 0) return PieceColor.Yellow;
                var axis = transform.TransformDirection(axisLocal);
                var spin = body != null ? Mathf.Abs(Vector3.Dot(body.angularVelocity, axis)) : 0f;
                var angle = Angle() - startAngle;
                var off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Round(angle / 120f) * 120f, angle));
                if (TouchedByRobot || spin > 0.5f || off > 12f || !Seated) return PieceColor.Yellow;
                var top = faces.OrderByDescending(f => Vector3.Dot(transform.TransformDirection(f.normal), Vector3.up)).First();
                return top.color;
            }
        }

        private void OnCollisionEnter(Collision c) { if (c.collider.gameObject.layer == Sandbox.Layer) touching.Add(c.collider); }
        private void OnCollisionExit(Collision c) => touching.Remove(c.collider);
    }

    // Building the pieces' collision shapes.
    public static class ElementShapes {
        public const float PinMass = 0.11f, CupMass = 0.065f, ToggleMass = 0.35f;
        private static PhysicsMaterial plastic;
        private static readonly Dictionary<Mesh, (float zLow, float zHigh, float[] radius)> Profiles = new();
        private static readonly Dictionary<(Mesh, int), Mesh> Wedges = new();

        public static PhysicsMaterial Plastic => plastic != null ? plastic : plastic = new PhysicsMaterial("Game Plastic") {
            staticFriction = 0.45f, dynamicFriction = 0.35f, bounciness = 0.05f,
            frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum,
        };

        public static Rigidbody Body(GameObject go, float mass) {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            rb.maxDepenetrationVelocity = 25f;
            rb.sleepThreshold = 4f;   // energy per mass in in²/s²: still below ~3 in/s (Unity's default is for metres)
            return rb;
        }

        /// <summary>A convex collider from a (readable) mesh, as a child placed like `source` relative to `owner`.</summary>
        public static MeshCollider Convex(Transform owner, Mesh mesh, Matrix4x4 local, int layer) {
            var go = new GameObject("Shape") { layer = layer };
            go.transform.SetParent(owner, false);
            go.transform.localPosition = local.GetColumn(3);
            go.transform.localRotation = local.rotation;
            go.transform.localScale = local.lossyScale;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = Hull(mesh);
            collider.convex = true;
            collider.sharedMaterial = Plastic;
            FlexGrip.RegisterElement(collider);
            return collider;
        }

        private static readonly Dictionary<Mesh, Mesh> Hulls = new();

        // A solid piece's shape: its farthest points in 96 directions (PhysX keeps a convex shape under 256 faces).
        public static Mesh Hull(Mesh mesh) {
            if (Hulls.TryGetValue(mesh, out var hull) && hull != null) return hull;
            const int Count = 96;
            var vertices = mesh.vertices;
            var golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            var points = new HashSet<Vector3>();
            for (var i = 0; i < Count; i++) {
                var y = 1f - 2f * (i + 0.5f) / Count;
                var r = Mathf.Sqrt(1f - y * y);
                var d = new Vector3(Mathf.Cos(golden * i) * r, y, Mathf.Sin(golden * i) * r);
                var best = vertices[0];
                var most = float.MinValue;
                foreach (var v in vertices) {
                    var dot = Vector3.Dot(v, d);
                    if (dot > most) { most = dot; best = v; }
                }
                points.Add(best);
            }
            var list = points.ToList();
            var triangles = new List<int>();
            for (var i = 1; i + 1 < list.Count; i++) triangles.AddRange(new[] { 0, i, i + 1 });
            hull = new Mesh { name = mesh.name + " (hull)" };
            hull.SetVertices(list);
            hull.SetTriangles(triangles, 0);
            hull.RecalculateBounds();
            Hulls[mesh] = hull;
            return hull;
        }

        /// <summary>
        /// A cup half (its mesh runs along +Z from the cup's middle) as a ring of thin convex wedges following the outer
        /// surface: 8 round, 2 bands along. Shared meshes per band; each wedge is a child turned round the axis.
        /// </summary>
        public static void CupHalf(Transform owner, Mesh mesh, Matrix4x4 local, int layer) {
            var (zLow, zHigh, radius) = Profile(mesh);
            const int Around = 8, Bands = 2;
            var holder = new GameObject("Half") { layer = layer }.transform;
            holder.SetParent(owner, false);
            holder.localPosition = local.GetColumn(3);
            holder.localRotation = local.rotation;
            for (var band = 0; band < Bands; band++) {
                var wedge = Wedge(mesh, band, Bands, Around, zLow, zHigh, radius);
                for (var i = 0; i < Around; i++) {
                    var go = new GameObject("Wall") { layer = layer };
                    go.transform.SetParent(holder, false);
                    go.transform.localRotation = Quaternion.AngleAxis(i * 360f / Around, Vector3.forward);
                    var collider = go.AddComponent<MeshCollider>();
                    collider.sharedMesh = wedge;
                    collider.convex = true;
                    collider.sharedMaterial = Plastic;
                    FlexGrip.RegisterElement(collider);
                }
            }
        }

        // The outer radius of a cup half along its axis, in 16 slices.
        private static (float, float, float[]) Profile(Mesh mesh) {
            if (Profiles.TryGetValue(mesh, out var known)) return known;
            var vertices = mesh.vertices;
            var zLow = vertices.Min(v => v.z);
            var zHigh = vertices.Max(v => v.z);
            const int Slices = 16;
            var radius = new float[Slices + 1];
            foreach (var v in vertices) {
                var slice = Mathf.Clamp(Mathf.RoundToInt((v.z - zLow) / (zHigh - zLow) * Slices), 0, Slices);
                radius[slice] = Mathf.Max(radius[slice], new Vector2(v.x, v.y).magnitude);
            }
            for (var i = 1; i <= Slices; i++) if (radius[i] < 0.3f) radius[i] = radius[i - 1];
            for (var i = Slices - 1; i >= 0; i--) if (radius[i] < 0.3f) radius[i] = radius[i + 1];
            Profiles[mesh] = (zLow, zHigh, radius);
            return (zLow, zHigh, radius);
        }

        private static float RadiusAt(float z, float zLow, float zHigh, float[] radius) {
            var t = Mathf.Clamp01((z - zLow) / (zHigh - zLow)) * (radius.Length - 1);
            var i = Mathf.Min((int)t, radius.Length - 2);
            return Mathf.Lerp(radius[i], radius[i + 1], t - i);
        }

        private static Mesh Wedge(Mesh source, int band, int bands, int around, float zLow, float zHigh, float[] radius) {
            if (Wedges.TryGetValue((source, band), out var known) && known != null) return known;
            const float Wall = 0.12f;
            var z0 = Mathf.Lerp(zLow, zHigh, band / (float)bands);
            var z1 = Mathf.Lerp(zLow, zHigh, (band + 1) / (float)bands);
            var r0 = RadiusAt(z0, zLow, zHigh, radius);
            var r1 = RadiusAt(z1, zLow, zHigh, radius);
            var half = Mathf.PI / around * 1.08f;   // a little overlap so nothing slips between wedges
            var points = new List<Vector3>();
            foreach (var (z, r) in new[] { (z0, r0), (z1, r1) })
                foreach (var a in new[] { -half, half })
                    foreach (var rr in new[] { r, r - Wall }) {
                        var c = Mathf.Cos(a) * rr;
                        var s = Mathf.Sin(a) * rr;
                        points.Add(new Vector3(c, s, z));
                    }
            var triangles = new List<int>();
            for (var i = 1; i + 1 < points.Count; i++) triangles.AddRange(new[] { 0, i, i + 1 });
            var mesh = new Mesh { name = $"Cup Wall {band}" };
            mesh.SetVertices(points);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            Wedges[(source, band)] = mesh;
            return mesh;
        }

        public static PieceColor ColorOf(Renderer renderer) {
            var name = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : renderer.name;
            if (name.Contains("Red")) return PieceColor.Red;
            if (name.Contains("Blue")) return PieceColor.Blue;
            if (name.Contains("Yellow")) return PieceColor.Yellow;
            if (name.Contains("Clear")) return PieceColor.Clear;
            return PieceColor.Gray;
        }
    }
}
