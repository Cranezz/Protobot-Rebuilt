using Protobot.Parts;
using Protobot.Tools;
using UnityEngine;

namespace Protobot.Building
{
    /// <summary>
    /// An outline drawn on a hole's face (square or round, like the hole), so you can see which hole you're
    /// aiming at or which hole of the part in your hand will go on it.
    /// </summary>
    public class HoleMarker
    {
        const int Segments = 28;
        readonly LineRenderer line;
        readonly Color color;

        public HoleMarker(string name, Color color)
        {
            this.color = color;
            var go = new GameObject(name) { layer = 2 };  // Ignore Raycast
            Object.DontDestroyOnLoad(go);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.widthMultiplier = 0.0009f;
            line.numCornerVertices = 1;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = ToolMaterials.Unlit(color);
            go.SetActive(false);
        }

        public void Hide()
        {
            if (line != null)
                line.gameObject.SetActive(false);
        }

        /// <summary>Outlines the hole on the face given by <paramref name="face"/> and <paramref name="outward"/>.</summary>
        public void Show(HoleAt hole, Vector3 face, Vector3 outward)
        {
            if (line == null)
                return;
            line.gameObject.SetActive(true);
            var partRotation = hole.part != null ? hole.part.transform.rotation : Quaternion.identity;
            var across = Vector3.zero;
            var bestDot = 2f;
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                var world = partRotation * axis;
                var d = Mathf.Abs(Vector3.Dot(world, outward));
                if (d < bestDot - 1e-3f)
                {
                    bestDot = d;
                    across = world;
                }
            }
            across = Vector3.ProjectOnPlane(across, outward).normalized;
            var up = Vector3.Cross(outward, across);
            var half = hole.Size * 0.62f;
            var centre = face + outward * 0.0004f;  // just off the surface so it isn't hidden
            if (hole.local.square)
            {
                line.positionCount = 4;
                line.SetPosition(0, centre + (across + up) * half);
                line.SetPosition(1, centre + (-across + up) * half);
                line.SetPosition(2, centre + (-across - up) * half);
                line.SetPosition(3, centre + (across - up) * half);
            }
            else
            {
                line.positionCount = Segments;
                for (var i = 0; i < Segments; i++)
                {
                    var a = i * Mathf.PI * 2f / Segments;
                    line.SetPosition(i, centre + (across * Mathf.Cos(a) + up * Mathf.Sin(a)) * half);
                }
            }
        }
    }
}
