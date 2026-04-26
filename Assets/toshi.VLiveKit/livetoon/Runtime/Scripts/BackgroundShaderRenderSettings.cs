using UnityEngine;

[ExecuteInEditMode]
public class BackgroundShaderRenderSettings : MonoBehaviour
{
    // This script controls the Cyclo & the Grid Color. Simple Global variables are exposed in the Shaders and tweaked here.


    [Header("BACKGROUND")]
    public Vector3 headPosition = new Vector3(0, 0, 0);
    public Color _TopColor = Color.grey;
    public Color _BottomColor = Color.grey;
    [Range(-2, 2)]
    public float _HorizonOrigin = 0;
    [Range((float)0.1, 2)]
    public float _GradiantSpread = (float)0.1;

    public GameObject targetHeadPosition;
    //[Space(20)]
    // [Header("GRID COLOR")]
    // public Color _GridColor = Color.grey;

    public void OnUpdateRenderSettings()
    {
        SetRender();
    }

    private void Awake()
    {
        SetRender();
    }

    private void OnEnable()
    {
        SetRender();
    }

    private void OnValidate()
    {
        SetRender();
    }

    void Update()
    {
        if (targetHeadPosition != null)
        {
            // warld position
            headPosition = targetHeadPosition.transform.position;
        }
        SetRender();
    }

    private void SetRender()
    {
        Shader.SetGlobalVector("_HeadPosition", headPosition);
        Shader.SetGlobalColor("_TopColor", _TopColor);
        Shader.SetGlobalColor("_BottomColor", _BottomColor);
        Shader.SetGlobalFloat("_HorizonOrigin", _HorizonOrigin);
        Shader.SetGlobalFloat("_GradiantSpread", _GradiantSpread);
        // Shader.SetGlobalColor("_GridColor", _GridColor);
    }
}
