using System.Collections.Generic;
using System.Linq;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.Game {
    public enum Quadrant { None, Left, Bottom, Right, Top, Midfield }

    // V2: the Override field in a game (VEX V5RC 2026-27, game manual Section 2 and Appendix A).
    //
    // Field frame (what positions are typed in): inches from the field's middle, seen from above with the red Alliance
    // Station on the left (the manual's "audience view"): +X toward blue, +Y away from the audience; a heading is
    // degrees clockwise from +Y. This object's own space is that frame (local x = X, local z = Y, y up, tiles' top at 0);
    // the game moves this object, never the build, to put the robot where it starts.
    //
    // The field model is drawn as it is (full detail or light); its pins, cups and toggles become simulation pieces
    // when a run starts (copies of their meshes, the originals hidden). Collision shapes come from the light model.
    // Quadrants: the X of tape lines splits the field into four triangles; red: Left (by the red Alliance Station) and
    // Bottom, blue: Right and Top. The Midfield is the 34.05" diamond in the middle.
    public class GameField : MonoBehaviour {
        public const float Half = 70.2f;                   // inner perimeter, ±70.2" (140.4" square)
        public const float MidfieldHalfDiagonal = 24.08f;  // 34.05" sides, turned 45°
        public const float LoadZoneDepth = 12.5f;          // red/blue tape L round each Loader: 11.44" + tape

        public class Goal {
            public Vector2 position;    // field frame
            public float top;           // height of the goal's top (field frame y)
            public Quadrant quadrant;
            public string name;
            public bool Midfield => quadrant == Quadrant.Midfield;
            public bool alliance;       // a red/blue Alliance Goal (else neutral)
        }

        public GameDefinition definition;
        public readonly List<Goal> goals = new();
        public readonly List<Vector2> loaders = new();       // the two red Loaders (skills uses only those)
        public readonly List<Vector2> blueLoaders = new();   // V2: the blue ones (a blue alliance's Match Loads)
        private Transform display, collision;
        private Transform colliders;
        private readonly List<Renderer> hiddenDuringRun = new();
        private readonly List<GameObject> stock = new();     // pins/cups outside the field: Match Loads and Preloads

        // ------------------------------------------------------------------ frame

        public Vector3 ToWorld(Vector2 p, float height = 0f) => transform.TransformPoint(new Vector3(p.x, height, p.y));
        public Vector2 ToField(Vector3 world) {
            var l = transform.InverseTransformPoint(world);
            return new Vector2(l.x, l.z);
        }
        public float HeightOf(Vector3 world) => transform.InverseTransformPoint(world).y;
        public Vector3 Direction(float heading) => transform.TransformDirection(new Vector3(Mathf.Sin(heading * Mathf.Deg2Rad), 0f, Mathf.Cos(heading * Mathf.Deg2Rad)));

        public static Quadrant QuadrantOf(Vector2 p) {
            if (Mathf.Abs(p.x) + Mathf.Abs(p.y) <= MidfieldHalfDiagonal) return Quadrant.Midfield;
            if (Mathf.Abs(p.x) >= Mathf.Abs(p.y)) return p.x < 0 ? Quadrant.Left : Quadrant.Right;
            return p.y < 0 ? Quadrant.Bottom : Quadrant.Top;
        }

        public static PieceColor ColorOf(Quadrant q) => q switch {
            Quadrant.Left or Quadrant.Bottom => PieceColor.Red,
            Quadrant.Right or Quadrant.Top => PieceColor.Blue,
            _ => PieceColor.Yellow,
        };

        public static bool InLoadZone(Vector2 p) => Mathf.Abs(p.x) > Half - LoadZoneDepth && Mathf.Abs(p.y) > 47.1f;

        // ------------------------------------------------------------------ making it

        public static GameField Create(GameDefinition definition, bool light, bool skills = true) {
            var root = new GameObject("Game Field");
            var field = root.AddComponent<GameField>();
            field.definition = definition;
            // The model's +X is the red side and its -Z the far side: turned half round, its space is the field frame.
            var prefab = light || definition.field == null ? definition.fieldLight : definition.field;
            field.display = Instantiate(prefab, root.transform).transform;
            field.display.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            if (prefab == definition.fieldLight) field.collision = field.display;
            else {
                field.collision = Instantiate(definition.fieldLight, root.transform).transform;
                field.collision.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                field.collision.gameObject.SetActive(false);   // only its meshes and places are used
            }
            field.Read();
            field.MakeColliders();
            if (skills) field.HideForSkills();
            else field.HideOffField();   // V2: a match starts with its Goals set up as the model shows
            return field;
        }

        private static Bounds BoundsOf(Transform t) {
            var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // Bounds of an inactive object's meshes (renderer bounds are empty while it's inactive).
        private static Bounds MeshBounds(Transform t) {
            var first = true;
            var b = new Bounds();
            foreach (var f in t.GetComponentsInChildren<MeshFilter>(true)) {
                if (f.sharedMesh == null) continue;
                var mb = f.sharedMesh.bounds;
                for (var i = 0; i < 8; i++) {
                    var corner = f.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { b = new Bounds(corner, Vector3.zero); first = false; }
                    else b.Encapsulate(corner);
                }
            }
            return b;
        }

        private Mesh pinHalf, cupHalf, toggleBar;

        private static Mesh Readable(Transform t) {
            var filter = t.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable ? filter.sharedMesh : null;
        }

        private void Read() {
            foreach (var t in collision.GetComponentsInChildren<Transform>(true)) {
                if (t.name.StartsWith("276-9250-840")) {
                    var b = MeshBounds(t);
                    var p = ToField(b.center);
                    var goal = new Goal {
                        position = p, top = HeightOf(b.max), name = t.name,
                        alliance = t.name.Contains("_Red") || t.name.Contains("_Blue"),
                        quadrant = t.name.Contains("Neutral Goal 0") ? Quadrant.Midfield : QuadrantOf(p),
                    };
                    goals.Add(goal);
                }
                else if (t.name.StartsWith("276-9250-031") && t.name.Contains("Red Loader")) loaders.Add(ToField(MeshBounds(t.parent).center));
                else if (t.name.StartsWith("276-9250-031") && t.name.Contains("Blue Loader")) blueLoaders.Add(ToField(MeshBounds(t.parent).center));
                // One shape each for every pin half, cup half and Toggle bar (they're all alike), from the light model.
                if (pinHalf == null && t.name.StartsWith("276-9250-001")) pinHalf = Readable(t);
                if (cupHalf == null && t.name.StartsWith("276-9250-002")) cupHalf = Readable(t);
                if (toggleBar == null && t.name.StartsWith("276-9250-023")) toggleBar = Readable(t);
            }
        }

        // ------------------------------------------------------------------ fixed collision shapes

        private void MakeColliders() {
            colliders = new GameObject("Field Colliders") { layer = Sandbox.FieldLayer }.transform;
            colliders.SetParent(transform, false);
            // The perimeter: four walls just outside the field (12" tall).
            const float Thick = 4f, Height = 12f;
            for (var side = 0; side < 4; side++) {
                var wall = new GameObject("Wall") { layer = Sandbox.FieldLayer };
                wall.transform.SetParent(colliders, false);
                var box = wall.AddComponent<BoxCollider>();
                var horizontal = side < 2;
                var sign = side % 2 == 0 ? -1f : 1f;
                box.center = horizontal ? new Vector3(sign * (Half + Thick / 2f), Height / 2f, 0f) : new Vector3(0f, Height / 2f, sign * (Half + Thick / 2f));
                box.size = horizontal ? new Vector3(Thick, Height, 2f * Half + 2f * Thick) : new Vector3(2f * Half + 2f * Thick, Height, Thick);
                box.sharedMaterial = ElementShapes.Plastic;
            }
            // Goals, Loaders and the Toggles' mounts: their own (light) meshes, fixed.
            foreach (var t in collision.GetComponentsInChildren<Transform>(true)) {
                var goal = t.name.StartsWith("276-9250-840");
                var loader = t.name.StartsWith("276-9250-830");
                var toggle = t.name.StartsWith("276-9250-120");
                if (!goal && !loader && !toggle) continue;
                foreach (var filter in t.GetComponentsInChildren<MeshFilter>(true)) {
                    if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                    if (toggle && filter.name.StartsWith("276-9250-023")) continue;   // the bar turns: made per run
                    if (filter.name.Contains("AprilTag") || filter.sharedMesh.bounds.size.magnitude < 0.4f) continue;
                    var go = new GameObject(filter.name) { layer = Sandbox.FieldLayer };
                    go.transform.SetParent(colliders, false);
                    go.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                    go.transform.localScale = filter.transform.lossyScale;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = filter.sharedMesh;
                    mc.sharedMaterial = ElementShapes.Plastic;
                }
            }
        }

        // ------------------------------------------------------------------ skills layout

        // Skills (RSC4): all Goals start empty; Match Loads and Preloads start off the field.
        private void HideForSkills() {
            foreach (var t in display.GetComponentsInChildren<Transform>(true)) {
                var pin = t.name.StartsWith("276-9250-80x");
                var cup = t.name.StartsWith("276-9250-81x");
                if (!pin && !cup) continue;
                var p = ToField(BoundsOf(t).center);
                var outside = Mathf.Abs(p.x) > Half + 0.3f || Mathf.Abs(p.y) > Half + 0.3f;
                var inGoal = pin && goals.Any(g => (g.position - p).magnitude < 2f);
                if (outside) stock.Add(t.gameObject);
                if (outside || inGoal) t.gameObject.SetActive(false);
            }
        }

        // V2: a match: only the pieces outside the field (Match Loads, Preloads) go; the Goals keep their pins.
        private void HideOffField() {
            foreach (var t in display.GetComponentsInChildren<Transform>(true)) {
                var pin = t.name.StartsWith("276-9250-80x");
                var cup = t.name.StartsWith("276-9250-81x");
                if (!pin && !cup) continue;
                var p = ToField(BoundsOf(t).center);
                if (Mathf.Abs(p.x) <= Half + 0.3f && Mathf.Abs(p.y) <= Half + 0.3f) continue;
                stock.Add(t.gameObject);
                t.gameObject.SetActive(false);
            }
        }

        /// <summary>A Match Load pin of these colors (from the stock outside the field), for its meshes.</summary>
        public GameObject StockPin(string colors) => stock.FirstOrDefault(s => s.name.StartsWith("276-9250-80x_" + colors));
        public GameObject StockCup() => stock.FirstOrDefault(s => s.name.StartsWith("276-9250-81x"));

        // ------------------------------------------------------------------ pieces for a run

        /// <summary>The pins, cups and toggles on the field as simulation pieces under `root`; the originals hide.</summary>
        public (List<Pin> pins, List<Cup> cups, List<Toggle> toggles) MakePieces(Transform root) {
            var pins = new List<Pin>();
            var cups = new List<Cup>();
            var toggles = new List<Toggle>();
            foreach (var t in display.GetComponentsInChildren<Transform>(false)) {
                if (t.name.StartsWith("276-9250-80x")) pins.Add(MakePin(t, root, t.position, t.rotation));
                else if (t.name.StartsWith("276-9250-81x")) cups.Add(MakeCup(t, root, t.position, t.rotation));
                else if (t.name.StartsWith("276-9250-120")) {
                    var toggle = MakeToggle(t, root);
                    if (toggle != null) toggles.Add(toggle);
                }
            }
            return (pins, cups, toggles);
        }

        public void ShowOriginals() {
            foreach (var r in hiddenDuringRun) if (r != null) r.enabled = true;
            hiddenDuringRun.Clear();
        }

        private GameObject Copy(Transform source, Transform root, Vector3 position, Quaternion rotation, string name, bool hide) {
            var go = new GameObject(name) { layer = Sandbox.ElementLayer };
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(position, rotation);
            foreach (var r in source.GetComponentsInChildren<MeshRenderer>(true)) {
                var part = new GameObject(r.name);
                part.transform.SetParent(go.transform, false);
                var local = Matrix4x4.TRS(source.position, source.rotation, Vector3.one).inverse * r.transform.localToWorldMatrix;
                part.transform.localPosition = local.GetColumn(3);
                part.transform.localRotation = local.rotation;
                part.AddComponent<MeshFilter>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                var copy = part.AddComponent<MeshRenderer>();
                copy.sharedMaterials = r.sharedMaterials;
                if (hide) {
                    r.enabled = false;
                    hiddenDuringRun.Add(r);
                }
            }
            return go;
        }

        // The halves of a piece: the light model's shape for that kind of half, placed like each drawn half.
        private List<(Mesh mesh, Matrix4x4 local, Renderer shown)> Halves(Transform source, Mesh shape) {
            var result = new List<(Mesh, Matrix4x4, Renderer)>();
            var toPiece = Matrix4x4.TRS(source.position, source.rotation, Vector3.one).inverse;
            foreach (var shown in source.GetComponentsInChildren<MeshRenderer>(true))
                result.Add((shape, toPiece * shown.transform.localToWorldMatrix, shown));
            return result;
        }

        public Pin MakePin(Transform source, Transform root, Vector3 position, Quaternion rotation, bool hide = true) {
            var go = Copy(source, root, position, rotation, "Pin", hide);
            var pin = go.AddComponent<Pin>();
            foreach (var (mesh, local, shown) in Halves(source, pinHalf)) {
                if (mesh == null) continue;
                ElementShapes.Convex(go.transform, mesh, local, Sandbox.ElementLayer);
                var direction = (Vector3)(local * new Vector4(0f, 0f, 1f, 0f));
                pin.halves.Add((direction.normalized, ElementShapes.ColorOf(shown)));
                pin.halfLength = Mathf.Max(pin.halfLength, mesh.bounds.max.z);
            }
            pin.body = ElementShapes.Body(go, ElementShapes.PinMass);
            return pin;
        }

        public Cup MakeCup(Transform source, Transform root, Vector3 position, Quaternion rotation, bool hide = true) {
            var go = Copy(source, root, position, rotation, "Cup", hide);
            var cup = go.AddComponent<Cup>();
            foreach (var (mesh, local, shown) in Halves(source, cupHalf)) {
                if (mesh == null) continue;
                ElementShapes.CupHalf(go.transform, mesh, local, Sandbox.ElementLayer);
                var direction = ((Vector3)(local * new Vector4(0f, 0f, 1f, 0f))).normalized;
                if (ElementShapes.ColorOf(shown) != PieceColor.Clear) cup.opaqueDirection = direction;
                cup.halfLength = mesh.bounds.max.z;
            }
            cup.body = ElementShapes.Body(go, ElementShapes.CupMass);
            return cup;
        }

        // A Toggle's bar (276-9250-023) turns on a hinge along its length, with its three faces colored.
        private Toggle MakeToggle(Transform assembly, Transform root) {
            var bar = assembly.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name.StartsWith("276-9250-023"));
            if (bar == null) return null;
            var twin = bar.transform;   // drawn bar's place, the light model's shape
            var mesh = toggleBar;
            if (mesh == null) return null;
            var center = bar.bounds.center;
            var go = new GameObject("Toggle") { layer = Sandbox.ElementLayer };
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(center, transform.rotation);   // in the field's axes
            var visual = new GameObject("Bar");
            visual.transform.SetParent(go.transform, false);
            visual.transform.SetPositionAndRotation(bar.transform.position, bar.transform.rotation);
            visual.AddComponent<MeshFilter>().sharedMesh = bar.GetComponent<MeshFilter>().sharedMesh;
            visual.AddComponent<MeshRenderer>().sharedMaterials = bar.sharedMaterials;
            bar.enabled = false;
            hiddenDuringRun.Add(bar);
            var local = go.transform.worldToLocalMatrix * twin.localToWorldMatrix;
            ElementShapes.Convex(go.transform, mesh, local, Sandbox.ElementLayer);

            var toggle = go.AddComponent<Toggle>();
            var points = mesh.vertices.Select(v => go.transform.InverseTransformPoint(twin.TransformPoint(v))).ToList();
            var spanX = points.Max(p => p.x) - points.Min(p => p.x);
            var spanZ = points.Max(p => p.z) - points.Min(p => p.z);
            toggle.axisLocal = spanX > spanZ ? Vector3.right : Vector3.forward;
            toggle.quadrant = QuadrantOf(ToField(center));
            // The bar's three flat faces: where its cross-section's support distance is least.
            var axis = toggle.axisLocal;
            var reference = Vector3.up;
            var size = new Vector3(spanX, 0f, spanZ);
            var support = new float[360];
            for (var a = 0; a < 360; a++) {
                var u = Quaternion.AngleAxis(a, axis) * reference;
                support[a] = points.Max(p => Vector3.Dot(p, u));
            }
            var faces = new List<int>();
            for (var a = 0; a < 360; a++) {
                var lowest = true;
                for (var d = -25; d <= 25 && lowest; d++)
                    if (d != 0 && support[(a + d + 360) % 360] < support[a]) lowest = false;
                if (lowest && faces.All(f => Mathf.Abs(Mathf.DeltaAngle(f, a)) > 40)) faces.Add(a);
            }
            if (faces.Count < 3) faces = new List<int> { 90, 210, 330 };   // fall back to a face on top
            var normals = faces.Take(3).Select(a => Quaternion.AngleAxis(a, axis) * reference).OrderByDescending(n => n.y).ToList();
            // Yellow on top (as at the start), then red and blue going round.
            var first = normals[0];
            var ordered = normals.OrderBy(n => (Vector3.SignedAngle(first, n, axis) + 360f) % 360f).ToList();
            var colors = new[] { PieceColor.Yellow, PieceColor.Red, PieceColor.Blue };
            var length = Mathf.Max(size.x, size.z) - 1.6f;
            for (var i = 0; i < 3; i++) {
                toggle.faces.Add((ordered[i], colors[i]));
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(strip.GetComponent<Collider>());
                strip.name = colors[i] + " Face";
                strip.layer = 0;   // drawn: the simulation layers are for colliders only
                strip.transform.SetParent(go.transform, false);
                var distance = points.Max(p => Vector3.Dot(p, ordered[i]));
                strip.transform.localPosition = ordered[i] * (distance + 0.01f);
                strip.transform.localRotation = Quaternion.LookRotation(ordered[i], axis);
                strip.transform.localScale = new Vector3(1.6f, length, 0.02f);
                strip.GetComponent<MeshRenderer>().sharedMaterial = FaceMaterial(colors[i]);
            }

            // The bar sits on its mounts and the wall's top: it only meets robots and pieces (a hinged bar pressed into
            // fixed shapes made PhysX push back on every step, 100 ms a frame).
            foreach (var mine in go.GetComponentsInChildren<Collider>())
                foreach (var fixedOne in colliders.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(mine, fixedOne, true);
            toggle.body = ElementShapes.Body(go, ElementShapes.ToggleMass);
            toggle.body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            var hinge = go.AddComponent<HingeJoint>();
            hinge.axis = axis;
            hinge.anchor = Vector3.zero;
            hinge.autoConfigureConnectedAnchor = true;
            return toggle;
        }

        private static readonly Dictionary<PieceColor, Material> faceMaterials = new();

        private Material FaceMaterial(PieceColor color) {
            if (faceMaterials.TryGetValue(color, out var m) && m != null) return m;
            var name = color switch { PieceColor.Red => "Field Red", PieceColor.Blue => "Field Blue", _ => "Field Yellow" };
            var source = display.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(x => x != null && x.name == name);
            m = source != null ? source : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            faceMaterials[color] = m;
            return m;
        }

        /// <summary>Where a red Match Load enters: above the Loader (it falls in).</summary>
        public Vector3 LoaderDrop(int loader, bool red = true) {
            var list = red ? loaders : blueLoaders;
            var p = list.Count > loader ? list[loader] : red ? new Vector2(-Half + 2f, -58.8f) : new Vector2(Half - 2f, 58.8f);
            return ToWorld(p, 15.5f);
        }
    }
}
