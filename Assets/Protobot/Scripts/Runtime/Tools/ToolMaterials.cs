using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.Tools
{
    /// <summary>See-through materials for tool previews (the part the saw would remove, the drill's cut).</summary>
    public static class ToolMaterials
    {
        static Material removed;
        static Material drillOk;
        static Material drillBad;

        /// <summary>Faint red ghost: material that will be cut away.</summary>
        public static Material Removed => removed ??= Ghost(new Color(1f, 0.25f, 0.15f, 0.28f));

        public static Material DrillOk => drillOk ??= Ghost(new Color(0.3f, 1f, 0.45f, 0.3f));
        public static Material DrillBad => drillBad ??= Ghost(new Color(1f, 0.3f, 0.2f, 0.3f));

        static Material kerfLine;

        /// <summary>Bright unlit yellow for the line where the saw will cut.</summary>
        public static Material KerfLine
        {
            get
            {
                if (kerfLine != null)
                    return kerfLine;
                kerfLine = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Kerf Line" };
                kerfLine.SetColor("_BaseColor", new Color(1f, 0.85f, 0.15f));
                return kerfLine;
            }
        }

        static Material cutPlane;

        /// <summary>Faint yellow sheet showing where the saw blade goes (seen from both sides).</summary>
        public static Material CutPlane
        {
            get
            {
                if (cutPlane != null)
                    return cutPlane;
                cutPlane = Ghost(new Color(1f, 0.82f, 0.2f, 0.22f));
                cutPlane.SetFloat("_Cull", (float)CullMode.Off);
                return cutPlane;
            }
        }

        static Material softParticle;

        /// <summary>Unlit, vertex-coloured soft dot for spray particles.</summary>
        public static Material SoftParticle
        {
            get
            {
                if (softParticle != null)
                    return softParticle;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Soft dot", wrapMode = TextureWrapMode.Clamp };
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                        var a = Mathf.Clamp01(1f - d);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                }
                tex.Apply();
                softParticle = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Spray Particle" };
                softParticle.SetTexture("_BaseMap", tex);
                softParticle.SetFloat("_Surface", 1f);
                softParticle.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                softParticle.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                softParticle.SetFloat("_ZWrite", 0f);
                softParticle.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                softParticle.SetOverrideTag("RenderType", "Transparent");
                softParticle.renderQueue = (int)RenderQueue.Transparent;
                return softParticle;
            }
        }

        public static Material Ghost(Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Tool Ghost" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * 0.4f);
            m.EnableKeyword("_EMISSION");
            return m;
        }
    }
}
