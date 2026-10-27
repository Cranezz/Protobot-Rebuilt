using System.Collections;
using Protobot.Audio;
using Protobot.Core;
using System.Collections.Generic;
using Protobot.Geometry;
using Protobot.Interaction;
using Protobot.Menus;
using Protobot.Parts;
using UnityEngine;

namespace Protobot.Tools
{
    /// <summary>
    /// Cordless drill for widening existing holes (e.g. a square hole into a round one for a bearing or a
    /// round shaft). Aim at a hole: a see-through cylinder shows the cut (green = OK, red = can't). Left click
    /// drills through that one wall. The mouse wheel changes the bit; right click opens the bit picker.
    /// </summary>
    public class DrillTool : PlayerTool
    {
        public static readonly (string name, float diameter)[] Bits =
        {
            ("1/8\"", 0.003175f), ("5/32\"", 0.003969f), ("3/16\"", 0.004763f), ("1/4\"", 0.00635f),
            ("17/64\"", 0.006747f), ("9/32\"", 0.007144f), ("5/16\"", 0.007938f), ("3/8\"", 0.009525f),
            ("7/16\"", 0.011113f), ("1/2\"", 0.0127f),
        };

        [SerializeField] DrillBitMenu bitMenu;
        [SerializeField] Sprite crosshair;
        [SerializeField] int bitIndex = 4;

        // Hole under the aim (cached until the aim moves)
        PartBody aimedPart;
        MeshRenderer aimedRenderer;
        HoleDrilling.Region region;
        HoleInfo hole;
        bool fits;
        string problem;
        Vector3 lastAimLocal;
        float nextSearch;
        GameObject preview;
        MeshRenderer previewRenderer;
        bool drilling;
        Transform bit;

        public int BitIndex => bitIndex;
        public float BitDiameter => Bits[bitIndex].diameter;
        string BitLabel => $"{Bits[bitIndex].name} bit ({Units.ToInches(BitDiameter):0.###} in)";

        public override string ControlsHint => $"Left click: drill   ·   Wheel: bit {BitLabel}   ·   Right click: choose bit";

        public override void OnEquip()
        {
            bit = ViewModel != null ? FindChild(ViewModel.transform, "Bit") : null;
            if (bit != null && bitRestScale == Vector3.zero)
            {
                bitRestScale = bit.localScale;
                // The bit's length runs along whichever local axis its mesh is longest in.
                var size = bit.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                bitAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            }
            ShowBitOnDrill();
        }

        Vector3 bitRestScale;
        int bitAxis = 2;

        public override void OnUnequip() => ClearAim();
        public override void OnSuspend() => ClearAim();

        public override void Scroll(int notches)
        {
            SetBit(Mathf.Clamp(bitIndex + notches, 0, Bits.Length - 1));
            Hud.ShowToast($"Drill bit: {BitLabel}", 1.2f);
        }

        public override void SecondaryTap() => bitMenu.Open(bitIndex, SetBit);
        public override void SecondaryHold() => bitMenu.Open(bitIndex, SetBit);

        public void SetBit(int index)
        {
            bitIndex = Mathf.Clamp(index, 0, Bits.Length - 1);
            ShowBitOnDrill();
            nextSearch = 0f;  // re-check the current hole with the new size
            lastAimLocal = Vector3.positiveInfinity;
        }

        public override void Tick()
        {
            if (drilling)
                return;
            var target = Cursor.Current;
            var part = target != null && Cursor.CurrentInReach ? target.GetComponentInParent<PartBody>() : null;
            if (part == null)
            {
                ClearAim();
                return;
            }
            UpdateAim(part, Cursor.CurrentHit);
        }

        public override void PrimaryDown()
        {
            if (drilling)
                return;
            if (aimedPart == null)
            {
                if (Cursor.Current != null && Cursor.CurrentInReach)
                    Hud.ShowError("Aim at a hole in a metal part to drill it");
                return;
            }
            if (!hole.found || !fits)
            {
                Hud.ShowError(problem);
                return;
            }
            StartCoroutine(DrillRoutine());
        }

        public override bool DescribeTarget(HoverTarget target, bool inReach, ref CursorView view)
        {
            view.reticle = crosshair;
            view.large = true;
            view.reticleColor = Color.white;
            if (target == null)
                return true;
            if (target.GetComponentInParent<PartBody>() == null)
            {
                view.hint = "The drill works on parts";
                view.available = false;
                return true;
            }
            if (!inReach || aimedPart == null)
                return true;
            var ok = hole.found && fits;
            view.hint = ok ? $"Drill with the {BitLabel}{(BreaksThroughEdge ? " — breaks through the edge" : "")}" : problem;
            view.available = ok;
            view.reticleColor = ok ? new Color(0.6f, 1f, 0.7f) : alreadyDrilled ? Color.white : new Color(1f, 0.6f, 0.45f);
            return true;
        }

        // ---------------------------------------------------------------- finding the hole

