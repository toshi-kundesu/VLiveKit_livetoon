
    struct appdata_full {
    float4 vertex   : POSITION;  // 頂点座標
    float4 tangent  : TANGENT;   // タンジェント（接線）ベクトル
    float3 normal   : NORMAL;    // 法線ベクトル
    float4 texcoord : TEXCOORD0; // UV座標（第1セット）
    float4 texcoord1 : TEXCOORD1; // UV座標（第2セット）
    float4 texcoord2 : TEXCOORD2; // UV座標（第3セット）
    float4 texcoord3 : TEXCOORD3; // UV座標（第4セット）
    float4 color    : COLOR;     // 頂点カラー
    UNITY_VERTEX_INPUT_INSTANCE_ID // インスタンシング用のID
};



struct v2f
    {
        float4 pos : SV_POSITION;
        float3 posWorld : TEXCOORD0;
        half3 tspace0 : TEXCOORD1;
        half3 tspace1 : TEXCOORD2;
        half3 tspace2 : TEXCOORD3;
        float2 uv0 : TEXCOORD4;
        float isOutline : TEXCOORD5;
        float3 normalWS					: TEXCOORD7;
        float3 bitangentWS              : TEXCOORD8;
        float4 projPos					: TEXCOORD9;
        float3 smoNorm					: TEXCOORD10;
        float4 color				: COLOR;
        float3 viewDirWS				: TEXCOORD11;

        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ★★ add : 簡易 RGB↔HSV
// float3 RgbToHsv(float3 c) {
//     float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
//     float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
//     float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

//     float d = q.x - min(q.w, q.y);
//     float e = 1.0e-10;
//     return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)),
//                   d / (q.x + e),
//                   q.x);
// }

// float3 HsvToRgb(float3 c) {
//     float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
//     float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
//     return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
// }

float LiveToonLuminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float LiveToonBoundarySaturationWeight(float3 baseColor, float weight, float alpha, float saturationStrength)
{
    float luma = LiveToonLuminance(saturate(baseColor));
    float visibleColor = smoothstep(0.08, 0.24, luma);
    return saturate(weight * max(saturationStrength, 0.0)) * visibleColor * saturate(alpha * 8.0);
}

float3 LiveToonPreventBoundaryDarkening(float3 baseColor, float3 boostedColor)
{
    float baseLuma = LiveToonLuminance(max(baseColor, float3(0.0, 0.0, 0.0)));
    float boostedLuma = LiveToonLuminance(max(boostedColor, float3(0.0, 0.0, 0.0)));
    boostedColor *= max(1.0, baseLuma / max(boostedLuma, 1.0e-4));
    return max(boostedColor, baseColor);
}

// ============================================================
// Custom Shadow (Hair ShadowMap) for DirectionalLight Attenuation
// ============================================================

// ============================================================
// VirtualLight Custom Shadow (Multi slots) 影attenだけ返す
// ============================================================

// ---- Globals from C# (Multi slots) ----
int _VirtualLightCount;

// Shadow maps & VP (slot0..3)
TEXTURE2D(_HairShadowMap0); SAMPLER(sampler_HairShadowMap0); float4x4 _HairShadow_LightVP0;
TEXTURE2D(_HairShadowMap1); float4x4 _HairShadow_LightVP1;
TEXTURE2D(_HairShadowMap2); float4x4 _HairShadow_LightVP2;
TEXTURE2D(_HairShadowMap3); float4x4 _HairShadow_LightVP3;

TEXTURE2D(_LiveToonBoxShadowMap); SAMPLER(sampler_LiveToonBoxShadowMap);
float4x4 _LiveToonBoxShadowVP;
float _LiveToonBoxShadowEnabled;
float _LiveToonBoxShadowStrength;
float _LiveToonBoxShadowBias;
float _LiveToonBoxShadowUseDepth;
float _LiveToonBoxShadowSilhouetteAttenuation;
float _LiveToonBoxShadowFlipU;
float _LiveToonBoxShadowFlipV;
float _LiveToonBoxShadowInvertSilhouette;

// projection texture
TEXTURE2D(_ProjectTex0); SAMPLER(sampler_ProjectTex0); float _ProjectEnable0; float _ProjectIntensity0; float _ProjectFlipU0; float _ProjectFlipV0;
TEXTURE2D(_ProjectTex1); float _ProjectEnable1; float _ProjectIntensity1; float _ProjectFlipU1; float _ProjectFlipV1;
TEXTURE2D(_ProjectTex2); float _ProjectEnable2; float _ProjectIntensity2; float _ProjectFlipU2; float _ProjectFlipV2;
TEXTURE2D(_ProjectTex3); float _ProjectEnable3; float _ProjectIntensity3; float _ProjectFlipU3; float _ProjectFlipV3;

// ---- Params ----
// 定数にする
float _ShadowBias=0.004;              // Light-space depth bias for the front hair shadow map.
float _ShadowStrength=0.85;    // 0..1 (1でフル適用)
float _ClampToBox=1;  // 0/1 (1でXY+Z箱判定)
float _LiveToonFrontHairShadowDebugForce;
float _LiveToonFrontHairShadowDebugAttenuation;
float _LiveToonFrontHairShadowDebugIgnoreProjection;
float _LiveToonFrontHairShadowDebugUseCasterSilhouette;

// 「1灯」分の影判定（投影範囲内か＆髪深度比較）
// 返り値：atten (1=光が届く, 0=遮蔽)
// out: inRange (範囲内 0/1)

// HDRP の Camera Relative Rendering 対策：WS を Absolute に戻す
float3 ToAbsoluteWorld(float3 posWS_or_RWS)
{
    // HDRP は内部でカメラ相対座標(RWS)を使うことがある
    // AbsoluteWS = RWS + _WorldSpaceCameraPos
    #if defined(SHADEROPTIONS_CAMERA_RELATIVE_RENDERING) && (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
        return posWS_or_RWS + _WorldSpaceCameraPos;
    #else
        return posWS_or_RWS;
    #endif
}

float3 LiveToonSafeNormalize(float3 value, float3 fallback)
{
    return dot(value, value) > 1.0e-6 ? normalize(value) : normalize(fallback);
}

float3 LiveToonLimitFaceLightDirection(float3 lightDir)
{
    float intensity = saturate(_FaceLightLimitIntensity);
    if (intensity <= 1.0e-4)
    {
        return normalize(lightDir);
    }

    float3 faceF = LiveToonSafeNormalize(_FaceForwardDirection.xyz, float3(0.0, 0.0, 1.0));
    float3 faceU = LiveToonSafeNormalize(_FaceUpDirection.xyz, float3(0.0, 1.0, 0.0));
    float3 faceR = LiveToonSafeNormalize(cross(faceU, faceF), float3(1.0, 0.0, 0.0));
    faceU = LiveToonSafeNormalize(cross(faceF, faceR), faceU);

    float3 toLight = -normalize(lightDir);
    float yawRad = atan2(dot(faceR, toLight), dot(faceF, toLight));
    float pitchRad = asin(clamp(dot(faceU, toLight), -1.0, 1.0));

    float yawStepRad = max(radians(max(_FaceLightYawStep, 1.0)), 1.0e-4);
    float fixedYaw = floor(yawRad / yawStepRad + 0.5) * yawStepRad;
    float halfYawStep = yawStepRad * 0.5;
    float yawOffset = atan2(sin(yawRad - fixedYaw), cos(yawRad - fixedYaw));
    float normalizedYawOffset = saturate(abs(yawOffset) / max(halfYawStep, 1.0e-4));
    float stickyRange = min(saturate(_FaceLightYawStickyRange), 0.95);
    float easedYawOffset = smoothstep(stickyRange, 1.0, normalizedYawOffset) * halfYawStep * sign(yawOffset);
    float limitedYaw = fixedYaw + easedYawOffset;
    float yawDelta = atan2(sin(limitedYaw - yawRad), cos(limitedYaw - yawRad));
    yawRad += yawDelta * intensity;

    pitchRad = lerp(pitchRad, 0.0, saturate(_FaceLightPitchFlatten) * intensity);

    float cosP = cos(pitchRad);
    float3 fixedToLight =
        faceF * cosP * cos(yawRad) +
        faceR * cosP * sin(yawRad) +
        faceU * sin(pitchRad);

    return normalize(-fixedToLight);
}

float2 LiveToonApplyProjectionFlip(float2 uv, float flipU, float flipV)
{
    uv.x = flipU > 0.5 ? 1.0 - uv.x : uv.x;
    uv.y = flipV > 0.5 ? 1.0 - uv.y : uv.y;
    return uv;
}

void SampleOneLight(
    float3 positionWS,
    float4x4 vp,
    TEXTURE2D_PARAM(shadowMap, shadowMap_sampler),
    float shadowBias,
    float projFlipU,
    float projFlipV,
    out float inRange,
    out float atten,
    out float2 uv
)
{
    float4 clip = mul(vp, float4(positionWS, 1));
    float3 ndc = clip.xyz / max(1e-6, clip.w); // -1..1

    uv = ndc.xy * 0.5 + 0.5;
    uv = LiveToonApplyProjectionFlip(uv, projFlipU, projFlipV);

    float inXY = (uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1) ? 1.0 : 0.0;
    float inZ  = (ndc.z >= -1.0 && ndc.z <= 1.0) ? 1.0 : 0.0;
    inRange = (_ClampToBox > 0.5) ? (inXY * inZ) : inXY;

    if (inRange < 0.5)
    {
        atten = 1.0;
        return;
    }

    float depth01 = saturate(ndc.z * 0.5 + 0.5);
    float hairDepth01 = SAMPLE_TEXTURE2D(shadowMap, shadowMap_sampler, uv).r;

    atten = step(depth01 - shadowBias, hairDepth01);
}

float LiveToonSampleBoxShadow(float3 positionWS)
{
    if (_LiveToonBoxShadowEnabled < 0.5)
    {
        return 1.0;
    }

    float4 clip = mul(_LiveToonBoxShadowVP, float4(positionWS, 1.0));
    float3 ndc = clip.xyz / max(abs(clip.w), 1.0e-6);
    float2 uv = ndc.xy * 0.5 + 0.5;
    uv = LiveToonApplyProjectionFlip(uv, _LiveToonBoxShadowFlipU, _LiveToonBoxShadowFlipV);

    float inXY = (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) ? 1.0 : 0.0;
    float inZ = (ndc.z >= -1.0 && ndc.z <= 1.0) ? 1.0 : 0.0;
    float inRange = inXY * inZ;
    if (inRange < 0.5)
    {
        return 1.0;
    }

    float depth01 = saturate(ndc.z * 0.5 + 0.5);
    float casterDepth01 = SAMPLE_TEXTURE2D(_LiveToonBoxShadowMap, sampler_LiveToonBoxShadowMap, uv).r;
    float casterMask = casterDepth01 < 0.999 ? 1.0 : 0.0;
    casterMask = _LiveToonBoxShadowInvertSilhouette > 0.5 ? 1.0 - casterMask : casterMask;

    float depthAttenuation = step(depth01 - _LiveToonBoxShadowBias, casterDepth01);
    float silhouetteAttenuation = lerp(1.0, saturate(_LiveToonBoxShadowSilhouetteAttenuation), casterMask);
    float attenuation = _LiveToonBoxShadowUseDepth > 0.5
        ? lerp(1.0, depthAttenuation, casterMask)
        : silhouetteAttenuation;

    return lerp(1.0, attenuation, saturate(_LiveToonBoxShadowStrength) * inRange);
}


float4 EL_AT_SC(PositionInputs posInput, float3 V, float4 inputColor)
{
	float4 result = inputColor;

    if (_BlendMode == (int)RENDER_MODE_OPAQUE)
    {

    }
    else if (_BlendMode == (int)RENDER_MODE_CUTOUT)
    {

    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT)
    {
#ifdef _ENABLE_FOG_ON_TRANSPARENT
        float3 volColor, volOpacity;
		EvaluateAtmosphericScattering(posInput, V, volColor, volOpacity);

		result.rgb = result.rgb * (1.0 - volOpacity) + volColor * result.a;
#endif
    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT_WITH_ZWRITE)
    {
#ifdef _ENABLE_FOG_ON_TRANSPARENT
        float3 volColor, volOpacity;
		EvaluateAtmosphericScattering(posInput, V, volColor, volOpacity);

		result.rgb = result.rgb * (1.0 - volOpacity) + volColor * result.a;
#endif

    }
    
	return result;
}

float hash21(float2 p) {
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 34.345);
    return frac(p.x * p.y);
}

