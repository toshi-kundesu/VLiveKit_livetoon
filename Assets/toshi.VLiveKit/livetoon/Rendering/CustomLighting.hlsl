#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.cs.hlsl"

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"


// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
// // #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
// //             // #pragma multi_compile SHADOW_LOW SHADOW_MEDIUM SHADOW_HIGH SHADOW_VERY_HIGH
// //             // #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowContext.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightDefinition.cs.hlsl"

#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS



// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.cs.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
// #define HAS_LIGHTLOOP
// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
// #define SCALARIZE_LIGHT_LOOP (defined(SUPPORTS_WAVE_INTRINSICS) && defined(LIGHTLOOP_TILE_PASS) && SHADERPASS == SHADERPASS_FORWARD)
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesGlobal.cs.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/ShaderVariablesLightLoop.cs.hlsl"

// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
CBUFFER_START(MyUnityPerMaterial)
float4 _TestData;
CBUFFER_END

// struct LightLoopContext
// {
//     int sampleReflection;

//     HDShadowContext shadowContext;
    
//     float contactShadow; // Currently we support only one contact shadow per view
//     float shadowValue; // Stores the value of the cascade shadow map
// };


void CalculateLight_float(
    float3 worldPos, 
    float4 positionSS, 
    float3 normalWS, 
    float3 viewDirWS,
    float _SpecPower, 
    float _LambertIntensity,
    float _SpecularIntensity,
    float _PunctualLightIntensity,
    float _LightIntensityMultiplier,
    float _SmoothStepValue,
    out float3 Direction, 
    out float3 Color,
    out float3 rim
    )
    {

    #if SHADERGRAPH_PREVIEW
    Direction = float3(0.5, 0.5, 0);
    Color = 1;
    rim = float3(0,0,0);
    #else
    Direction = float3(0, 0, 0);
    Color = 0;
    rim = float3(0,0,0);
    if (_DirectionalLightCount > 0)
    {
        for (int i = 0; i < _DirectionalLightCount; i++)
        {
            DirectionalLightData directionalLightData = _DirectionalLightDatas[i];
            Direction = -directionalLightData.forward.xyz;
            Color += directionalLightData.color * GetCurrentExposureMultiplier();
        }
        // DirectionalLightData directionalLightData = _DirectionalLightDatas[0];
        // Direction = -directionalLightData.forward.xyz;
        // Color += directionalLightData.color;
    }
    

    if (_PunctualLightCount > 0)
    {
        for (int i = 0; i < _PunctualLightCount; i++)
        {
            LightData lightData = _LightDatas[i];
            // LightData lightData = FetchLight(i);
            float4 distance;
            float3 punctualLightDir = float3(0.0,0.0,0.0);
            float3 lightToSample = worldPos - lightData.positionRWS;
            distance.w = dot(lightToSample, lightData.forward);

            float3 pixelToLightVec = -lightToSample;
            float  distanceSquared = dot(pixelToLightVec, pixelToLightVec);
            float  reciprocalDistance = rsqrt(distanceSquared);
            float  actualDistance = distanceSquared * reciprocalDistance;
            punctualLightDir = pixelToLightVec * reciprocalDistance;
            distance.xyz = float3(actualDistance, distanceSquared, reciprocalDistance);

            float punctunalLightAttenuation = PunctualLightAttenuation(
                distance, 
                lightData.rangeAttenuationScale, 
                lightData.rangeAttenuationBias, 
                lightData.angleScale, 
                lightData.angleOffset);

            // LightLoopContext context;
            // context.shadowContext  = InitShadowContext();
            // context.shadowValue = 1;			
            // context.sampleReflection = 0;
            // context.splineVisibility = -1;
            // context.contactShadowFade = 0.0;
            // context.contactShadow = 0;

            // float punctualShadowAttenuationValue = 1.0f;


            // if ((lightData.shadowDimmer > 0))
            // {
            //     punctualShadowAttenuationValue = GetPunctualShadowAttenuation(context.shadowContext, positionSS, WorldPos, 0 , lightData.shadowIndex,punctualLightDir, distance.x, lightData.lightType == GPULIGHTTYPE_POINT, lightData.lightType != GPULIGHTTYPE_PROJECTOR_BOX);
            // }

            // float punctualShadowAttenuation = smoothstep(0.0f, 1.0f,punctualShadowAttenuationValue);

            float3 NomalizedLightToSample = normalize(lightToSample);

            float t_Lambert = dot(normalWS, NomalizedLightToSample);
            // t_Lambert *= -1.0f;
            t_Lambert *= 1.0f;

            if (t_Lambert < 0.0f)
            {
                t_Lambert = 0.0f;
            }
            //_SmoothStepValue = 0.5f;

            // float3 diffuseToon = lerp(lightData.color * _ToonLightColor.xyz, _ToonDarkColor.xyz, step(t_Lambert, 0.1f));
            // float3 diffuseToon = lerp(lightData.color * _ToonLightColor.xyz, _ToonDarkColor.xyz, smoothstep(0.01f, 0.19f, t_Lambert));
            float3 diffuseToon = lerp(lightData.color * float3(1,1,1), float3(0,0,0), smoothstep(_SmoothStepValue, 0.2f - _SmoothStepValue, t_Lambert));
            float3 diffuseLig = diffuseToon;

            float3 refVec = reflect(NomalizedLightToSample, normalWS);

            float3 toEye = viewDirWS - worldPos;

            toEye = normalize(toEye);

            


            
            float t_Specular = dot(refVec, toEye);

            if (t_Specular < 0.0f)
            {
                t_Specular = 0.0f;
            }

            float temp_t_Specular = t_Specular;
            float3 tempTestColor = float3(0,0,1) *1000 * pow(t_Specular, _SpecPower);

            // t_Specular = pow(t_Specular, _SpecPower);
            // // ふつうのスペキュラの場合
            // float3 specularLightColor = lightData.color * t_Specular;

            // float3 specularToon = lerp(lightData.color * _ToonLightColor.xyz, _ToonDarkColor.xyz, smoothstep(0.1f, 0.1f, t_Specular));
            float epsilon = 0.1f;
            float lerpValue = 1.0f;
            // smoothstepを自作する

            // smoothstepを自作する
            if (t_Specular < _SmoothStepValue+epsilon)
            {
                lerpValue = 1.0f;
            }
            if (t_Specular > _SmoothStepValue-epsilon)
            {
                lerpValue = 0.0f;
            }
            // SmoothStepValue+-epsilonの間のとき
            if (_SmoothStepValue-epsilon < t_Specular && t_Specular < _SmoothStepValue+epsilon)
            {
                // lerpValue = 0.5f;
                lerpValue = smoothstep(_SmoothStepValue - epsilon, _SmoothStepValue + epsilon, t_Specular);
                lerpValue = 1-lerpValue;
            }

            float3 debugCol = float3(0,0,0);

            float3 specularToon = lerp(float3(1,1,1) *10, float3(0,0,0), lerpValue);

            float3 specularLig = specularToon + debugCol;

            float3 lig = diffuseLig * _LambertIntensity + specularLig * _SpecularIntensity;

            lig *= _PunctualLightIntensity * _LightIntensityMultiplier;
            lig *= punctunalLightAttenuation;

            float NdotV = saturate(dot(normalize(normalWS), normalize(viewDirWS)));
            float _RimPower = 5.0;

            rim = float3(1,1,1) * pow(1.0 - NdotV, 10.001 - _RimPower);

            // if (_PUNCTUALSHADOWATTENUATION > 0.5)
            // {
            //     lig *= punctualShadowAttenuation;
            // }

            // finalLightColor += lig;











            Direction = -lightData.forward.xyz;
            // Color += lightData.color;
            Color += lig * GetCurrentExposureMultiplier();
        }
        // LightData lightData = _LightDatas[0];
        // Direction = -lightData.forward.xyz;
        // Color += lightData.color;
    }
    #endif
}
#endif // CUSTOM_LIGHTING_INCLUDED
