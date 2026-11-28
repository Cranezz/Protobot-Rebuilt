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
        // V2 (build 79): the detent is the bar's own weight. A triangular bar resting on a face has to be rolled over an
        // edge to turn: the push back is its weight times how far its middle is beside that edge (R cos(30° + turn),
        // R = middle to corner), most at the start (half a side: ~0.09 N·m for this bar), nothing at 60°, where it falls
        // onto the next face. It was a spring of 1500 kg·in²/s² per radian (about 1 N·m before it gave: 12x a real bar),
        // so a robot's arm pressing it never turned it over.
        private const float Damping = 1.2f;   // kg·in²/s² per rad/s
        private const float Landing = 0.2f;   // spin kept when it lands flat on a face (plastic on plastic: a dull thud)
        private float lastError;
        /// <summary>V2: middle of the bar's cross-section to a corner (inches).</summary>
        public float cornerRadius = 1.15f;

        /// <summary>V2: how far the bar can be lifted in its brackets (it drops back by its weight).</summary>
        public const float Lift = 1f;
        public float restHeight;
        /// <summary>V2: the middle of the bar's cross-section (local): it turns about this line.</summary>
        public Vector3 pivotLocal;

        /// <summary>V2: how far it's lifted in its brackets (inches).</summary>
        public float Lifted => transform.TransformPoint(pivotLocal).y - restHeight;

        /// <summary>V2: down in its brackets (SC: a Toggle counts only when fully seated).</summary>
        public bool Seated => body == null || Lifted < 0.1f;

        public bool TouchedByRobot => touching.Count > 0;

        private void Start() => startAngle = Angle();

        private float Angle() {
            // The yellow face's turn about the axis, from straight up.
            var axis = transform.TransformDirection(axisLocal);
            var normal = transform.TransformDirection(faces[0].normal);
            var reference = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
            return Vector3.SignedAngle(reference, Vector3.ProjectOnPlane(normal, axis), axis);
        }

        // V2 (tests): a push at a world point for some physics steps, and a trace of the turn and lift every 60 steps.
        private Vector3 testForce, testPoint;
        private int testSteps, testStep = -1;
        public readonly System.Text.StringBuilder TestTrace = new();

        public void TestPush(Vector3 force, Vector3 localPoint, int steps) {
            testForce = force;
            testPoint = localPoint;
            testSteps = steps;
            testStep = 0;
            TestTrace.Clear();
            body.WakeUp();
        }

        private void FixedUpdate() {
            if (body == null || faces.Count == 0) return;
            if (testStep >= 0) {
                if (testStep < testSteps) body.AddForceAtPosition(testForce, transform.TransformPoint(testPoint), ForceMode.Force);
                if (testStep % 60 == 0) TestTrace.Append(" [" + testStep + ": " + Turned.ToString("F0") + " h" + Lifted.ToString("F2") + "]");
                if (++testStep > testSteps + 1200) testStep = -1;
            }
            var axis = transform.TransformDirection(axisLocal);
            var angle = Angle() - startAngle;
            var nearest = Mathf.Round(angle / 120f) * 120f;
            var error = Mathf.DeltaAngle(nearest, angle) * Mathf.Deg2Rad;
            var spin = Vector3.Dot(body.angularVelocity, axis);
            // Rolling onto a face is a landing: most of the spin goes (else it would roll on over the next corner).
            if (lastError != 0f && Mathf.Sign(error) != Mathf.Sign(lastError) && Mathf.Abs(error) < 0.35f && Mathf.Abs(lastError) < 0.35f) {
                body.angularVelocity += axis * (spin * (Landing - 1f));
                spin *= Landing;
            }
            lastError = error;
            var turned = Mathf.Abs(error);
            var lever = cornerRadius * Mathf.Cos(Mathf.PI / 6f + Mathf.Min(turned, Mathf.PI / 3f));
            var weight = body.mass * Physics.gravity.magnitude;
            var soft = Mathf.Clamp01(turned / (1.5f * Mathf.Deg2Rad));   // no chatter right on the face
            body.AddTorque(axis * (-Mathf.Sign(error) * soft * weight * lever - Damping * spin), ForceMode.Force);
        }

        /// <summary>V2 (tests): the turn from the start (degrees) and the steadiest push that rolls it over.</summary>
        public float Turned => Angle() - startAngle;
        public float HoldTorque => body != null ? body.mass * Physics.gravity.magnitude * cornerRadius * Mathf.Cos(Mathf.PI / 6f) : 0f;

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
        /// A cup half (its mesh runs along +Z from the cup's middle) as a ring of thin convex wedges round its wall: 16 round,
        /// 4 bands along. Shared meshes per band; each wedge is a child turned round the axis.
        /// V2 (build 88): the wedges surround the drawn surface. They were 8 flats inside it and 2 straight bands per half,
        /// so the drawn cup was 3.25" across but its shape 2.99", with parts of the surface up to 0.4" outside it: a
        /// roller or claw sank into the cup without touching it (a tester: "the cup can't be grabbed, or part of it").
        /// </summary>
        public static void CupHalf(Transform owner, Mesh mesh, Matrix4x4 local, int layer) {
            var profile = Profile(mesh);
            const int Around = 16, Bands = 4;
            var holder = new GameObject("Half") { layer = layer }.transform;
            holder.SetParent(owner, false);
            holder.localPosition = local.GetColumn(3);
            holder.localRotation = local.rotation;
            holder.localScale = local.lossyScale;
            for (var band = 0; band < Bands; band++) {
                var wedge = Wedge(mesh, band, Bands, Around, profile);
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

        private class CupProfile {
            public float zLow, zHigh;
            public float[] outer, inner;   // radius of the outside and of the inside (the hollow) by slice along the axis
        }

        private static readonly Dictionary<Mesh, CupProfile> CupProfiles = new();

        // The outside and inside radius of a cup half along its axis, in 32 slices.
        private static CupProfile Profile(Mesh mesh) {
            if (CupProfiles.TryGetValue(mesh, out var known)) return known;
            var vertices = mesh.vertices;
            var zLow = vertices.Min(v => v.z);
            var zHigh = vertices.Max(v => v.z);
            const int Slices = 32;
            var outer = new float[Slices + 1];
            var inner = Enumerable.Repeat(float.MaxValue, Slices + 1).ToArray();
            foreach (var v in vertices) {
                var slice = Mathf.Clamp(Mathf.RoundToInt((v.z - zLow) / (zHigh - zLow) * Slices), 0, Slices);
                var r = new Vector2(v.x, v.y).magnitude;
                outer[slice] = Mathf.Max(outer[slice], r);
                if (r > 0.3f) inner[slice] = Mathf.Min(inner[slice], r);
            }
            for (var i = 1; i <= Slices; i++) if (outer[i] < 0.3f) outer[i] = outer[i - 1];
            for (var i = Slices - 1; i >= 0; i--) if (outer[i] < 0.3f) outer[i] = outer[i + 1];
            for (var i = 0; i <= Slices; i++) inner[i] = inner[i] > outer[i] ? outer[i] : inner[i];
            var profile = new CupProfile { zLow = zLow, zHigh = zHigh, outer = outer, inner = inner };
            CupProfiles[mesh] = profile;
            return profile;
        }

        private static float RadiusAt(float z, float zLow, float zHigh, float[] radius) {
            var t = Mathf.Clamp01((z - zLow) / (zHigh - zLow)) * (radius.Length - 1);
            var i = Mathf.Min((int)t, radius.Length - 2);
            return Mathf.Lerp(radius[i], radius[i + 1], t - i);
        }

        private static Mesh Wedge(Mesh source, int band, int bands, int around, CupProfile profile) {
            if (Wedges.TryGetValue((source, band), out var known) && known != null) return known;
            var zLow = profile.zLow;
            var zHigh = profile.zHigh;
            var z0 = Mathf.Lerp(zLow, zHigh, band / (float)bands);
            var z1 = Mathf.Lerp(zLow, zHigh, (band + 1) / (float)bands);
            // Each face is a straight line along the band, moved out to the highest point of the profile in it: the
            // outside never lies beyond the shape (nothing sinks into the cup), the inside never inside the hollow (a
            // pin nests as deep as in VEX's model).
            float o0 = RadiusAt(z0, zLow, zHigh, profile.outer), o1 = RadiusAt(z1, zLow, zHigh, profile.outer);
            float i0 = RadiusAt(z0, zLow, zHigh, profile.inner), i1 = RadiusAt(z1, zLow, zHigh, profile.inner);
            var slices = profile.outer.Length - 1;
            float outerExcess = 0f, innerExcess = 0f;
            for (var k = 0; k <= slices; k++) {
                var z = Mathf.Lerp(zLow, zHigh, k / (float)slices);
                if (z < z0 - 1e-4f || z > z1 + 1e-4f) continue;
                var t = (z - z0) / Mathf.Max(1e-4f, z1 - z0);
                outerExcess = Mathf.Max(outerExcess, profile.outer[k] - Mathf.Lerp(o0, o1, t));
                innerExcess = Mathf.Max(innerExcess, profile.inner[k] - Mathf.Lerp(i0, i1, t));
            }
            o0 += outerExcess;
            o1 += outerExcess;
            i0 = Mathf.Min(i0 + innerExcess, o0 - 0.06f);
            i1 = Mathf.Min(i1 + innerExcess, o1 - 0.06f);
            var half = Mathf.PI / around * 1.04f;            // a little overlap so nothing slips between wedges
            var outward = 1f / Mathf.Sqrt(Mathf.Cos(half));  // the outside flats a hair in, their corners a hair out
            var inward = 1f / Mathf.Cos(half);               // the inside flats on the hollow's surface
            var points = new List<Vector3>();
            foreach (var (z, o, i) in new[] { (z0, o0, i0), (z1, o1, i1) })
                foreach (var a in new[] { -half, half })
                    foreach (var rr in new[] { o * outward, i * inward }) {
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