float fade(float t) {
    return t * t * t * (t * (t * 6 - 15) + 10);
}

float gradient(int hash, float x, float y) {
    switch (hash & 3) {
        case 0: return  x + y;
        case 1: return -x + y;
        case 2: return  x - y;
        case 3: return -x - y;
    }
    return 0.0;
}

float perlinNoise(float2 p)
{
    float2 pi = floor(p);
    float2 pf = frac(p);

    int xi = (int)pi.x;
    int yi = (int)pi.y;

    float2 u = float2(fade(pf.x), fade(pf.y));

    int a = xi + yi * 57;
    int b = (xi + 1) + yi * 57;
    int c = xi + (yi + 1) * 57;
    int d = (xi + 1) + (yi + 1) * 57;

    float aa = gradient(a, pf.x, pf.y);
    float ba = gradient(b, pf.x - 1.0, pf.y);
    float ab = gradient(c, pf.x, pf.y - 1.0);
    float bb = gradient(d, pf.x - 1.0, pf.y - 1.0);

    float x1 = lerp(aa, ba, u.x);
    float x2 = lerp(ab, bb, u.x);
    return lerp(x1, x2, u.y) * 0.5 + 0.5;
}


float4 CalculateOutlineVertexClipPosition(appdata_full v)
{
    float BrushScale = 200;
    float2 uv = v.texcoord * BrushScale;

    // Perlin風ノイズで線の太さにゆらぎを加える
    // 揺らぎ周期を早くするには、BrushScaleを大きくする
    float outlineNoise = perlinNoise(uv);
    // float outlineWidth = _OutlineWidth * 3 * lerp(0.1, 1.5, outlineNoise);
    float outlineWidth = _OutlineWidth;

float outlineTex = SAMPLE_TEXTURE2D_LOD(_OutlineWidthTexture, sampler_OutlineWidthTexture, TRANSFORM_TEX(v.texcoord, _MainTex), 0).r;            float4 positionOS = v.vertex;
 #if defined(MTOON_OUTLINE_WIDTH_WORLD)
 // Use_Macro_UNITY_MATRIX_I_M_instead_of_unity_WorldToObject
    float3 worldNormalLength = length(mul((float3x3)transpose(UNITY_MATRIX_I_M), v.normal));
    float3 outlineOffset = 0.003 *1.5 * outlineWidth * outlineTex * worldNormalLength * v.normal;
    float4 vertex = TransformObjectToHClip(v.vertex + outlineOffset);
 #elif defined(MTOON_OUTLINE_WIDTH_SCREEN)
 // unity_CameraInvProjection はビルトイン変数 
    // float4 nearUpperRight = mul(unity_CameraInvProjection, float4(1, 1, UNITY_NEAR_CLIP_VALUE, _ProjectionParams.y));
    float4 nearUpperRight = mul(UNITY_MATRIX_P, float4(1, 1, UNITY_NEAR_CLIP_VALUE, _ProjectionParams.y));
    float aspect = abs(nearUpperRight.y / nearUpperRight.x);
    float4 vertex = TransformObjectToHClip(v.vertex);
    // 修正: Built-in依存 → HDRP対応
    float3 normalWS = TransformObjectToWorldNormal(v.normal.xyz);
    float3 viewNormal = TransformWorldToViewDir(normalWS);
    // float3 clipNormal = TransformViewToProjection(viewNormal.xyz);
    float3 clipNormal = mul(UNITY_MATRIX_P, float4(viewNormal, 0));
    float2 projectedNormal = normalize(clipNormal.xy);
    projectedNormal *= min(vertex.w, _OutlineScaledMaxDistance);
    projectedNormal.x *= aspect;
    vertex.xy += 0.01 *1.5 * outlineWidth * outlineTex * projectedNormal.xy * saturate(1 - abs(normalize(viewNormal).z)); // ignore offset when normal toward camera
    // zオフセット
    // vertex.z += 0.01 * vertex.w;
 #else
    float4 vertex = TransformObjectToHClip(v.vertex);
 #endif
    return vertex;
        }

float CalculateTransparentOpacity(float4 mainTex)
{
    float threshold = clamp(-20.0, 1.0, _TransparentThreshold);
    float thresholdAlpha = smoothstep(threshold, 1.0, mainTex.a);
    float maskedAlpha = thresholdAlpha * mainTex.r;
    return lerp(maskedAlpha, thresholdAlpha, _Color.a);
}

        //RT TRANS CO
void RT_TRANS_CO( float2 uv , float4 _MainTex_var , out float RTD_TRAN_OPA_Sli , float RTD_CO , out bool bo_co_val, bool is_rt, float3 positionWS, float3 normalDirection, float2 positionCS, inout float3 GLO_OUT) 
{
	RTD_TRAN_OPA_Sli = 1.0;
	bo_co_val = false;



    if (_BlendMode == (int)RENDER_MODE_OPAQUE)
    {

    }
    else if (_BlendMode == (int)RENDER_MODE_CUTOUT)
    {
			float RTD_CO_ON;
			if (1.0 - _Cutoff > 0.5)
			{
				RTD_CO_ON = 1.0 - (1.0 - 2.0 * ((1.0 - _Cutoff) - 0.5)) * (1.0 - (_MainTex_var.a));
			}
			else
			{
				
                RTD_CO_ON = 2.0 * (1.0 - _Cutoff) * (_MainTex_var.a);
			}
			RTD_CO = RTD_CO_ON;
					clip(RTD_CO - 0.5);


    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT)
    {
				RTD_TRAN_OPA_Sli = CalculateTransparentOpacity(_MainTex_var);

    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT_WITH_ZWRITE)
    {
				RTD_TRAN_OPA_Sli = CalculateTransparentOpacity(_MainTex_var);

    }

}
inline v2f InitializeV2F(appdata_full v, float4 projectedVertex, float isOutline)
    {
        v2f o;
        
        o.pos = projectedVertex;
        // o.posWorld = mul(UNITY_MATRIX_M, v.vertex);
        o.posWorld = TransformObjectToWorld(v.vertex.xyz);
        o.normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, v.normal));
        // half4 worldTangent = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);
        o.uv0 = v.texcoord;
        half3 worldNormal = TransformObjectToWorldNormal(v.normal);
        // half3 worldTangent = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);
        half3 worldTangent = TransformObjectToWorldDir(v.tangent);
        half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
        half3 worldBitangent = cross(worldNormal, worldTangent) * tangentSign;
        o.tspace0 = half3(worldTangent.x, worldBitangent.x, worldNormal.x);
        o.tspace1 = half3(worldTangent.y, worldBitangent.y, worldNormal.y);
        o.tspace2 = half3(worldTangent.z, worldBitangent.z, worldNormal.z);
        o.isOutline = isOutline;
        o.color = v.color;

        // add
        o.viewDirWS = normalize(GetWorldSpaceViewDir(o.posWorld));
  
        return o;
    }
float3 RT_RELGI_SUB1(
    PositionInputs posInput,
    float3 viewReflectDirection,
    float3 viewDirection,
    float3 RTD_GI_FS_OO,
    float3 RTD_SHAT_COL,
    float3 RTD_MCIALO,
    float RTD_STIAL,

    in float3 ref_dif,
    bool isNonRT)
{

    float3 RTD_SL_OFF_OTHERS = float3(1.0, 1.0, 1.0);

    float4 IDTex = LOAD_TEXTURE2D_X(_IndirectDiffuseTexture, posInput.positionSS);
    float3 ALGI = EvaluateAmbientProbe(lerp(float3(0.0, 0.0, 0.0), float3(1.0, 1.0, 1.0), RTD_GI_FS_OO));
    float ADVAL = 7;

    RTD_SL_OFF_OTHERS =
        lerp(RTD_SHAT_COL, RTD_MCIALO, RTD_STIAL) *
        lerp(ALGI, (IDTex.xyz * 2.0) * GetInverseCurrentExposureMultiplier(), 1.0) *
        GetCurrentExposureMultiplier() *
        (1.0 + ADVAL);

    return RTD_SL_OFF_OTHERS;
}

