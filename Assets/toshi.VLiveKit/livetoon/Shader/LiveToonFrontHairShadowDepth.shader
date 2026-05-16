Shader "Hidden/VLiveKit/LiveToon/FrontHairShadowDepth"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "FrontHairShadowDepth"
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            float4x4 _LiveToonHairShadowVP;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float depth01 : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = GetAbsolutePositionWS(TransformObjectToWorld(input.positionOS));
                float4 shadowClip = mul(_LiveToonHairShadowVP, float4(positionWS, 1.0));
                float invW = rcp(max(abs(shadowClip.w), 1.0e-6));
                float3 shadowNdc = shadowClip.xyz * invW;

                output.positionCS = float4(shadowNdc.xy, saturate(shadowNdc.z * 0.5 + 0.5), 1.0);
                output.depth01 = saturate(shadowNdc.z * 0.5 + 0.5);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return float4(input.depth01, input.depth01, input.depth01, 1.0);
            }
            ENDHLSL
        }
    }
}
