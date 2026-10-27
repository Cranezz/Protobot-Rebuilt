// Paints a coloured edge just outside each silhouette in the outline mask, in that silhouette's colour,
// over the camera image (Protobot.Rendering.OutlineFeature).
Shader "Hidden/Protobot/Outline"
{
    Properties
    {
        _OutlineWidth ("Width (px)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Outline"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _OutlineWidth;
            float4 _OutlineTexel;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float inside = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).a;
                float4 around = 0;  // the strongest silhouette nearby, with its colour
                [unroll]
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * (6.2831853 / 12.0);
                    float2 offset = float2(cos(angle), sin(angle)) * _OutlineTexel.xy;
                    float4 far = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset * _OutlineWidth);
                    float4 near = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset * _OutlineWidth * 0.5);
                    around = far.a > around.a ? far : around;
                    around = near.a > around.a ? near : around;
                }
                float edge = saturate(around.a - inside);
                float3 color = around.rgb / max(around.a, 1e-4);  // undo the bilinear fade toward the clear border
                return half4(color, edge);
            }
            ENDHLSL
        }
    }
}
