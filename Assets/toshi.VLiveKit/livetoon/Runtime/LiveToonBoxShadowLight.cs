using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("VLiveKit/LiveToon/Box Shadow Light")]
public sealed class LiveToonBoxShadowLight : MonoBehaviour
{
    private const int MaxShadowTextureSize = 4096;
    private const string DepthShaderName = "Hidden/VLiveKit/LiveToon/FrontHairShadowDepth";
    private const string LiveToonShaderName = "toshi/VLiveKit/livetoon";
    private static readonly Vector3 FrontHairFaceBoxCenterOffset = new Vector3(0f, -0.99f, 0f);
    private static readonly Vector3 FrontHairFaceBoxSize = new Vector3(1.52f, 3.05f, 0.6f);

    private static readonly int DepthShaderVPId = Shader.PropertyToID("_LiveToonHairShadowVP");
    private static readonly int BoxShadowEnabledId = Shader.PropertyToID("_LiveToonBoxShadowEnabled");
    private static readonly int BoxShadowMapId = Shader.PropertyToID("_LiveToonBoxShadowMap");
    private static readonly int BoxShadowVPId = Shader.PropertyToID("_LiveToonBoxShadowVP");
    private static readonly int BoxShadowStrengthId = Shader.PropertyToID("_LiveToonBoxShadowStrength");
    private static readonly int BoxShadowBiasId = Shader.PropertyToID("_LiveToonBoxShadowBias");
    private static readonly int BoxShadowUseDepthId = Shader.PropertyToID("_LiveToonBoxShadowUseDepth");
    private static readonly int BoxShadowSilhouetteAttenuationId = Shader.PropertyToID("_LiveToonBoxShadowSilhouetteAttenuation");
    private static readonly int BoxShadowFlipUId = Shader.PropertyToID("_LiveToonBoxShadowFlipU");
    private static readonly int BoxShadowFlipVId = Shader.PropertyToID("_LiveToonBoxShadowFlipV");
    private static readonly int BoxShadowInvertSilhouetteId = Shader.PropertyToID("_LiveToonBoxShadowInvertSilhouette");

    [Header("Source Light")]
    [SerializeField]
    private Light sourceDirectionalLight;

    [SerializeField]
    private bool syncDirectionFromSourceLight = true;

    [Header("Targets")]
    [SerializeField]
    private bool applyInEditMode = true;

    [SerializeField, Min(0.02f)]
    private float editModeUpdateInterval = 0.25f;

    [SerializeField]
    private Renderer[] shadowCasters = Array.Empty<Renderer>();

    [SerializeField]
    private Renderer[] shadowReceivers = Array.Empty<Renderer>();

    [Header("Self Shadow")]
    [SerializeField]
    private bool fullBodySelfShadowMode;

    [SerializeField, HideInInspector]
    private bool fullBodySelfShadowDefaultsApplied;

    [SerializeField, HideInInspector]
    private bool frontHairFaceShadowDefaultsApplied;

    [Header("Whole Character Mode")]
    [SerializeField, HideInInspector]
    private bool useWholeCharacterTargets;

    [SerializeField]
    private Transform targetRoot;

    [SerializeField]
    private bool collectCastersFromTargetRoot;

    [SerializeField]
    private bool collectReceiversFromTargetRoot;

    [SerializeField]
    private Renderer[] excludedRenderers = Array.Empty<Renderer>();

    [SerializeField]
    private Renderer[] excludedCasters = Array.Empty<Renderer>();

    [SerializeField]
    private Renderer[] excludedReceivers = Array.Empty<Renderer>();

    [Header("Box Light")]
    [SerializeField]
    private Vector3 boxCenterOffset = Vector3.zero;

    [SerializeField]
    private Vector3 boxSize = new Vector3(0.35f, 0.35f, 0.6f);

    [SerializeField]
    private bool autoFitBoxToTargets;

    [SerializeField, Range(1f, 2f)]
    private float autoFitPadding = 1.12f;

    [SerializeField, Range(0f, 1f)]
    private float autoFitDepthPadding = 0.12f;

