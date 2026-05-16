using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("VLiveKit/LiveToon/Front Hair Shadow Light")]
public sealed class LiveToonFrontHairShadowLight : MonoBehaviour
{
    private const int MaxShadowTextureSize = 4096;
    private const string DepthShaderName = "Hidden/VLiveKit/LiveToon/FrontHairShadowDepth";

    private static readonly int VirtualLightCountId = Shader.PropertyToID("_VirtualLightCount");
    private static readonly int HairShadowMap0Id = Shader.PropertyToID("_HairShadowMap0");
    private static readonly int HairShadowLightVP0Id = Shader.PropertyToID("_HairShadow_LightVP0");
    private static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
    private static readonly int ShadowStrengthId = Shader.PropertyToID("_ShadowStrength");
    private static readonly int ClampToBoxId = Shader.PropertyToID("_ClampToBox");
    private static readonly int ProjectFlipU0Id = Shader.PropertyToID("_ProjectFlipU0");
    private static readonly int ProjectFlipV0Id = Shader.PropertyToID("_ProjectFlipV0");
    private static readonly int DebugForceShadowId = Shader.PropertyToID("_LiveToonFrontHairShadowDebugForce");
    private static readonly int DebugShadowAttenuationId = Shader.PropertyToID("_LiveToonFrontHairShadowDebugAttenuation");
    private static readonly int DebugIgnoreProjectionId = Shader.PropertyToID("_LiveToonFrontHairShadowDebugIgnoreProjection");
    private static readonly int DebugUseCasterSilhouetteId = Shader.PropertyToID("_LiveToonFrontHairShadowDebugUseCasterSilhouette");
    private static readonly int DepthShaderVPId = Shader.PropertyToID("_LiveToonHairShadowVP");

    [Header("Source Light")]
    [SerializeField]
    private Light sourceDirectionalLight;

    [SerializeField]
    private bool autoFindDirectionalLight = true;

    [Header("Targets")]
    [SerializeField]
    private bool applyInEditMode = true;

    [SerializeField, Min(0.02f)]
    private float editModeUpdateInterval = 0.25f;

    [SerializeField]
    private bool autoCollectHairCasters = true;

    [SerializeField]
    private bool includeInactiveRenderers;

    [SerializeField]
    private Renderer[] shadowCasters = Array.Empty<Renderer>();

    [SerializeField]
    private Renderer[] boundsRenderers = Array.Empty<Renderer>();

    [SerializeField]
    private bool filterCasterSubMeshesByMaterialName;

    [Header("Placement")]
    [SerializeField]
    private Transform shadowCenter;

    [SerializeField]
    private Vector3 centerOffset = new Vector3(0f, 1.45f, 0.04f);

    [SerializeField]
    private bool autoFitBounds = true;

    [SerializeField, Min(0.01f)]
    private float manualHalfSize = 0.35f;

    [SerializeField, Min(0.01f)]
    private float minimumHalfSize = 0.22f;

    [SerializeField, Min(0.01f)]
    private float manualDepthRange = 0.6f;

    [SerializeField, Min(0f)]
    private float depthPadding = 0.06f;

    [SerializeField, Min(1f)]
    private float boundsPadding = 1.15f;

    [Header("Shadow")]
    [SerializeField, Range(64, MaxShadowTextureSize)]
    private int textureSize = 1024;

    [SerializeField, Range(0f, 1f)]
    private float shadowStrength = 0.85f;

    [SerializeField, Range(0f, 0.05f)]
    private float shadowBias = 0.004f;

    [SerializeField]
    private bool clampToBox = true;

    [Header("Debug")]
    [SerializeField]
    private bool forceVisibleDebugShadow = true;

    [SerializeField, Range(0f, 1f)]
    private float debugShadowAttenuation = 0.08f;

    [SerializeField]
    private bool debugIgnoreProjectionBounds = true;

    [SerializeField]
    private bool debugUseCasterSilhouette = true;

