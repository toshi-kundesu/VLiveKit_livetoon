using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("VLiveKit/LiveToon/Character Look Controller")]
public sealed class LiveToonCharacterLookController : MonoBehaviour
{
    private const string LiveToonShaderName = "toshi/VLiveKit/livetoon";

    private enum SphericalNormalScope
    {
        FaceMaterials,
        AllLiveToonMaterials
    }

    private enum MaterialRole
    {
        Character,
        Face,
        Hair,
        Ignore
    }

    private static readonly int IsFaceId = Shader.PropertyToID("_isFace");
    private static readonly int IsCharFaceId = Shader.PropertyToID("_isCharFace");
    private static readonly int IsHairId = Shader.PropertyToID("_isHair");
    private static readonly int FacePositionId = Shader.PropertyToID("_FacePosition");
    private static readonly int FaceForwardDirectionId = Shader.PropertyToID("_FaceForwardDirection");
    private static readonly int FaceUpDirectionId = Shader.PropertyToID("_FaceUpDirection");
    private static readonly int FaceSphereIntensityId = Shader.PropertyToID("_FaceSphereIntensity");
    private static readonly int FaceLightLimitIntensityId = Shader.PropertyToID("_FaceLightLimitIntensity");
    private static readonly int FaceLightYawStepId = Shader.PropertyToID("_FaceLightYawStep");
    private static readonly int FaceLightYawStickyRangeId = Shader.PropertyToID("_FaceLightYawStickyRange");
    private static readonly int FaceLightPitchFlattenId = Shader.PropertyToID("_FaceLightPitchFlatten");
    private static readonly int PerspectiveIntensityId = Shader.PropertyToID("_LiveToonPerspectiveCorrectionIntensity");
    private static readonly int PerspectiveCenterWSId = Shader.PropertyToID("_LiveToonPerspectiveCorrectionCenterWS");
    private static readonly int PerspectiveGroundYId = Shader.PropertyToID("_LiveToonPerspectiveCorrectionGroundY");
    private static readonly int PerspectiveHeightId = Shader.PropertyToID("_LiveToonPerspectiveCorrectionHeight");
    private static readonly int PerspectiveHeightPowerId = Shader.PropertyToID("_LiveToonPerspectiveCorrectionHeightPower");

    [Header("Targets")]
    [SerializeField]
    private bool applyInEditMode = true;

    [SerializeField, Min(0.02f)]
    private float editModeUpdateInterval = 0.1f;

    [SerializeField]
    private bool autoCollectRenderers = true;

    [SerializeField]
    private bool includeInactiveRenderers;

    [SerializeField]
    private Renderer[] targetRenderers = Array.Empty<Renderer>();

    [Header("Role Overrides")]
    [SerializeField]
    private Renderer[] faceRenderers = Array.Empty<Renderer>();

    [SerializeField]
    private Renderer[] hairRenderers = Array.Empty<Renderer>();

    [SerializeField]
    private Material[] faceMaterials = Array.Empty<Material>();

    [SerializeField]
    private Material[] hairMaterials = Array.Empty<Material>();

    [SerializeField]
    private bool autoDetectFaceMaterials;

    [FormerlySerializedAs("autoDetectMaterialRoles")]
    [SerializeField]
    private bool autoDetectHairMaterials = true;

    [Header("Face Reference")]
    [SerializeField]
    private Transform head;

    [SerializeField]
    private Vector3 headPositionOffset;

    [Header("Spherical Normals")]
    [SerializeField]
    private bool enableSphericalNormals = true;

    [SerializeField]
    private SphericalNormalScope sphericalNormalScope = SphericalNormalScope.FaceMaterials;

    [SerializeField, Range(0f, 1f)]
    private float sphericalNormalIntensity = 0.65f;

    [Header("Gizmos")]
    [SerializeField]
    private bool showSphericalNormalGizmo = true;

    [SerializeField, Min(0.01f)]
    private float sphericalNormalGizmoRadius = 0.14f;

    [Header("Directional Light Limit")]
    [SerializeField]
    private bool enableFaceLightDirectionLimit = true;

    [SerializeField, Range(0f, 1f)]
    private float faceLightLimitIntensity = 1f;

