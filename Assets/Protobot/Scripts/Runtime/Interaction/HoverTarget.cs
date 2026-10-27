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
    /// Marks an object the cursor reacts to: the cursor shape, a tooltip, and a subtle highlight.
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
        [SerializeField, Range(1f, 2f)] float highlightBrightness = 1.3f;

        Renderer[] renderers;
        MaterialPropertyBlock block;
        bool highlighted;
        Color? paint;

        public CursorKind Cursor => cursor;
        public string Title => title;
        public string Hint => hint;
        public Color? Paint => paint;

        public void Configure(CursorKind kind, string newTitle, string newHint)
        {
            cursor = kind;
            title = newTitle;
            hint = newHint;
        }

        public void SetHint(string newHint) => hint = newHint;

        /// <summary>Brightens the object while the cursor is over it.</summary>
        public void SetHighlighted(bool on)
        {
            highlighted = on;
            Apply();
        }

        /// <summary>Paints the whole object one colour (a satin paint finish); null removes the paint.</summary>
        public void SetPaint(Color? color)
        {
            paint = color;
            Apply();
        }

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
                var materials = r.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if ((!highlighted && paint == null) || material == null || !material.HasProperty(BaseColor))
                    {
                        r.SetPropertyBlock(null, i);
                        continue;
                    }
                    block.Clear();
                    var color = paint ?? material.GetColor(BaseColor);
                    if (highlighted)
                        color = new Color(color.r * highlightBrightness, color.g * highlightBrightness, color.b * highlightBrightness, color.a);
                    block.SetColor(BaseColor, color);
                    if (paint != null)
                    {
                        block.SetFloat(Metallic, 0.05f);
                        block.SetFloat(Smoothness, 0.55f);
                    }
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
