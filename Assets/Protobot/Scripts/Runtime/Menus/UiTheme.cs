using TMPro;
using UnityEngine;

namespace Protobot.Menus
{
    /// <summary>
    /// The workshop look shared by every menu: dark painted steel panels, paper-label cards like the
    /// bin labels, safety-yellow accents and hazard stripes, and a condensed industrial font.
    /// Edit the colours here to restyle all menus at once.
    /// </summary>
    [CreateAssetMenu(menuName = "Protobot/UI Theme", fileName = "UiTheme")]
    public class UiTheme : ScriptableObject
    {
        [Header("Fonts")]
        public TMP_FontAsset font;
        public TMP_FontAsset boldFont;

        [Header("Sprites")]
        public Sprite rounded;
        public Sprite roundedOutline;
        public Sprite hazardStripes;
        public Sprite shadow;
        public Sprite reticleDot;

        [Header("Colours")]
        public Color panel = new(0.13f, 0.145f, 0.165f, 0.97f);
        public Color panelInset = new(0.085f, 0.095f, 0.11f, 1f);
        public Color panelLine = new(1f, 1f, 1f, 0.08f);
        public Color card = new(0.93f, 0.91f, 0.86f, 1f);
        public Color cardHover = new(1f, 0.98f, 0.92f, 1f);
        public Color cardText = new(0.12f, 0.12f, 0.13f, 1f);
        public Color accent = new(0.95f, 0.72f, 0.02f, 1f);
        public Color accentText = new(0.08f, 0.08f, 0.09f, 1f);
        public Color text = new(0.93f, 0.94f, 0.95f, 1f);
        public Color textDim = new(0.62f, 0.66f, 0.71f, 1f);
        public Color button = new(0.22f, 0.245f, 0.275f, 1f);
        public Color buttonHover = new(0.3f, 0.33f, 0.37f, 1f);

        [Header("Bin row colours (bottom row first), matching the plastic bins")]
        public Color[] binRows =
        {
            new(0.11f, 0.33f, 0.75f, 1f),
            new(0.78f, 0.16f, 0.14f, 1f),
            new(0.95f, 0.68f, 0.08f, 1f),
            new(0.13f, 0.55f, 0.25f, 1f),
        };

        public Color BinColor(int binIndex) => binRows[Mathf.Clamp(binIndex / 6, 0, binRows.Length - 1)];
    }
}
