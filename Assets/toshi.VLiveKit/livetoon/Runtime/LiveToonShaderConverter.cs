using UnityEngine;

public enum LiveToonShaderConversionSource
{
    MToon = 0,
    MMD4Mecanim = 1,
    OfficialHDRPMMD = 2
}

public enum MmdTransparentFogMode
{
    PreserveMmdQueue = 0,
    HdrpMmdStackRangeWithSurfaceFog = 1,
    PreserveMmdQueueNoSurfaceFog = 2,
    PreserveMmdQueueWithSurfaceFog = 3,
    HdrpTransparentRangeNoSurfaceFog = 4,
    HdrpTransparentRangeWithSurfaceFog = 5,
    HdrpMmdStackRangeNoSurfaceFog = 6
}

[DisallowMultipleComponent]
[AddComponentMenu("VLiveKit/LiveToon/Shader Converter")]
public sealed class LiveToonShaderConverter : MonoBehaviour
{
    public const string DefaultShaderName = "toshi/VLiveKit/livetoon";
    public const string OfficialHdrpMmdShaderName = "MMD4Mecanim/HDRP/MMDLit";

    [SerializeField]
    private GameObject targetObject;

    [SerializeField]
    private Shader shaderToUse;

    [SerializeField]
    private LiveToonShaderConversionSource conversionSource;

    [SerializeField]
    private MmdTransparentFogMode mmdTransparentFogMode = MmdTransparentFogMode.HdrpMmdStackRangeWithSurfaceFog;

    [SerializeField]
    private bool createMaterialBackups;

    [SerializeField]
    private bool disableOutlineOnConvert;

    public GameObject TargetObject => targetObject != null ? targetObject : gameObject;

    public Shader ShaderToUse => conversionSource == LiveToonShaderConversionSource.OfficialHDRPMMD
        ? Shader.Find(OfficialHdrpMmdShaderName)
        : shaderToUse != null
            ? shaderToUse
            : Shader.Find(DefaultShaderName);

    public LiveToonShaderConversionSource ConversionSource => conversionSource;

    public MmdTransparentFogMode MmdTransparentFogMode => mmdTransparentFogMode;

    public bool CreateMaterialBackups => createMaterialBackups;

    public bool DisableOutlineOnConvert => disableOutlineOnConvert;

    private void Reset()
    {
        targetObject = gameObject;
        shaderToUse = Shader.Find(DefaultShaderName);
        conversionSource = LiveToonShaderConversionSource.MToon;
        mmdTransparentFogMode = MmdTransparentFogMode.HdrpMmdStackRangeWithSurfaceFog;
    }
}
