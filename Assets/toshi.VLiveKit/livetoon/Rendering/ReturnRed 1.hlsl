#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

void CalculateLight_float(float3 WorldPos, out float3 Direction, out float3 Color){
    #if SHADERGRAPH_PREVIEW
    Direction = float3(0.5, 0.5, 0);
    Color = 1;
    #else
    Color = 0;
    if (_DirectionalLightCount > 0)
    {
        DirectionalLightData directionalLightData = _DirectionalLightDatas[0];
        Direction = -directionalLightData.forward.xyz;
        Color += directionalLightData.color;
    }

    // if (_PunctualLightCount > 0)
    // {
        LightData punctualLightData = FetchLight(0);
        Direction = -punctualLightData.forward.xyz;
        Color += punctualLightData.color;
    // }
    #endif
}
#endif // CUSTOM_LIGHTING_INCLUDED