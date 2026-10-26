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
    /// Needs a collider on this object or a child so the cursor's raycast can find it.
    /// </summary>
    public class HoverTarget : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] CursorKind cursor = CursorKind.Point;
        [SerializeField] string title;
        [SerializeField, TextArea] string hint;
        [SerializeField, Range(1f, 2f)] float highlightBrightness = 1.3f;

        Renderer[] renderers;
        MaterialPropertyBlock block;

        public CursorKind Cursor => cursor;
        public string Title => title;
        public string Hint => hint;

        public void Configure(CursorKind kind, string newTitle, string newHint)
        {
            cursor = kind;
            title = newTitle;
            hint = newHint;
        }

        /// <summary>Brightens the object while the cursor is over it.</summary>
        public void SetHighlighted(bool on)
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
                    if (!on || materials[i] == null || !materials[i].HasProperty(BaseColor))
                    {
                        r.SetPropertyBlock(null, i);
                        continue;
                    }
                    var color = materials[i].GetColor(BaseColor);
                    block.SetColor(BaseColor, new Color(color.r * highlightBrightness, color.g * highlightBrightness, color.b * highlightBrightness, color.a));
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        void OnDisable() => SetHighlighted(false);
    }
}
