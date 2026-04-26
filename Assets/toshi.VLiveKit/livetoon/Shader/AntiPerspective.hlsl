#ifndef ANTIPERSPECTIVE_INCLUDED
#define ANTIPERSPECTIVE_INCLUDED

void AntiPerspective_float
(
    float3 ObjectSpacePos_IN,
    float _AntiPerspectiveIntensity,
    out float3 OUT_ObjectSpacePos
)
{
    OUT_ObjectSpacePos = ObjectSpacePos_IN;

    float4 vet = TransformObjectToHClip(ObjectSpacePos_IN);
    float centerVSz = mul(UNITY_MATRIX_V, float4(UNITY_MATRIX_M._m03_m13_m23, 1.0)).z;
    float abs_vet_w = abs(vet.w);
    vet.xy *= lerp(1.0, abs_vet_w / -centerVSz, _AntiPerspectiveIntensity);

    float4 positionClip = vet;
    float4 positionWorld = mul(Inverse(GetWorldToHClipMatrix()), positionClip);
    float4 positionObject = mul(Inverse(GetObjectToWorldMatrix()), positionWorld);

    OUT_ObjectSpacePos = positionObject.xyz;
}

// 嘘パース
void FakePerspective_float
(
    float3 ObjectSpacePos_IN,
    float _Distance,
    float _Focal,
    float _Size,
    out float3 OUT_ObjectSpacePos
)
{
    OUT_ObjectSpacePos = ObjectSpacePos_IN;

    // ワールド座標へ変換
    float3 worldPos = mul(GetObjectToWorldMatrix(), float4(ObjectSpacePos_IN, 1.0)).xyz;

    // カメラ位置を取得
    float3 cameraPos = _WorldSpaceCameraPos;
    float3 viewdir = cameraPos - worldPos;
    float len = length(viewdir) / _Distance;
    float maxv = 2.0;
    len = clamp(len, 0.0, maxv) / maxv;
    len = pow(1.0 - len, 1.0 - _Focal + 0.0001);

    // カメラの向き（後方）を取得
    float3 cameradir = -float3(UNITY_MATRIX_V._m02, UNITY_MATRIX_V._m12, UNITY_MATRIX_V._m22);
    float3 offset = (-cameradir * len * _Size + (viewdir - cameradir) * len * _Size);

    // ワールド座標へオフセットを加え、オブジェクト空間に戻す
    float3 newWorldPos = worldPos + offset;
    float4 newObjectPos4 = mul(Inverse(GetObjectToWorldMatrix()), float4(newWorldPos, 1.0));
    OUT_ObjectSpacePos = newObjectPos4.xyz;
}

#endif // ANTIPERSPECTIVE_INCLUDED
