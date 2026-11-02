using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Protobot.Rendering
{
    /// <summary>
    /// Draws an outline around whatever is on the outline rendering layer (<see cref="Layer"/>). The objects are
    /// drawn as flat silhouettes into a mask, each in its own outline colour (<see cref="SetColor"/>, a
    /// MaterialPropertyBlock value; objects without one use the feature's colour), then a full-screen pass
    /// paints that colour just outside the silhouette. Added to each URP renderer; only game cameras draw it.
    /// </summary>
    public class OutlineFeature : ScriptableRendererFeature
    {
        /// <summary>The rendering layer bit that marks objects to outline.</summary>
        public const uint Layer = 1u << 20;

        /// <summary>The per-renderer outline colour (MaterialPropertyBlock), read by the mask shader.</summary>
        public static readonly int ObjectColorId = Shader.PropertyToID("_OutlineObjectColor");

        /// <summary>Outlines a renderer in <paramref name="color"/>, or stops outlining it.</summary>
        public static void SetOutline(Renderer renderer, bool on, Color color = default)
        {
            if (!on && (renderer.renderingLayerMask & Layer) == 0)
                return;  // not outlined: leave it without a property block (keeps it SRP-batched)
            renderer.renderingLayerMask = on ? renderer.renderingLayerMask | Layer : renderer.renderingLayerMask & ~Layer;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            color.a = 1f;  // the mask leaves out alpha 0 (no colour given)
            block.SetColor(ObjectColorId, on ? color : Color.clear);
            renderer.SetPropertyBlock(block);
        }

        [SerializeField] Shader maskShader;
        [SerializeField] Shader compositeShader;
        [SerializeField] Color color = new(1f, 0.82f, 0.2f, 1f);
        [Tooltip("Outline thickness in pixels at 1080p (scaled with the screen height).")]
        [SerializeField, Range(1f, 6f)] float width = 2.5f;

        Material maskMaterial;
        Material compositeMaterial;
        OutlinePass pass;

        public void SetShaders(Shader mask, Shader composite)
        {
            maskShader = mask;
            compositeShader = composite;
        }

        public override void Create()
        {
            // After opaques and the sky, before transparents (tool ghosts draw over the outline).
            pass = new OutlinePass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (maskShader == null || compositeShader == null || renderingData.cameraData.cameraType != CameraType.Game)
                return;
            if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
                return;  // the hand-held tool camera
            if (maskMaterial == null)
                maskMaterial = CoreUtils.CreateEngineMaterial(maskShader);
            if (compositeMaterial == null)
                compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
            maskMaterial.SetColor(ObjectColorId, Color.clear);  // only objects given a colour are outlined
            compositeMaterial.SetFloat(WidthId, width * Mathf.Max(1f, renderingData.cameraData.camera.pixelHeight / 1080f));
            pass.Setup(maskMaterial, compositeMaterial);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(maskMaterial);
            CoreUtils.Destroy(compositeMaterial);
        }

        static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int TexelId = Shader.PropertyToID("_OutlineTexel");

        class OutlinePass : ScriptableRenderPass
        {
            static readonly List<ShaderTagId> Tags = new() { new("UniversalForward"), new("UniversalForwardOnly"), new("SRPDefaultUnlit") };

            Material mask;
            Material composite;

            public void Setup(Material maskMaterial, Material compositeMaterial)
            {
                mask = maskMaterial;
                composite = compositeMaterial;
            }

            class MaskData
            {
                public RendererListHandle list;
            }

            class CompositeData
            {
                public TextureHandle mask;
                public Material material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var lights = frameData.Get<UniversalLightData>();

                var desc = camera.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.depthStencilFormat = GraphicsFormat.None;
                desc.msaaSamples = 1;
                desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                var maskTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_OutlineMask", true, FilterMode.Bilinear);
                composite.SetVector(TexelId, new Vector4(1f / desc.width, 1f / desc.height, 0f, 0f));

                var drawing = RenderingUtils.CreateDrawingSettings(Tags, rendering, camera, lights, SortingCriteria.None);
                drawing.overrideMaterial = mask;
                drawing.overrideMaterialPassIndex = 0;
                var filtering = new FilteringSettings(RenderQueueRange.all, -1, Layer);
                var list = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));

                using (var builder = renderGraph.AddRasterRenderPass<MaskData>("Outline Mask", out var data))
                {
                    data.list = list;
                    builder.UseRendererList(list);
                    builder.SetRenderAttachment(maskTexture, 0, AccessFlags.Write);
                    builder.SetRenderFunc((MaskData d, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(false, true, Color.clear);
                        context.cmd.DrawRendererList(d.list);
                    });
                }

                using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Outline Composite", out var data))
                {
                    data.mask = maskTexture;
                    data.material = composite;
                    builder.UseTexture(maskTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderFunc((CompositeData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.mask, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
                }
            }
        }
    }
}