    [SerializeField]
    private bool showGizmo = true;

    [Header("Shadow")]
    [SerializeField, Range(64, MaxShadowTextureSize)]
    private int textureSize = 1024;

    [SerializeField, Range(0f, 1f)]
    private float shadowStrength = 1f;

    [SerializeField, Range(0f, 0.05f)]
    private float shadowBias = 0.003f;

    [SerializeField]
    private bool useDepthComparison;

    [SerializeField, Range(0f, 1f)]
    private float silhouetteAttenuation = 0.12f;

    [SerializeField]
    private bool flipU;

    [SerializeField]
    private bool flipV = true;

    [SerializeField]
    private bool invertSilhouette;

    private readonly List<Renderer> appliedReceivers = new List<Renderer>();
    private readonly List<Renderer> resolvedCasters = new List<Renderer>();
    private readonly List<Renderer> resolvedReceivers = new List<Renderer>();
    private MaterialPropertyBlock propertyBlock;
    private RenderTexture shadowTexture;
    private Material depthMaterial;
    private bool pendingEditModeRender;
    private bool hasLastEditModeState;
    private float nextEditModeUpdateTime;
    private Vector3 lastPosition;
    private Vector3 lastScale;
    private Quaternion lastRotation;
    private Quaternion lastSourceRotation;

    private void OnEnable()
    {
        MigrateWholeCharacterTargets();
        ApplyFullBodySelfShadowDefaultsIfNeeded();

        if (ShouldApplyNow())
        {
            RequestRenderAndApply();
        }
    }

    private void OnDisable()
    {
        ClearAppliedReceivers();
        ReleaseResources();
    }

    private void OnValidate()
    {
        MigrateWholeCharacterTargets();
        ApplyFullBodySelfShadowDefaultsIfNeeded();

        textureSize = Mathf.Clamp(textureSize, 64, MaxShadowTextureSize);
        shadowStrength = Mathf.Clamp01(shadowStrength);
        shadowBias = Mathf.Clamp(shadowBias, 0f, 0.05f);
        silhouetteAttenuation = Mathf.Clamp01(silhouetteAttenuation);
        autoFitPadding = Mathf.Clamp(autoFitPadding, 1f, 2f);
        autoFitDepthPadding = Mathf.Clamp01(autoFitDepthPadding);
        editModeUpdateInterval = Mathf.Max(0.02f, editModeUpdateInterval);
        boxSize = new Vector3(
            Mathf.Max(0.01f, boxSize.x),
            Mathf.Max(0.01f, boxSize.y),
            Mathf.Max(0.01f, boxSize.z));

        if (isActiveAndEnabled && ShouldApplyNow())
        {
            RequestRenderAndApply();
        }
    }

    public void SetSourceDirectionalLight(Light directionalLight)
    {
        if (directionalLight != null && directionalLight.type == LightType.Directional)
        {
            sourceDirectionalLight = directionalLight;
            syncDirectionFromSourceLight = true;
        }

        SyncDirectionFromSourceLight();

        if (isActiveAndEnabled && ShouldApplyNow())
        {
            RequestRenderAndApply();
        }
    }

    public void ApplyFrontHairFaceShadowDefaultsIfNeeded()
    {
        if (frontHairFaceShadowDefaultsApplied)
        {
            return;
        }

        ApplyFrontHairFaceShadowDefaults();
    }