    private readonly List<Renderer> resolvedCasters = new List<Renderer>();
    private readonly List<Renderer> resolvedBoundsRenderers = new List<Renderer>();
    private readonly List<Renderer> scratchRenderers = new List<Renderer>();

    private RenderTexture shadowTexture;
    private Material depthMaterial;
    private bool pendingEditModeRender;
    private bool hasLastEditModeState;
    private float nextEditModeUpdateTime;
    private Vector3 lastShadowCenter;
    private Quaternion lastSourceRotation;

    public void SetupFromHumanoid(Animator animator, Light directionalLight)
    {
        if (directionalLight != null && directionalLight.type == LightType.Directional)
        {
            sourceDirectionalLight = directionalLight;
        }

        autoFindDirectionalLight = sourceDirectionalLight == null;
        autoCollectHairCasters = true;

        if (animator != null && animator.isHuman)
        {
            var humanoidHead = animator.GetBoneTransform(HumanBodyBones.Head);
            if (humanoidHead != null)
            {
                shadowCenter = humanoidHead;
            }
        }

        if (isActiveAndEnabled && ShouldApplyNow())
        {
            RequestRenderAndApply();
        }
    }

    [ContextMenu("Collect Hair Casters")]
    public void CollectHairCasters()
    {
        var hairCasters = new List<Renderer>();
        scratchRenderers.Clear();
        GetComponentsInChildren<Renderer>(includeInactiveRenderers, scratchRenderers);
        foreach (var targetRenderer in scratchRenderers)
        {
            if (IsHairLike(targetRenderer, null))
            {
                AddRenderer(targetRenderer, hairCasters);
            }
        }

        shadowCasters = hairCasters.ToArray();
        boundsRenderers = shadowCasters;

        if (isActiveAndEnabled && ShouldApplyNow())
        {
            RequestRenderAndApply();
        }
    }

    private void OnEnable()
    {
        if (ShouldApplyNow())
        {
            RequestRenderAndApply();
        }
    }

    private void OnDisable()
    {
        Shader.SetGlobalInt(VirtualLightCountId, 0);
        ClearDebugGlobals();
        ReleaseResources();
    }

    private void OnValidate()
    {
        textureSize = Mathf.Clamp(textureSize, 64, MaxShadowTextureSize);
        shadowStrength = Mathf.Clamp01(shadowStrength);
        debugShadowAttenuation = Mathf.Clamp01(debugShadowAttenuation);
        shadowBias = Mathf.Clamp(shadowBias, 0f, 0.05f);
        manualHalfSize = Mathf.Max(0.01f, manualHalfSize);
        minimumHalfSize = Mathf.Max(0.01f, minimumHalfSize);
        manualDepthRange = Mathf.Max(0.01f, manualDepthRange);
        depthPadding = Mathf.Max(0f, depthPadding);
        boundsPadding = Mathf.Max(1f, boundsPadding);
        editModeUpdateInterval = Mathf.Max(0.02f, editModeUpdateInterval);

        if (isActiveAndEnabled && ShouldApplyNow())
        {
            RequestRenderAndApply();
        }
    }

    private void LateUpdate()
    {
        if (!ShouldRenderInLateUpdate())
        {
            return;
        }

        RenderAndApply();
        CacheEditModeState();
    }

    private bool ShouldApplyNow()
    {
        return Application.isPlaying || applyInEditMode;
    }

    private void RequestRenderAndApply()
    {
        if (Application.isPlaying)
        {
            RenderAndApply();
            return;
        }

        pendingEditModeRender = true;
    }

    private bool ShouldRenderInLateUpdate()
    {
        if (Application.isPlaying)
        {
            return true;
        }

        if (!applyInEditMode)
        {
            return false;
        }

        if (!pendingEditModeRender && !HasEditModeStateChanged())
        {
            return false;
        }

        var now = Time.realtimeSinceStartup;
        if (now < nextEditModeUpdateTime)
        {
            return false;
        }

        nextEditModeUpdateTime = now + editModeUpdateInterval;
        pendingEditModeRender = false;
        return true;
    }

