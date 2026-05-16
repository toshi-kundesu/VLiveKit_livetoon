using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("VLiveKit/LiveToon/LiveToon Setup")]
public sealed class LiveToonSetup : MonoBehaviour
{
    private const string BoxShadowLightObjectName = "VLiveBoxLight";
    private static readonly Vector3 BoxShadowLightLocalEuler = new Vector3(-26.61f, 13.5f, 0f);
    private static readonly int ShadowBoundarySaturationId = Shader.PropertyToID("_Sat");

    [Header("Auto Setup")]
    [SerializeField]
    private bool setupOnEnable = true;

    [SerializeField]
    private bool requireHumanoid = true;

    [SerializeField]
    private Animator characterAnimator;

    [SerializeField]
    private Light sourceDirectionalLight;

    [Header("Components")]
    [SerializeField]
    private bool setupCharacterLookController = true;

    [SerializeField]
    private bool setupFrontHairShadowLight = true;

    [SerializeField]
    private bool setupBoxShadowLight = true;

    [SerializeField]
    private LiveToonCharacterLookController characterLookController;

    [SerializeField]
    private LiveToonFrontHairShadowLight frontHairShadowLight;

    [SerializeField]
    private LiveToonBoxShadowLight boxShadowLight;

    [Header("Look")]
    [SerializeField]
    private bool overrideShadowBoundarySaturation;

    [SerializeField, Range(0f, 2f)]
    private float shadowBoundarySaturation = 1f;

    private MaterialPropertyBlock propertyBlock;

    public bool IsHumanoid
    {
        get
        {
            var animator = ResolveAnimator();
            return animator != null && animator.isHuman;
        }
    }

    private void Reset()
    {
        characterAnimator = FindAnimator();
        sourceDirectionalLight = FindDirectionalLight();
        Setup();
    }

    private void OnEnable()
    {
        if (setupOnEnable)
        {
            Setup();
        }
    }

    [ContextMenu("Setup LiveToon")]
    public void Setup()
    {
        var animator = ResolveAnimator();
        if (requireHumanoid && (animator == null || !animator.isHuman))
        {
            return;
        }

        if (sourceDirectionalLight == null)
        {
            sourceDirectionalLight = FindDirectionalLight();
        }

        if (setupCharacterLookController)
        {
            characterLookController = EnsureComponent(characterLookController);
            characterLookController.SetupFromHumanoid(animator);
        }

        if (setupFrontHairShadowLight)
        {
            frontHairShadowLight = EnsureComponent(frontHairShadowLight);
            frontHairShadowLight.SetupFromHumanoid(animator, sourceDirectionalLight);
        }

        if (setupBoxShadowLight)
        {
            boxShadowLight = EnsureBoxShadowLight(animator);
            if (boxShadowLight != null)
            {
                boxShadowLight.SetSourceDirectionalLight(sourceDirectionalLight);
                boxShadowLight.ApplyFrontHairFaceShadowDefaultsIfNeeded();
            }
        }

        ApplyShadowBoundarySaturation();
    }

