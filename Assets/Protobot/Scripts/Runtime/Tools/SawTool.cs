using System.Collections;
using Protobot.Audio;
using System.Collections.Generic;
using Protobot.Core;
using Protobot.Geometry;
using Protobot.Interaction;
using Protobot.Menus;
using Protobot.Parts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// Hack saw for metal stock and shafts. Aim at a part: a cut plane appears where you aim (snapping to the
    /// chosen step, 1/4 in by default, so cuts land through a hole or between two), the piece that would be
    /// removed turns see-through red, and left click saws it off; the offcut becomes a part of its own. The
    /// cut can be angled about the part's X, Y and Z axes (up to 90°): the wheel turns Y, Shift + wheel X,
    /// Ctrl + wheel Z, in angle steps that right click changes. F flips which side is kept; holding right
    /// click opens the precise cut menu. A cut that wouldn't go through any metal isn't allowed.
    /// </summary>
    public class SawTool : PlayerTool
    {
        public const float HolePitch = 0.0127f;          // VEX hole spacing: 1/2 inch
        public const float MaxAngle = 90f;
        const float MinVolume = 2e-9f;                   // 2 mm³: anything less isn't really a cut
        const float MinOffcutVolume = 4e-8f;             // 40 mm³: smaller offcuts are swarf, not a new part
        static readonly int[] AngleSteps = { 5, 10, 15, 30, 45 };

        /// <summary>Where cuts snap along the part (inches; 0 = no snap). Shared by aiming and the precise cut menu.</summary>
        public static readonly (string label, float inches)[] SnapChoices =
        {
            ("Snap to 1/2 in", 0.5f), ("Snap to 1/4 in", 0.25f), ("Snap to 1/8 in", 0.125f), ("Snap to 1/16 in", 0.0625f), ("No snap", 0f),
        };

        public static int SnapChoice { get; set; } = 1;
        public static float SnapStep => Units.FromInches(SnapChoices[SnapChoice].inches);

        [SerializeField] SawMenu menu;
        [SerializeField] Sprite crosshair;

        int angleStepIndex = 2;
        Vector3 angles;   // about the part's X, Y and Z (degrees, each ±90)
        bool flip;
        bool cutting;

        // Current preview (cached until the cut changes)
        PartBody previewPart;
        string previewKey;
        bool previewValid;
        string previewProblem;
        float previewKept, previewRemoved, previewRemovedVolume;
        readonly List<(MeshRenderer source, Mesh kept, Mesh removed)> previewMeshes = new();
        readonly List<GameObject> previewObjects = new();
        CutPlan plan;

        // Menu mode: the plan is frozen on this part and driven by the menu's numbers.
        PartBody lockedPart;
        float lockedT;

        public int AngleStep => AngleSteps[angleStepIndex];
        public Vector3 Angles => angles;
        public bool Flip => flip;
        public float LockedT => lockedT;

        /// <summary>A cut in the part's own space.</summary>
        public struct CutPlan
        {
            public bool valid;
            public string problem;
            public Vector3 point;
            public Vector3 keepNormal;
            public float t;              // position along the part (m, from the profile origin)
            public float keptLength;     // how long each piece ends up along the part
            public float removedLength;
        }

        public override string ControlsHint =>
            $"Left click: cut   ·   Wheel: angle Y {angles.y:0}° (Shift: X {angles.x:0}°, Ctrl: Z {angles.z:0}°)   ·   Right click: {AngleStep}° steps   ·   F: flip side   ·   Hold right: precise cut";

        public override void OnUnequip() => ClearPreview();
        public override void OnSuspend()
        {
            if (lockedPart == null)
                ClearPreview();
        }

        public override void Tick()
        {
            if (cutting)
                return;
            var keyboard = Keyboard.current;
            if (keyboard.fKey.wasPressedThisFrame)
            {
                flip = !flip;
                PlaySound(Sounds.Tick, 0.3f);
            }

            var part = AimedStock(out var hit);
            if (part == null)
            {
                ClearPreview();
                return;
            }
            plan = Plan(part, hit.point, keyboard.altKey.isPressed);
            ShowPreview(part);
        }

        public override void Scroll(int notches)
        {
            var keyboard = Keyboard.current;
            var axis = keyboard.ctrlKey.isPressed ? 2 : keyboard.shiftKey.isPressed ? 0 : 1;
            angles[axis] = Mathf.Clamp(angles[axis] + notches * AngleStep, -MaxAngle, MaxAngle);
            PlaySound(Sounds.Tick, 0.25f);
        }

        public override void SecondaryTap()
        {
            angleStepIndex = (angleStepIndex + 1) % AngleSteps.Length;
            for (var i = 0; i < 3; i++)
                angles[i] = Mathf.Round(angles[i] / AngleStep) * AngleStep;
            Hud.ShowToast($"Angle steps: {AngleStep}°", 1.2f);
            PlaySound(Sounds.Tick, 0.3f);
        }

        public override void SecondaryHold()
        {
            var part = AimedStock(out var hit);
            if (part == null)
            {
                Hud.ShowError("Aim at a piece of metal or a shaft to open the precise cut menu");
                return;
            }
            lockedPart = part;
            lockedT = Plan(part, hit.point, false).t;
            menu.Open(this, part);
        }

        public override void PrimaryDown()
        {
            if (cutting)
                return;
            if (previewPart == null)
            {
                if (Cursor.Current != null && Cursor.CurrentInReach)
                    Hud.ShowError(Cursor.Current.GetComponentInParent<PartBody>() != null
                        ? "The saw cuts metal stock and shafts" : "Aim at a part to cut it");
                return;
            }
            if (!plan.valid)
            {
                Hud.ShowError(plan.problem);
                return;
            }
            StartCoroutine(CutRoutine(previewPart));
        }

        public override bool DescribeTarget(HoverTarget target, bool inReach, ref CursorView view)
        {
            view.reticle = crosshair;
            view.large = true;
            view.reticleColor = Color.white;
            if (target == null)
                return true;
            var part = target.GetComponentInParent<PartBody>();
            if (part == null)
            {
                view.reticleColor = new Color(1f, 1f, 1f, 0.5f);
                return true;
            }
            if (part.GetComponent<StockProfile>() == null)
            {
                view.hint = "The saw cuts metal stock and shafts";
                view.available = false;
                view.reticleColor = new Color(1f, 1f, 1f, 0.5f);
                return true;
            }
            if (!inReach || previewPart != part)
                return true;
            view.hint = plan.valid
                ? $"Keeps {Describe(plan.keptLength, part)}   ·   cuts off {Describe(plan.removedLength, part)}"
                : plan.problem;
            view.available = plan.valid;
            view.reticleColor = plan.valid ? Color.white : new Color(1f, 0.6f, 0.4f);
            return true;
        }

        // ---------------------------------------------------------------- planning

        PartBody AimedStock(out RaycastHit hit)
        {
            hit = Cursor.CurrentHit;
            var target = Cursor.Current;
            if (target == null || !Cursor.CurrentInReach)
                return null;
            var part = target.GetComponentInParent<PartBody>();
            return part != null && part.GetComponent<StockProfile>() != null ? part : null;
        }

        CutPlan Plan(PartBody part, Vector3 worldPoint, bool free)
        {
            var profile = part.GetComponent<StockProfile>();
            var t = profile.AlongAxis(part.transform.InverseTransformPoint(worldPoint));
            // Snap along the part, counted from its end, so cuts land through or between holes.
            var step = free || SnapStep <= 0f ? 0.0005f : SnapStep;
            t = profile.Start + Mathf.Round((t - profile.Start) / step) * step;
            return PlanAt(part, t);
        }

        /// <summary>The cut plane's direction: the part's length axis turned by the X, Y, Z angles.</summary>
        public static Vector3 CutNormal(StockProfile profile, Vector3 eulerXYZ) =>
            Quaternion.AngleAxis(eulerXYZ.z, profile.Axis) * Quaternion.AngleAxis(eulerXYZ.y, profile.V)
            * Quaternion.AngleAxis(eulerXYZ.x, profile.U) * profile.Axis;

        public CutPlan PlanAt(PartBody part, float t)
        {
            var profile = part.GetComponent<StockProfile>();
            var cut = new CutPlan { t = t, valid = true };
            var normal = CutNormal(profile, angles);
            cut.point = profile.Origin + profile.Axis * t;

            // Keep the side the player is on (the far side is what gets cut off), unless flipped.
            var camLocal = part.transform.InverseTransformPoint(View.transform.position);
            var keepPositive = Vector3.Dot(camLocal - cut.point, normal) >= 0f;
            if (flip)
                keepPositive = !keepPositive;
            cut.keepNormal = keepPositive ? normal : -normal;
            return cut;
        }

        public void SetMenuValues(float t, Vector3 newAngles, bool flipSide)
        {
            lockedT = t;
            angles = newAngles;
            flip = flipSide;
            if (lockedPart != null)
            {
                plan = PlanAt(lockedPart, lockedT);
                ShowPreview(lockedPart);
            }
        }

        /// <summary>The menu's view of the current cut (for its summary line).</summary>
        public CutPlan CurrentPlan => plan;

        /// <summary>Called by the precise cut menu.</summary>
        public void CutFromMenu()
        {
            if (lockedPart == null)
                return;
            plan = PlanAt(lockedPart, lockedT);
            ShowPreview(lockedPart);
            var part = lockedPart;
            lockedPart = null;
            if (!plan.valid)
            {
                Hud.ShowError(plan.problem);
                ClearPreview();
                return;
            }
            StartCoroutine(CutRoutine(part));
        }

        public void CancelMenu()
        {
            lockedPart = null;
            ClearPreview();
        }

        /// <summary>A length the way VEX builders talk about it: inches, plus the hole count on structure.</summary>
        public static string Describe(float length, PartBody part)
        {
            var inches = Units.Inches(length);
            if (IsShaft(part))
                return inches;
            var holes = Mathf.RoundToInt(length / HolePitch);
            return holes < 1 ? inches : $"{inches} (~{holes} {(holes == 1 ? "hole" : "holes")})";
        }

        static bool IsShaft(PartBody part) => part != null && part.PartId != null && part.PartId.StartsWith("Motion/Shaft");

        // ---------------------------------------------------------------- preview

        void ShowPreview(PartBody part)
        {
            var cut = plan;
            var key = $"{part.GetInstanceID()}|{cut.point.x:F5}|{cut.point.y:F5}|{cut.point.z:F5}|{cut.keepNormal.x:F4}|{cut.keepNormal.y:F4}|{cut.keepNormal.z:F4}|{lockedPart != null}";
            if (key != previewKey)
                BuildPreview(part, cut, key);
            plan.valid = previewValid;
            plan.problem = previewProblem;
            plan.keptLength = previewKept;
            plan.removedLength = previewRemoved;
        }

        void BuildPreview(PartBody part, CutPlan cut, string key)
        {
            ClearPreview();
            previewKey = key;
            previewPart = part;
            var profile = part.GetComponent<StockProfile>();

            // Slice every full-detail mesh both ways and measure what each side would keep.
            float keptVolume = 0f, removedVolume = 0f;
            float keptMin = float.MaxValue, keptMax = float.MinValue, removedMin = float.MaxValue, removedMax = float.MinValue;
            foreach (var r in PartShape.DetailRenderers(part.gameObject))
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var plane = LocalPlane(part.transform, r.transform, cut.point, cut.keepNormal);
                var kept = MeshSlicer.Slice(mesh, plane);
                var removed = MeshSlicer.Slice(mesh, plane.flipped);
                previewMeshes.Add((r, kept, removed));
                var toPart = part.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                if (kept != null)
                {
                    keptVolume += MeshSlicer.Volume(kept);
                    Extent(kept, toPart, profile, ref keptMin, ref keptMax);
                }
                if (removed != null)
                {
                    removedVolume += MeshSlicer.Volume(removed);
                    Extent(removed, toPart, profile, ref removedMin, ref removedMax);
                }
            }
            previewKept = keptMax > keptMin ? keptMax - keptMin : 0f;
            previewRemoved = removedMax > removedMin ? removedMax - removedMin : 0f;
            previewRemovedVolume = removedVolume;
            previewValid = keptVolume >= MinVolume && removedVolume >= MinVolume;
            previewProblem = previewValid ? null
                : keptVolume < MinVolume && removedVolume < MinVolume ? "Can't cut here"
                : "That cut doesn't go through any metal — move it onto the part";

            if (previewValid)
            {
                foreach (var (r, kept, removed) in previewMeshes)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    var plane = LocalPlane(part.transform, r.transform, cut.point, cut.keepNormal);
                    foreach (var loop in MeshSlicer.Section(mesh.vertices, mesh.triangles, plane))
                        previewObjects.Add(KerfLine(r.transform, loop.points3D));
                    if (kept != null)
                        previewObjects.Add(PreviewObject(r, kept, r.sharedMaterials, true));
                    if (removed != null)
                    {
                        var ghost = new Material[removed.subMeshCount];
                        for (var i = 0; i < ghost.Length; i++)
                            ghost[i] = ToolMaterials.Removed;
                        previewObjects.Add(PreviewObject(r, removed, ghost, false));
                    }
                }
                foreach (var r in part.GetComponentsInChildren<Renderer>())
                    if (!r.name.StartsWith(PartShape.PreviewName))
                        r.forceRenderingOff = true;
            }
            previewObjects.Add(CutPlane(part, cut, previewValid));
            if (lockedPart != null)
                previewObjects.Add(AxisGizmo(part, cut));
        }

        static void Extent(Mesh mesh, Matrix4x4 toPart, StockProfile profile, ref float min, ref float max)
        {
            foreach (var v in mesh.vertices)
            {
                var along = profile.AlongAxis(toPart.MultiplyPoint3x4(v));
                min = Mathf.Min(min, along);
                max = Mathf.Max(max, along);
            }
        }

        /// <summary>A bright line around the metal where the blade will cut (in the mesh's own space).</summary>
        static GameObject KerfLine(Transform meshTransform, List<Vector3> points)
        {
            var go = new GameObject(PartShape.PreviewName + " (kerf)");
            go.transform.SetParent(meshTransform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
            line.widthMultiplier = 0.0012f;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = ToolMaterials.KerfLine;
            return go;
        }

        /// <summary>A see-through sheet where the blade will go (red when the cut isn't possible).</summary>
        static GameObject CutPlane(PartBody part, CutPlan cut, bool valid)
        {
            var profile = part.GetComponent<StockProfile>();
            var across = profile.CrossSectionRadius * 2f + 0.012f;
            // The steeper the cut, the further the sheet reaches along the part (all of it for a lengthwise cut).
            var normal = cut.keepNormal.normalized;
            var cos = Mathf.Abs(Vector3.Dot(normal, profile.Axis));
            var alongPart = profile.Axis - normal * Vector3.Dot(profile.Axis, normal);
            var up = alongPart.sqrMagnitude > 1e-6f ? alongPart.normalized : Vector3.Cross(normal, profile.U).normalized;
            var reach = Mathf.Min(across / Mathf.Max(cos, 0.05f), profile.Length + 0.02f);
            // Centred where the sheet meets the middle of the remaining metal.
            var middle = profile.Origin + profile.Axis * ((profile.Start + profile.End) / 2f);
            var centre = cos > 0.7f ? cut.point : middle - normal * Vector3.Dot(middle - cut.point, normal);
            var go = new GameObject(PartShape.PreviewName + " (cut plane)") { layer = part.gameObject.layer };
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.LookRotation(normal, up);
            go.transform.localScale = new Vector3(across, reach, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            go.AddComponent<MeshRenderer>().sharedMaterial = valid ? ToolMaterials.CutPlane : ToolMaterials.CutPlaneBad;
            return go;
        }

        /// <summary>The part's X (red), Y (green) and Z (blue) axes at the cut, so the menu's angles make sense.</summary>
        static GameObject AxisGizmo(PartBody part, CutPlan cut)
        {
            var profile = part.GetComponent<StockProfile>();
            var length = profile.CrossSectionRadius + 0.015f;
            var root = new GameObject(PartShape.PreviewName + " (axes)") { layer = part.gameObject.layer };
            root.transform.SetParent(part.transform, false);
            var axes = new[] { (profile.U, new Color(0.95f, 0.25f, 0.2f)), (profile.V, new Color(0.3f, 0.85f, 0.3f)), (profile.Axis, new Color(0.25f, 0.5f, 1f)) };
            foreach (var (direction, color) in axes)
            {
                var go = new GameObject("Axis");
                go.transform.SetParent(root.transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 2;
                line.SetPositions(new[] { cut.point, cut.point + direction * length });
                line.widthMultiplier = 0.0015f;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.sharedMaterial = ToolMaterials.Unlit(color);
            }
            return root;
        }

        static Mesh quad;

        static Mesh Quad()
        {
            if (quad != null)
                return quad;
            quad = new Mesh { name = "Cut plane" };
            quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            quad.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quad.RecalculateNormals();
            return quad;
        }

        static Plane LocalPlane(Transform part, Transform meshTransform, Vector3 partPoint, Vector3 partNormal)
        {
            var point = meshTransform.InverseTransformPoint(part.TransformPoint(partPoint));
            var normal = meshTransform.InverseTransformDirection(part.TransformDirection(partNormal)).normalized;
            return new Plane(normal, point);
        }

        static MaterialPropertyBlock copiedBlock;

        static GameObject PreviewObject(MeshRenderer source, Mesh mesh, Material[] materials, bool keepLook)
        {
            var go = new GameObject(PartShape.PreviewName) { layer = source.gameObject.layer };
            go.transform.SetParent(source.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            if (keepLook)
            {
                // Paint (and the hover highlight) live in property blocks on the original renderer.
                copiedBlock ??= new MaterialPropertyBlock();
                for (var i = 0; i < materials.Length; i++)
                {
                    source.GetPropertyBlock(copiedBlock, i);
                    r.SetPropertyBlock(copiedBlock, i);
                }
            }
            return go;
        }

        void ClearPreview()
        {
            if (previewPart != null)
                foreach (var r in previewPart.GetComponentsInChildren<Renderer>())
                    r.forceRenderingOff = false;
            foreach (var go in previewObjects)
            {
                if (go == null)
                    continue;
                go.transform.SetParent(null, false);  // Destroy waits for the frame's end; don't let a copy of the part take it along
                Destroy(go);
            }
            previewObjects.Clear();
            previewMeshes.Clear();
            previewPart = null;
            previewKey = null;
        }

        // ---------------------------------------------------------------- cutting

        IEnumerator CutRoutine(PartBody part)
        {
            cutting = true;
            PlaySound(Sounds.Saw, 0.55f);
            // A few saw strokes on the tool in hand.
            if (ViewModel != null)
            {
                var start = ViewModel.transform.localPosition;
                for (var time = 0f; time < 0.45f; time += Time.deltaTime)
                {
                    ViewModel.transform.localPosition = start + new Vector3(0f, 0f, Mathf.Sin(time * 40f) * 0.04f);
                    yield return null;
                }
                ViewModel.transform.localPosition = start;
            }

            if (part != null && previewPart == part)
                ApplyCut(part);
            cutting = false;
        }

        void ApplyCut(PartBody part)
        {
            var volumeBefore = PartShape.Volume(part.gameObject);
            var cut = plan;
            var offcutVolume = previewRemovedVolume;
            var meshes = new List<(MeshRenderer source, Mesh kept, Mesh removed)>(previewMeshes);
            ClearPreview();

            // The offcut becomes a part of its own (a copy that keeps the other side), so both pieces of
            // the stock stay usable. Tiny slivers just fall away as swarf.
            GameObject offcut = null;
            if (offcutVolume >= MinOffcutVolume)
            {
                offcut = Instantiate(part.gameObject, part.transform.position, part.transform.rotation, part.transform.parent);
                offcut.name = part.name;
                foreach (var (source, _, removed) in meshes)
                {
                    var copy = source != null ? offcut.transform.Find(RelativePath(part.transform, source.transform)) : null;
                    if (copy == null)
                        continue;
                    if (removed == null)
                        Destroy(copy.gameObject);
                    else
                        copy.GetComponent<MeshFilter>().sharedMesh = removed;
                }
            }

            foreach (var (source, kept, _) in meshes)
            {
                if (source == null)
                    continue;
                if (kept == null)
                    Destroy(source.gameObject);
                else
                    source.GetComponent<MeshFilter>().sharedMesh = kept;
            }
            FinishPiece(part.gameObject, cut.point, cut.keepNormal, volumeBefore);

            if (offcut != null)
            {
                PlaySound(Sounds.Clack, 0.35f);
                FinishPiece(offcut, cut.point, -cut.keepNormal, volumeBefore);
                if (part.TryGetComponent(out HoverTarget hover) && offcut.TryGetComponent(out HoverTarget offcutHover))
                    offcutHover.CopyPaintFrom(hover);  // color overrides don't survive the copy
                // The offcut comes away from the kept piece (as it would when the blade breaks through), so
                // the cut is easy to see.
                var away = -part.transform.TransformDirection(cut.keepNormal);
                var body = offcut.GetComponent<Rigidbody>();
                body.position += away * 0.002f;
                body.linearVelocity = away * 0.15f;
                Hud.ShowToast($"Cut: kept {Describe(cut.keptLength, part)}, offcut {Describe(cut.removedLength, part)}");
            }
            else
            {
                Hud.ShowToast($"Cut: kept {Describe(cut.keptLength, part)} (the sliver fell away)");
            }
        }

        /// <summary>After its mesh was cut: LODs off, colliders clipped, the cut recorded, mass and tooltip updated.</summary>
        static void FinishPiece(GameObject piece, Vector3 point, Vector3 keepNormal, float volumeBefore)
        {
            PartShape.DropLods(piece);
            var profile = piece.GetComponent<StockProfile>();
            float min = float.MaxValue, max = float.MinValue;
            foreach (var r in PartShape.DetailRenderers(piece))
                Extent(r.GetComponent<MeshFilter>().sharedMesh, piece.transform.worldToLocalMatrix * r.transform.localToWorldMatrix, profile, ref min, ref max);
            profile.AddCut(point, keepNormal, min < max ? min : profile.Start, min < max ? max : profile.End);
            PartModifications.On(piece).cuts.Add(new PartModifications.Cut { point = point, keepNormal = keepNormal });
            PartShape.AfterEdit(piece, volumeBefore);
            Rename(piece, profile.Length);
        }

        /// <summary>
        /// Names say how long stock is ("1x2x1 Aluminum C-Channel (35)", "24" HS Shaft"); after a cut they say
        /// what's left: "(20)", "5.5" HS Shaft".
        /// </summary>
        static void Rename(GameObject piece, float length)
        {
            var name = piece.name;
            var holes = System.Text.RegularExpressions.Regex.Match(name, @"\((\d+)\)$");
            if (holes.Success)
                name = name.Substring(0, holes.Index) + $"({Mathf.Max(1, Mathf.RoundToInt(length / HolePitch))})";
            else if (System.Text.RegularExpressions.Regex.IsMatch(name, "^[\\d.]+\" "))
                name = $"{Units.ToInches(length).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\"" + name.Substring(name.IndexOf('"') + 1);
            piece.name = name;
            if (piece.TryGetComponent(out HoverTarget hover))
                hover.Configure(hover.Cursor, name, hover.Hint);
        }

        static string RelativePath(Transform root, Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
