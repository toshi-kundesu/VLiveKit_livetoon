using UnityEngine;

public enum LiveToonShaderConversionSource
{
    MToon = 0,
    MMD4Mecanim = 1
}

[DisallowMultipleComponent]
[AddComponentMenu("VLiveKit/LiveToon/Shader Converter")]
public sealed class LiveToonShaderConverter : MonoBehaviour
{
    public const string DefaultShaderName = "toshi/VLiveKit/livetoon";

    [SerializeField]
    private GameObject targetObject;

    [SerializeField]
    private Shader shaderToUse;

    [SerializeField]
    private LiveToonShaderConversionSource conversionSource;

    [SerializeField]
    private bool createMaterialBackups;

    [SerializeField]
    private bool disableOutlineOnConvert;

    public GameObject TargetObject => targetObject != null ? targetObject : gameObject;

    public Shader ShaderToUse => shaderToUse != null ? shaderToUse : Shader.Find(DefaultShaderName);

    public LiveToonShaderConversionSource ConversionSource => conversionSource;

    public bool CreateMaterialBackups => createMaterialBackups;

    public bool DisableOutlineOnConvert => disableOutlineOnConvert;

    private void Reset()
    {
        targetObject = gameObject;
        shaderToUse = Shader.Find(DefaultShaderName);
        conversionSource = LiveToonShaderConversionSource.MToon;
    }
}