        void UpdateAim(PartBody part, RaycastHit hit)
        {
            if (Time.unscaledTime < nextSearch && part == aimedPart)
                return;

            // Work in the space of the mesh the aim ray touches.
            MeshRenderer best = null;
            var bestDistance = float.MaxValue;
            foreach (var r in PartShape.DetailRenderers(part.gameObject))
            {
                var d = r.bounds.SqrDistance(hit.point);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = r;
                }
            }
            if (best == null)
            {
                ClearAim();
                return;
            }
            if (!best.GetComponent<MeshFilter>().sharedMesh.isReadable)
            {
                // Only metal structure and shafts can be reshaped (see PartModelImportGuard).
                ClearAim();
                aimedPart = part;
                hole = new HoleInfo { problem = "The drill works on metal structure parts" };
                problem = hole.problem;
                nextSearch = Time.unscaledTime + 0.25f;
                return;
            }

            var local = best.transform.InverseTransformPoint(hit.point);
            if (part == aimedPart && best == aimedRenderer && (local - lastAimLocal).sqrMagnitude < 0.0008f * 0.0008f)
                return;
            // Still aiming inside the hole already found: nothing to search for.
            if (part == aimedPart && best == aimedRenderer && hole.found && bitAtSearch == bitIndex)
            {
                var offset = local - hole.center;
                offset -= hole.normal * Vector3.Dot(offset, hole.normal);
                if (offset.magnitude < hole.holeRadius * 0.9f)
                    return;
            }

            nextSearch = Time.unscaledTime + 0.08f;
            if (part != aimedPart || best != aimedRenderer)
                region = null;
            aimedPart = part;
            aimedRenderer = best;
            lastAimLocal = local;

            if (region == null || Vector3.Distance(local, lastRegionCenter) > 0.012f)
            {
                region = new HoleDrilling.Region(best.GetComponent<MeshFilter>().sharedMesh, local, 0.03f);
                lastRegionCenter = local;
            }

            // The hit normal from the collider points out of the surface we aim at.
            var normal = best.transform.InverseTransformDirection(hit.normal).normalized;
            if (!TryDrilledHole(part, best, local, out hole))
                hole = HoleDrilling.Find(region, local, normal);
            bitAtSearch = bitIndex;
            fits = false;
            alreadyDrilled = false;
            walls = null;
            problem = hole.problem;
            if (hole.found)
            {
                var radius = BitDiameter / 2f;
                if (hole.round && radius <= hole.holeRadius + 0.00015f)
                {
                    alreadyDrilled = radius >= hole.holeRadius - 0.0004f;
                    problem = !alreadyDrilled
                        ? $"Bit is smaller than this hole ({Units.Inches(hole.holeRadius * 2f)})"
                        : bitIndex < Bits.Length - 1
                            ? "Already a round hole this size — pick a bigger bit to widen it"
                            : "Already drilled with the biggest bit";
                }
                else if (radius < hole.holeRadius)
                {
                    problem = $"Bit is smaller than this hole — use at least {Units.Inches(hole.holeRadius * 2f)}";
                }
                else
                {
                    // Near an edge or a bend the hole may break through; it's still allowed.
                    walls = HoleDrilling.WallSegments(region, hole, radius);
                    fits = true;
                }
            }
            ShowPreview();
        }

        /// <summary>
        /// The biggest hole this drill already made under the aim point, from the part's record. A hole that
        /// breaks through an edge can't be found by searching the mesh (its empty space runs off the part),
        /// and the record knows the exact size anyway.
        /// </summary>
        static bool TryDrilledHole(PartBody part, MeshRenderer renderer, Vector3 local, out HoleInfo found)
        {
            found = default;
            var mods = part.GetComponent<PartModifications>();
            if (mods == null)
                return false;
            foreach (var drilled in mods.holes)
            {
                var center = renderer.transform.InverseTransformPoint(part.transform.TransformPoint(drilled.center));
                var normal = renderer.transform.InverseTransformDirection(part.transform.TransformDirection(drilled.normal)).normalized;
                var offset = local - center;
                var along = Vector3.Dot(offset, normal);
                var radius = drilled.diameter / 2f;
                if (Mathf.Abs(along) > drilled.depth + 0.001f || (offset - normal * along).magnitude > radius)
                    continue;
                if (found.found && radius <= found.holeRadius)
                    continue;
                found = new HoleInfo { found = true, round = true, center = center, normal = normal, thickness = drilled.depth, holeRadius = radius };
            }
            return found.found;
        }

        Vector3 lastRegionCenter;
        int bitAtSearch = -1;
        bool[] walls;  // which sides of the new hole have metal (null = all)

        bool BreaksThroughEdge
        {
            get
            {
                if (walls == null)
                    return false;
                foreach (var w in walls)
                    if (!w)
                        return true;
                return false;
            }
        }

        bool alreadyDrilled;

