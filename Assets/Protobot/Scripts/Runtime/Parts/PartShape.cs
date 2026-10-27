using System.Collections.Generic;
using Protobot.Geometry;
using Protobot.Interaction;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// Helpers for tools that change a part's geometry (saw, drill): its full-detail meshes, its size and
    /// volume in its own space, and tidying up after an edit (the simplified LOD meshes no longer match
    /// the edited shape, so they're removed; mass and tooltip are updated).
    /// </summary>
    public static class PartShape
    {
        public const string PreviewName = "Tool Preview";

        /// <summary>The renderers that show the part at full detail (LOD0, or all if it has no LODs).</summary>
        public static List<MeshRenderer> DetailRenderers(GameObject part)
        {
            var result = new List<MeshRenderer>();
            var hasLods = part.GetComponent<LODGroup>() != null;
            foreach (var r in part.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.name.StartsWith(PreviewName))
                    continue;
                if (hasLods && !r.name.EndsWith("_LOD0"))
                    continue;
                if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                    result.Add(r);
            }
            return result;
        }

        /// <summary>Removes the simplified LOD meshes so the edited full-detail mesh is shown at every distance.</summary>
        public static void DropLods(GameObject part)
        {
            if (!part.TryGetComponent(out LODGroup group))
                return;
            foreach (var lod in group.GetLODs())
                foreach (var r in lod.renderers)
                    if (r != null && !r.name.EndsWith("_LOD0"))
                        Object.Destroy(r.gameObject);
            Object.Destroy(group);
        }

        /// <summary>Bounds of the full-detail meshes in the part's own space.</summary>
        public static Bounds LocalBounds(GameObject part)
        {
            var root = part.transform;
            var first = true;
            var bounds = new Bounds();
            foreach (var r in DetailRenderers(part))
            {
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                foreach (var v in mesh.isReadable ? mesh.vertices : Corners(mesh.bounds))
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (first)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }
            return bounds;
        }

        /// <summary>Most part meshes aren't CPU-readable (only metal stock is, see PartModelImportGuard): use their box.</summary>
        static Vector3[] Corners(Bounds b)
        {
            var corners = new Vector3[8];
            for (var i = 0; i < 8; i++)
                corners[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            return corners;
        }

        public static float Volume(GameObject part)
        {
            var total = 0f;
            foreach (var r in DetailRenderers(part))
                total += MeshSlicer.Volume(r.GetComponent<MeshFilter>().sharedMesh);
            return total;
        }

        /// <summary>After an edit: scale the mass by how much material is left and refresh the tooltip.</summary>
        public static void AfterEdit(GameObject part, float volumeBefore)
        {
            var volumeAfter = Volume(part);
            part.TryGetComponent(out Rigidbody body);
            if (part.TryGetComponent(out PartBody partBody))
            {
                // Also when the part is in an assembly (no body of its own): the assembly's mass follows.
                if (volumeBefore > 0f)
                    partBody.SetMass(Mathf.Max(0.0005f, partBody.Mass * volumeAfter / volumeBefore));
                if (Building.Assembly.Of(partBody) is { } assembly)
                    assembly.Recompute();
            }
            if (body != null)
                body.WakeUp();
            if (part.TryGetComponent(out HoverTarget hover))
            {
                hover.RefreshRenderers();
                var size = LocalBounds(part).size;
                var mass = partBody != null ? partBody.Mass : body != null ? body.mass : 0f;
                hover.SetHint($"{PartEntry.FormatSize(size)}  ·  {PartEntry.FormatMass(mass)}");
            }
        }
    }
}
