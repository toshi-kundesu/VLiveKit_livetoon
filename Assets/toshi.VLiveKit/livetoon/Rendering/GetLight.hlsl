#ifndef GETLIGHT_INCLUDED
#define GETLIGHT_INCLUDED

// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
// #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"

void GetLight_float(float3 input, out float3 output)
{
    float3 finalColor = float3(0, 0, 0);
    // DirectionalLightData directionalLightData = _DirectionalLightDatas[0];
    for (int i = 0; i < _PunctualLightCount; i++)
    {
        LightData lightData = FetchLight(i);
        finalColor += lightData.color;

    // output = input * (directionalLightData.color + lightData.color);

    }
    
    output = input * finalColor;
}

#endif // GETLIGHT_INCLUDED