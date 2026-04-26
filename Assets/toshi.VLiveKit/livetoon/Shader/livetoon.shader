
Shader "toshi/VLiveKit/livetoon"
{
    Properties
    {
        _TestFloat    ("Test Float", Range(0, 1)) = 0.5
        _TestTexture  ("Test Texture", 2D)        = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Color ("Lit Color + Alpha", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.97, 0.81, 0.86, 1)
        [NoScaleOffset] _MainTex ("Lit Texture + Alpha", 2D) = "white" {}
        [NoScaleOffset] _ShadeTexture ("Shade Texture", 2D) = "white" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        [Normal] _BumpMap ("Normal Texture", 2D) = "bump" {}
        _ReceiveShadowRate ("Receive Shadow", Range(0, 1)) = 1
        [NoScaleOffset] _ReceiveShadowTexture ("Receive Shadow Texture", 2D) = "white" {}
        _ShadingGradeRate ("Shading Grade", Range(0, 1)) = 1
        [NoScaleOffset] _ShadingGradeTexture ("Shading Grade Texture", 2D) = "white" {}
        _ShadeShift ("Shade Shift", Range(-1, 1)) = 0
        _ShadeToony ("Shade Toony", Range(0, 1)) = 0.9
        _LightColorAttenuation ("Light Color Attenuation", Range(0, 1)) = 0
        _IndirectLightIntensity ("Indirect Light Intensity", Range(0, 1)) = 0.1
        [HDR] _RimColor ("Rim Color", Color) = (0,0,0)
        [NoScaleOffset] _RimTexture ("Rim Texture", 2D) = "white" {}
        _RimLightingMix ("Rim Lighting Mix", Range(0, 1)) = 0
        [PowerSlider(4.0)] _RimFresnelPower ("Rim Fresnel Power", Range(0, 100)) = 1
        _RimLift ("Rim Lift", Range(0, 1)) = 0
        [NoScaleOffset] _SphereAdd ("Sphere Texture(Add)", 2D) = "black" {}
        [HDR] _EmissionColor ("Color", Color) = (0,0,0)
        [NoScaleOffset] _EmissionMap ("Emission", 2D) = "white" {}
        [NoScaleOffset] _OutlineWidthTexture ("Outline Width Tex", 2D) = "white" {}
        _OutlineWidth ("Outline Width", Range(0.01, 1)) = 0.5
        _OutlineScaledMaxDistance ("Outline Scaled Max Distance", Range(1, 10)) = 1
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineLightingMix ("Outline Lighting Mix", Range(0, 1)) = 1
        [NoScaleOffset] _UvAnimMaskTexture ("UV Animation Mask", 2D) = "white" {}
        _UvAnimScrollX ("UV Animation Scroll X", Float) = 0
        _UvAnimScrollY ("UV Animation Scroll Y", Float) = 0
        _UvAnimRotation ("UV Animation Rotation", Float) = 0

        [HideInInspector] _MToonVersion ("_MToonVersion", Float) = 39
        [HideInInspector] _DebugMode ("_DebugMode", Float) = 0.0
        [HideInInspector] _BlendMode ("_BlendMode", Float) = 0.0
        [HideInInspector] _OutlineWidthMode ("_OutlineWidthMode", Float) = 0.0
        [HideInInspector] _OutlineColorMode ("_OutlineColorMode", Float) = 0.0
        [HideInInspector] _CullMode ("_CullMode", Float) = 2.0
        [HideInInspector] _OutlineCullMode ("_OutlineCullMode", Float) = 1.0
        [HideInInspector] _SrcBlend ("_SrcBlend", Float) = 1.0
        [HideInInspector] _DstBlend ("_DstBlend", Float) = 0.0
        [HideInInspector] _ZWrite ("_ZWrite", Float) = 1.0
        [HideInInspector] _AlphaToMask ("_AlphaToMask", Float) = 0.0

        [HideInInspector] _UseCustomBlend ("_UseCustomBlend", Float) = 0
[HideInInspector] _BlendOp ("_BlendOp", Float) = 0

        // original
		_ReduSha ("Reduce Shadow", Float ) = 0.0
		[HideInInspector] _ZTeForLiOpa("ZTeForLiOpa", int) = 3
        _LambertThresh ("LambertThresh", Float) = 0.5
        _GradWidth     ("ShadowWidth", Range(0.003,1)) = 0.1
        // _Sat           ("Sat", Range(0,2)) = 1
        _Sat           ("Sat", Float) = 0.5

        _Distance("Distance", Range(0, 2)) = 0.95
        // _Distance("Distance", Float) = 0.1
        _Focal("Focal", Range(0, 1)) = 0.1
        // _Focal("Focal", Float) = 0.01
        _Size("Size", Range(0, 2)) = 1.1
        // _Size("Size", Float) = 0.01
        _AntiPerspectiveIntensity("AntiPerspectiveIntensity", Range(0, 1)) = 0.1

        // mat.SetVector("_FaceForwardDirection", fwd);
        //     mat.SetVector("_FaceUpDirection",      up);
        _FaceForwardDirection("FaceForwardDirection", Vector) = (0,0,0,0)
        _FaceUpDirection("FaceUpDirection", Vector) = (0,0,0,0)
        [Toggle] _IsFace ("Is Face", Float) = 0   // 0=Off, 1=On
        _isFACE("isFACE", Float) = 0
        _testFloatData("testFloatData", Float) = 0
        _isCharFace("isCharFace", Float) = 0


        _FacePosition("FacePosition", Vector) = (0,0,0,0)
        _FaceSphereIntensity("FaceSphereIntensity", Float) = 0
        // レンブラントライティング用のマスク
        _RembrandLightingMask("RembrandLightingMask", 2D) = "white" {}

        _AnisoAxis("0=Tangent 1=Bitangent", Float) = 0
        _AnisoStrength("Aniso Strength", Range(0,1)) = 0.6
        _Shininess("Shininess", Range(8,256)) = 64
        _SpecCut("Spec Threshold", Range(0,1)) = 0.6
        _SpecColor("SpecColor", Color) = (1,1,1,1)
        _isHair("isHair", Float) = 0

        _Sharpness("Sharpness", Float) = 30
        _Intensity("Intensity", Float) = 0.5
        _Position("Position", Float) = 0.3

        _JitterTex("JitterTex", 2D) = "white" {}
        _JitterIntensity("JitterIntensity", Float) = 0.5
    }

    HLSLINCLUDE

    #pragma target 4.5


    #define RENDER_MODE_OPAQUE 0
    #define RENDER_MODE_CUTOUT 1
    #define RENDER_MODE_TRANSPARENT 2
    #define RENDER_MODE_TRANSPARENT_WITH_ZWRITE 3



    //Global Includes
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"


CBUFFER_START(UnityPerMaterial)

uniform float _TestFloat;
uniform float4 _TestTexture_ST;

uniform float _ReduSha;




uniform float _TransparentThreshold;



uniform float _Cutoff;
uniform float4 _Color;
uniform float4 _ShadeColor;
uniform float4 _MainTex_ST;
uniform float4 _ShadeTexture_ST;
uniform float _BumpScale;
uniform float4 _BumpMap_ST;
uniform float _ReceiveShadowRate;
uniform float4 _ReceiveShadowTexture_ST;
uniform float _ShadingGradeRate;
uniform float4 _ShadingGradeTexture_ST;
uniform float _ShadeShift;
uniform float _ShadeToony;
uniform float _LightColorAttenuation;
uniform float _IndirectLightIntensity;

uniform float4 _RimColor;
uniform float4 _RimTexture_ST;
uniform float _RimLightingMix;
uniform float _RimFresnelPower;
uniform float _RimLift;
uniform float4 _SphereAdd_ST;

uniform float4 _EmissionColor;
uniform float4 _EmissionMap_ST;

uniform float4 _OutlineWidthTexture_ST;
uniform float _OutlineWidth;
uniform float _OutlineScaledMaxDistance;
uniform float4 _OutlineColor;
uniform float _OutlineLightingMix;

uniform float4 _UvAnimMaskTexture_ST;
uniform float _UvAnimScrollX;
uniform float _UvAnimScrollY;
uniform float _UvAnimRotation;

uniform float _MToonVersion;
uniform float _DebugMode;
uniform float _BlendMode;
uniform float _OutlineWidthMode;
uniform float _OutlineColorMode;
uniform float _CullMode;
uniform float _OutlineCullMode;
uniform float _SrcBlend;
uniform float _DstBlend;
uniform float _ZWrite;
uniform float _UseCustomBlend;
uniform float _BlendOp;

float _DistortionScale;
float _DistortionVectorScale;
float _DistortionVectorBias;
float _DistortionBlurScale;
float _DistortionBlurRemapMin;
float _DistortionBlurRemapMax;

float3 _EmissiveColor;
float _AlbedoAffectEmissive;
float _EmissiveExposureWeight;

float4 _BaseColor;
float4 _BaseColorMap_ST;
float4 _BaseColorMap_TexelSize;
float4 _BaseColorMap_MipInfo;

float _Metallic;
float _Smoothness;

float _NormalScale;

float4 _DetailMap_ST;
float _testFloatData;

float _Anisotropy;

float _DiffusionProfileHash;
float _SubsurfaceMask;
float _Thickness;

float4 _SpecularColor;

float _TexWorldScale;
float4 _UVMappingMask;
float4 _UVDetailsMappingMask;
float4 _UVMappingMaskEmissive;
float _LinkDetailsWithBase;

float _AlphaRemapMin;
float _AlphaRemapMax;
float _ObjectSpaceUVMapping;
float _TransmissionMask;

float  _LambertThresh;   // ★★ add
float  _GradWidth;       // ★★ add
float  _Sat;             // ★★ add

float  _Distance;
float  _Focal;
float  _Size;
float  _AntiPerspectiveIntensity;

float3 _FaceForwardDirection;
float3 _FaceUpDirection;
float _isFace;
float _isFACE;
float3 _FacePosition;
float _FaceSphereIntensity;

float _AnisoAxis;
float _AnisoStrength;
float _Shininess;
float _SpecCut;
float4 _SpecColor;
float _isHair;
float _isCharFace;
float _Sharpness;
float _Intensity;
float _Position;

float4 _JitterTex_ST;
float _JitterIntensity;

CBUFFER_END


TEXTURE2D(_TestTexture);
SAMPLER(sampler_TestTexture);

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

TEXTURE2D(_ShadeTexture);
SAMPLER(sampler_ShadeTexture);

TEXTURE2D(_UvAnimMaskTexture);
SAMPLER(sampler_UvAnimMaskTexture);

TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);