    private bool HasEditModeStateChanged()
    {
        return !hasLastEditModeState
            || ResolveShadowCenter() != lastShadowCenter
            || ResolveKnownSourceLightRotation() != lastSourceRotation;
    }

    private void CacheEditModeState()
    {
        if (Application.isPlaying)
        {
            return;
        }

        hasLastEditModeState = true;
        lastShadowCenter = ResolveShadowCenter();
        lastSourceRotation = ResolveKnownSourceLightRotation();
    }

    private void RenderAndApply()
    {
        ApplyDebugGlobals();

        var directionalLight = ResolveDirectionalLight();
        if (directionalLight == null)
        {
            Shader.SetGlobalInt(VirtualLightCountId, 0);
            return;
        }

        ResolveCasters(resolvedCasters);
        if (resolvedCasters.Count == 0)
        {
            Shader.SetGlobalInt(VirtualLightCountId, 0);
            return;
        }

        ResolveBoundsRenderers(resolvedCasters, resolvedBoundsRenderers);

        if (!EnsureResources())
        {
            Shader.SetGlobalInt(VirtualLightCountId, 0);
            return;
        }

        var lightDirection = directionalLight.transform.forward.normalized;
        var lightRight = directionalLight.transform.right.normalized;
        var lightUp = directionalLight.transform.up.normalized;
        var center = ResolveShadowCenter();
        var shadowMatrix = BuildShadowMatrix(center, lightRight, lightUp, lightDirection, resolvedBoundsRenderers);

        RenderDepth(resolvedCasters, shadowMatrix);

        Shader.SetGlobalInt(VirtualLightCountId, 1);
        Shader.SetGlobalTexture(HairShadowMap0Id, shadowTexture);
        Shader.SetGlobalMatrix(HairShadowLightVP0Id, shadowMatrix);
        Shader.SetGlobalFloat(ShadowBiasId, shadowBias);
        Shader.SetGlobalFloat(ShadowStrengthId, forceVisibleDebugShadow ? 1f : shadowStrength);
        Shader.SetGlobalFloat(ClampToBoxId, clampToBox ? 1f : 0f);
        Shader.SetGlobalFloat(ProjectFlipU0Id, 0f);
        Shader.SetGlobalFloat(ProjectFlipV0Id, 0f);
    }

    private void ApplyDebugGlobals()
    {
        Shader.SetGlobalFloat(DebugForceShadowId, forceVisibleDebugShadow ? 1f : 0f);
        Shader.SetGlobalFloat(DebugShadowAttenuationId, debugShadowAttenuation);
        Shader.SetGlobalFloat(DebugIgnoreProjectionId, debugIgnoreProjectionBounds ? 1f : 0f);
        Shader.SetGlobalFloat(DebugUseCasterSilhouetteId, debugUseCasterSilhouette ? 1f : 0f);
    }

    private static void ClearDebugGlobals()
    {
        Shader.SetGlobalFloat(DebugForceShadowId, 0f);
        Shader.SetGlobalFloat(DebugShadowAttenuationId, 1f);
        Shader.SetGlobalFloat(DebugIgnoreProjectionId, 0f);
        Shader.SetGlobalFloat(DebugUseCasterSilhouetteId, 0f);
    }

    private Light ResolveDirectionalLight()
    {
        if (sourceDirectionalLight != null && sourceDirectionalLight.type == LightType.Directional)
        {
            return sourceDirectionalLight;
        }

        if (!autoFindDirectionalLight)
        {
            return null;
        }

        var lights = FindSceneLights();
        foreach (var light in lights)
        {
            if (light != null && light.type == LightType.Directional && light.isActiveAndEnabled)
            {
                return light;
            }
        }

        return null;
    }

    private static Light[] FindSceneLights()
    {
#if UNITY_2022_2_OR_NEWER || UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
        return FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        return FindObjectsOfType<Light>();
#endif
    }

