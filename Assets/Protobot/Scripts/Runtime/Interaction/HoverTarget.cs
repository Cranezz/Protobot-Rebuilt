using System.Collections.Generic;
using Protobot.Rendering;
using UnityEngine;

namespace Protobot.Interaction
{
    /// <summary>What the cursor turns into over something, telling the player what they can do with it.</summary>
    public enum CursorKind
    {
        Default,
        /// <summary>Something to click: bins, buttons, tools.</summary>
        Point,
        /// <summary>Something to pick up: parts.</summary>
        Grab,
        /// <summary>While holding something.</summary>
        Grabbing,
    }

    /// <summary>
    /// Marks an object the cursor reacts to: the cursor, a tooltip, and an outline while it's aimed at.
    /// Also owns the object's colour overrides (spray paint), so painting and highlighting work together.
    /// Needs a collider on this object or a child so the cursor's raycast can find it.
    /// </summary>
    public class HoverTarget : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        [SerializeField] CursorKind cursor = CursorKind.Point;
        [SerializeField] string title;
        [SerializeField, TextArea] string hint;

        Renderer[] renderers;
        MaterialPropertyBlock block;
        bool highlighted;
        readonly Dictionary<Material, Color> paints = new();  // by the material it covers (one part can have several)

        public CursorKind Cursor => cursor;
        public string Title => title;
        public string Hint => hint;
        public bool IsPainted => paints.Count > 0;

        /// <summary>The paint colour if the whole part is painted one colour, otherwise null.</summary>
        public Color? Paint
        {
            get
            {
                Color? colour = null;
                foreach (var material in Materials)
                {
                    if (!paints.TryGetValue(material, out var c))
                        return null;
                    if (colour != null && colour.Value != c)
                        return null;
                    colour = c;
                }
                return colour;
            }
        }

        /// <summary>The different materials the object is made of (e.g. a wheel's tyre and hub).</summary>
        public List<Material> Materials
        {
            get
            {
                renderers ??= GetComponentsInChildren<Renderer>();
                var result = new List<Material>();
                foreach (var r in renderers)
                    if (r != null && r is MeshRenderer && !r.name.StartsWith("Tool Preview"))
                        foreach (var m in r.sharedMaterials)
                            if (m != null && !result.Contains(m))
                                result.Add(m);
                return result;
            }
        }

        /// <summary>What a material looks like now: its paint, or its own colour.</summary>
        public Color ColorOf(Material material) =>
            paints.TryGetValue(material, out var c) ? c : material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Color.white;

        public bool IsPaintedMaterial(Material material) => paints.ContainsKey(material);

        public void Configure(CursorKind kind, string newTitle, string newHint)
        {
            cursor = kind;
            title = newTitle;
            hint = newHint;
        }

        public void SetHint(string newHint) => hint = newHint;

        /// <summary>Outlines the object while the cursor is over it.</summary>
        public void SetHighlighted(bool on)
        {
            highlighted = on;
            Apply();
        }

        /// <summary>Paints the whole object one colour (a satin paint finish); null removes the paint.</summary>
        public void SetPaint(Color? color)
        {
            paints.Clear();
            if (color != null)
                foreach (var material in Materials)
                    paints[material] = color.Value;
            Apply();
        }

        /// <summary>Paints just the parts of the object made of <paramref name="material"/>; null removes that paint.</summary>
        public void SetPaint(Material material, Color? color)
        {
            if (color == null)
                paints.Remove(material);
            else
                paints[material] = color.Value;
            Apply();
        }

        /// <summary>Takes over another object's paint (e.g. a saw offcut from the piece it was cut from).</summary>
        public void CopyPaintFrom(HoverTarget other)
        {
            paints.Clear();
            foreach (var pair in other.paints)
                paints[pair.Key] = pair.Value;
            Apply();
        }

        /// <summary>Each painted material's name and colour, for saving.</summary>
        public IEnumerable<KeyValuePair<Material, Color>> Paints => paints;

        /// <summary>Call after renderers were added or removed (e.g. a part was cut).</summary>
        public void RefreshRenderers()
        {
            renderers = null;
            Apply();
        }

        void Apply()
        {
            renderers ??= GetComponentsInChildren<Renderer>();
            block ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                // The highlight is an outline drawn around everything on the outline rendering layer.
                r.renderingLayerMask = highlighted ? r.renderingLayerMask | OutlineFeature.Layer : r.renderingLayerMask & ~OutlineFeature.Layer;
                var materials = r.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    var paint = default(Color);
                    var painted = material != null && paints.TryGetValue(material, out paint);
                    if (!painted || !material.HasProperty(BaseColor))
                    {
                        r.SetPropertyBlock(null, i);
                        continue;
                    }
                    block.Clear();
                    block.SetColor(BaseColor, paint);
                    block.SetFloat(Metallic, 0.05f);
                    block.SetFloat(Smoothness, 0.55f);
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        void OnDisable()
        {
            highlighted = false;
            Apply();
        }
    }
}