TEXTURE2D(_OutlineWidthTexture);
SAMPLER(sampler_OutlineWidthTexture);

TEXTURE2D(_ReceiveShadowTexture);
SAMPLER(sampler_ReceiveShadowTexture);

TEXTURE2D(_ShadingGradeTexture);
SAMPLER(sampler_ShadingGradeTexture);

TEXTURE2D(_RimTexture);
SAMPLER(sampler_RimTexture);

TEXTURE2D(_SphereAdd);
SAMPLER(sampler_SphereAdd);

TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);

TEXTURE2D(_RembrandLightingMask);
SAMPLER(sampler_RembrandLightingMask);





TEXTURE2D(_DistortionVectorMap);
SAMPLER(sampler_DistortionVectorMap);

TEXTURE2D(_BaseColorMap);
SAMPLER(sampler_BaseColorMap);

TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);

TEXTURE2D(_HeightMap);
SAMPLER(sampler_HeightMap);

TEXTURE2D(_JitterTex);
SAMPLER(sampler_JitterTex);


	ENDHLSL



    SubShader
    {

        Tags{"Queue" = "Geometry+225" "RenderPipeline"="HDRenderPipeline" "RenderType" = "HDLitShader"}
        
        

		Pass
        {

Name"GBuffer"
Tags{"LightMode"="GBuffer"}

            Cull [_CullMode]
			ZTest LEqual    


            HLSLPROGRAM

            #pragma only_renderers d3d11 playstation xboxone vulkan xboxseries metal switch

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment PROBE_VOLUMES_OFF PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment DECALS_OFF DECALS_3RT DECALS_4RT
            #pragma multi_compile_fragment _ RENDERING_LAYERS
            //#pragma multi_compile _ DEBUG_DISPLAY //Temporary Removed (It produces error about "undefined unity_MipmapStreaming_DebugTex_ST")

            #define VARYINGS_NEED_POSITION_WS

			#ifndef DEBUG_DISPLAY
				#define SHADERPASS_GBUFFER_BYPASS_ALPHA_TEST
			#endif

            #define SHADERPASS SHADERPASS_GBUFFER

            #ifdef DEBUG_DISPLAY
                #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VertMesh.hlsl"
uniform sampler2D _Depth0RT;
uniform float4 _FrustumCorner;
void fake_perspective(inout float4 v) {
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

void anime_perspective_original(inout float4 v) {
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
float _AntiPerspHeight = 1.5;
    // clip空間へ
    float4 vet = TransformObjectToHClip(ObjectSpacePos_IN);

    // 通常の打ち消し係数
    float  centerVSz  = mul(UNITY_MATRIX_V, float4(UNITY_MATRIX_M._m03_m13_m23, 1.0)).z;
    float  abs_vet_w  = abs(vet.w);
    float  baseCoeff  = lerp(1.0, abs_vet_w / -centerVSz, 1);

    // 高さに応じたフェード係数
    float  heightK = saturate(ObjectSpacePos_IN.y / _AntiPerspHeight);

    // 係数をブレンド
    float  finalCoeff = lerp(1.0, baseCoeff, heightK);

    // XY をスケールして視野角を打ち消し
    vet.xy *= finalCoeff;

    // object 空間へ戻す
    float4 positionWorld  = mul(Inverse(GetWorldToHClipMatrix()), vet);
    float4 positionObject = mul(Inverse(GetObjectToWorldMatrix()), positionWorld);
    ObjectSpacePos_IN     = positionObject.xyz;
}
PackedVaryingsType Vert(AttributesMesh inputMesh)
{
	VaryingsType varyingsType;

	{
        float4 worldPos = mul(UNITY_MATRIX_M, float4(inputMesh.positionOS.xyz, 1));
        // worldPos.xyz += float3(1.0, 0.0, 0.0);
        float3 objectSpacePos = mul(UNITY_MATRIX_I_M, worldPos);
        // AntiPerspective(objectSpacePos);
        // float4 objectSpacePos4 = float4(objectSpacePos, 1.0);
        // fake_perspective(objectSpacePos4);
        // objectSpacePos = objectSpacePos4.xyz;

        inputMesh.positionOS.xyz = objectSpacePos;
        // inputMesh.vertex.xyz += float3(1.0, 0.0, 0.0);
		
        varyingsType.vmesh = VertMesh(inputMesh);

		
		
	}

	return PackVaryingsType(varyingsType);
}

float3 uvToEyeSpacePos(float2 uv, sampler2D depth)
            {
                float d = tex2D(depth, ClampAndScaleUVForPoint(uv)).x;
                float3 frustumRay = float3(
                lerp(_FrustumCorner.x, _FrustumCorner.y, uv.x),
                lerp(_FrustumCorner.z, _FrustumCorner.w, uv.y),
                -_ProjectionParams.z
                );
                return frustumRay * d;
            }

void Frag(PackedVaryingsToPS packedInput  
            ,OUTPUT_GBUFFER(outGBuffer)
			// ,out GBufferType0 GBT0 : SV_Target0
			// ,out GBufferType1 GBT1 : SV_Target1
			// ,out GBufferType2 GBT2 : SV_Target2
			// ,out GBufferType3 GBT3 : SV_Target3
)
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);
	
	FragInputs input = UnpackVaryingsMeshToFragInputs(packedInput);
    // input.positionRWS += float3(10.0, 10.0, 10.0);
    // 頂点を横にずらす
    // input.positionRWS += float3(10000.0, 0.0, 0.0);
    float2 uv = input.texCoord0.xy;
    float3 eyeSpacePos = uvToEyeSpacePos(uv, _Depth0RT);
    float4 clipSpacePos = mul(UNITY_MATRIX_P, float4(eyeSpacePos, 1));
    float outputDepth = clipSpacePos.z / clipSpacePos.w;
    float3 ddx = uvToEyeSpacePos(uv + float2(1 / _ScreenParams.x, 0), _Depth0RT) - eyeSpacePos;
                float3 ddx2 = eyeSpacePos - uvToEyeSpacePos(uv - float2(1 / _ScreenParams.x, 0), _Depth0RT);
                if (abs(ddx.z) > abs(ddx2.z)) {
                ddx = ddx2;
                }

                float3 ddy = uvToEyeSpacePos(uv + float2(0, 1 / _ScreenParams.y), _Depth0RT) - eyeSpacePos;
                float3 ddy2 = eyeSpacePos - uvToEyeSpacePos(uv - float2(0, 1 / _ScreenParams.y), _Depth0RT);
                if (abs(ddy2.z) < abs(ddy.z)) {
                ddy = ddy2;
                }

                float3 normal = cross(ddx, ddy);
                normal = normalize(normal);

                float4 worldSpacewNormal = mul(
                    transpose(UNITY_MATRIX_V),
                    float4(normal, 0)
                );

	PositionInputs posInput = GetPositionInput(input.positionSS.xy, _ScreenSize.zw, input.positionSS.z, input.positionSS.w, input.positionRWS);

		float3 V = GetWorldSpaceNormalizeViewDir(input.positionRWS);

	// ZERO_INITIALIZE(GBufferType0,GBT0);
	// ZERO_INITIALIZE(GBufferType1,GBT1);
	// ZERO_INITIALIZE(GBufferType2,GBT2);
	// ZERO_INITIALIZE(GBufferType3,GBT3);

    SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD;
                surfaceData.normalWS = worldSpacewNormal;
                surfaceData.ambientOcclusion = 1;
                surfaceData.perceptualSmoothness = 0.2;
                surfaceData.specularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(ClampNdotV(dot(surfaceData.normalWS, V)), surfaceData.ambientOcclusion, PerceptualSmoothnessToRoughness(surfaceData.perceptualSmoothness));
                surfaceData.baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texCoord0.xy);
                surfaceData.metallic = 1;

                float4 RWSpos = mul(UNITY_MATRIX_I_V, float4(eyeSpacePos, 1));
                input.positionRWS = RWSpos;
                posInput.positionWS = RWSpos;

    BuiltinData builtinData;
    GetBuiltinData(input, V, posInput, surfaceData, 1, float3(1, 1, 1), 0.0, builtinData);

    // builtinData.emissiveColor = float3(1, 0, 0);
    
    // GetSurfaceAndBuiltinData(input, V, posInput, surfaceData, builtinData);

	
		// surfaceData.perceptualSmoothness = 1.0;



    if (_BlendMode == (int)RENDER_MODE_OPAQUE)
    {
    }
    else if (_BlendMode == (int)RENDER_MODE_CUTOUT)
    {
        float4 _MainTex_var = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(input.texCoord0.xy, _MainTex));
        _MainTex_var.rgb = float3(1.0, 0.0, 0.0);
	

            float cutoutResult;
            if ((1.0 - _Cutoff) > 0.5)
            {
                cutoutResult = 1.0 - (1.0 - 2.0 * ((1.0 - _Cutoff) - 0.5)) * (1.0 - (_MainTex_var.a));
            }
            else
            {
                
                cutoutResult = 2.0 * (1.0 - _Cutoff) * (_MainTex_var.a);
            }
            float finalCutout = saturate(cutoutResult);
            float RTD_CO_ON = finalCutout;
			clip(RTD_CO_ON - 0.5);

    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT)
    {
        // clip(surfaceData.alpha - 0.5);
    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT_WITH_ZWRITE)
    {
        // clip(surfaceData.alpha - 0.5);
    }

	// GBT0 = float4(1.0,0.0,0.0,0.0);
	// EncodeIntoNormalBuffer(ConvertSurfaceDataToNormalData(surfaceData), posInput.positionSS, GBT1);
	// GBT2 = float4(0.0,0.0,0.0,0.0);
	// GBT3 = float4(0.0,0.0,0.0,0.0);
    ENCODE_INTO_GBUFFER(surfaceData, builtinData, posInput.positionSS, outGBuffer);

}

            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }



		



		Pass
        {

Name"ShadowCaster"
Tags{"LightMode"="ShadowCaster"}

            Cull [_CullMode]

			ZClip On
            ZWrite On
            ZTest LEqual

            ColorMask 0

            HLSLPROGRAM

            #pragma multi_compile _ _ALPHATEST_ON _ALPHABLEND_ON
            #pragma multi_compile_shadowcaster


            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"


float4 _ShadowBias;
float3 _LightDirection;

sampler3D _DitherMaskLOD;
float dither;

struct Attributes
{

	float4 positionOS   : POSITION;
	float3 normalOS     : NORMAL;
    float4 tangentOS	: TANGENT;
	float2 texcoord     : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID

};

struct Varyings
{

	float2 uv           : TEXCOORD0;
    float3 normalWS		: TEXCOORD1;
    float4 tangentWS	: TEXCOORD2;
	float4 projPos		: TEXCOORD3;
	float3 positionWS	: TEXCOORD4;
	float4 positionCS   : SV_POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO

};
float4 ComputeScreenPos(float4 positionCS)
{
	float4 o = positionCS * 0.5f;
	o.xy = float2(o.x, o.y * _ProjectionParams.x) + o.w;
	o.zw = positionCS.zw;
	return o;
}
float4 GetShadowPositionHClip(Attributes input, float3 normalWS)
{

	float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

	float invNdotL = 1.0 - saturate(dot(_LightDirection, positionWS));
	float scale = invNdotL * _ShadowBias.y;

	positionWS = _LightDirection * _ShadowBias.xxx + positionWS;
	positionWS = normalWS * scale.xxx + positionWS;
	float4 positionCS = TransformWorldToHClip( positionWS );

	#if UNITY_REVERSED_Z
		positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE) + - _ReduSha * 0.01;
	#else
		positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE) + _ReduSha * 0.01;
	#endif

	return positionCS;

}