bool TryBuildPunctualFallbackDirectionalLight(v2f i, out DirectionalLightData fallbackDirectionalLightData)
{
    fallbackDirectionalLightData = (DirectionalLightData)0;

    float3 accumulatedColor = float3(0.0, 0.0, 0.0);
    float3 accumulatedDirection = float3(0.0, 0.0, 0.0);
    float accumulatedWeight = 0.0;

    for (int lightIndex = 0; lightIndex < _PunctualLightCount; lightIndex++)
    {
        LightData punctualLightData = _LightDatas[lightIndex];

        float4 distance;
        float3 lightToSample = i.posWorld.xyz - punctualLightData.positionRWS;
        distance.w = dot(lightToSample, punctualLightData.forward);

        float3 pixelToLightVec = -lightToSample;
        float distanceSquared = max(dot(pixelToLightVec, pixelToLightVec), 1.0e-4);
        float reciprocalDistance = rsqrt(distanceSquared);
        float actualDistance = distanceSquared * reciprocalDistance;
        float3 punctualLightDir = pixelToLightVec * reciprocalDistance;
        distance.xyz = float3(actualDistance, distanceSquared, reciprocalDistance);

        float attenuation = PunctualLightAttenuation(
            distance,
            punctualLightData.rangeAttenuationScale,
            punctualLightData.rangeAttenuationBias,
            punctualLightData.angleScale,
            punctualLightData.angleOffset);

        float3 weightedColor = punctualLightData.color * attenuation;
        float weight = max(weightedColor.r, max(weightedColor.g, weightedColor.b));

        accumulatedColor += weightedColor;
        accumulatedDirection += punctualLightDir * weight;
        accumulatedWeight += weight;
    }

    if (accumulatedWeight <= 1.0e-4)
    {
        return false;
    }

    float3 fallbackLightDir = normalize(normalize(i.viewDirWS) + float3(0.0, 0.25, 0.0));
    if (dot(accumulatedDirection, accumulatedDirection) > 1.0e-4)
    {
        fallbackLightDir = normalize(accumulatedDirection / accumulatedWeight);
    }

    float fallbackIntensity = max(_FallbackLightIntensity, 0.0);
    float3 fallbackColor = accumulatedColor * _FallbackLightColor.rgb * fallbackIntensity * 2.5;
    float fallbackMax = max(fallbackColor.r, max(fallbackColor.g, fallbackColor.b));
    float fallbackMaxAllowed = 16.0 * max(fallbackIntensity, 1.0e-4);

    if (fallbackMax > fallbackMaxAllowed)
    {
        fallbackColor *= fallbackMaxAllowed / fallbackMax;
    }

    fallbackDirectionalLightData.forward = -fallbackLightDir;
    fallbackDirectionalLightData.color = fallbackColor;
    fallbackDirectionalLightData.shadowIndex = -1;

    return true;
}

float3 LiveToonAttenuateLightColor(float3 color)
{
    float monochrome = max(1.0e-5, max(color.r, max(color.g, color.b)));
    return lerp(color, monochrome.xxx, saturate(_LightColorAttenuation));
}

float3 LiveToonSampleEnvironmentReflection(PositionInputs posInput, float3 viewDirectionWS, float3 normalWS, float perceptualSmoothness)
{
    float3 reflectionColor = float3(0.0, 0.0, 0.0);
    float hierarchyWeight = 0.0;
    float perceptualRoughness = saturate(1.0 - perceptualSmoothness);
    float3 reflectionDirection = reflect(-normalize(viewDirectionWS), normalize(normalWS));

    LightLoopContext context;
    context.shadowContext = InitShadowContext();
    context.shadowValue = 1;
    context.contactShadow = 0;
    context.contactShadowFade = 0;
    context.sampleReflection = SINGLE_PASS_CONTEXT_SAMPLE_REFLECTION_PROBES;
#ifdef APPLY_FOG_ON_SKY_REFLECTIONS
    context.positionWS = posInput.positionWS;
#endif

    uint envLightStart;
    uint envLightCount;
#ifndef LIGHTLOOP_DISABLE_TILE_AND_CLUSTER
    GetCountAndStart(posInput, LIGHTCATEGORY_ENV, envLightStart, envLightCount);
#else
    envLightCount = _EnvLightCount;
    envLightStart = 0;
#endif

    uint envLightListOffset = 0;
    while (envLightListOffset < envLightCount && hierarchyWeight < 1.0)
    {
        uint envLightIndex = FetchIndex(envLightStart, envLightListOffset);
        envLightListOffset++;
        if (envLightIndex == -1)
        {
            break;
        }

        EnvLightData envLightData = FetchEnvLight(envLightIndex);
        float weight = 1.0;
        float3 probeDirection = reflectionDirection;
        EvaluateLight_EnvIntersection(posInput.positionWS, normalWS, envLightData, envLightData.influenceShapeType, probeDirection, weight);

        float lod = PerceptualRoughnessToMipmapLevel(perceptualRoughness) * envLightData.roughReflections;
        float4 sampleColor = SampleEnv(context, envLightData.envIndex, probeDirection, lod, envLightData.rangeCompressionFactorCompensation, posInput.positionNDC);
        weight *= sampleColor.a;
        UpdateLightingHierarchyWeights(hierarchyWeight, weight);
        reflectionColor += sampleColor.rgb * weight * envLightData.multiplier;
    }

    if (_EnvLightSkyEnabled && hierarchyWeight < 1.0)
    {
        context.sampleReflection = SINGLE_PASS_CONTEXT_SAMPLE_SKY;
        EnvLightData skyEnvLightData = InitSkyEnvLightData(0);
        float skyWeight = 1.0 - hierarchyWeight;
        float skyLod = PerceptualRoughnessToMipmapLevel(perceptualRoughness) * skyEnvLightData.roughReflections;
        float4 skyColor = SampleEnv(context, skyEnvLightData.envIndex, reflectionDirection, skyLod, skyEnvLightData.rangeCompressionFactorCompensation, posInput.positionNDC);
        reflectionColor += skyColor.rgb * skyWeight * skyEnvLightData.multiplier;
    }

    return reflectionColor * GetCurrentExposureMultiplier();
}

float3 LiveToonEvaluateEnvironmentLighting(PositionInputs posInput, float3 viewDirectionWS, float3 normalWS, float3 baseColor)
{
    float3 ambientDiffuse = max(EvaluateAmbientProbe(normalWS), float3(0.0, 0.0, 0.0)) * GetCurrentExposureMultiplier();
    ambientDiffuse = LiveToonAttenuateLightColor(ambientDiffuse);

    float3 environment = ambientDiffuse * baseColor * saturate(_IndirectLightIntensity);

    float reflectionIntensity = max(_ReflectionProbeIntensity, 0.0);
    if (reflectionIntensity > 1.0e-4)
    {
        float3 probeReflection = LiveToonSampleEnvironmentReflection(posInput, viewDirectionWS, normalWS, saturate(_ReflectionProbeSmoothness));
        probeReflection = LiveToonAttenuateLightColor(probeReflection);

        float fresnel = pow(1.0 - saturate(dot(normalize(normalWS), normalize(viewDirectionWS))), 5.0);
        float reflectionWeight = lerp(0.04, 1.0, fresnel) * reflectionIntensity;
        environment += probeReflection * reflectionWeight;
    }

    return environment;
}

float2 LiveToonWetHash2(float2 value)
{
    float2 h = float2(
        dot(value, float2(127.1, 311.7)),
        dot(value, float2(269.5, 183.3)));
    return frac(sin(h) * 43758.5453);
}

float LiveToonEvaluateSweatMask(float2 uv)
{
    float scale = max(1.0, _SweatScale);
    float2 gridUV = uv * scale;
    float2 baseCell = floor(gridUV);
    float mask = 0.0;

    UNITY_UNROLL
    for (int y = -1; y <= 1; y++)
    {
        UNITY_UNROLL
        for (int x = -1; x <= 1; x++)
        {
            float2 cell = baseCell + float2(x, y);
            float2 randomValue = LiveToonWetHash2(cell);
            float2 local = gridUV - cell;
            float fall = _Time.y * _SweatSpeed * lerp(0.2, 1.15, randomValue.x);
            float2 center = float2(randomValue.x, frac(randomValue.y - fall));
            float2 p = local - center;
            p.x *= 1.35;
            float d = length(p);
            float radius = lerp(0.075, 0.19, LiveToonWetHash2(cell + 13.7).x);
            float drop = 1.0 - smoothstep(radius * 0.45, radius, d);

            float trailDown = center.y - local.y;
            float trailLength = radius * lerp(1.4, 4.8, randomValue.y);
            float trail = smoothstep(0.0, radius * 0.3, trailDown)
                * (1.0 - smoothstep(trailLength, trailLength * 1.2, trailDown))
                * (1.0 - smoothstep(radius * 0.16, radius * 0.62, abs(p.x)));

            mask = max(mask, drop + trail * _SweatTrail);
        }
    }

    return saturate(mask);
}

float3 LiveToonApplyWetSkinOverlays(float3 color, float2 mainUv, float3 viewDirectionWS, float3 normalWS)
{
    float sweatIntensity = saturate(_SweatIntensity);
    if (sweatIntensity > 1.0e-4)
    {
        float sweatMask = LiveToonEvaluateSweatMask(mainUv) * sweatIntensity;
        float viewRim = pow(1.0 - saturate(dot(normalize(viewDirectionWS), normalize(normalWS))), 2.5);
        float sparkle = saturate(0.2 + viewRim * 1.4) * sweatMask * _SweatHighlight;
        color = lerp(color, color * lerp(float3(1.0, 1.0, 1.0), _SweatColor.rgb, 0.25), sweatMask * 0.18);
        color += _SweatColor.rgb * sparkle;
    }

    float wetHairIntensity = saturate(_WetHairOverlayIntensity);
    if (wetHairIntensity > 1.0e-4)
    {
        float2 overlayUv = TRANSFORM_TEX(mainUv, _WetHairOverlayTex);
        float4 wetHair = SAMPLE_TEXTURE2D(_WetHairOverlayTex, sampler_WetHairOverlayTex, overlayUv);
        float wetHairMask = saturate(max(wetHair.a, max(wetHair.r, max(wetHair.g, wetHair.b))) * wetHairIntensity);
        float3 wetHairColor = lerp(_WetHairOverlayColor.rgb, wetHair.rgb * _WetHairOverlayColor.rgb, step(0.001, max(wetHair.r, max(wetHair.g, wetHair.b))));
        color = lerp(color, wetHairColor, wetHairMask * _WetHairOverlayColor.a);
        color += _WetHairOverlayColor.rgb * wetHairMask * _WetHairOverlayGloss * 0.35;
    }

    return color;
}

float4 CalculateDirectionalLighting(v2f i, DirectionalLightData directionalLightData, bool useSceneShadow, out float3 rimColor, out float3 specCol)
{
    // rimColor = float3(1,0,0);
    #ifdef MTOON_CLIP_IF_OUTLINE_IS_NONE
    #ifdef MTOON_OUTLINE_WIDTH_WORLD
    #elif MTOON_OUTLINE_WIDTH_SCREEN
    #else
        clip(-1);
    #endif
#endif
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
UNITY_SETUP_INSTANCE_ID (i);

// const
const float PI_2 = 6.28318530718;
const float EPS_COL = 0.00001;

float2 mainUv = TRANSFORM_TEX(i.uv0, _MainTex);

float uvAnim = SAMPLE_TEXTURE2D(_UvAnimMaskTexture, sampler_UvAnimMaskTexture, mainUv).r * _Time.y;
mainUv += float2(_UvAnimScrollX, _UvAnimScrollY) * uvAnim;
float rotateRad = _UvAnimRotation * PI_2 * uvAnim;
const float2 rotatePivot = float2(0.5, 0.5);
mainUv = mul(float2x2(cos(rotateRad), -sin(rotateRad), sin(rotateRad), cos(rotateRad)), mainUv - rotatePivot) + rotatePivot;

float4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex , mainUv);



