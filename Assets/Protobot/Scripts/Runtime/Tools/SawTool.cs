using System.Collections;
using System.Collections.Generic;
using Protobot.Geometry;
using Protobot.Interaction;
using Protobot.Menus;
using Protobot.Parts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// Hack saw for metal stock and shafts. Aim at a part: a cut plane appears where you aim (snapping to
    /// every half hole, so cuts land through a hole or between two), the piece that would be removed turns
    /// see-through red, and left click saws it off. The removed side is the one away from you (F flips it).
    /// Mouse wheel angles the cut (Shift + wheel tilts it the other way) in snap steps; right click changes
    /// the snap step; holding right click opens the precise cut menu with exact numbers.
    /// </summary>
    public class SawTool : PlayerTool
    {
        public const float HolePitch = 0.0127f;      // VEX hole spacing: 1/2 inch
        const float MinPiece = 0.003f;               // don't leave slivers shorter than 3 mm
        const float MinOffcut = 0.01f;               // offcuts shorter than this are swarf, not a new part
        static readonly int[] SnapSteps = { 5, 10, 15, 30, 45 };

        [SerializeField] SawMenu menu;
        [SerializeField] Sprite crosshair;

        int snapIndex = 2;
        float angleA;
        float angleB;
        bool flip;
        bool cutting;

        // Current preview
        PartBody previewPart;
        string previewKey;
        readonly List<(MeshRenderer source, Mesh kept, Mesh removed)> previewMeshes = new();
        readonly List<GameObject> previewObjects = new();
        CutPlan plan;

        // Menu mode: the plan is frozen on this part and driven by the menu's numbers.
        PartBody lockedPart;
        float lockedT;

        public int SnapDegrees => SnapSteps[snapIndex];

        /// <summary>A cut in the part's own space.</summary>
        public struct CutPlan
        {
            public bool valid;
            public string problem;
            public Vector3 point;
            public Vector3 keepNormal;
            public float t;              // position along the part (m, from the profile origin)
            public float keptLength;     // along the part's length
            public float removedLength;
        }

        public override string ControlsHint =>
            $"Left click: cut   ·   Wheel: angle {angleA:0}°  ·  Shift+wheel: tilt {angleB:0}°   ·   Right click: snap {SnapDegrees}°   ·   F: flip side   ·   Hold right: precise";

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
                flip = !flip;
            if (keyboard.rKey.wasPressedThisFrame)
                angleA = angleB = 0f;

            var part = AimedStock(out var hit);
            if (part == null)
            {
                ClearPreview();
                return;
            }
            var free = keyboard.altKey.isPressed;
            plan = Plan(part, hit.point, free);
            ShowPreview(part, plan);
        }

        public override void Scroll(int notches)
        {
            var shift = Keyboard.current.shiftKey.isPressed;
            if (shift)
                angleB = Mathf.Clamp(angleB + notches * SnapDegrees, -75f, 75f);
            else
                angleA = Mathf.Clamp(angleA + notches * SnapDegrees, -75f, 75f);
        }

        public override void SecondaryTap()
        {
            snapIndex = (snapIndex + 1) % SnapSteps.Length;
            angleA = Mathf.Round(angleA / SnapDegrees) * SnapDegrees;
            angleB = Mathf.Round(angleB / SnapDegrees) * SnapDegrees;
            Hud.ShowToast($"Angle snap: {SnapDegrees}°", 1.2f);
        }

        public override void SecondaryHold()
        {
            var part = AimedStock(out var hit);
            if (part == null)
            {
                Hud.ShowToast("Aim at a piece of metal or a shaft to open the precise cut menu");
                return;
            }
            lockedPart = part;
            lockedT = Plan(part, hit.point, false).t;
            menu.Open(this, part);
        }

        public override void PrimaryDown()
        {
            if (cutting || previewPart == null)
                return;
            if (!plan.valid)
            {
                Hud.ShowToast(plan.problem);
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
            if (!inReach)
                return true;
            view.hint = plan.valid
                ? $"Keeps {Describe(plan.keptLength, part)}   ·   removes {Describe(plan.removedLength, part)}"
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
            var local = part.transform.InverseTransformPoint(worldPoint);
            var t = profile.AlongAxis(local);
            // Snap to every half hole, counted from the part's end, so cuts land through or between holes.
            var step = free ? 0.0005f : HolePitch / 2f;
            t = profile.Start + Mathf.Round((t - profile.Start) / step) * step;
            return PlanAt(part, t);
        }

        public CutPlan PlanAt(PartBody part, float t)
        {
            var profile = part.GetComponent<StockProfile>();
            var plan = new CutPlan { t = t };
            var axis = profile.Axis;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(axis, u);
            var normal = Quaternion.AngleAxis(angleA, v) * Quaternion.AngleAxis(angleB, u) * axis;
            plan.point = profile.Origin + axis * t;

            // Keep the side the player is on (the far end is what gets cut off), unless flipped.
            var camLocal = part.transform.InverseTransformPoint(View.transform.position);
            var keepTowardsAxis = Vector3.Dot(camLocal - plan.point, axis) >= 0f;
            if (flip)
                keepTowardsAxis = !keepTowardsAxis;
            plan.keepNormal = keepTowardsAxis ? normal : -normal;

            plan.keptLength = keepTowardsAxis ? profile.End - t : t - profile.Start;
            plan.removedLength = profile.Length - plan.keptLength;
            if (plan.keptLength < MinPiece || plan.removedLength < MinPiece)
            {
                plan.problem = "Too close to the end to cut here";
                return plan;
            }
            plan.valid = true;
            return plan;
        }

        public void SetMenuValues(float t, float a, float b, bool flipSide)
        {
            lockedT = t;
            angleA = a;
            angleB = b;
            flip = flipSide;
            if (lockedPart != null)
            {
                plan = PlanAt(lockedPart, lockedT);
                ShowPreview(lockedPart, plan);
            }
        }

        public (float a, float b, bool flipSide) Angles => (angleA, angleB, flip);
        public float LockedT => lockedT;

        /// <summary>Called by the precise cut menu.</summary>
        public void CutFromMenu()
        {
            if (lockedPart == null)
                return;
            plan = PlanAt(lockedPart, lockedT);
            ShowPreview(lockedPart, plan);
            var part = lockedPart;
            lockedPart = null;
            if (!plan.valid)
            {
                Hud.ShowToast(plan.problem);
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

        /// <summary>A length as people talk about it: mm and inches for shafts, mm and hole count for structure.</summary>
        static string Describe(float length, PartBody part)
        {
            if (IsShaft(part))
                return $"{length * 1000f:0.#} mm ({length / 0.0254f:0.##} in)";
            var holes = Mathf.RoundToInt(length / HolePitch);
            return holes < 1 ? $"{length * 1000f:0.#} mm" : $"{length * 1000f:0.#} mm (~{holes} {(holes == 1 ? "hole" : "holes")})";
        }

        static bool IsShaft(PartBody part) => part != null && part.PartId != null && part.PartId.StartsWith("Motion/Shaft");

        // ---------------------------------------------------------------- preview

        void ShowPreview(PartBody part, CutPlan cut)
        {
            var key = $"{part.GetInstanceID()}|{cut.point.x:F5}|{cut.point.y:F5}|{cut.point.z:F5}|{cut.keepNormal.x:F4}|{cut.keepNormal.y:F4}|{cut.keepNormal.z:F4}";
            if (key == previewKey)
                return;
            ClearPreview();
            previewKey = key;
            previewPart = part;
            if (!cut.valid)
                return;

            foreach (var r in PartShape.DetailRenderers(part.gameObject))
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var plane = LocalPlane(part.transform, r.transform, cut.point, cut.keepNormal);
                var kept = MeshSlicer.Slice(mesh, plane);
                var removed = MeshSlicer.Slice(mesh, plane.flipped);
                previewMeshes.Add((r, kept, removed));
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
            previewObjects.Add(CutPlane(part, cut));
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

        /// <summary>A see-through sheet where the blade will go, a little bigger than the part's cross-section.</summary>
        static GameObject CutPlane(PartBody part, CutPlan cut)
        {
            var size = part.GetComponent<StockProfile>().CrossSectionRadius * 2f + 0.012f;
            var go = new GameObject(PartShape.PreviewName + " (cut plane)") { layer = part.gameObject.layer };
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = cut.point;
            go.transform.localRotation = Quaternion.LookRotation(cut.keepNormal);
            go.transform.localScale = new Vector3(size, size, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            go.AddComponent<MeshRenderer>().sharedMaterial = ToolMaterials.CutPlane;
            return go;
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
            PlaySound(ToolSounds.Saw, 0.55f);
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
            var meshes = new List<(MeshRenderer source, Mesh kept, Mesh removed)>(previewMeshes);
            ClearPreview();

            // The offcut becomes a part of its own (a copy that keeps the other side), so both pieces of
            // the stock stay usable. Slivers shorter than a centimetre just fall away as swarf.
            GameObject offcut = null;
            if (cut.removedLength >= MinOffcut)
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
                PlaySound(ToolSounds.Clack, 0.35f);
                FinishPiece(offcut, cut.point, -cut.keepNormal, volumeBefore);
                if (part.TryGetComponent(out HoverTarget hover) && offcut.TryGetComponent(out HoverTarget offcutHover))
                    offcutHover.SetPaint(hover.Paint);  // colour overrides don't survive the copy
                // The offcut comes away from the kept piece (as it would when the blade breaks through), so
                // the cut is easy to see.
                var away = -part.transform.TransformDirection(cut.keepNormal);
                var body = offcut.GetComponent<Rigidbody>();
                body.position += away * 0.002f;
                body.linearVelocity = away * 0.15f;
                Hud.ShowToast($"Cut — kept {Describe(cut.keptLength, part)}, offcut {Describe(cut.removedLength, part)}");
            }
            else
            {
                Hud.ShowToast($"Cut — kept {Describe(cut.keptLength, part)}");
            }
        }

        /// <summary>After its mesh was cut: LODs off, colliders shortened, the cut recorded, mass and tooltip updated.</summary>
        static void FinishPiece(GameObject piece, Vector3 point, Vector3 keepNormal, float volumeBefore)
        {
            PartShape.DropLods(piece);
            var profile = piece.GetComponent<StockProfile>();
            float min = float.MaxValue, max = float.MinValue;
            foreach (var r in PartShape.DetailRenderers(piece))
            {
                var m = piece.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                foreach (var v in r.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    var along = profile.AlongAxis(m.MultiplyPoint3x4(v));
                    min = Mathf.Min(min, along);
                    max = Mathf.Max(max, along);
                }
            }
            if (min < max)
                profile.SetExtent(min, max);
            PartModifications.On(piece).cuts.Add(new PartModifications.Cut { point = point, keepNormal = keepNormal });
            PartShape.AfterEdit(piece, volumeBefore);
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