Varyings ShadowPassVertex(Attributes input)
{

	Varyings output;
	ZERO_INITIALIZE(Varyings, output);

	UNITY_SETUP_INSTANCE_ID (input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	output.uv = input.texcoord;

	float4 objPos = mul (GetObjectToWorldMatrix(), float4(0.0,0.0,0.0,1.0) );

    output.normalWS = TransformObjectToWorldDir(input.normalOS);
    output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
	output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
		
    output.projPos = ComputeScreenPos (output.positionCS);
	output.positionCS = GetShadowPositionHClip(input, output.normalWS);

	return output;

}

void ShadowPassFragment(Varyings input, out float4 outColor : SV_Target0)
{

	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	UNITY_SETUP_INSTANCE_ID (input);

	float4 objPos = mul ( GetObjectToWorldMatrix(), float4(0.0,0.0,0.0,1.0) );
    float2 sceneUVs = (input.projPos.xy / input.projPos.w);
	float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);

	float2 RTD_OB_VP_CAL = distance(objPos.xyz, GetCurrentViewPosition());
	float2 RTD_VD_Cal = (float2((sceneUVs.x * 2.0 - 1.0)*(_ScreenParams.r/_ScreenParams.g), sceneUVs.y * 2.0 - 1.0).rg*RTD_OB_VP_CAL);

		

        float4 _MainTex_var = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

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
			float RTD_CO = RTD_CO_ON;
		

				clip(RTD_CO - 0.5);
    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT)
    {

			float RTD_TRAN_MAS = (smoothstep(clamp(-20.0,1.0,_TransparentThreshold),1.0,_MainTex_var.a) *_MainTex_var.r);
			float RTD_TRAN_OPA_Sli = lerp( RTD_TRAN_MAS, smoothstep(clamp(-20.0,1.0,_TransparentThreshold) , 1.0, _MainTex_var.a)  ,_Color.a);

			dither = tex3D(_DitherMaskLOD, float3(input.positionCS.xy * 0.25, RTD_TRAN_OPA_Sli * 0.99)).a;
                clip(1.0-(1.0-2.0*(0.74-0.5))*(1.0-dither) - 0.5);
    }
    else if (_BlendMode == (int)RENDER_MODE_TRANSPARENT_WITH_ZWRITE)
    {

			float RTD_TRAN_MAS = (smoothstep(clamp(-20.0,1.0,_TransparentThreshold),1.0,_MainTex_var.a) *_MainTex_var.r);
			float RTD_TRAN_OPA_Sli = lerp( RTD_TRAN_MAS, smoothstep(clamp(-20.0,1.0,_TransparentThreshold) , 1.0, _MainTex_var.a)  ,_Color.a);

			dither = tex3D(_DitherMaskLOD, float3(input.positionCS.xy * 0.25, RTD_TRAN_OPA_Sli * 0.99)).a;
                clip(1.0-(1.0-2.0*(0.74-0.5))*(1.0-dither) - 0.5);

    }


	outColor = 0;
}

			
            ENDHLSL
        }


		