//RT_TRANS_CO

float RTD_TRAN_OPA_Sli;
bool bo_co_val;
float RTD_CO;
float3 GLO_OUT = (float3)0.0;
RT_TRANS_CO(i.uv0, mainTex, RTD_TRAN_OPA_Sli, RTD_CO, bo_co_val, false, i.posWorld.xyz, i.normalWS, i.pos.xy, GLO_OUT);

float alpha = RTD_TRAN_OPA_Sli;


// normal
#ifdef _NORMALMAP
    half3 tangentNormal = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, float4(mainUv, 0, 0)), _BumpScale);
    half3 worldNormal;
    worldNormal.x = dot(i.tspace0, tangentNormal);
    worldNormal.y = dot(i.tspace1, tangentNormal);
    worldNormal.z = dot(i.tspace2, tangentNormal);
#else
    half3 worldNormal = half3(i.tspace0.z, i.tspace1.z, i.tspace2.z);
#endif
// worldNormal = half3(i.tspace0.z, i.tspace1.z, i.tspace2.z);
// float3 worldView = normalize(lerp(_WorldSpaceCameraPos.xyz - i.posWorld.xyz, UNITY_MATRIX_V[2].xyz, unity_OrthoParams.w));
float3 worldView = normalize(i.viewDirWS);
worldNormal *= step(0, dot(worldView, worldNormal)) * 2 - 1; // flip if projection matrix is flipped
worldNormal *= lerp(+1.0, -1.0, i.isOutline);
worldNormal = normalize(worldNormal);

float3 worldNormal_rembrand = worldNormal;
float3 worldNormal_default = worldNormal;
float3 rembrandLightingTex = float3(0,0,0);
if (_isCharFace == 1)
{
    rembrandLightingTex = SAMPLE_TEXTURE2D(_RembrandLightingMask, sampler_RembrandLightingMask, i.uv0.xy).rgb;
    // 黒い部分のローカルx軸反転を行う、マスクの部分のみ法線方向にVector3(-1,1,1)を乗算
    // 一度ワールドノーマルをオブジェクトスペースに戻す
    float3 objectNormal = mul((float3x3)UNITY_MATRIX_I_M, worldNormal);

    objectNormal = lerp(objectNormal, float3(-1,1,1) * objectNormal, 1-rembrandLightingTex.r);
    // もう一度ワールドノーマルに変換
    worldNormal_rembrand = mul((float3x3)UNITY_MATRIX_M, objectNormal);
    
}

// Unity lighting
float3 lightDir = float3(0.5, 0.5, -0.5);
float3 lightColor = float3(1.0, 1.0, 1.0);
float3 directionalLightColor = float3(0,0,0);
float3 punctualLightColor = float3(0,0,0);
// UNITY_LIGHT_ATTENUATION(shadowAttenuation, i, i.posWorld.xyz);
float shadowAttenuation = 1;
DirectionalLightData directionalLightData_test = directionalLightData;
lightDir = directionalLightData_test.forward.xyz;
lightDir *= -1;


//------------------------------------------------------------
// Face 用ライト固定処理
//------------------------------------------------------------
//------------------------------------------------------------
// Face-light 角度固定処理（Yaw & Pitch）
//------------------------------------------------------------
if (_isCharFace == 1 && _FaceLightLimitIntensity > EPS_COL)
{
    lightDir = LiveToonLimitFaceLightDirection(lightDir);
}

float3 V = normalize(worldView);
float3 N = normalize(worldNormal_default);
float3 L = normalize(lightDir);

// JGT wrapped lighting
// mainTex.rgbの彩度を上げたものをサブサーフェスカラーにする
float3 sssColorBase = RgbToHsv(mainTex.rgb);
sssColorBase.y *= 3.5;
// 彩度が上がりすぎたらおさえる
if(sssColorBase.y > 0.8) {
    sssColorBase.y = 0.8;
}
sssColorBase = HsvToRgb(sssColorBase);
float4 _SubsurfaceColor = float4(sssColorBase,0.5);
float _SubsurfaceRadius = 1;
float _Shininess = 64;
float4 _SpecularColor = float4(1,1,1,1);

float NdotL = saturate(dot(N, L));
float subsurfaceRadius = _SubsurfaceRadius;
float norm  = (2.0 + subsurfaceRadius) / (2.0 * (1.0 + subsurfaceRadius));
float t     = max(0, NdotL + subsurfaceRadius) / (1.0 + subsurfaceRadius);
float wrapped = pow(t, 1.0 + subsurfaceRadius) * norm;

// Subsurface contribution
// dot積において0.5より小さい場合は0にする
float3 sssColor = directionalLightData.color * _SubsurfaceColor.rgb * wrapped;

// Specular (Blinn-Phong)
// toonにする
// float3 H = normalize(V + L);
// float NdotH = saturate(dot(N, H));
// float spec = pow(NdotH, _Shininess) * _SpecularColor.a;
// // toonにする
// spec = step(0.5, spec);
// float3 specColor = directionalLightData.color * _SpecularColor.rgb * spec;
// directionalLightColor += specColor * GetCurrentExposureMultiplier();





// Blend SSS and base color (mainTex.rgb)
float3 baseColor = mainTex.rgb;
float scatter = saturate(_SubsurfaceColor.a);
float3 litColor = lerp(baseColor, sssColor, scatter);
litColor *= GetCurrentExposureMultiplier() *1;
// directionalLightColor += litColor;

// rim
// rimを強く出す
float rimPower = 1.1;

float rim_test = pow(saturate(1.0 - dot(worldNormal, worldView) + _RimLift), max(rimPower, EPS_COL));
float rim_test_default = rim_test;
// メインテクスチャ×カラーの、リムライト部分だけ取得し、その部分を彩度を上げる
float3 rim_mainTexRGB = rim_test * mainTex.rgb;
float3 rim_mainTexHSV = RgbToHsv(rim_mainTexRGB);
rim_mainTexHSV.y *= 3.5;
// 彩度が上がりすぎたらおさえる
if(rim_mainTexHSV.y > 0.8) {
    rim_mainTexHSV.y = 0.8;
}
rim_mainTexRGB = HsvToRgb(rim_mainTexHSV);
// あとでスクリーン合成する


// rim_test = rim_test * rim_mainTexHSV;
rim_test = step(0.5, rim_test) * directionalLightData.color * GetCurrentExposureMultiplier();
// ライトのdiffuseドット積を0-1正規化したものをかける
float rim_dot = saturate(dot(L, worldNormal));
rim_test *= rim_dot;
// directionalLightColor += rim_test * _RimColor.rgb * SAMPLE_TEXTURE2D(_RimTexture, sampler_RimTexture, i.uv0).rgb;




directionalLightColor += directionalLightData_test.color * GetCurrentExposureMultiplier() / 10;

lightColor = directionalLightColor + punctualLightColor;
// 法線をいじる前のハーフランバートと比較し値が大きい方のみ採用
half dotNL_rembrand = dot(lightDir, worldNormal_rembrand);
half dotNL = dot(lightDir, worldNormal);
dotNL = max(dotNL, dotNL_rembrand);
// dotNL = dotNL_rembrand;
// #ifdef MTOON_FORWARD_ADD
// まず PositionInputs を作る（frag内で作って引数で渡すのが理想）
// uint2 tileIndex = uint2(i.pos.xy) / GetTileSize();
uint2 tileIndex = uint2(i.pos.xy) / GetTileSize();
PositionInputs posInput = GetPositionInput(i.pos.xy, _ScreenSize.zw, i.pos.z, i.pos.w, i.posWorld.xyz, tileIndex);
// PositionInputs posInput = GetPositionInput(i.pos.xy, _ScreenSize.zw, i.pos.z, i.pos.w, i.posWorld.xyz, tileIndex);

// shadow
if (useSceneShadow && directionalLightData.shadowIndex >= 0)
{
HDShadowContext sc = InitShadowContext();
float3 shadow = GetDirectionalShadowAttenuation(
    sc,
    posInput.positionSS,          // 2D pixel coords (or posInput.positionSS.xy)
    posInput.positionWS,
    worldNormal,                  // あなたが作ってる最終N（worldNormal_default など）
    directionalLightData.shadowIndex,
    -directionalLightData.forward // 光が来る方向
);

shadowAttenuation = shadow.x; // まずはxでOK
}
else
{
    shadowAttenuation = 1;
}

float hairSpecDirectionalShadow = saturate(shadowAttenuation);

if(_isFace > 0.5) {
    shadowAttenuation = 1;
}
if(_isHair > 0.5) {
    shadowAttenuation = 1;
}
// SampleOneLight(
//     i.positionWS,
//     _HairShadow_LightVP0,
//     TEXTURE2D_ARGS(_HairShadowMap0, sampler_HairShadowMap0),
//     _ShadowBias,
//     _ProjectFlipU0,
//     _ProjectFlipV0,
//     inRange, atten, uv
// );
float shadowMul = 1.0;
float shadowDebugInRange = 0.0;
float shadowDebugCasterSilhouette = 0.0;

float3 absWS = ToAbsoluteWorld(i.posWorld); // ★ここが肝
shadowMul *= LiveToonSampleBoxShadow(absWS);

if (_VirtualLightCount > 0)
{
    float inRange, atten;
    float2 uv;
    SampleOneLight(absWS, _HairShadow_LightVP0,
        TEXTURE2D_ARGS(_HairShadowMap0, sampler_HairShadowMap0),
        _ShadowBias, _ProjectFlipU0, _ProjectFlipV0,
        inRange, atten, uv);

    shadowDebugInRange = max(shadowDebugInRange, inRange);
    float debugCasterDepth0 = SAMPLE_TEXTURE2D(_HairShadowMap0, sampler_HairShadowMap0, saturate(uv)).r;
    float debugCasterRange0 = max(inRange, saturate(_LiveToonFrontHairShadowDebugIgnoreProjection));
    shadowDebugCasterSilhouette = max(shadowDebugCasterSilhouette, (debugCasterDepth0 < 0.999 ? 1.0 : 0.0) * debugCasterRange0);
    shadowMul *= lerp(1.0, atten, _ShadowStrength * inRange);
}