        void ShowPreview()
        {
            if (!hole.found || alreadyDrilled)
            {
                if (preview != null)
                    preview.SetActive(false);
                return;
            }
            if (preview == null)
            {
                preview = new GameObject(PartShape.PreviewName + " (drill)");
                preview.AddComponent<MeshFilter>().sharedMesh = Cylinder();
                previewRenderer = preview.AddComponent<MeshRenderer>();
            }
            preview.SetActive(true);
            preview.transform.SetParent(aimedRenderer.transform, false);
            preview.layer = aimedRenderer.gameObject.layer;
            var radius = BitDiameter / 2f;
            var length = hole.thickness + 0.004f;
            preview.transform.localRotation = Quaternion.FromToRotation(Vector3.up, -hole.normal);
            preview.transform.localPosition = hole.center + hole.normal * 0.002f + (-hole.normal) * (length / 2f);
            preview.transform.localScale = new Vector3(radius * 2f, length / 2f, radius * 2f);
            previewRenderer.sharedMaterial = fits ? ToolMaterials.DrillOk : ToolMaterials.DrillBad;
        }

        void ClearAim()
        {
            aimedPart = null;
            aimedRenderer = null;
            region = null;
            hole = default;
            alreadyDrilled = false;
            if (preview != null)
            {
                preview.transform.SetParent(null, false);
                preview.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- drilling

        IEnumerator DrillRoutine()
        {
            drilling = true;
            PlaySound(Sounds.Drill, 0.5f);
            var part = aimedPart;
            var renderer = aimedRenderer;
            var target = hole;
            var targetWalls = walls;
            var radius = BitDiameter / 2f;
            var rest = ViewModel != null ? ViewModel.transform.localPosition : Vector3.zero;
            for (var t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                if (bit != null)
                {
                    var spin = Vector3.zero;
                    spin[bitAxis] = 2400f * Time.deltaTime;
                    bit.Rotate(spin, Space.Self);
                }
                if (ViewModel != null)
                    ViewModel.transform.localPosition = rest + new Vector3(Mathf.Sin(t * 90f), Mathf.Cos(t * 70f), 0f) * 0.0015f;
                yield return null;
            }
            if (ViewModel != null)
                ViewModel.transform.localPosition = rest;

            if (part != null && renderer != null)
            {
                var volumeBefore = PartShape.Volume(part.gameObject);
                var filter = renderer.GetComponent<MeshFilter>();
                var result = HoleDrilling.Drill(filter.sharedMesh, target, radius, targetWalls);
                if (result == null)
                {
                    Hud.ShowError("Nothing to drill here");
                }
                else
                {
                    filter.sharedMesh = result;
                    PartShape.DropLods(part.gameObject);
                    var partSpaceCenter = part.transform.InverseTransformPoint(renderer.transform.TransformPoint(target.center));
                    var partSpaceNormal = part.transform.InverseTransformDirection(renderer.transform.TransformDirection(target.normal));
                    PartModifications.On(part.gameObject).holes.Add(new PartModifications.DrilledHole
                    {
                        center = partSpaceCenter, normal = partSpaceNormal, diameter = radius * 2f, depth = target.thickness,
                    });
                    PartShape.AfterEdit(part.gameObject, volumeBefore);
                    Hud.ShowToast($"Drilled a {Bits[bitIndex].name} hole");
                }
            }
            ClearAim();
            drilling = false;
        }

        void ShowBitOnDrill()
        {
            if (bit == null)
                return;
            // The bit model is made 1 cm across; scale its thickness to the chosen size.
            var d = BitDiameter / 0.01f;
            var scale = bitRestScale * d;
            scale[bitAxis] = bitRestScale[bitAxis];
            bit.localScale = scale;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains(name))
                    return t;
            return null;
        }

        static Mesh cylinder;

        /// <summary>Unit cylinder (diameter 1, height 2, centred) like Unity's built-in one, but without a collider.</summary>
        static Mesh Cylinder()
        {
            if (cylinder != null)
                return cylinder;
            const int segments = 32;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (var i = 0; i <= segments; i++)
            {
                var a = i * Mathf.PI * 2f / segments;
                var p = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
                verts.Add(p + Vector3.up);
                verts.Add(p - Vector3.up);
            }
            for (var i = 0; i < segments; i++)
            {
                int k = i * 2;
                tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            var normals = new List<Vector3>();
            foreach (var p in verts)
                normals.Add(new Vector3(p.x, 0f, p.z).normalized);

            // Flat ends with their own vertices, so the shading reads as a cylinder rather than a blob.
            foreach (var sign in new[] { 1f, -1f })
            {
                var centre = verts.Count;
                verts.Add(Vector3.up * sign);
                normals.Add(Vector3.up * sign);
                var ring = verts.Count;
                for (var i = 0; i <= segments; i++)
                {
                    var a = i * Mathf.PI * 2f / segments;
                    verts.Add(new Vector3(Mathf.Cos(a) * 0.5f, sign, Mathf.Sin(a) * 0.5f));
                    normals.Add(Vector3.up * sign);
                }
                for (var i = 0; i < segments; i++)
                    tris.AddRange(sign > 0f ? new[] { centre, ring + i + 1, ring + i } : new[] { centre, ring + i, ring + i + 1 });
            }
            cylinder = new Mesh { name = "Drill preview" };
            cylinder.SetVertices(verts);
            cylinder.SetNormals(normals);
            cylinder.SetTriangles(tris, 0);
            return cylinder;
        }
    }
}