    private Vector3 ResolveShadowCenter()
    {
        var centerTransform = shadowCenter != null ? shadowCenter : transform;
        return centerTransform.TransformPoint(centerOffset);
    }

    private Quaternion ResolveKnownSourceLightRotation()
    {
        return sourceDirectionalLight != null && sourceDirectionalLight.type == LightType.Directional
            ? sourceDirectionalLight.transform.rotation
            : Quaternion.identity;
    }

    private Matrix4x4 BuildShadowMatrix(Vector3 center, Vector3 right, Vector3 up, Vector3 forward, List<Renderer> renderers)
    {
        var halfSize = manualHalfSize;
        var depthRange = manualDepthRange;
        var nearDepth = Vector3.Dot(center - forward * (depthRange * 0.5f), forward);

        if (autoFitBounds && TryCalculateLightBounds(center, right, up, forward, renderers, out var maxAbsX, out var maxAbsY, out var minDepth, out var maxDepth))
        {
            halfSize = Mathf.Max(minimumHalfSize, Mathf.Max(maxAbsX, maxAbsY) * boundsPadding);
            nearDepth = minDepth - depthPadding;
            depthRange = Mathf.Max(0.01f, (maxDepth - minDepth) + depthPadding * 2f);
        }

        return BuildWorldToShadowClip(center, right, up, forward, halfSize, halfSize, nearDepth, depthRange);
    }

    private static Matrix4x4 BuildWorldToShadowClip(Vector3 center, Vector3 right, Vector3 up, Vector3 forward, float halfWidth, float halfHeight, float nearDepth, float depthRange)
    {
        var matrix = Matrix4x4.zero;
        matrix.SetRow(0, new Vector4(right.x / halfWidth, right.y / halfWidth, right.z / halfWidth, -Vector3.Dot(right, center) / halfWidth));
        matrix.SetRow(1, new Vector4(up.x / halfHeight, up.y / halfHeight, up.z / halfHeight, -Vector3.Dot(up, center) / halfHeight));

        var depthScale = 2f / depthRange;
        matrix.SetRow(2, new Vector4(forward.x * depthScale, forward.y * depthScale, forward.z * depthScale, -nearDepth * depthScale - 1f));
        matrix.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
        return matrix;
    }

