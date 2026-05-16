using UnityEngine;

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
    private bool createMaterialBackups;

    [SerializeField]
    private bool disableOutlineOnConvert;

    public GameObject TargetObject => targetObject != null ? targetObject : gameObject;

    public Shader ShaderToUse => shaderToUse != null ? shaderToUse : Shader.Find(DefaultShaderName);

    public bool CreateMaterialBackups => createMaterialBackups;

    public bool DisableOutlineOnConvert => disableOutlineOnConvert;

    private void Reset()
    {
        targetObject = gameObject;
        shaderToUse = Shader.Find(DefaultShaderName);
    }
}
