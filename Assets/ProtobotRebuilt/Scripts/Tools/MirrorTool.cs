using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools {
    // V2: Mirror (key M, Edit > Mirror, or Mirror in the Select tool's options, with a part selected): a mirror icon
    // sits on each side of the part along its own axes, like the transform gizmo's handles. Click one and the part
    // (with everything that moves with it: its screwed-together group; Shift: the part alone) is copied to that side,
    // mirrored through the plane of its outside face, so the copy touches it. Hold and drag the icon outward to put
    // the copy further away (the gap snaps to the transform tool's move step, and the mirror plane onto the build's centre line when it's
    // near it; Shift free). One undo step.
    // Real parts can't be turned inside out, so each copy is the part turned to look like its mirror image: flipped
    // through the plane of its own that it is symmetric about (found from its mesh), which makes a proper turn. A part
    // with no such plane (a left gusset) gets the closest turn, and the message says so. A copy landing on the same
    // part already there is left out. Drive motors swap sides (L <-> R) and turn the way the mirror image would.
    public class MirrorTool : MonoBehaviour {
        private static MirrorTool runner;
        private GameObject part;
        private int hovered = -1, dragging = -1;
        private float dragStart, gap;
        private Vector2 pressAt;
        private bool draggingAlone;

        private static readonly Color[] AxisColors = {
            new(0.93f, 0.27f, 0.27f), new(0.36f, 0.82f, 0.36f), new(0.27f, 0.55f, 0.98f)
        };

        public static bool Active => runner != null && runner.part != null;

        /// <summary>V2: shows the mirror icons on this part (from its right-click menu).</summary>
        public static void OpenFor(GameObject target) {
            if (target == null) return;
            if (runner == null) runner = new GameObject("Mirror Tool").AddComponent<MirrorTool>();
            runner.part = target;
            runner.pinned = true;
            runner.hovered = runner.dragging = -1;
            Interface.Interface.Say("Mirror: click an arrow to copy " + PartLabels.Short(target) + " to that side; drag it out for a gap");
        }

        private bool pinned;

        /// <summary>Shows the mirror icons on the selected part (or hides them again).</summary>
        public static void Open() {
            if (Active) { Stop("Mirror closed"); return; }
            var selected = SelectTool.SelectedPart;
            if (selected == null) {
                var key = Keybinds.Label("Mirror");
                Interface.Interface.Say("Select a part first, then press " + (key == "" ? "Mirror" : key), false);
                return;
            }
            if (runner == null) runner = new GameObject("Mirror Tool").AddComponent<MirrorTool>();
            runner.part = selected;
            runner.pinned = false;
            runner.hovered = runner.dragging = -1;
            Interface.Interface.Say("Mirror: click an arrow to copy " + PartLabels.Short(selected) + " to that side; drag it out for a gap");
        }

        private static void Stop(string message) {
            if (runner != null) {
                runner.part = null;
                runner.dragging = runner.hovered = -1;
            }
            if (message != null) Interface.Interface.Say(message);
        }

        private List<GameObject> Targets(bool alone) {
            if (alone) return new List<GameObject> { part };
            var group = part.GetConnectedObjects(true, true).Where(o => o != null && o.GetComponent<SavedObject>() != null && Live(o)).Distinct().ToList();
            return WithShafts(group);
        }

        /// <summary>V2: shafts aren't screwed into a group (they turn in it), so a drive side mirrored without them lost
        /// its motor shafts and the gears and spacers on them: every shaft through the parts goes too, with what's on it.</summary>
        public static List<GameObject> WithShafts(List<GameObject> parts) {
            var set = new HashSet<GameObject>(parts);
            var grew = true;
            while (grew) {
                grew = false;
                foreach (var shaft in FindObjectsByType<Shaft>(FindObjectsSortMode.None)) {
                    if (shaft == null || !Live(shaft.gameObject) || set.Contains(shaft.gameObject) || shaft.holeDetector == null) continue;
                    var on = shaft.holeDetector.holes.Where(h => h != null && h.holeData != null && h.holeData.part != null)
                        .Select(h => h.holeData.part.gameObject).Where(o => o.GetComponent<SavedObject>() != null && Live(o)).Distinct().ToList();
                    if (!on.Any(set.Contains)) continue;
                    set.Add(shaft.gameObject);
                    foreach (var o in on) set.Add(o);
                    grew = true;
                }
            }
            return parts.Concat(set.Where(o => !parts.Contains(o))).ToList();
        }

        private static bool Live(GameObject p) =>
            p != null && p.activeInHierarchy && p.layer != LayerMask.NameToLayer("Deleted");

        /// <summary>The parts' box in the part's own frame.</summary>
        private Bounds LocalBounds(List<GameObject> parts) {
            var inverse = part.transform.worldToLocalMatrix;
            var has = false;
            var box = new Bounds();
            foreach (var p in parts) {
                var filter = p.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                var toLocal = inverse * p.transform.localToWorldMatrix;
                for (var i = 0; i < 8; i++) {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var q = toLocal.MultiplyPoint3x4(corner);
                    if (!has) { box = new Bounds(q, Vector3.zero); has = true; }
                    else box.Encapsulate(q);
                }
            }
            return box;
        }

        public static float PerPixel(Camera camera, Vector3 at) {
            if (camera.orthographic) return camera.orthographicSize * 2f / Mathf.Max(1, camera.pixelHeight);
            var depth = Mathf.Max(0.01f, Vector3.Dot(at - camera.transform.position, camera.transform.forward));
            return 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight);
        }

        /// <summary>Along a world line, the point nearest the mouse's ray (distance from `origin`).</summary>
        public static float AlongLine(Camera camera, Vector3 origin, Vector3 direction) {
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            var w = origin - ray.origin;
            float a = Vector3.Dot(direction, direction), b = Vector3.Dot(direction, ray.direction), c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(direction, w), e = Vector3.Dot(ray.direction, w);
            var denominator = a * c - b * b;
            if (Mathf.Abs(denominator) < 1e-6f) return 0f;
            return (b * e - c * d) / denominator;
        }

        /// <summary>V2 (tutorial videos): where the icon of the side facing most toward a world direction is on screen
        /// (pixels), or null when the tool isn't showing.</summary>
        public static Vector2? IconToward(Vector3 world, float gap = 0f) {
            var tool = FindAnyObjectByType<MirrorTool>();
            var camera = Overlay.ViewCamera;
            if (tool == null || tool.part == null || camera == null) return null;
            var box = tool.LocalBounds(tool.Targets(false));
            var perPixel = PerPixel(camera, tool.part.transform.TransformPoint(box.center));
            var best = -1;
            var bestDot = -2f;
            for (var k = 0; k < 6; k++) {
                var dot = Vector3.Dot(tool.Side(box, k).direction, world.normalized);
                if (dot > bestDot) { bestDot = dot; best = k; }
            }
            var (direction, face) = tool.Side(box, best);
            var at = camera.WorldToScreenPoint(face + direction * (gap / 2f + 26f * perPixel));
            return new Vector2(at.x, at.y);
        }

        /// <summary>V2 (tutorial videos): the gap that puts the mirror plane of the side facing a world direction on
        /// the build's centre line (0 when the tool isn't showing).</summary>
        public static float CentreGap(Vector3 world) {
            var tool = FindAnyObjectByType<MirrorTool>();
            if (tool == null || tool.part == null) return 0f;
            var box = tool.LocalBounds(tool.Targets(false));
            var best = 0;
            var bestDot = -2f;
            for (var k = 0; k < 6; k++) {
                var dot = Vector3.Dot(tool.Side(box, k).direction, world.normalized);
                if (dot > bestDot) { bestDot = dot; best = k; }
            }
            var (direction, face) = tool.Side(box, best);
            return Mathf.Max(0f, -2f * Vector3.Dot(face, direction));
        }

        private (Vector3 direction, Vector3 face) Side(Bounds box, int k) {
            var axis = k / 2;
            var sign = k % 2 == 0 ? 1f : -1f;
            var local = Vector3.zero;
            local[axis] = sign;
            var faceLocal = box.center;
            faceLocal[axis] += sign * box.extents[axis];
            return (part.transform.TransformDirection(local).normalized, part.transform.TransformPoint(faceLocal));
        }

        private void Update() {
            if (part == null) return;
            var keyboard = Keyboard.current;
            if (!Live(part) || (!pinned && SelectTool.SelectedPart != part) || Placement.IsPlacing || Motion.Sandbox.Running) {
                Stop(null);
                return;
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputEvents.TextInput.IsTyping) {
                Stop("Mirror closed");
                return;
            }
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            var alone = dragging >= 0 ? draggingAlone : keyboard != null && keyboard.shiftKey.isPressed;
            var parts = Targets(alone);
            var box = LocalBounds(parts);
            var perPixel = PerPixel(camera, part.transform.TransformPoint(box.center));
            Overlay.Begin();

            var mouse = Overlay.Mouse;
            if (dragging < 0) hovered = -1;

            for (var k = 0; k < 6; k++) {
                var (direction, face) = Side(box, k);
                // An arrow pointing straight at the viewer (or away) would sit on the part's middle: leave it out.
                if (Mathf.Abs(Vector3.Dot(direction, camera.transform.forward)) > 0.96f && dragging != k) continue;
                var offset = dragging == k ? gap / 2f : 0f;
                var icon = face + direction * (offset + 26f * perPixel);
                if (!Overlay.ToScreen(icon, out var at) || !Overlay.ToScreen(face, out var faceAt)) continue;
                if (dragging < 0 && Vector2.Distance(mouse, at) < 15f) hovered = k;
                var color = AxisColors[k / 2];
                var lit = hovered == k || dragging == k;
                Overlay.Line(faceAt, at, new Color(color.r, color.g, color.b, 0.6f), 1.5f);
                Overlay.Disc(at, lit ? 13f : 11f, lit ? Color.Lerp(color, Color.white, 0.25f) : color, true);
                // The icon: a mirror line across the axis with a triangle on each side of it.
                var screenDir = (at - faceAt).sqrMagnitude > 1f ? (at - faceAt).normalized : Vector2.right;
                var across = new Vector2(-screenDir.y, screenDir.x);
                Overlay.Line(at - across * 6.5f, at + across * 6.5f, Color.white, 1.6f);
                Overlay.Arrowhead(at + screenDir * 7f, at + screenDir * 2f, Color.white, 4.5f, 7f);
                Overlay.Arrowhead(at - screenDir * 7f, at - screenDir * 2f, new Color(1f, 1f, 1f, 0.55f), 4.5f, 7f);
                if (lit) DrawPreview(part.transform, box, k / 2, k % 2 == 0 ? 1f : -1f, offset, color);
            }

            if (dragging >= 0) {
                var (direction, face) = Side(box, dragging);
                if (Vector2.Distance(mouse, pressAt) > 4f) {
                    var raw = Mathf.Max(0f, (AlongLine(camera, face, direction) - dragStart) * 2f);
                    var step = TransformGizmo.MoveStep;
                    var shift = keyboard != null && keyboard.shiftKey.isPressed;
                    gap = shift || step <= 0f ? raw : Mathf.Round(raw / step) * step;
                    // V2: near the build's centre line (the world plane through the origin square to a world axis), the
                    // mirror plane goes on it: a robot's two sides come out exactly symmetric about its middle.
                    var centre = false;
                    if (!shift && Mathf.Max(Mathf.Abs(direction.x), Mathf.Max(Mathf.Abs(direction.y), Mathf.Abs(direction.z))) > 0.999f) {
                        var onCentre = raw - 2f * Vector3.Dot(face + direction * (raw / 2f), direction);
                        if (onCentre >= 0f && Mathf.Abs(onCentre - raw) < Mathf.Max(step, 0.25f)) {
                            gap = onCentre;
                            centre = true;
                        }
                    }
                    Overlay.MouseText("gap " + Inch.Format(gap) + (centre ? " · on the centre line" : ""), Theme.Accent, null, true);
                }
                if (!MouseInput.LeftButton.isPressed) {
                    dragging = -1;
                    Mirror(parts, direction, face + direction * (gap / 2f), gap);
                    gap = 0f;
                    Stop(null);
                }
            }
            else if (hovered >= 0 && MouseInput.LeftButton.wasPressedThisFrame) {
                var (direction, face) = Side(box, hovered);
                dragging = hovered;
                draggingAlone = alone;
                gap = 0f;
                pressAt = mouse;
                dragStart = AlongLine(camera, face, direction);
            }
            if (hovered >= 0 && dragging < 0)
                Overlay.MouseText("Mirror " + (parts.Count == 1 ? PartLabels.Short(part) : parts.Count + " parts") + " (Shift: this part) · drag for a gap", Color.white);
        }

        /// <summary>The plane (at the face, moved out by `offset`) and the box the copy will fill.</summary>
        private static void DrawPreview(Transform frame, Bounds box, int axis, float sign, float offset, Color color) {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            Vector3 At(float a, float b, float along, float grow) {
                var p = box.center;
                p[axis] = along;
                p[u] += a * (box.extents[u] + grow);
                p[v] += b * (box.extents[v] + grow);
                return frame.TransformPoint(p);
            }
            var plane = box.center[axis] + sign * (box.extents[axis] + offset);
            var faint = new Color(color.r, color.g, color.b, 0.6f);
            var corners = new[] { (1f, 1f), (-1f, 1f), (-1f, -1f), (1f, -1f) };
            for (var i = 0; i < 4; i++)
                Overlay.Line(At(corners[i].Item1, corners[i].Item2, plane, 0.3f), At(corners[(i + 1) % 4].Item1, corners[(i + 1) % 4].Item2, plane, 0.3f), faint, 1.5f);
            // Where the copy goes: the same box, on the far side of the plane.
            var near = plane + sign * offset;
            var far = near + sign * box.size[axis];
            var ghost = new Color(1f, 1f, 1f, 0.5f);
            foreach (var along in new[] { near, far })
                for (var i = 0; i < 4; i++)
                    Overlay.Line(At(corners[i].Item1, corners[i].Item2, along, 0f), At(corners[(i + 1) % 4].Item1, corners[(i + 1) % 4].Item2, along, 0f), ghost, 1.2f);
            foreach (var (a, b) in corners)
                Overlay.Line(At(a, b, near, 0f), At(a, b, far, 0f), ghost, 1.2f);
        }

        // ------------------------------------------------------------------ mirroring

        /// <summary>Copies the parts to the other side of the plane (normal, point) in one undo step.</summary>
        public static void Mirror(List<GameObject> targets, Vector3 normal, Vector3 point, float gap = 0f) {
            targets = targets.Where(Live).ToList();
            if (targets.Count == 0) {
                Interface.Interface.Say("Nothing to mirror", false);
                return;
            }
            normal.Normalize();
            var mirror = Matrix4x4.identity;
            for (var r = 0; r < 3; r++)
                for (var col = 0; col < 3; col++)
                    mirror[r, col] = (r == col ? 1f : 0f) - 2f * normal[r] * normal[col];
            var shift = 2f * Vector3.Dot(point, normal) * normal;
            mirror.SetColumn(3, new Vector4(shift.x, shift.y, shift.z, 1f));

            var existing = PartsManager.FindLoadedObjects().Where(Live).ToList();
            var made = new List<GameObject>();
            int skipped = 0, chiral = 0, chains = 0;
            // Screws and other connecting parts last, as when a build loads (they look for the holes they go in).
            foreach (var part in targets.OrderBy(p => p.GetComponent<ConnectingPart>() != null ? 1 : 0)) {
                var saved = part.GetComponent<SavedObject>();
                if (saved.id.StartsWith("CHAN")) { chains++; continue; }   // a chain re-forms on its sprockets: make it again
                var filter = part.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                var (flip, symmetric) = SymmetryAxis(mesh);
                if (!symmetric) chiral++;
                var local = Matrix4x4.identity;
                var centre = mesh != null ? mesh.bounds.center : Vector3.zero;
                local[flip, flip] = -1f;
                local[flip, 3] = 2f * centre[flip];
                var result = mirror * part.transform.localToWorldMatrix * local;
                var position = (Vector3)result.GetColumn(3);
                var rotation = Quaternion.LookRotation(result.GetColumn(2), result.GetColumn(1));

                // The same part already there (it lies across the plane, or nearly: two identical parts can't be in
                // one place): leave it.
                var landing = position + rotation * Vector3.Scale(part.transform.lossyScale, centre);
                bool Same(GameObject o) => o.GetComponent<SavedObject>().id == saved.id
                                           && (o.transform.TransformPoint(centre) - landing).sqrMagnitude < 0.3f * 0.3f;
                if (Same(part) || existing.Any(Same)) {
                    skipped++;
                    continue;
                }

                var copy = PartsManager.GeneratePart(saved.id, position, rotation);
                if (copy == null) continue;
                CopyLook(part, copy);
                if (!string.IsNullOrEmpty(saved.state) && copy.TryGetComponent(out SavedObject copySaved)) {
                    copySaved.state = saved.state;
                    PartDrilling.ApplySaved(copy);
                    MirrorControls(part, copy, mirror);
                }
                made.Add(copy);
            }
            if (made.Count == 0) {
                Interface.Interface.Say(skipped > 0 ? "Every mirrored copy would land on a part that's already there" : "Nothing could be mirrored", false);
                return;
            }

            // One undo step: the copies didn't exist before.
            foreach (var copy in made) StateSystem.AddElement(new ObjectElement(copy) { existing = false });
            StateSystem.AddState(new State(made.Select(c => (IElement)new ObjectElement(c)).ToList()));
            var list = Object.FindObjectOfType<PartListOutput>();
            if (list != null) list.CalculatePartsList();

            var message = "Mirrored " + (made.Count == 1 ? PartLabels.Short(made[0]) : made.Count + " parts") + (gap > 0.001f ? ", " + Inch.Format(gap) + " apart" : "");
            if (skipped > 0) message += "; " + skipped + " already there";
            if (chiral > 0) message += "; " + chiral + (chiral == 1 ? " part has" : " parts have") + " no mirror image of its own (turned to match)";
            if (chains > 0) message += "; make the chains again with the Chain tool";
            Interface.Interface.Say(message);
            Audio.Sounds.Play(Audio.Sounds.Clack);
        }

        private static void CopyLook(GameObject from, GameObject to) {
            if (!from.TryGetComponent(out Renderer source) || !to.TryGetComponent(out Renderer target)) return;
            var a = source.materials;
            var b = target.materials;
            for (var i = 0; i < a.Length && i < b.Length; i++)
                if (a[i] != null && b[i] != null) b[i].color = a[i].color;
        }

        /// <summary>Drive motors swap sides, and every motor turns the way its mirror image would.</summary>
        private static void MirrorControls(GameObject from, GameObject to, Matrix4x4 mirror) {
            var side = Motion.Controls.Side(from);
            if (side == "L" || side == "R") {
                // Sides swap when the plane runs front to back through the robot (its normal across the front).
                // The plane's normal: M = I - 2nn^T, so (I - M) has columns 2n n_i.
                var normal = Vector3.zero;
                for (var c = 0; c < 3; c++) {
                    var column = new Vector3(c == 0 ? 1f : 0f, c == 1 ? 1f : 0f, c == 2 ? 1f : 0f) - (Vector3)mirror.GetColumn(c);
                    if (column.sqrMagnitude > normal.sqrMagnitude) normal = column;
                }
                normal.Normalize();
                var front = FrontAxis();
                if (Mathf.Abs(normal.y) < 0.7f && Mathf.Abs(Vector3.Dot(normal, front)) < 0.5f) Motion.Controls.SetSide(to, side == "L" ? "R" : "L");
            }
            var a = from.GetComponent<Motion.MotorDrive>();
            var b = to.GetComponent<Motion.MotorDrive>();
            if (a == null || b == null) return;
            // A turn is an axial vector: through a mirror it becomes -(M * axis).
            var wanted = -(Vector3)(mirror.MultiplyVector(a.Forward));
            var reversed = Motion.Controls.Reversed(from);
            if (Vector3.Dot(b.Forward, wanted) < 0f) reversed = !reversed;
            Motion.Controls.SetReversed(to, reversed);
        }

        /// <summary>The robot's front: across the line from its left drive motors to its right ones, else +Z.</summary>
        private static Vector3 FrontAxis() {
            Vector3 left = Vector3.zero, right = Vector3.zero;
            int l = 0, r = 0;
            foreach (var motor in Motion.MotorDrive.Live()) {
                var side = Motion.Controls.Side(motor.gameObject);
                if (side == "L") { left += motor.transform.position; l++; }
                else if (side == "R") { right += motor.transform.position; r++; }
            }
            if (l == 0 || r == 0) return Vector3.forward;
            var across = right / r - left / l;
            across.y = 0f;
            return across.sqrMagnitude < 1e-4f ? Vector3.forward : Vector3.Cross(Vector3.up, across.normalized);
        }

        // ------------------------------------------------------------------ the part's own symmetry

        private static readonly Dictionary<int, (int, bool)> symmetry = new();

        /// <summary>The local axis whose plane (through the mesh's middle) the mesh is most nearly symmetric about,
        /// and whether it truly is.</summary>
        private static (int axis, bool symmetric) SymmetryAxis(Mesh mesh) {
            if (mesh == null || !mesh.isReadable) return (0, true);
            if (symmetry.TryGetValue(mesh.GetInstanceID(), out var known)) return known;
            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            var cell = Mathf.Max(0.02f, bounds.size.magnitude / 60f);
            var grid = new Dictionary<Vector3Int, List<Vector3>>();
            Vector3Int Key(Vector3 p) => new(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
            foreach (var v in vertices) {
                var key = Key(v);
                if (!grid.TryGetValue(key, out var bucket)) grid[key] = bucket = new List<Vector3>();
                bucket.Add(v);
            }
            float Nearest(Vector3 p) {
                var key = Key(p);
                var best = cell * 2f;
                for (var x = -1; x <= 1; x++)
                    for (var y = -1; y <= 1; y++)
                        for (var z = -1; z <= 1; z++)
                            if (grid.TryGetValue(new Vector3Int(key.x + x, key.y + y, key.z + z), out var bucket))
                                foreach (var q in bucket) best = Mathf.Min(best, (q - p).magnitude);
                return best;
            }
            var step = Mathf.Max(1, vertices.Length / 500);
            var scores = new float[3];
            for (var k = 0; k < 3; k++) {
                float sum = 0f;
                var n = 0;
                for (var i = 0; i < vertices.Length; i += step) {
                    var p = vertices[i];
                    p[k] = 2f * bounds.center[k] - p[k];
                    sum += Nearest(p);
                    n++;
                }
                scores[k] = n > 0 ? sum / n : 0f;
            }
            var bestAxis = 0;
            for (var k = 1; k < 3; k++) if (scores[k] < scores[bestAxis] - 1e-5f) bestAxis = k;
            var result = (bestAxis, scores[bestAxis] < Mathf.Max(0.01f, cell * 0.25f));
            symmetry[mesh.GetInstanceID()] = result;
            return result;
        }
    }
}