if (_VirtualLightCount > 1)
{
    float inRange, atten;
    float2 uv;
    SampleOneLight(absWS, _HairShadow_LightVP1,
        TEXTURE2D_ARGS(_HairShadowMap1, sampler_HairShadowMap0),
        _ShadowBias, _ProjectFlipU1, _ProjectFlipV1,
        inRange, atten, uv);

    shadowDebugInRange = max(shadowDebugInRange, inRange);
    float debugCasterDepth1 = SAMPLE_TEXTURE2D(_HairShadowMap1, sampler_HairShadowMap0, saturate(uv)).r;
    float debugCasterRange1 = max(inRange, saturate(_LiveToonFrontHairShadowDebugIgnoreProjection));
    shadowDebugCasterSilhouette = max(shadowDebugCasterSilhouette, (debugCasterDepth1 < 0.999 ? 1.0 : 0.0) * debugCasterRange1);
    shadowMul *= lerp(1.0, atten, _ShadowStrength * inRange);
}

if (_VirtualLightCount > 2)
{
    float inRange, atten;
    float2 uv;
    SampleOneLight(absWS, _HairShadow_LightVP2,
        TEXTURE2D_ARGS(_HairShadowMap2, sampler_HairShadowMap0),
        _ShadowBias, _ProjectFlipU2, _ProjectFlipV2,
        inRange, atten, uv);

    shadowDebugInRange = max(shadowDebugInRange, inRange);
    float debugCasterDepth2 = SAMPLE_TEXTURE2D(_HairShadowMap2, sampler_HairShadowMap0, saturate(uv)).r;
    float debugCasterRange2 = max(inRange, saturate(_LiveToonFrontHairShadowDebugIgnoreProjection));
    shadowDebugCasterSilhouette = max(shadowDebugCasterSilhouette, (debugCasterDepth2 < 0.999 ? 1.0 : 0.0) * debugCasterRange2);
    shadowMul *= lerp(1.0, atten, _ShadowStrength * inRange);
}

if (_VirtualLightCount > 3)
{
    float inRange, atten;
    float2 uv;
    SampleOneLight(absWS, _HairShadow_LightVP3,
        TEXTURE2D_ARGS(_HairShadowMap3, sampler_HairShadowMap0),
        _ShadowBias, _ProjectFlipU3, _ProjectFlipV3,
        inRange, atten, uv);

    shadowDebugInRange = max(shadowDebugInRange, inRange);
    float debugCasterDepth3 = SAMPLE_TEXTURE2D(_HairShadowMap3, sampler_HairShadowMap0, saturate(uv)).r;
    float debugCasterRange3 = max(inRange, saturate(_LiveToonFrontHairShadowDebugIgnoreProjection));
    shadowDebugCasterSilhouette = max(shadowDebugCasterSilhouette, (debugCasterDepth3 < 0.999 ? 1.0 : 0.0) * debugCasterRange3);
    shadowMul *= lerp(1.0, atten, _ShadowStrength * inRange);
}

if (_LiveToonFrontHairShadowDebugForce > 0.5)
{
    float debugRange = _LiveToonFrontHairShadowDebugUseCasterSilhouette > 0.5
        ? shadowDebugCasterSilhouette
        : max(shadowDebugInRange, saturate(_LiveToonFrontHairShadowDebugIgnoreProjection));
    shadowMul = min(shadowMul, lerp(1.0, saturate(_LiveToonFrontHairShadowDebugAttenuation), debugRange));
}


// testShadowCol = 

#ifdef MTOON_FORWARD_ADD
    half lightAttenuation = 1;
#else
    half lightAttenuation = shadowAttenuation * lerp(1, shadowAttenuation, _ReceiveShadowRate * SAMPLE_TEXTURE2D(_ReceiveShadowTexture, sampler_ReceiveShadowTexture, float4(mainUv, 0, 0)).r);
#endif

lightAttenuation *= shadowMul;

// float customShadowMul = SampleVirtualLightsShadowAtten(posInput.positionWS);


// Decide albedo color rate from Direct Light
half shadingGrade = 1.0 - _ShadingGradeRate * (1.0 - SAMPLE_TEXTURE2D(_UvAnimMaskTexture, sampler_UvAnimMaskTexture, float4(mainUv, 0, 0)).r);
half lightIntensity = dotNL; // [-1, +1]
lightIntensity = lightIntensity * 0.5 + 0.5; // from [-1, +1] to [0, 1]
// lightAttenuation = 1;
// まず、仮のlightIntensityを作って、lightAttenuationを掛けたを掛けたあとのものと、レンブラントのlightIntensityを用意して、レンブラントの処理がなくならないようにする
half lightIntensity_tmp = lightIntensity;
lightIntensity = lightIntensity * lightAttenuation; // receive shadow
// lightIntensity = max(lightIntensity, lightIntensity_tmp);
// レンブラントのテクスチャが少しでも塗られている場合、そこだけ上書きする
// if(_isCharFace == 1) {
//     if(rembrandLightingTex.r > 0) {
//         lightIntensity = lightIntensity_tmp;
//     }
// }
lightIntensity = lightIntensity * shadingGrade; // darker
lightIntensity = lightIntensity * 2.0 - 1.0; // from [0, 1] to [-1, +1]
// tooned. mapping from [minIntensityThreshold, maxIntensityThreshold] to [0, 1]
    half maxIntensityThreshold = lerp(1, _ShadeShift, _ShadeToony);
    half minIntensityThreshold = _ShadeShift;
    lightIntensity = saturate((lightIntensity - minIntensityThreshold) / max(EPS_COL, (maxIntensityThreshold - minIntensityThreshold)));
    // Albedo color
    half4 shade = _ShadeColor * SAMPLE_TEXTURE2D(_ShadeTexture, sampler_ShadeTexture, float4(mainUv, 0, 0));
    half4 lit = _Color * mainTex;
half3 col = lerp(shade.rgb, lit.rgb, lightIntensity);

// Direct Light
    half3 lighting = lightColor;
    lighting = lerp(lighting, max(EPS_COL, max(lighting.x, max(lighting.y, lighting.z))), _LightColorAttenuation); // color atten
#ifdef MTOON_FORWARD_ADD
#ifdef _ALPHABLEND_ON
    lighting *= step(0, dotNL); // darken if transparent. Because Unity's transparent material can't receive shadowAttenuation.
#endif
    lighting *= 0.5; // darken if additional light.
    lighting *= min(0, dotNL) + 1; // darken dotNL < 0 area by using half lambert
    lighting *= shadowAttenuation; // darken if receiving shadow
#else
    // base light does not darken.
#endif
    col *= lighting;

#ifdef MTOON_FORWARD_ADD
#else
    // half3 toonedGI = 0.5 * (ShadeSH9(half4(0, 1, 0, 1)) + ShadeSH9(half4(0, -1, 0, 1)));
    // half3 indirectLighting = lerp(toonedGI, ShadeSH9(half4(worldNormal, 1)), _IndirectLightIntensity);
    half3 toonedGI = half3(0,0,0);
    half3 indirectLighting = half3(0,0,0);
    indirectLighting = lerp(indirectLighting, max(EPS_COL, max(indirectLighting.x, max(indirectLighting.y, indirectLighting.z))), _LightColorAttenuation); // color atten
    col += indirectLighting * lit;

    // Lambert を 0‑1 で取得
    half uNormDot = saturate(dot(lightDir, worldNormal) * 0.5 + 0.5);

    // _LambertThresh ± _GradWidth で 0→1 のフェード係数を作る
    //  uNormDot >= _LambertThresh        → 0   (オーバーレイ無し)
    //  uNormDot <= _LambertThresh‑Width  → 1   (フルオーバーレイ)
    half overlayW = saturate((_LambertThresh - uNormDot) / _GradWidth);

    // オーバーレイ用の色を生成
    half boundarySaturationStrength = max(_Sat, 0.0h);
    half3 hsv  = RgbToHsv(lightColor);
    hsv.y     *= lerp(1.0h, 3.5h, saturate(boundarySaturationStrength));
    hsv.y     *= max(1.0h, boundarySaturationStrength);
    hsv.y      = min(hsv.y, lerp(0.8h, 1.0h, saturate(boundarySaturationStrength - 1.0h)));
    half3 ovIn = HsvToRgb(hsv);
    // return float4(ovIn, 1);

    // Photoshop Overlay
    half3 ovTh = step(0.5h, col);
    half3 ovCol = lerp(ovIn * col * 2.0h,
                       1.0h - 2.0h * (1.0h - ovIn) * (1.0h - col),
                       ovTh);
    // ovColは、colより明るくならない
    // そのためには、colの明るさを取得して、それをovColの明るさが超えないようにする
    half maxCol = max(ovCol.x, max(ovCol.y, ovCol.z));
    if(maxCol > 1) {
        ovCol = ovCol / maxCol;
    }
    // return float4(ovCol, 1);

    // _GradWidth に従って滑らかに合成
    // lightintensity によってオーバーレイの強さを変えるが、明るいところ(1)ではそのままで、0でもそのまま、1から0に向かって弱くなる、ということをしたい
    // そのためには、lightintensity を 0-1 に変換してから、その値をオーバーレイの強さとして使う必要がある
    half lightIntensity01;
    // float3 col_test = float3(lightIntensity, lightIntensity, lightIntensity);
    // return float4(lightIntensity01, lightIntensity01, lightIntensity01, 1);
    // return float4(lightIntensity01, lightIntensity01, lightIntensity01, 1);
    // col = lerp(col, ovCol, lightIntensity01);
    lightIntensity01 = lightIntensity;
    if(lightIntensity01 == 1) {
        lightIntensity01 = 0;
    }
    //　ここで、0-1になっているが急な変化になっているので、0.9-1.0の間では徐々に0になるようにする
    if(lightIntensity01 > 0.5) {
        lightIntensity01 = -1;
        lightIntensity01 += 1;
    }

    specCol = 0;