    private static bool TryCalculateLightBounds(Vector3 center, Vector3 right, Vector3 up, Vector3 forward, List<Renderer> renderers, out float maxAbsX, out float maxAbsY, out float minDepth, out float maxDepth)
    {
        maxAbsX = 0f;
        maxAbsY = 0f;
        minDepth = float.PositiveInfinity;
        maxDepth = float.NegativeInfinity;
        var hasBounds = false;

        foreach (var targetRenderer in renderers)
        {
            if (!IsRendererUsable(targetRenderer))
            {
                continue;
            }

            var bounds = targetRenderer.bounds;
            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                        var relative = corner - center;
                        maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(Vector3.Dot(relative, right)));
                        maxAbsY = Mathf.Max(maxAbsY, Mathf.Abs(Vector3.Dot(relative, up)));

                        var depth = Vector3.Dot(corner, forward);
                        minDepth = Mathf.Min(minDepth, depth);
                        maxDepth = Mathf.Max(maxDepth, depth);
                        hasBounds = true;
                    }
                }
            }
        }

        return hasBounds;
    }

    private void RenderDepth(List<Renderer> casters, Matrix4x4 shadowMatrix)
    {
        depthMaterial.SetMatrix(DepthShaderVPId, shadowMatrix);

        var commandBuffer = new CommandBuffer { name = "VLiveKit LiveToon Front Hair Shadow" };
        commandBuffer.SetRenderTarget(shadowTexture);
        commandBuffer.ClearRenderTarget(true, true, Color.white);

        foreach (var caster in casters)
        {
            if (!IsRendererUsable(caster))
            {
                continue;
            }

            var materials = caster.sharedMaterials;
            var drewAnySubMesh = false;
            var isExplicitCaster = Contains(shadowCasters, caster);
            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                if (filterCasterSubMeshesByMaterialName && !isExplicitCaster && !IsHairLike(caster, materials[materialIndex]))
                {
                    continue;
                }

                commandBuffer.DrawRenderer(caster, depthMaterial, materialIndex, 0);
                drewAnySubMesh = true;
            }

            if (!drewAnySubMesh && !filterCasterSubMeshesByMaterialName)
            {
                commandBuffer.DrawRenderer(caster, depthMaterial, 0, 0);
            }
        }

        Graphics.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Release();
    }

    private bool EnsureResources()
    {
        if (depthMaterial == null)
        {
            var shader = Shader.Find(DepthShaderName);
            if (shader == null)
            {
                return false;
            }

            depthMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (shadowTexture != null && shadowTexture.width == textureSize && shadowTexture.height == textureSize)
        {
            return true;
        }

        ReleaseShadowTexture();
        shadowTexture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
        {
            name = "VLiveKit LiveToon Front Hair Shadow",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
            hideFlags = HideFlags.HideAndDontSave
        };
        shadowTexture.Create();
        return true;
    }

    private void ReleaseResources()
    {
        ReleaseShadowTexture();

        if (depthMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(depthMaterial);
            }
            else
            {
                DestroyImmediate(depthMaterial);
            }

            depthMaterial = null;
        }
    }

    private void ReleaseShadowTexture()
    {
        if (shadowTexture == null)
        {
            return;
        }

        shadowTexture.Release();
        if (Application.isPlaying)
        {
            Destroy(shadowTexture);
        }
        else
        {
            DestroyImmediate(shadowTexture);
        }

        shadowTexture = null;
    }

    private void ResolveCasters(List<Renderer> results)
    {
        results.Clear();

        if (!autoCollectHairCasters && shadowCasters != null)
        {
            AddRenderers(shadowCasters, results);
            return;
        }

        scratchRenderers.Clear();
        GetComponentsInChildren<Renderer>(includeInactiveRenderers, scratchRenderers);
        foreach (var targetRenderer in scratchRenderers)
        {
            if (IsHairLike(targetRenderer, null))
            {
                AddRenderer(targetRenderer, results);
            }
        }

        AddRenderers(shadowCasters, results);
    }

    private void ResolveBoundsRenderers(List<Renderer> casters, List<Renderer> results)
    {
        results.Clear();
        AddRenderers(boundsRenderers, results);

        if (results.Count == 0)
        {
            foreach (var caster in casters)
            {
                AddRenderer(caster, results);
            }
        }

        foreach (var caster in casters)
        {
            AddRenderer(caster, results);
        }
    }

    private static void AddRenderers(Renderer[] source, List<Renderer> results)
    {
        if (source == null)
        {
            return;
        }

        foreach (var targetRenderer in source)
        {
            AddRenderer(targetRenderer, results);
        }
    }

    private static void AddRenderer(Renderer targetRenderer, List<Renderer> results)
    {
        if (targetRenderer == null || results.Contains(targetRenderer))
        {
            return;
        }

        results.Add(targetRenderer);
    }

    private static bool Contains<T>(T[] values, T value) where T : UnityEngine.Object
    {
        if (values == null || value == null)
        {
            return false;
        }

        foreach (var candidate in values)
        {
            if (candidate == value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRendererUsable(Renderer targetRenderer)
    {
        return targetRenderer != null
            && targetRenderer.enabled
            && targetRenderer.gameObject.activeInHierarchy;
    }

    private static bool IsHairLike(Renderer targetRenderer, Material material)
    {
        var rendererName = targetRenderer != null ? targetRenderer.name.ToLowerInvariant() : string.Empty;
        var materialName = material != null ? material.name.ToLowerInvariant() : string.Empty;
        var combinedName = rendererName + " " + materialName;
        return combinedName.Contains("hair")
            || combinedName.Contains("kami")
            || combinedName.Contains("\u9aea");
    }
}