    [ContextMenu("Apply Shadow Boundary Saturation")]
    public void ApplyShadowBoundarySaturation()
    {
        var root = ResolveAnimator();
        var rootTransform = root != null ? root.transform : transform;
        if (rootTransform == null)
        {
            return;
        }

        var renderers = rootTransform.GetComponentsInChildren<Renderer>(true);
        foreach (var targetRenderer in renderers)
        {
            if (targetRenderer == null)
            {
                continue;
            }

            var materials = targetRenderer.sharedMaterials;
            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var material = materials[materialIndex];
                if (material == null || !material.HasProperty(ShadowBoundarySaturationId))
                {
                    continue;
                }

                var value = overrideShadowBoundarySaturation
                    ? shadowBoundarySaturation
                    : material.GetFloat(ShadowBoundarySaturationId);

                var block = GetPropertyBlock();
                targetRenderer.GetPropertyBlock(block, materialIndex);
                block.SetFloat(ShadowBoundarySaturationId, value);
                targetRenderer.SetPropertyBlock(block, materialIndex);
            }
        }
    }

    public void SetShadowBoundarySaturation(float value)
    {
        overrideShadowBoundarySaturation = true;
        shadowBoundarySaturation = Mathf.Clamp(value, 0f, 2f);
        ApplyShadowBoundarySaturation();
    }

    public void ClearShadowBoundarySaturationOverride()
    {
        overrideShadowBoundarySaturation = false;
        ApplyShadowBoundarySaturation();
    }

    private void OnValidate()
    {
        shadowBoundarySaturation = Mathf.Clamp(shadowBoundarySaturation, 0f, 2f);
        if (isActiveAndEnabled)
        {
            ApplyShadowBoundarySaturation();
        }
    }

    private MaterialPropertyBlock GetPropertyBlock()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        return propertyBlock;
    }

    private Animator ResolveAnimator()
    {
        if (characterAnimator != null)
        {
            return characterAnimator;
        }

        characterAnimator = FindAnimator();
        return characterAnimator;
    }

    private Animator FindAnimator()
    {
        return GetComponentInChildren<Animator>(true);
    }

    private static Light FindDirectionalLight()
    {
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

    private T EnsureComponent<T>(T current) where T : Component
    {
        if (current != null)
        {
            return current;
        }

        if (TryGetComponent<T>(out var existing))
        {
            return existing;
        }

        return gameObject.AddComponent<T>();
    }

    private LiveToonBoxShadowLight EnsureBoxShadowLight(Animator animator)
    {
        var head = FindHumanoidHead(animator);
        var parent = head != null ? head : transform;
        var light = boxShadowLight;
        var applyTransformDefaults = false;

        if (light == null)
        {
            light = FindOrAddNamedBoxShadowLight(parent);
        }

        if (light == null && parent != transform)
        {
            light = FindOrAddNamedBoxShadowLight(transform);
        }

        if (light == null)
        {
            var lightObject = new GameObject(BoxShadowLightObjectName);
            lightObject.transform.SetParent(parent, false);
            light = lightObject.AddComponent<LiveToonBoxShadowLight>();
            applyTransformDefaults = true;
        }

        ConstrainBoxShadowLightToHead(light, parent, applyTransformDefaults);
        return light;
    }

    private static void ConstrainBoxShadowLightToHead(LiveToonBoxShadowLight light, Transform parent, bool applyTransformDefaults)
    {
        if (light == null || parent == null)
        {
            return;
        }

        var lightTransform = light.transform;
        if (lightTransform.parent != parent)
        {
            lightTransform.SetParent(parent, !applyTransformDefaults);
        }

        if (!applyTransformDefaults)
        {
            return;
        }

        lightTransform.localPosition = Vector3.zero;
        lightTransform.localRotation = Quaternion.Euler(BoxShadowLightLocalEuler);
        lightTransform.localScale = Vector3.one;
    }

    private static LiveToonBoxShadowLight FindOrAddNamedBoxShadowLight(Transform parent)
    {
        if (parent == null)
        {
            return null;
        }

        var namedChild = parent.Find(BoxShadowLightObjectName);
        if (namedChild != null)
        {
            if (namedChild.TryGetComponent<LiveToonBoxShadowLight>(out var namedLight))
            {
                return namedLight;
            }

            return namedChild.gameObject.AddComponent<LiveToonBoxShadowLight>();
        }

        var lights = parent.GetComponentsInChildren<LiveToonBoxShadowLight>(true);
        foreach (var light in lights)
        {
            if (light != null && light.gameObject.name == BoxShadowLightObjectName)
            {
                return light;
            }
        }

        return null;
    }

    private static Transform FindHumanoidHead(Animator animator)
    {
        if (animator == null || !animator.isHuman)
        {
            return null;
        }

        return animator.GetBoneTransform(HumanBodyBones.Head);
    }
}