if (_isHair > 0.5) {
    // float3 Nw = worldNormal;
    float3 V_tmp = normalize(worldView);
    float3 L_tmp = normalize(lightDir);
    
// Tangent, Bitangent をそれぞれ列方向で復元
float3 T = float3(i.tspace0.x, i.tspace1.x, i.tspace2.x);
float3 B = float3(i.tspace0.y, i.tspace1.y, i.tspace2.y);
float3 N = float3(i.tspace0.z, i.tspace1.z, i.tspace2.z); // = worldNormal
float  NdL = saturate(dot(N, L_tmp));

/* --- 異方性ハイライト + ジッター -------------------------------- */

// ★ 基本パラメータ（固定値なら直接書き換え）
const float BasePosition  = _Position;   // 帯中心の基準オフセット
const float Sharpness     = _Sharpness; // 帯の鋭さ
const float Intensity     = _Intensity;   // 強度 0-1

// ── 1. ジッター値を取得（UV ノイズ → -0.5〜+0.5）
float jitter = SAMPLE_TEXTURE2D(_JitterTex, sampler_JitterTex, float4(mainUv, 0, 0)).r;           // 0-1
float jitterOffset = (jitter - 0.5) * _JitterIntensity;

// ── 2. Binormal をシフト
float Position = BasePosition + jitterOffset;       // 最終オフセット
float3 BinormalShifted = normalize(B - N * Position);

// ── 3. ハーフベクトル
float3 H = normalize(L_tmp + V_tmp);

// ── 4. Dot → 0-1
float bdoth = dot(BinormalShifted, H) * 0.5 + 0.5;

// ── 5. 帯状関数（山形）
float band = bdoth * (1.0 - bdoth) * 4.0;

// ── 6. シャープ化 & 強度
float highlight = pow(band, Sharpness);
// ここで2極化
highlight  *= Intensity;

// ── 7. 色合成（連続トーン）
specCol = _SpecColor.rgb * highlight *
          directionalLightData.color;

// 2極化
// if
// specCol = step(0.2, specCol);
specCol *= GetCurrentExposureMultiplier() * 0.1;

// lightIntensityをかけると、境界でフェードしてくれないので、専用のマスクを作る
// specNdLをかけると、境界でフェードしてくれないので、専用のマスクを作る
float specNdL = dot(N, L_tmp);
if(specNdL < 0) {
    specNdL = 0;
}

// float specMask = smoothstep(0.5, 1, lightIntensity) * specNdL;
float specShadowMask = smoothstep(0.05, 0.95, hairSpecDirectionalShadow);
specCol *= specNdL * specShadowMask;
// specCol += specNdL;

/* --- 異方性ハイライト + ジッター ここまで ---------------------- */

}


    // return float4(lightIntensity01, lightIntensity01, lightIntensity01, 1);
    half boundarySaturationWeight = LiveToonBoundarySaturationWeight(col, lightIntensity01, alpha, boundarySaturationStrength);
    ovCol = LiveToonPreventBoundaryDarkening(col, ovCol);
    col = lerp(col, ovCol, boundarySaturationWeight);
    
    col = min(col, lit); // comment out if you want to PBR absolutely.
#endif
// parametric rim lighting
    rimColor = float3(0,0,0);
#ifdef MTOON_FORWARD_ADD
    half3 staticRimLighting = 0;
    half3 mixedRimLighting = lighting;
#else
    half3 staticRimLighting = 1;
    half3 mixedRimLighting = lighting + indirectLighting;
    // half3 mixedRimLighting = lighting;
#endif
    half3 rimLighting = lerp(staticRimLighting, mixedRimLighting, _RimLightingMix);
    half3 rim = pow(saturate(1.0 - dot(worldNormal, worldView) + _RimLift), max(_RimFresnelPower, EPS_COL)) * _RimColor.rgb * SAMPLE_TEXTURE2D(_RimTexture, sampler_RimTexture, i.uv0).rgb;
    // col += lerp(rim * rimLighting, half3(0, 0, 0), i.isOutline);
    rimColor += lerp(rim * rimLighting, half3(0, 0, 0), i.isOutline);

// additive matcap
#ifdef MTOON_FORWARD_ADD
#else
    half3 worldCameraUp = normalize(UNITY_MATRIX_V[1].xyz);
    half3 worldViewUp = normalize(worldCameraUp - worldView * dot(worldView, worldCameraUp));
    half3 worldViewRight = normalize(cross(i.viewDirWS, worldViewUp));
    half2 matcapUv = half2(dot(worldViewRight, worldNormal), dot(worldViewUp, worldNormal)) * 0.5 + 0.5;
    half3 matcapLighting = SAMPLE_TEXTURE2D(_SphereAdd, sampler_SphereAdd, matcapUv);
    col += lerp(matcapLighting, half3(0, 0, 0), i.isOutline);
#endif

                 // Emission
#ifdef MTOON_FORWARD_ADD
                #else
                    half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, float4(mainUv, 0, 0)).rgb * _EmissionColor.rgb;
                    col += lerp(emission, half3(0, 0, 0), i.isOutline);
                #endif
    // outline
#ifdef MTOON_OUTLINE_COLOR_FIXED
    col = lerp(col, _OutlineColor, i.isOutline);
#elif MTOON_OUTLINE_COLOR_MIXED
    col = lerp(col, _OutlineColor * lerp(half3(1, 1, 1), col, _OutlineLightingMix), i.isOutline);
#else
#endif

    // debug
#ifdef MTOON_DEBUG_NORMAL
    #ifdef MTOON_FORWARD_ADD
        return float4(0, 0, 0, 0);
    #else
        return float4(worldNormal * 0.5 + 0.5, alpha);
    #endif
#elif MTOON_DEBUG_LITSHADERATE
    #ifdef MTOON_FORWARD_ADD
        return float4(0, 0, 0, 0);
    #else
        return float4(lightIntensity * lighting, alpha);
    #endif
#endif


////////// global illumination //////////
// uint2 tileIndex = uint2(i.pos.xy) / GetTileSize();
	// PositionInputs posInput = GetPositionInput(i.pos.xy, _ScreenSize.zw, i.pos.z, i.pos.w, i.posWorld.xyz, tileIndex);

	// float isFrontFace = ( facing >= 0 ? 1 : 0 );
	float4 objPos = mul ( GetObjectToWorldMatrix(), float4(0.0,0.0,0.0,1.0) );
	float2 sceneUVs = (i.projPos.xy / i.projPos.w);
    // float3x3 tangentTransform = float3x3( i.tangentWS.xyz, i.bitangentWS, i.normalWS);
    float3 viewDirection = GetWorldSpaceNormalizeViewDir(i.posWorld);

    // float3 _NormalMap_var = UnpackNormal( SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap , TRANSFORM_TEX(i.uv0, _NormalMap) ) );
	// ふつうにノーマルマップを適用
    float3 _NormalMap_var = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap , TRANSFORM_TEX(i.uv0, _BumpMap) ).rgb;
		// #endif
	
		float3 normalLocal = lerp(float3(0.0,0.0,1.0),_NormalMap_var,_BumpScale);

    // float3 normalDirection = SafeNormalize(mul( normalLocal, tangentTransform ));
	float3 normalDirection = normalize(normalLocal);
    float3 viewReflectDirection = reflect( -viewDirection, normalDirection );
		
		float4 _MainTex_var = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex , i.uv0);

	float3 RTD_TEX_COL;
	RTD_TEX_COL = _MainTex_var.rgb * (_Color.rgb * 0.1);
		// float3 RTD_MCIALO_IL = _MainTex_var.rgb * (_MainColor.rgb * 0.1);

	// //RT_TRANS_CO
	// float RTD_TRAN_OPA_Sli;
	// #if N_F_TRANS_ON
		
	// 	float Trans_Val = 1.0;
		
	// 	#ifndef N_F_CO_ON
	// 		Trans_Val = RTD_TRAN_OPA_Sli;
	// 	#endif	
		
	// #else

	// 	float Trans_Val = 1.0;

	// #endif
	float Trans_Val = 1.0;

		float3 RTD_CA = RT_RELGI_SUB1(posInput, viewReflectDirection, viewDirection, float3(0,0,0), 0, RTD_TEX_COL, 1, (float3)0.0, false);

	float3 outColor = float3(RTD_CA) *2;
    // col += outColor;
    // col = litColor;
    // col = rim_test;
	float4 result = float4(col, alpha);
    result.rgb += specCol;
    
    // スクリーン合成
    // result.rgb = 1- ((1 - result.rgb) * (1 - rim_mainTexRGB * 0.5));
    // result.rgb = float3(_testFloatData,_isCharFace,_isHair);
    // result.rgb = float3(dotNL,dotNL,dotNL);
    // result.rgb = customShadowMul;
    // result.rgb= float3(shadowMul,shadowMul,shadowMul);
    return result;
}

float4 CalculatePunctualLighting(v2f i, LightData punctualLightData, out float3 punctualDiffuse)
{
    punctualDiffuse = float3(0,0,0);
    // float4 result = (float4)0.0;
    #ifdef MTOON_CLIP_IF_OUTLINE_IS_NONE
    #ifdef MTOON_OUTLINE_WIDTH_WORLD
    #elif MTOON_OUTLINE_WIDTH_SCREEN
    #else
        clip(-1);
    #endif
#endif
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
UNITY_SETUP_INSTANCE_ID (i);

// const
const float PI_2 = 6.28318530718;
const float EPS_COL = 0.00001;

float2 mainUv = TRANSFORM_TEX(i.uv0, _MainTex);

float uvAnim = SAMPLE_TEXTURE2D(_UvAnimMaskTexture, sampler_UvAnimMaskTexture, mainUv).r * _Time.y;
mainUv += float2(_UvAnimScrollX, _UvAnimScrollY) * uvAnim;
float rotateRad = _UvAnimRotation * PI_2 * uvAnim;
const float2 rotatePivot = float2(0.5, 0.5);
mainUv = mul(float2x2(cos(rotateRad), -sin(rotateRad), sin(rotateRad), cos(rotateRad)), mainUv - rotatePivot) + rotatePivot;

float4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex , mainUv);


//RT_TRANS_CO

float RTD_TRAN_OPA_Sli;
bool bo_co_val;
float RTD_CO;
float3 GLO_OUT = (float3)0.0;
RT_TRANS_CO(i.uv0, mainTex, RTD_TRAN_OPA_Sli, RTD_CO, bo_co_val, false, i.posWorld.xyz, i.normalWS, i.pos.xy, GLO_OUT);

float alpha = RTD_TRAN_OPA_Sli;


// normal
#ifdef _NORMALMAP
    half3 tangentNormal = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, float4(mainUv, 0, 0)), _BumpScale);
    half3 worldNormal;
    worldNormal.x = dot(i.tspace0, tangentNormal);
    worldNormal.y = dot(i.tspace1, tangentNormal);
    worldNormal.z = dot(i.tspace2, tangentNormal);
#else
    half3 worldNormal = half3(i.tspace0.z, i.tspace1.z, i.tspace2.z);
#endif

// float3 worldView = normalize(lerp(_WorldSpaceCameraPos.xyz - i.posWorld.xyz, UNITY_MATRIX_V[2].xyz, unity_OrthoParams.w));
float3 worldView = normalize(i.viewDirWS);
worldNormal *= step(0, dot(worldView, worldNormal)) * 2 - 1; // flip if projection matrix is flipped
worldNormal *= lerp(+1.0, -1.0, i.isOutline);
worldNormal = normalize(worldNormal);

// float3 V   = normalize(_WorldSpaceCameraPos - i.worldPos);


