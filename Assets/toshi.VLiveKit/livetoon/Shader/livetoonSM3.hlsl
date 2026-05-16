#include "./livetoonCore.hlsl"
void anime_perspective(inout float4 v) {
                float3 cameradir = float3(-UNITY_MATRIX_V._m02, -UNITY_MATRIX_V._m12, -UNITY_MATRIX_V._m22); // Camera direction vector
                float3 viewdir = GetWorldSpaceViewDir(v); // Vector from vertex to camera
                float len = length(viewdir) / _Distance; // Distance from vertex to camera
                float maxv = 2; // Maximum distance to parse
                len = clamp(len, 0.0, maxv) / maxv; // Set the distance to be parsed to a range of 0 to 1.
                len = pow(1 - len, 1 - _Focal + 0.0001);
                float factor = _Size; // Good coefficient
                // Use_Macro_UNITY_MATRIX_I_M_instead_of_unity_WorldToObject
                v.xyz += mul(UNITY_MATRIX_I_M, (-cameradir) * len * factor + (viewdir - cameradir) * len * factor); // Move vertices
            }

void AntiPerspective(inout float3 ObjectSpacePos_IN)
{
    // マテリアル側で調整したい場合は [Material] 属性を付ける
float _AntiPerspHeight = 1.5;   // 最大補正になる高さ
    // [1] clip空間へ
    float4 vet = TransformObjectToHClip(ObjectSpacePos_IN);

    // [2] 通常の打ち消し係数を計算
    float  centerVSz  = mul(UNITY_MATRIX_V, float4(UNITY_MATRIX_M._m03_m13_m23, 1.0)).z;
    float  abs_vet_w  = abs(vet.w);
    float  baseCoeff  = lerp(1.0, abs_vet_w / -centerVSz, 1);

    // [3] 高さに応じたフェード係数を作成（y = 0 → 0,  y = _AntiPerspHeight → 1）
    // - 下方向は 0 にクランプ、上方向は 1 でサチる
    float  heightK = saturate(ObjectSpacePos_IN.y / _AntiPerspHeight);

    // [4] 係数をブレンド
    float  finalCoeff = lerp(1.0, baseCoeff, heightK);

    // [5] XY をスケールして視野角を打ち消し
    vet.xy *= finalCoeff;

    // [6] object 空間へ戻す
    float4 positionWorld  = mul(Inverse(GetWorldToHClipMatrix()), vet);
    float4 positionObject = mul(Inverse(GetObjectToWorldMatrix()), positionWorld);
    ObjectSpacePos_IN     = positionObject.xyz;
}
#ifndef VLIVEKIT_LIVETOON_PERSPECTIVE_CORRECTION_INCLUDED
#define VLIVEKIT_LIVETOON_PERSPECTIVE_CORRECTION_INCLUDED
float3 ApplyLiveToonPerspectiveCorrectionOS(float3 positionOS)
{
    float intensity = saturate(_LiveToonPerspectiveCorrectionIntensity);
    if (intensity <= 1.0e-4)
    {
        return positionOS;
    }

    float3 positionRWS = TransformObjectToWorld(positionOS);
    float3 positionWS = GetAbsolutePositionWS(positionRWS);
    float height = saturate((positionWS.y - _LiveToonPerspectiveCorrectionGroundY) / max(_LiveToonPerspectiveCorrectionHeight, 1.0e-4));
    height = pow(height, max(_LiveToonPerspectiveCorrectionHeightPower, 1.0e-4));
    float weight = intensity * height;
    if (weight <= 1.0e-4)
    {
        return positionOS;
    }

    float3 centerRWS = GetCameraRelativePositionWS(_LiveToonPerspectiveCorrectionCenterWS.xyz);
    float4 positionCS = TransformWorldToHClip(positionRWS);
    float4 centerCS = TransformWorldToHClip(centerRWS);
    float centerW = max(abs(centerCS.w), 1.0e-4);
    float correction = abs(positionCS.w) / centerW;
    float2 correctedXY = centerCS.xy + (positionCS.xy - centerCS.xy) * correction;
    positionCS.xy = lerp(positionCS.xy, correctedXY, weight);

    float4 correctedRWS = mul(Inverse(GetWorldToHClipMatrix()), positionCS);
    if (abs(correctedRWS.w) > 1.0e-4)
    {
        correctedRWS.xyz /= correctedRWS.w;
    }

    return TransformWorldToObject(correctedRWS.xyz);
}
#endif

float3 ApplyLiveToonSphericalNormalOS(float3 positionOS, float3 normalOS)
{
    float intensity = saturate(_FaceSphereIntensity);
    if (_isCharFace < 0.5 || intensity <= 1.0e-4)
    {
        return normalize(normalOS);
    }

    float3 sphereNormalOS = positionOS - _FacePosition.xyz;
    if (dot(sphereNormalOS, sphereNormalOS) <= 1.0e-6)
    {
        return normalize(normalOS);
    }

    sphereNormalOS = normalize(sphereNormalOS);
    if (dot(sphereNormalOS, normalOS) < 0.0)
    {
        sphereNormalOS = -sphereNormalOS;
    }

    return normalize(lerp(normalize(normalOS), sphereNormalOS, intensity));
}

v2f LitPassVertex( appdata_full v)
{
    // UNITY_SETUP_INSTANCE_ID (v);
    // anime_perspective(v.vertex);
        // AntiPerspective(v.vertex.xyz);
        // AntiPerspective(_FacePosition.xyz);
    // v.vertex.z += -0.02 * v.vertex.w;
    // v.vertex.xyz += float3(1.0, 0.0, 0.0);
    v.normal = ApplyLiveToonSphericalNormalOS(v.vertex.xyz, v.normal);
    v.vertex.xyz = ApplyLiveToonPerspectiveCorrectionOS(v.vertex.xyz);


    // if ( _isFace == 1) {
    // float4 Sphericalize = float4(0.0, 0.0, 0.0, 1.0);
    // float3 sphereNormal = normalize(v.vertex - Sphericalize.xyz);
	// v.normal = normalize(lerp(v.normal,sphereNormal,_FaceSphereIntensity));
    // }

    // ここで法線の球面化
    
    float4 result = TransformObjectToHClip(v.vertex.xyz);
    return InitializeV2F(v, result, 0);
}

v2f LitPassVertex_Outline(appdata_full v)
{
	// UNITY_SETUP_INSTANCE_ID (v);
    // anime_perspective(v.vertex);
    // AntiPerspective(v.vertex.xyz);
    // v.vertex.z += -0.01 * v.vertex.w;
    // v.vertex.xyz += float3(1.0, 0.0, 0.0);
    v.normal = ApplyLiveToonSphericalNormalOS(v.vertex.xyz, v.normal);
    v.vertex.xyz = ApplyLiveToonPerspectiveCorrectionOS(v.vertex.xyz);
    float4 result = CalculateOutlineVertexClipPosition(v);
    return InitializeV2F(v, result, 1);
}