    public void ApplyFrontHairFaceShadowDefaults()
    {
        if (shadowCasters == null)
        {
            shadowCasters = Array.Empty<Renderer>();
        }

        if (shadowReceivers == null)
        {
            shadowReceivers = Array.Empty<Renderer>();
        }

        fullBodySelfShadowMode = false;
        fullBodySelfShadowDefaultsApplied = false;
        useWholeCharacterTargets = false;
        targetRoot = null;
        collectCastersFromTargetRoot = false;
        collectReceiversFromTargetRoot = false;

        applyInEditMode = true;
        boxCenterOffset = FrontHairFaceBoxCenterOffset;
        boxSize = FrontHairFaceBoxSize;
        autoFitBoxToTargets = false;
        autoFitPadding = 1.12f;
        autoFitDepthPadding = 0.12f;
        showGizmo = true;

        textureSize = MaxShadowTextureSize;
        shadowStrength = 1f;
        shadowBias = 0.003f;
        useDepthComparison = false;
        silhouetteAttenuation = 0f;
        flipU = false;
        flipV = true;
        invertSilhouette = false;
        frontHairFaceShadowDefaultsApplied = true;

        if (isActiveAndEnabled && ShouldApplyNow())
        {
            RequestRenderAndApply();
        }
    }

    [ContextMenu("Use Front Hair Face Shadow Defaults")]
    private void UseFrontHairFaceShadowDefaults()
    {
        ApplyFrontHairFaceShadowDefaults();
    }

    [ContextMenu("Use Full Body Self Shadow Defaults")]
    private void UseFullBodySelfShadowDefaults()
    {
        fullBodySelfShadowMode = true;
        fullBodySelfShadowDefaultsApplied = false;
        ApplyFullBodySelfShadowDefaultsIfNeeded();
        RequestRenderAndApply();
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
            || transform.position != lastPosition
            || transform.rotation != lastRotation
            || transform.lossyScale != lastScale
            || ResolveSourceLightRotation() != lastSourceRotation;
    }

    private void CacheEditModeState()
    {
        if (Application.isPlaying)
        {
            return;
        }

        hasLastEditModeState = true;
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastScale = transform.lossyScale;
        lastSourceRotation = ResolveSourceLightRotation();
    }

    private void RenderAndApply()
    {
        SyncDirectionFromSourceLight();
        ResolveCasters(resolvedCasters);
        ResolveReceivers(resolvedReceivers);

        if (resolvedCasters.Count == 0 || resolvedReceivers.Count == 0)
        {
            ClearAppliedReceivers();
            return;
        }

        if (!EnsureResources())
        {
            ClearAppliedReceivers();
            return;
        }

        var shadowMatrix = BuildShadowMatrix(GetEffectiveBoxCenterOffset(), GetEffectiveBoxSize());
        RenderDepth(shadowMatrix, resolvedCasters);
        ApplyReceivers(shadowMatrix, resolvedReceivers);
    }

    private Matrix4x4 BuildShadowMatrix(Vector3 centerOffset, Vector3 size)
    {
        var center = TransformLightLocalPoint(centerOffset);
        var right = transform.right.normalized;
        var up = transform.up.normalized;
        var forward = transform.forward.normalized;
        var halfWidth = Mathf.Max(0.005f, size.x * 0.5f);
        var halfHeight = Mathf.Max(0.005f, size.y * 0.5f);
        var depthRange = Mathf.Max(0.01f, size.z);
        var nearDepth = Vector3.Dot(center - forward * (depthRange * 0.5f), forward);

        var matrix = Matrix4x4.zero;
        matrix.SetRow(0, new Vector4(right.x / halfWidth, right.y / halfWidth, right.z / halfWidth, -Vector3.Dot(right, center) / halfWidth));
        matrix.SetRow(1, new Vector4(up.x / halfHeight, up.y / halfHeight, up.z / halfHeight, -Vector3.Dot(up, center) / halfHeight));

        var depthScale = 2f / depthRange;
        matrix.SetRow(2, new Vector4(forward.x * depthScale, forward.y * depthScale, forward.z * depthScale, -nearDepth * depthScale - 1f));
        matrix.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
        return matrix;
    }

    private void RenderDepth(Matrix4x4 shadowMatrix, List<Renderer> casters)
    {
        depthMaterial.SetMatrix(DepthShaderVPId, shadowMatrix);

        var commandBuffer = new CommandBuffer { name = "VLiveKit LiveToon Box Shadow" };
        commandBuffer.SetRenderTarget(shadowTexture);
        commandBuffer.ClearRenderTarget(true, true, Color.white);

        foreach (var caster in casters)
        {
            if (!IsRendererUsable(caster))
            {
                continue;
            }

            var materials = caster.sharedMaterials;
            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                commandBuffer.DrawRenderer(caster, depthMaterial, materialIndex, 0);
            }
        }

