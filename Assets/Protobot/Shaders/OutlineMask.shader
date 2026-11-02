// Draws an object into the outline mask in its outline colour, alpha 1 (Protobot.Rendering.OutlineFeature).
// Objects without a colour of their own (MaterialPropertyBlock) are left out.
Shader "Hidden/Protobot/OutlineMask"
{
    Properties
    {
        _OutlineObjectColor ("Color", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Mask"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _OutlineObjectColor;  // per renderer (MaterialPropertyBlock), else the material's

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // No colour given (alpha 0): a copy that kept the outline bit without its colour. Draw nothing.
                clip(_OutlineObjectColor.a - 0.5);
                return half4(_OutlineObjectColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
