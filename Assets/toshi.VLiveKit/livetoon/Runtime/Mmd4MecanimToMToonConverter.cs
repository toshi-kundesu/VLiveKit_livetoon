using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("VLiveKit/LiveToon/MMD4Mecanim To MToon Converter")]
public sealed class Mmd4MecanimToMToonConverter : MonoBehaviour
{
    public const string DefaultShaderName = "VRM/MToon";

    [SerializeField]
    private GameObject targetObject;

    [SerializeField]
    private Shader shaderToUse;

    [SerializeField]
    private bool includeInactive = true;

    [SerializeField]
    private bool preserveMmdRenderQueue = true;

    [SerializeField]
    private bool overwriteExistingCopies = true;

    [SerializeField]
    private bool disableOutlineOnConvert;

    public GameObject TargetObject => targetObject != null ? targetObject : gameObject;

    public Shader ShaderToUse => shaderToUse != null ? shaderToUse : Shader.Find(DefaultShaderName);

    public bool IncludeInactive => includeInactive;

    public bool PreserveMmdRenderQueue => preserveMmdRenderQueue;

    public bool OverwriteExistingCopies => overwriteExistingCopies;

    public bool DisableOutlineOnConvert => disableOutlineOnConvert;

    private void Reset()
    {
        targetObject = gameObject;
        shaderToUse = Shader.Find(DefaultShaderName);
        includeInactive = true;
        preserveMmdRenderQueue = true;
        overwriteExistingCopies = true;
    }
}