// Unity lighting
float3 lightDir = float3(0.5, 0.5, -0.5);
float3 lightColor = float3(1.0, 1.0, 1.0);
float3 directionalLightColor = float3(0,0,0);
float3 punctualLightColor = float3(0,0,0);
// UNITY_LIGHT_ATTENUATION(shadowAttenuation, i, i.posWorld.xyz);
float shadowAttenuation = 1;
LightData LightData_test = punctualLightData;

float4 distance;
            float3 punctualLightDir = float3(0.0,0.0,0.0);
            float3 lightToSample = i.posWorld.xyz - punctualLightData.positionRWS;
            distance.w = dot(lightToSample, punctualLightData.forward);

            float3 pixelToLightVec = -lightToSample;
            float  distanceSquared = dot(pixelToLightVec, pixelToLightVec);
            float  reciprocalDistance = rsqrt(distanceSquared);
            float  actualDistance = distanceSquared * reciprocalDistance;
            punctualLightDir = pixelToLightVec * reciprocalDistance;
            distance.xyz = float3(actualDistance, distanceSquared, reciprocalDistance);
// distance = 1;
//////
            float punctunalLightAttenuation = PunctualLightAttenuation(
                distance, 
                punctualLightData.rangeAttenuationScale, 
                punctualLightData.rangeAttenuationBias, 
                punctualLightData.angleScale, 
                punctualLightData.angleOffset);
            // sAttenuation = punctunalLightAttenuation;
            LightLoopContext context;
            context.shadowContext  = InitShadowContext();
            context.shadowValue = 1;			
            context.sampleReflection = 0;
            // context.splineVisibility = -1;
            context.contactShadowFade = 0.0;
            context.contactShadow = 0;
            float punctualShadowAttenuationValue = 1.0f;
                            uint2 tileIndex = uint2(i.pos.xy) / GetTileSize();
                PositionInputs posInput = GetPositionInput(i.pos.xy, _ScreenSize.zw, i.pos.z, i.pos.w, i.posWorld.xyz, tileIndex);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(i.posWorld);
                    if ((punctualLightData.shadowDimmer > 0))
                    {
                        punctualShadowAttenuationValue = GetPunctualShadowAttenuation(context.shadowContext, posInput.positionSS, posInput.positionWS, 0 , punctualLightData.shadowIndex,punctualLightDir, distance.x, punctualLightData.lightType == GPULIGHTTYPE_POINT, punctualLightData.lightType != GPULIGHTTYPE_PROJECTOR_BOX);
                    }
                    float punctualShadowAttenuation = smoothstep(0.0f, 1.0f,punctualShadowAttenuationValue);

            float3 NomalizedLightToSample = normalize(lightToSample);
            lightDir = NomalizedLightToSample;
lightDir *= -1;



punctualLightColor += punctualLightData.color * GetCurrentExposureMultiplier() / 10;

lightColor = directionalLightColor + punctualLightColor;
// T



half dotNL = dot(lightDir, worldNormal);

// 影なし
// dotNL = 1;
// #ifdef MTOON_FORWARD_ADD
#ifdef MTOON_FORWARD_ADD
    // half lightAttenuation = 1;
    half lightAttenuation = punctunalLightAttenuation;
#else
    // half lightAttenuation = shadowAttenuation * lerp(1, shadowAttenuation, _ReceiveShadowRate * SAMPLE_TEXTURE2D(_ReceiveShadowTexture, sampler_ReceiveShadowTexture, float4(mainUv, 0, 0)).r);
    half lightAttenuation = punctunalLightAttenuation;
#endif
// Decide albedo color rate from Direct Light
half shadingGrade = 1.0 - _ShadingGradeRate * (1.0 - SAMPLE_TEXTURE2D(_UvAnimMaskTexture, sampler_UvAnimMaskTexture, float4(mainUv, 0, 0)).r);
half lightIntensity = dotNL; // [-1, +1]

lightIntensity = lightIntensity * 0.5 + 0.5; // from [-1, +1] to [0, 1]
lightIntensity = lightIntensity * lightAttenuation; // receive shadow
// lightIntensity = lightIntensity * shadingGrade; // darker
// lightIntensity = lightIntensity * 2.0 - 1.0; // from [0, 1] to [-1, +1]
// // tooned. mapping from [minIntensityThreshold, maxIntensityThreshold] to [0, 1]
//     half maxIntensityThreshold = lerp(1, _ShadeShift, _ShadeToony);
//     half minIntensityThreshold = _ShadeShift;
//     lightIntensity = saturate((lightIntensity - minIntensityThreshold) / max(EPS_COL, (maxIntensityThreshold - minIntensityThreshold)));
//     // Albedo color
    half4 shade = _ShadeColor * SAMPLE_TEXTURE2D(_ShadeTexture, sampler_ShadeTexture, float4(mainUv, 0, 0));
    half4 lit = _Color * mainTex;
half3 col = lerp(shade.rgb, lit.rgb, lightIntensity);
half3 colWithoutTex = lerp(float3(0,0,0), float3(1,1,1), lightIntensity);
// punctualDiffuse = lightIntensity;

// Direct Light
    half3 lighting = lightColor;
    lighting = lerp(lighting, max(EPS_COL, max(lighting.x, max(lighting.y, lighting.z))), _LightColorAttenuation); // color atten
#ifdef MTOON_FORWARD_ADD
#ifdef _ALPHABLEND_ON
    lighting *= step(0, dotNL); // darken if transparent. Because Unity's transparent material can't receive shadowAttenuation.
#endif
    lighting *= 0.5; // darken if additional light.
    lighting *= min(0, dotNL) + 1; // darken dotNL < 0 area by using half lambert
    lighting *= shadowAttenuation; // darken if receiving shadow
#else
    // base light does not darken.
#endif
// lighting = 1;
    col *= lighting;
    colWithoutTex *= lighting;
    punctualDiffuse = colWithoutTex;
    // punctualDiffuse = lighting;

    

#ifdef MTOON_FORWARD_ADD
#else
    // half3 toonedGI = 0.5 * (ShadeSH9(half4(0, 1, 0, 1)) + ShadeSH9(half4(0, -1, 0, 1)));
    // half3 indirectLighting = lerp(toonedGI, ShadeSH9(half4(worldNormal, 1)), _IndirectLightIntensity);
    half3 toonedGI = half3(0,0,0);
    half3 indirectLighting = half3(0,0,0);
    indirectLighting = lerp(indirectLighting, max(EPS_COL, max(indirectLighting.x, max(indirectLighting.y, indirectLighting.z))), _LightColorAttenuation); // color atten
    col += indirectLighting * lit;

    // Lambert を 0‑1 で取得
    half uNormDot = saturate(dot(lightDir, worldNormal) * 0.5 + 0.5);

    uNormDot = 1;

    // _LambertThresh ± _GradWidth で 0→1 のフェード係数を作る
    //  uNormDot >= _LambertThresh        → 0   (オーバーレイ無し)
    //  uNormDot <= _LambertThresh‑Width  → 1   (フルオーバーレイ)
    half overlayW = saturate((_LambertThresh - uNormDot) / _GradWidth);

    // オーバーレイ用の色を生成
    half3 hsv  = RgbToHsv(lightColor);
    hsv.y     *= 4;                      // 彩度ブースト
    half3 ovIn = HsvToRgb(hsv);
    // return float4(ovIn, 1);

    // Photoshop Overlay
    half3 ovTh = step(0.5h, col);
    half3 ovCol = lerp(ovIn * col * 2.0h,
                       1.0h - 2.0h * (1.0h - ovIn) * (1.0h - col),
                       ovTh);
    // return float4(ovCol, 1);

    // _GradWidth に従って滑らかに合成
    // lightintensity によってオーバーレイの強さを変えるが、明るいところ(1)ではそのままで、0でもそのまま、1から0に向かって弱くなる、ということをしたい
    // そのためには、lightintensity を 0-1 に変換してから、その値をオーバーレイの強さとして使う必要がある
    half lightIntensity01;
    // float3 col_test = float3(lightIntensity, lightIntensity, lightIntensity);
    // return float4(lightIntensity01, lightIntensity01, lightIntensity01, 1);
    // return float4(lightIntensity01, lightIntensity01, lightIntensity01, 1);
    // col = lerp(col, ovCol, lightIntensity01);
    lightIntensity01 = lightIntensity;
    if(lightIntensity01 == 1) {
        lightIntensity01 = 0;
    }
    //　ここで、0-1になっているが急な変化になっているので、0.9-1.0の間では徐々に0になるようにする
    if(lightIntensity01 > 0.5) {
        lightIntensity01 = -1;
        lightIntensity01 += 1;
    }

    // return float4(lightIntensity01, lightIntensity01, lightIntensity01, 1);
    // col = lerp(col, ovCol, lightIntensity01);
    
    // col = min(col, lit); // comment out if you want to PBR absolutely.
#endif
// parametric rim lighting
#ifdef MTOON_FORWARD_ADD
    half3 staticRimLighting = 0;
    half3 mixedRimLighting = lighting;
#else
    half3 staticRimLighting = 1;
    half3 mixedRimLighting = lighting + indirectLighting;
    // half3 mixedRimLighting = lighting;
#endif
    half3 rimLighting = lerp(staticRimLighting, mixedRimLighting, _RimLightingMix);
    half3 rim = pow(saturate(1.0 - dot(worldNormal, worldView) + _RimLift), max(_RimFresnelPower, EPS_COL)) * _RimColor.rgb * SAMPLE_TEXTURE2D(_RimTexture, sampler_RimTexture, i.uv0).rgb;
    // col += lerp(rim * rimLighting, half3(0, 0, 0), i.isOutline);

// additive matcap
#ifdef MTOON_FORWARD_ADD
#else
    half3 worldCameraUp = normalize(UNITY_MATRIX_V[1].xyz);
    half3 worldViewUp = normalize(worldCameraUp - worldView * dot(worldView, worldCameraUp));
    half3 worldViewRight = normalize(cross(i.viewDirWS, worldViewUp));
    half2 matcapUv = half2(dot(worldViewRight, worldNormal), dot(worldViewUp, worldNormal)) * 0.5 + 0.5;
    half3 matcapLighting = SAMPLE_TEXTURE2D(_SphereAdd, sampler_SphereAdd, matcapUv);
    col += lerp(matcapLighting, half3(0, 0, 0), i.isOutline);
#endif

                 // Emission
#ifdef MTOON_FORWARD_ADD
                #else
                    half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, float4(mainUv, 0, 0)).rgb * _EmissionColor.rgb;
                    col += lerp(emission, half3(0, 0, 0), i.isOutline);
                #endif
    // outline
#ifdef MTOON_OUTLINE_COLOR_FIXED
    col = lerp(col, _OutlineColor, i.isOutline);
#elif MTOON_OUTLINE_COLOR_MIXED
    col = lerp(col, _OutlineColor * lerp(half3(1, 1, 1), col, _OutlineLightingMix), i.isOutline);