        Graphics.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Release();
    }

    private void ApplyReceivers(Matrix4x4 shadowMatrix, List<Renderer> receivers)
    {
        ClearAppliedReceivers();

        foreach (var receiver in receivers)
        {
            if (!IsRendererUsable(receiver))
            {
                continue;
            }

            var materials = receiver.sharedMaterials;
            var applied = false;
            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var material = materials[materialIndex];
                if (!IsLiveToonMaterial(material))
                {
                    continue;
                }

                var block = GetPropertyBlock();
                receiver.GetPropertyBlock(block, materialIndex);
                block.SetFloat(BoxShadowEnabledId, 1f);
                block.SetTexture(BoxShadowMapId, shadowTexture);
                block.SetMatrix(BoxShadowVPId, shadowMatrix);
                block.SetFloat(BoxShadowStrengthId, shadowStrength);
                block.SetFloat(BoxShadowBiasId, shadowBias);
                block.SetFloat(BoxShadowUseDepthId, ShouldUseDepthComparison() ? 1f : 0f);
                block.SetFloat(BoxShadowSilhouetteAttenuationId, silhouetteAttenuation);
                block.SetFloat(BoxShadowFlipUId, flipU ? 1f : 0f);
                block.SetFloat(BoxShadowFlipVId, flipV ? 1f : 0f);
                block.SetFloat(BoxShadowInvertSilhouetteId, invertSilhouette ? 1f : 0f);
                receiver.SetPropertyBlock(block, materialIndex);
                applied = true;
            }

            if (applied)
            {
                AddRenderer(receiver, appliedReceivers);
            }
        }
    }

    private void ResolveCasters(List<Renderer> results)
    {
        results.Clear();

        if (fullBodySelfShadowMode || collectCastersFromTargetRoot)
        {
            CollectWholeCharacterRenderers(results, excludedCasters);
            return;
        }

        AddUsableRenderers(shadowCasters, results);
    }

    private void ResolveReceivers(List<Renderer> results)
    {
        results.Clear();

        if (fullBodySelfShadowMode || collectReceiversFromTargetRoot)
        {
            CollectWholeCharacterRenderers(results, excludedReceivers);
            return;
        }

        AddUsableRenderers(shadowReceivers, results);
    }

    private void MigrateWholeCharacterTargets()
    {
        if (!useWholeCharacterTargets)
        {
            return;
        }

        collectCastersFromTargetRoot = true;
        collectReceiversFromTargetRoot = true;
        useWholeCharacterTargets = false;
    }

    private void ApplyFullBodySelfShadowDefaultsIfNeeded()
    {
        if (!fullBodySelfShadowMode)
        {
            fullBodySelfShadowDefaultsApplied = false;
            return;
        }

        if (fullBodySelfShadowDefaultsApplied)
        {
            return;
        }

        collectCastersFromTargetRoot = true;
        collectReceiversFromTargetRoot = true;
        useDepthComparison = true;
        autoFitBoxToTargets = true;
        textureSize = MaxShadowTextureSize;
        shadowStrength = 1f;
        shadowBias = Mathf.Max(shadowBias, 0.003f);
        fullBodySelfShadowDefaultsApplied = true;
    }

    private bool ShouldUseDepthComparison()
    {
        return fullBodySelfShadowMode || useDepthComparison;
    }

    private bool ShouldAutoFitBox()
    {
        return fullBodySelfShadowMode || autoFitBoxToTargets;
    }

    private Vector3 GetEffectiveBoxCenterOffset()
    {
        return TryFitBoxToResolvedTargets(out var centerOffset, out _) ? centerOffset : boxCenterOffset;
    }

    private Vector3 GetEffectiveBoxSize()
    {
        return TryFitBoxToResolvedTargets(out _, out var size) ? size : boxSize;
    }

    private bool TryFitBoxToResolvedTargets(out Vector3 centerOffset, out Vector3 size)
    {
        centerOffset = boxCenterOffset;
        size = boxSize;

        if (!ShouldAutoFitBox())
        {
            return false;
        }

        var hasBounds = false;
        var min = Vector3.zero;
        var max = Vector3.zero;

        AccumulateRendererBounds(resolvedCasters, ref hasBounds, ref min, ref max);
        AccumulateRendererBounds(resolvedReceivers, ref hasBounds, ref min, ref max);
        if (!hasBounds)
        {
            return false;
        }

        centerOffset = (min + max) * 0.5f;
        size = max - min;
        size.x = Mathf.Max(0.01f, size.x * autoFitPadding);
        size.y = Mathf.Max(0.01f, size.y * autoFitPadding);
        size.z = Mathf.Max(0.01f, (size.z * autoFitPadding) + autoFitDepthPadding * 2f);
        return true;
    }

    private void AccumulateRendererBounds(List<Renderer> renderers, ref bool hasBounds, ref Vector3 min, ref Vector3 max)
    {
        foreach (var targetRenderer in renderers)
        {
            if (!IsRendererUsable(targetRenderer))
            {
                continue;
            }

            AccumulateBounds(targetRenderer.bounds, ref hasBounds, ref min, ref max);
        }
    }

    private void AccumulateBounds(Bounds bounds, ref bool hasBounds, ref Vector3 min, ref Vector3 max)
    {
        var boundsMin = bounds.min;
        var boundsMax = bounds.max;

        AccumulatePoint(new Vector3(boundsMin.x, boundsMin.y, boundsMin.z), ref hasBounds, ref min, ref max);
        AccumulatePoint(new Vector3(boundsMin.x, boundsMin.y, boundsMax.z), ref hasBounds, ref min, ref max);
        AccumulatePoint(new Vector3(boundsMin.x, boundsMax.y, boundsMin.z), ref hasBounds, ref min, ref max);
        AccumulatePoint(new Vector3(boundsMin.x, boundsMax.y, boundsMax.z), ref hasBounds, ref min, ref max);
        AccumulatePoint(new Vector3(boundsMax.x, boundsMin.y, boundsMin.z), ref hasBounds, ref min, ref max);
        AccumulatePoint(new Vector3(boundsMax.x, boundsMin.y, boundsMax.z), ref hasBounds, ref min, ref max);
        AccumulatePoint(new Vector3(boundsMax.x, boundsMax.y, boundsMin.z), ref hasBounds, ref min, ref max);
        AccumulatePoint(new Vector3(boundsMax.x, boundsMax.y, boundsMax.z), ref hasBounds, ref min, ref max);
    }

    private void AccumulatePoint(Vector3 pointWS, ref bool hasBounds, ref Vector3 min, ref Vector3 max)
    {
        var lightLocalPoint = WorldToLightLocalPoint(pointWS);
        if (!hasBounds)
        {
            min = lightLocalPoint;
            max = lightLocalPoint;
            hasBounds = true;
            return;
        }

        min = Vector3.Min(min, lightLocalPoint);
        max = Vector3.Max(max, lightLocalPoint);
    }

    private void CollectWholeCharacterRenderers(List<Renderer> results, Renderer[] exclusions)
    {
        var root = targetRoot != null ? targetRoot : transform;
        if (root == null)
        {
            return;
        }

        var childRenderers = root.GetComponentsInChildren<Renderer>(false);
        foreach (var targetRenderer in childRenderers)
        {
            if (!IsRendererUsable(targetRenderer)
                || ContainsRenderer(excludedRenderers, targetRenderer)
                || ContainsRenderer(exclusions, targetRenderer))
            {
                continue;
            }

            AddRenderer(targetRenderer, results);
        }
    }

    private static void AddUsableRenderers(Renderer[] source, List<Renderer> results)
    {
        if (source == null)
        {
            return;
        }

        foreach (var targetRenderer in source)
        {
            if (!IsRendererUsable(targetRenderer))
            {
                continue;
            }

            AddRenderer(targetRenderer, results);
        }
    }

    private void ClearAppliedReceivers()
    {
        foreach (var receiver in appliedReceivers)
        {
            if (receiver == null)
            {
                continue;
            }

            var materials = receiver.sharedMaterials;
            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                if (!IsLiveToonMaterial(materials[materialIndex]))
                {
                    continue;
                }

                var block = GetPropertyBlock();
                receiver.GetPropertyBlock(block, materialIndex);
                block.SetFloat(BoxShadowEnabledId, 0f);
                receiver.SetPropertyBlock(block, materialIndex);
            }
        }

        appliedReceivers.Clear();
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
            name = "VLiveKit LiveToon Box Shadow",
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

    private MaterialPropertyBlock GetPropertyBlock()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        return propertyBlock;
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmo)
        {
            return;
        }

        SyncDirectionFromSourceLight();
        ResolveCasters(resolvedCasters);
        ResolveReceivers(resolvedReceivers);

        var centerOffset = GetEffectiveBoxCenterOffset();
        var size = GetEffectiveBoxSize();
        var previousColor = Gizmos.color;
        var previousMatrix = Gizmos.matrix;

        Gizmos.matrix = Matrix4x4.TRS(TransformLightLocalPoint(centerOffset), transform.rotation, size);
        Gizmos.color = new Color(0.1f, 0.65f, 1f, 0.75f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.matrix = Matrix4x4.identity;
        var center = TransformLightLocalPoint(centerOffset);
        Gizmos.DrawLine(center - transform.forward * size.z * 0.5f, center + transform.forward * size.z * 0.5f);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Gizmos.DrawLine(center, center + transform.forward * Mathf.Max(0.05f, size.z * 0.35f));

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private Vector3 TransformLightLocalPoint(Vector3 lightLocalPoint)
    {
        return transform.position
            + transform.right.normalized * lightLocalPoint.x
            + transform.up.normalized * lightLocalPoint.y
            + transform.forward.normalized * lightLocalPoint.z;
    }

    private Vector3 WorldToLightLocalPoint(Vector3 worldPoint)
    {
        var originRelativePoint = worldPoint - transform.position;
        return new Vector3(
            Vector3.Dot(originRelativePoint, transform.right.normalized),
            Vector3.Dot(originRelativePoint, transform.up.normalized),
            Vector3.Dot(originRelativePoint, transform.forward.normalized));
    }

    private void SyncDirectionFromSourceLight()
    {
        if (!syncDirectionFromSourceLight)
        {
            return;
        }

        if (sourceDirectionalLight == null || sourceDirectionalLight.type != LightType.Directional)
        {
            return;
        }

        var sourceRotation = sourceDirectionalLight.transform.rotation;
        if (transform.rotation != sourceRotation)
        {
            transform.rotation = sourceRotation;
        }
    }

    private Quaternion ResolveSourceLightRotation()
    {
        if (!syncDirectionFromSourceLight
            || sourceDirectionalLight == null
            || sourceDirectionalLight.type != LightType.Directional)
        {
            return Quaternion.identity;
        }

        return sourceDirectionalLight.transform.rotation;
    }

    private static bool IsRendererUsable(Renderer targetRenderer)
    {
        return targetRenderer != null
            && targetRenderer.enabled
            && targetRenderer.gameObject.activeInHierarchy;
    }

    private static bool IsLiveToonMaterial(Material material)
    {
        return material != null
            && material.shader != null
            && material.shader.name == LiveToonShaderName;
    }

    private static bool ContainsRenderer(Renderer[] renderers, Renderer targetRenderer)
    {
        if (renderers == null || targetRenderer == null)
        {
            return false;
        }

        foreach (var renderer in renderers)
        {
            if (renderer == targetRenderer)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddRenderer(Renderer targetRenderer, List<Renderer> results)
    {
        if (targetRenderer == null || results.Contains(targetRenderer))
        {
            return;
        }

        results.Add(targetRenderer);
    }
}