    [SerializeField, Range(1f, 180f)]
    private float faceLightYawStep = 45f;

    [SerializeField, Range(0f, 0.95f)]
    private float faceLightYawStickyRange = 0.45f;

    [SerializeField, Range(0f, 1f)]
    private float faceLightPitchFlatten = 1f;

    [Header("Perspective Correction")]
    [SerializeField]
    private bool enablePerspectiveCorrection = true;

    [SerializeField, Range(0f, 1f)]
    private float perspectiveCorrectionIntensity = 0.1f;

    [SerializeField]
    private Vector3 perspectiveCenterOffset = new Vector3(0f, 1f, 0f);

    [SerializeField]
    private Vector3 perspectiveGroundOffset = Vector3.zero;

    [SerializeField, Min(0.01f)]
    private float perspectiveCorrectionHeight = 1.5f;

    [SerializeField, Min(0.01f)]
    private float perspectiveCorrectionHeightPower = 1f;

    [NonSerialized]
    private MaterialPropertyBlock propertyBlock;

    private bool pendingEditModeApply;
    private bool hasLastEditModeState;
    private float nextEditModeUpdateTime;
    private Vector3 lastRootPosition;
    private Vector3 lastRootScale;
    private Vector3 lastFacePositionWS;
    private Quaternion lastRootRotation;
    private Quaternion lastFaceRotation;
    private readonly List<Renderer> resolvedRenderers = new List<Renderer>();

    private void Reset()
    {
        targetRenderers = Array.Empty<Renderer>();
        head = FindHumanoidHead();
    }

    public void SetupFromHumanoid(Animator animator)
    {
        autoCollectRenderers = true;

        if (animator != null && animator.isHuman)
        {
            var humanoidHead = animator.GetBoneTransform(HumanBodyBones.Head);
            if (humanoidHead != null)
            {
                head = humanoidHead;
            }
        }

        if (isActiveAndEnabled && ShouldApplyNow())
        {
            RequestApplyLookSettings();
        }
    }

    private void OnEnable()
    {
        if (ShouldApplyNow())
        {
            RequestApplyLookSettings();
        }
    }

    private void OnDisable()
    {
        ApplyLookSettings(false);
    }

    private void OnValidate()
    {
        sphericalNormalIntensity = Mathf.Clamp01(sphericalNormalIntensity);
        faceLightLimitIntensity = Mathf.Clamp01(faceLightLimitIntensity);
        faceLightYawStep = Mathf.Clamp(faceLightYawStep, 1f, 180f);
        faceLightYawStickyRange = Mathf.Clamp(faceLightYawStickyRange, 0f, 0.95f);
        faceLightPitchFlatten = Mathf.Clamp01(faceLightPitchFlatten);
        sphericalNormalGizmoRadius = Mathf.Max(0.01f, sphericalNormalGizmoRadius);
        editModeUpdateInterval = Mathf.Max(0.02f, editModeUpdateInterval);
        perspectiveCorrectionIntensity = Mathf.Clamp01(perspectiveCorrectionIntensity);
        perspectiveCorrectionHeight = Mathf.Max(0.01f, perspectiveCorrectionHeight);
        perspectiveCorrectionHeightPower = Mathf.Max(0.01f, perspectiveCorrectionHeightPower);

        if (isActiveAndEnabled && ShouldApplyNow())
        {
            RequestApplyLookSettings();
        }
    }

    private void LateUpdate()
    {
        if (!ShouldApplyInLateUpdate())
        {
            return;
        }

        ApplyLookSettings(true);
        CacheEditModeState();
    }

    private bool ShouldApplyNow()
    {
        return Application.isPlaying || applyInEditMode;
    }

    private void RequestApplyLookSettings()
    {
        if (Application.isPlaying)
        {
            ApplyLookSettings(true);
            return;
        }

        pendingEditModeApply = true;
    }

    private bool ShouldApplyInLateUpdate()
    {
        if (Application.isPlaying)
        {
            return true;
        }

        if (!applyInEditMode)
        {
            return false;
        }

        if (!pendingEditModeApply && !HasEditModeStateChanged())
        {
            return false;
        }

        var now = Time.realtimeSinceStartup;
        if (now < nextEditModeUpdateTime)
        {
            return false;
        }

        nextEditModeUpdateTime = now + editModeUpdateInterval;
        pendingEditModeApply = false;
        return true;
    }