// Forward Base + Add
                Pass
        {

Name"FORWARD_BASE + ADD"
Tags{"LightMode"="ForwardOnly"}

           

            Cull [_CullMode]
            // _乗算合成に変更
            Blend[_SrcBlend][_DstBlend]  
			ZWrite [_ZWrite]
            ZTest [_ZTeForLiOpa] 

            BlendOp Add, Max
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM


			#pragma vertex LitPassVertex
            #pragma fragment frag_forward

            // HDRP shadow filter algorithm fallback
            #ifndef SHADOW_LOW
                #ifndef SHADOW_MEDIUM
                    #ifndef SHADOW_HIGH
                        #define SHADOW_LOW
                    #endif
                #endif
            #endif

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"


			#pragma multi_compile _ DEBUG_DISPLAY
	        #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
	        #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RayTracingCommon.hlsl"

			#ifdef DEBUG_DISPLAY
			    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
			#endif

			#define HAS_LIGHTLOOP
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.cs.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinGIUtilities.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AreaLighting.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/AtmosphericScattering/AtmosphericScattering.hlsl"

			#include "./livetoonSM3.hlsl"



			
            ENDHLSL
			 
        }
        
        Pass {

Name"Outline"
Tags{"LightMode"="SRPDefaultUnlit"}

			Cull [_OutlineCullMode]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Offset 1, 1
            BlendOp Add, Max
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM

            #pragma shader_feature _ MTOON_DEBUG_NORMAL MTOON_DEBUG_LITSHADERATE
            #pragma multi_compile _ MTOON_OUTLINE_WIDTH_WORLD MTOON_OUTLINE_WIDTH_SCREEN
            #pragma multi_compile _ MTOON_OUTLINE_COLOR_FIXED MTOON_OUTLINE_COLOR_MIXED
            #pragma multi_compile _ _NORMALMAP
            #pragma multi_compile _ _ALPHATEST_ON _ALPHABLEND_ON
            #define MTOON_CLIP_IF_OUTLINE_IS_NONE
            #pragma vertex LitPassVertex_Outline
            #pragma fragment frag_forward

            // HDRP shadow filter algorithm fallback
            // HDRP 14.x の HDShadowAlgorithms.hlsl は SHADOW_LOW / MEDIUM / HIGH を要求する
            #ifndef SHADOW_LOW
                #ifndef SHADOW_MEDIUM
                    #ifndef SHADOW_HIGH
                        #define SHADOW_LOW
                    #endif
                #endif
            #endif


	        #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
	        #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"

			#define HAS_LIGHTLOOP
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/AtmosphericScattering/AtmosphericScattering.hlsl"

			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinGIUtilities.hlsl"

			




//
#include "./livetoonSM3.hlsl"








			ENDHLSL
			
		}

    }



    //

FallBack "Hidden/InternalErrorShader"
CustomEditor "MToon.MToonInspector"

}