#else
#endif

    // debug
#ifdef MTOON_DEBUG_NORMAL
    #ifdef MTOON_FORWARD_ADD
        return float4(0, 0, 0, 0);
    #else
        return float4(worldNormal * 0.5 + 0.5, alpha);
    #endif
#elif MTOON_DEBUG_LITSHADERATE
    #ifdef MTOON_FORWARD_ADD
        return float4(0, 0, 0, 0);
    #else
        return float4(lightIntensity * lighting, alpha);
    #endif
#endif

if (lightAttenuation <= 0.0)
{
    col = float3(0,0,0);
}

float4 result = float4(col, alpha);
// result.rgb = lightIntensity;
// result.rgb = float3(1000000, 0, 0);
    return result;

}

float4 frag_outline(v2f i) : SV_TARGET
{
#if defined(MTOON_CLIP_IF_OUTLINE_IS_NONE) && !defined(MTOON_OUTLINE_WIDTH_WORLD) && !defined(MTOON_OUTLINE_WIDTH_SCREEN)
    clip(-1);
#endif

    if (_OutlineWidthMode == 0)
    {
        clip(-1);
    }

    float2 mainUv = LiveToonApplyUvAnimation(i.uv0);
    float4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, mainUv);

    if (_BlendMode == (int)RENDER_MODE_CUTOUT)
    {
        clip(LiveToonCutoutDepthValue(mainTex.a) - 0.5);
    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT || _BlendMode == (int)RENDER_MODE_TRANSPARENT_WITH_ZWRITE)
    {
        clip(LiveToonTransparentDepthOpacity(mainTex) - 0.001);
    }

    float3 outlineColor = _OutlineColor.rgb;
#if defined(MTOON_OUTLINE_COLOR_MIXED)
    outlineColor *= lerp(float3(1.0, 1.0, 1.0), mainTex.rgb * _Color.rgb, _OutlineLightingMix);
#endif

    return float4(outlineColor, _OutlineColor.a);
}

float4 frag_forward(v2f i) : SV_TARGET
{
        #ifdef MTOON_CLIP_IF_OUTLINE_IS_NONE
    #ifdef MTOON_OUTLINE_WIDTH_WORLD
    #elif MTOON_OUTLINE_WIDTH_SCREEN
    #else
        clip(-1);
    #endif
#endif
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
	UNITY_SETUP_INSTANCE_ID (i);

    // const
    const float PI_2 = 6.28318530718;
    const float EPS_COL = 0.00001;

    float2 mainUv = TRANSFORM_TEX(i.uv0, _MainTex);
    
    float uvAnim = SAMPLE_TEXTURE2D(_UvAnimMaskTexture, sampler_UvAnimMaskTexture, mainUv).r * _Time.y;
    mainUv += float2(_UvAnimScrollX, _UvAnimScrollY) * uvAnim;
    float rotateRad = _UvAnimRotation * PI_2 * uvAnim;
    const float2 rotatePivot = float2(0.5, 0.5);
    mainUv = mul(float2x2(cos(rotateRad), -sin(rotateRad), sin(rotateRad), cos(rotateRad)), mainUv - rotatePivot) + rotatePivot;

    // float4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex , mainUv);
    
        float4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex , mainUv);
    

//RT_TRANS_CO
    
	float RTD_TRAN_OPA_Sli;
	bool bo_co_val;
	float RTD_CO;
	float3 GLO_OUT = (float3)0.0;
    // float4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex , i.uv0);
    RT_TRANS_CO(i.uv0, mainTex, RTD_TRAN_OPA_Sli, RTD_CO, bo_co_val, false, i.posWorld.xyz, i.normalWS, i.pos.xy, GLO_OUT);

float alpha = RTD_TRAN_OPA_Sli;
    float4 result = (float4)0.0;
    // 関数化する
    float3 mtoonRimColor = float3(0,0,0);
    // シンプルなリムライト
    // rimpowerを高めにする
    float rimPower = _RimFresnelPower;
    rimPower = 100;
    float rimLift = _RimLift;
    rimLift = 0.4;
    float3 rim_test_simple = pow(saturate(1.0 - dot(i.normalWS, normalize(i.viewDirWS)) + rimLift), max(rimPower, EPS_COL));
    float3 rimColor_mask = rim_test_simple;
    if (_DirectionalLightCount > 0)
    {
        for (int j = 0; j < _DirectionalLightCount; j++)
        {
            DirectionalLightData directionalLightData = _DirectionalLightDatas[j];
            float3 directionalRimColor = float3(0,0,0);
            float3 directionalSpecCol = float3(0,0,0);
            
            result += CalculateDirectionalLighting(i, directionalLightData, true, directionalRimColor, directionalSpecCol);
            mtoonRimColor += directionalRimColor;
        }
    }
    else if (_FallbackLightIntensity > 0)
    {
        DirectionalLightData fallbackDirectionalLightData = (DirectionalLightData)0;
        bool hasPunctualFallback = TryBuildPunctualFallbackDirectionalLight(i, fallbackDirectionalLightData);
        if (!hasPunctualFallback)
        {
            float3 fallbackLightDir = normalize(normalize(i.viewDirWS) + float3(0.0, 0.25, 0.0));
            fallbackDirectionalLightData.forward = -fallbackLightDir;
            fallbackDirectionalLightData.color = _FallbackLightColor.rgb * (_FallbackLightIntensity * 10.0);
            fallbackDirectionalLightData.shadowIndex = -1;
        }

        float3 fallbackRimColor = float3(0,0,0);
        float3 fallbackSpecCol = float3(0,0,0);
        result += CalculateDirectionalLighting(i, fallbackDirectionalLightData, false, fallbackRimColor, fallbackSpecCol);
        mtoonRimColor += fallbackRimColor;
    }
    float3 punctualLightColorResult = float3(0,0,0);
    // result = float4(0,0,0,0);
    float3 punctualDiffuse = float3(0,0,0);
    if(_PunctualLightCount > 0)
    {
        
        for (int j = 0; j < _PunctualLightCount; j++)
        {
            LightData punctualLightData = _LightDatas[j];
            float3 punctualDiffuse_b = float3(0,0,0);
            float4 punctualLightColorResult_b = CalculatePunctualLighting(i, punctualLightData, punctualDiffuse_b);
            punctualDiffuse += punctualDiffuse_b;
            punctualLightColorResult += punctualLightColorResult_b.rgb;
            // result.rgb += punctualLightColorResult_b.rgb;
        }
        
    }
    // isFaceなら、rimCOlorMaskを0にする
    if(_isCharFace == 1) {
        rimColor_mask = float3(0,0,0);
    }
    else {
        // nothing to do
    }
    result.rgb += mtoonRimColor;

    float3 customRimLight = punctualDiffuse * rimColor_mask * _PunctualLightIntensity;
    float customRimLightMax = max(customRimLight.r, max(customRimLight.g, customRimLight.b));
    float customRimLightMaxAllowed = (_DirectionalLightCount > 0) ? 1.0 : 1.6;
    if (customRimLightMax > customRimLightMaxAllowed)
    {
        customRimLight *= customRimLightMaxAllowed / customRimLightMax;
    }

    float customRimSceneScale = (_DirectionalLightCount > 0) ? 1.0 : 0.9;
    result.rgb += customRimLight * _CustomRimIntensity * customRimSceneScale * mainTex.rgb;

    if (_DirectionalLightCount == 0)
    {
        float3 noDirectionalBaseLift = saturate(punctualDiffuse) * _PunctualLightIntensity * 0.22 * mainTex.rgb;
        result.rgb += noDirectionalBaseLift;
    }
    // result.rgb += rimColor * punctualDiffuse;
    // result.rgb += specCol * punctualDiffuse;
    // result.rgb += punctualDiffuse;

    // result /= _DirectionalLightCount;
uint2 tileIndex = uint2(i.pos.xy) / GetTileSize();
                PositionInputs posInput = GetPositionInput(i.pos.xy, _ScreenSize.zw, i.pos.z, i.pos.w, i.posWorld.xyz, tileIndex);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(i.posWorld);
                float3 environmentNormalWS = normalize(i.normalWS);
                float3 environmentBaseColor = mainTex.rgb * _Color.rgb;
                result.rgb += LiveToonEvaluateEnvironmentLighting(posInput, viewDirection, environmentNormalWS, environmentBaseColor);
                result.rgb = LiveToonApplyWetSkinOverlays(result.rgb, i.uv0, viewDirection, environmentNormalWS);
    
	//  UNITY_APPLY_FOG(i.fogCoord, result);に相当する処理

        float4 outColor = EL_AT_SC(posInput, viewDirection, float4(result.rgb, alpha));
        if(i.isOutline == 1 && _OutlineWidthMode == 0)
{
    outColor = float4(0,0,0,0);
}

// シンプルなランバート二極化
if (_DirectionalLightCount > 0)
{
DirectionalLightData directionalLightData_b = _DirectionalLightDatas[0];
float3 lightDir_b = directionalLightData_b.forward;
lightDir_b *= -1;
// if(_isFace == 1) {
//     lightDir_b *= -1;
// }
// 横からのライトにする
// float3 lightDir_b = float3(5,0,0);
float3 lightColor_b = float3(1,1,1);
// float3 lightAttenuation_b = directionalLightData_b.attenuation;
// float3 worldNormal_b = TransformObjectToWorld(i.normal)
float3 worldNormal_b = i.normalWS;
float lightIntensity_b = dot(lightDir_b, worldNormal_b);
// 2極化 ランバート0.5を閾値に
// if (lightIntensity_b <= 0) {
//     lightIntensity_b = 0;
// }
// else {
//     // lightIntensity_b = 1;
// }
// // 2極化 ランバート0.5を閾値に
// if (lightIntensity_b > 0.5) {
//     lightIntensity_b = 1;
// }
// else {
//     lightIntensity_b = 0;
// }
// float4 outColor = float4(0,0,0,alpha);
// if (lightIntensity_b == 0) {
//     outColor.rgb = _ShadeColor.rgb * mainTex.rgb;
// }
// else {
//     outColor.rgb = lightColor_b * lightIntensity_b * mainTex.rgb;
// }

// 明るいところは白、暗いところは灰色でデバック
// if (lightIntensity_b > 0.5) {
//     outColor.rgb = float3(1,1,1) * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv0).rgb;
// }
// else {
//     outColor.rgb = float3(0.5,0.5,0.5) * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv0).rgb;
// }
// outColor.rgb = i.normalWS;
// float3 rembrandLightingTex = SAMPLE_TEXTURE2D(_RembrandLightingMask, sampler_RembrandLightingMask, i.uv0.xy).rgb;
// outColor.rgb = rembrandLightingTex;
}
return outColor;
}