    private bool HasEditModeStateChanged()
    {
        var faceReference = ResolveHead();
        var facePositionWS = GetFacePositionWS(faceReference);
        var faceRotation = faceReference != null ? faceReference.rotation : Quaternion.identity;

        return !hasLastEditModeState
            || transform.position != lastRootPosition
            || transform.rotation != lastRootRotation
            || transform.lossyScale != lastRootScale
            || facePositionWS != lastFacePositionWS
            || faceRotation != lastFaceRotation;
    }

    private void CacheEditModeState()
    {
        if (Application.isPlaying)
        {
            return;
        }

        var faceReference = ResolveHead();
        hasLastEditModeState = true;
        lastRootPosition = transform.position;
        lastRootRotation = transform.rotation;
        lastRootScale = transform.lossyScale;
        lastFacePositionWS = GetFacePositionWS(faceReference);
        lastFaceRotation = faceReference != null ? faceReference.rotation : Quaternion.identity;
    }

    private void ApplyLookSettings(bool enabledState)
    {
        ResolveRenderers(resolvedRenderers);
        if (resolvedRenderers.Count == 0)
        {
            return;
        }

        var faceReference = ResolveHead();
        var facePositionWS = GetFacePositionWS(faceReference);
        var faceForwardWS = faceReference.forward.normalized;
        var faceUpWS = faceReference.up.normalized;
        var perspectiveCenterWS = transform.TransformPoint(perspectiveCenterOffset);
        var perspectiveGroundY = transform.TransformPoint(perspectiveGroundOffset).y;

        foreach (var targetRenderer in resolvedRenderers)
        {
            if (targetRenderer == null)
            {
                continue;
            }

            var materials = targetRenderer.sharedMaterials;
            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var material = materials[materialIndex];
                if (!IsLiveToonMaterial(material))
                {
                    continue;
                }

                var role = DetermineRole(targetRenderer, material);
                if (role == MaterialRole.Ignore)
                {
                    continue;
                }

                var isFace = role == MaterialRole.Face;
                var isHair = role == MaterialRole.Hair;
                var useSphericalNormals = enabledState
                    && enableSphericalNormals
                    && (sphericalNormalScope == SphericalNormalScope.AllLiveToonMaterials || isFace);
                var useFaceLightLimit = enabledState && enableFaceLightDirectionLimit && isFace;
                var usePerspectiveCorrection = enabledState && enablePerspectiveCorrection;
                var localFacePosition = targetRenderer.transform.InverseTransformPoint(facePositionWS);

                var block = GetPropertyBlock();
                targetRenderer.GetPropertyBlock(block, materialIndex);
                block.SetFloat(IsFaceId, enabledState && isFace ? 1f : 0f);
                block.SetFloat(IsCharFaceId, enabledState && isFace ? 1f : 0f);
                block.SetFloat(IsHairId, enabledState && isHair ? 1f : 0f);
                block.SetVector(FacePositionId, localFacePosition);
                block.SetVector(FaceForwardDirectionId, faceForwardWS);
                block.SetVector(FaceUpDirectionId, faceUpWS);
                block.SetFloat(FaceSphereIntensityId, useSphericalNormals ? sphericalNormalIntensity : 0f);
                block.SetFloat(FaceLightLimitIntensityId, useFaceLightLimit ? faceLightLimitIntensity : 0f);
                block.SetFloat(FaceLightYawStepId, faceLightYawStep);
                block.SetFloat(FaceLightYawStickyRangeId, faceLightYawStickyRange);
                block.SetFloat(FaceLightPitchFlattenId, faceLightPitchFlatten);
                block.SetFloat(PerspectiveIntensityId, usePerspectiveCorrection ? perspectiveCorrectionIntensity : 0f);
                block.SetVector(PerspectiveCenterWSId, perspectiveCenterWS);
                block.SetFloat(PerspectiveGroundYId, perspectiveGroundY);
                block.SetFloat(PerspectiveHeightId, perspectiveCorrectionHeight);
                block.SetFloat(PerspectiveHeightPowerId, perspectiveCorrectionHeightPower);
                targetRenderer.SetPropertyBlock(block, materialIndex);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showSphericalNormalGizmo || !enableSphericalNormals)
        {
            return;
        }

        var faceReference = ResolveHead();
        if (faceReference == null)
        {
            return;
        }

        var center = GetFacePositionWS(faceReference);
        var radius = Mathf.Max(0.01f, sphericalNormalGizmoRadius);
        var alpha = Mathf.Lerp(0.25f, 0.9f, sphericalNormalIntensity);

        var previousColor = Gizmos.color;
        var previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.identity;

        Gizmos.color = new Color(0.15f, 0.55f, 1f, alpha);
        Gizmos.DrawWireSphere(center, radius);

        Gizmos.color = new Color(0.15f, 0.55f, 1f, Mathf.Min(1f, alpha + 0.1f));
        Gizmos.DrawSphere(center, radius * 0.055f);
        Gizmos.DrawLine(faceReference.position, center);

        var axisLength = radius * 0.75f;
        Gizmos.color = new Color(0.2f, 0.75f, 1f, alpha);
        Gizmos.DrawLine(center, center + faceReference.forward.normalized * axisLength);
        Gizmos.color = new Color(0.55f, 0.9f, 1f, alpha);
        Gizmos.DrawLine(center, center + faceReference.up.normalized * axisLength);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private static Vector3 GetFacePositionWS(Transform faceReference, Vector3 offset)
    {
        return faceReference != null ? faceReference.TransformPoint(offset) : offset;
    }

    private Vector3 GetFacePositionWS(Transform faceReference)
    {
        return GetFacePositionWS(faceReference, headPositionOffset);
    }

    private MaterialPropertyBlock GetPropertyBlock()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        return propertyBlock;
    }

    private void ResolveRenderers(List<Renderer> results)
    {
        results.Clear();
        if (autoCollectRenderers)
        {
            GetComponentsInChildren<Renderer>(includeInactiveRenderers, results);
            return;
        }

        if (targetRenderers == null)
        {
            return;
        }

        foreach (var targetRenderer in targetRenderers)
        {
            if (targetRenderer != null)
            {
                results.Add(targetRenderer);
            }
        }
    }

    private Transform ResolveHead()
    {
        if (head != null)
        {
            return head;
        }

        var humanoidHead = FindHumanoidHead();
        return humanoidHead != null ? humanoidHead : transform;
    }

    private Transform FindHumanoidHead()
    {
        var animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            var humanoidHead = animator.GetBoneTransform(HumanBodyBones.Head);
            if (humanoidHead != null)
            {
                return humanoidHead;
            }
        }

        return null;
    }

    private MaterialRole DetermineRole(Renderer targetRenderer, Material material)
    {
        if (Contains(faceRenderers, targetRenderer) || Contains(faceMaterials, material))
        {
            return MaterialRole.Face;
        }

        if (Contains(hairRenderers, targetRenderer) || Contains(hairMaterials, material))
        {
            return MaterialRole.Hair;
        }

        var rendererName = targetRenderer.name.ToLowerInvariant();
        var materialName = material.name.ToLowerInvariant();
        var combinedName = rendererName + " " + materialName;

        if (autoDetectHairMaterials && ContainsAny(combinedName, "hair", "kami", "\u9aea"))
        {
            return MaterialRole.Hair;
        }

        if (autoDetectFaceMaterials && ContainsAny(combinedName, "face", "eye", "iris", "highlight", "brow", "mayu", "mouth", "lip", "eyelash", "matuge", "\u776b", "\u7709", "\u76ee", "\u53e3"))
        {
            return MaterialRole.Face;
        }

        return MaterialRole.Character;
    }

    private static bool IsLiveToonMaterial(Material material)
    {
        return material != null
            && material.shader != null
            && material.shader.name == LiveToonShaderName;
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

    private static bool ContainsAny(string value, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            if (value.Contains(fragment))
            {
                return true;
            }
        }

        return false;
    }
}
