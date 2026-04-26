using UnityEngine;
using UnityEditor;

public class ShaderConverterTool : EditorWindow
{
    private GameObject selectedObject;
    private Shader shaderToUse;

    [MenuItem("Tools/Shader Converter Tool")]
    public static void ShowWindow()
    {
        GetWindow<ShaderConverterTool>("Shader Converter");
    }

    void OnGUI()
    {
        GUILayout.Label("Base Settings", EditorStyles.boldLabel);
        selectedObject = (GameObject)EditorGUILayout.ObjectField("Selected Object", selectedObject, typeof(GameObject), true);
        shaderToUse = (Shader)EditorGUILayout.ObjectField("Shader to Use", shaderToUse, typeof(Shader), false);

        if (GUILayout.Button("Convert Shaders"))
        {
            ConvertShadersForSelectedModel();
        }
    }

    private void ConvertShadersForSelectedModel()
    {
        if (selectedObject == null)
        {
            Debug.LogError("No object selected. Please select a model first.");
            return;
        }

        Renderer[] renderers = selectedObject.GetComponentsInChildren<Renderer>();
        int testInt = 0;

        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                #region change shader
                if (material == null) continue;

                // materialのshaderの名前がshaderToUseの名前と一致していたら処理をスキップ
                if (material.shader.name == shaderToUse.name)
                {
                    continue;
                }


             
                Debug.Log("Before:" + material.shader.name);
      
                // Get MToon Prperties
                #region Get MToon Properties
                float _tmpBlendMode = 1.0f;
                if (material.HasProperty("_BlendMode"))
                {
                    _tmpBlendMode = material.GetFloat("_BlendMode");
                }
                int _tmpCullMode = 2;
                if (material.HasProperty("_CullMode"))
                {
                    _tmpCullMode = material.GetInt("_CullMode");
                }
                bool isAlphaTestOn = material.IsKeywordEnabled("_ALPHATEST_ON");
                #endregion // Get MToon Properties


                material.shader = shaderToUse;

             
                material.SetFloat("_SphereNormalIntensity", 0.1f);
                material.SetFloat("_RimIntensity", 0.0f);
                string renderTypeDebugText = "";
                // }

                #region Opaque
                // shaderにtransparentという名前が含まれていなかったら、opaqueとして扱う
                if (_tmpBlendMode == 0.0f || !material.shader.name.Contains("transparent"))
                {
                    renderTypeDebugText = "Opaque";
                    material.SetInt("_CullMode", _tmpCullMode);
                    material.SetInt("_OpaqueCullMode", _tmpCullMode);
                    material.SetInt("_CullModeForward", _tmpCullMode);
              
                    material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.DisableKeyword("_ALPHATEST_ON"); // ok
                    material.renderQueue = 2000; // ok
                    material.SetOverrideTag("RenderType", "");
                    // material.SetInt("_AlphaCutofffEnable", 0);
                    material.SetInt("_AlphaCutofffEnable", 0); // ok
                    material.SetInt("_AlphaDstBlend", 10); // ok
                    material.SetInt("_DstBlend", 0);
                    material.SetInt("_RenderQueueType", 1);
                    material.SetInt("_SurfaceType", 0);
                    material.SetInt("_ZTestDepthEqualForOpaque", 3);
                    material.SetInt("_ZWrite", 1);
                    
                    testInt += 1;


                        
                }
                #endregion

                #region Cutout
                if (_tmpBlendMode == 1.0f)
                {
                    renderTypeDebugText = "Cutout";
                    testInt += 1;
                    
                    material.SetInt("_CullMode", _tmpCullMode);
                    material.SetInt("_OpaqueCullMode", _tmpCullMode);
                    material.SetInt("_CullModeForward", _tmpCullMode);
        
            
                    material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.EnableKeyword("_ALPHATEST_ON"); // ok
                    material.renderQueue = 2450; // ok
                    material.SetOverrideTag("RenderType", "TransparentCutout"); // ok
                    material.SetInt("_AlphaCutofffEnable", 1); // ok
                    material.SetInt("_AlphaDstBlend", 0); // ok
                    material.SetInt("_DstBlend", 0);
                    material.SetInt("_RenderQueueType", 1);
                    material.SetInt("_SurfaceType", 0);
                    material.SetInt("_ZTestDepthEqualForOpaque", 3);
                    material.SetInt("_ZWrite", 1);

                }
                #endregion

                #region Transparent
                if (_tmpBlendMode == 2.0f || material.shader.name.Contains("transparent"))
                {
                    renderTypeDebugText = "Transparent"; 
                    testInt += 1;
                    material.SetFloat("_CutOff", 0);
                    material.SetOverrideTag("RenderType", "Transparent"); // ok
                    material.SetInt("_CullMode", _tmpCullMode);
                    material.SetInt("_OpaqueCullMode", _tmpCullMode);
                    material.SetInt("_CullModeForward", _tmpCullMode);
                    // material.SetInt("_CullMode", _tmpCullMode);
                    // means alphaCutOffEnable
                    bool utilSwitch = false;
                    material.SetInt("_BlendMode", 0);
                    material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT"); // ok
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); // ok
                    // 分岐
                    if (utilSwitch)
                    {
                        material.EnableKeyword("_ALPHATEST_ON");
                    }
                    else
                    {
                        material.DisableKeyword("_ALPHATEST_ON");
                    }

                    material.EnableKeyword("_ALPHATEST_ON");
          
                    material.renderQueue = 3000; // ok
                    
      
                    material.SetInt("_AlphaCutofffEnable", 0); // ok

                    material.SetInt("_AlphaDstBlend", 10); // ok
                    material.SetInt("_DstBlend", 10);
                    material.SetInt("_RenderQueueType", 4);
                    material.SetInt("_SurfaceType", 1);
                    material.SetInt("_ZTestDepthEqualForOpaque", 4);
                    material.SetInt("_ZWrite", 0);

                 

                    
                }
                #endregion

                if (_tmpBlendMode == 3.0f)
                {
                    renderTypeDebugText = "TransparentWithZWrite"; 
                    testInt += 1;
                    material.SetFloat("_CutOff", 0);
                    material.SetOverrideTag("RenderType", "Transparent"); // ok
                    material.SetInt("_CullMode", _tmpCullMode);
                    material.SetInt("_OpaqueCullMode", _tmpCullMode);
                    material.SetInt("_CullModeForward", _tmpCullMode);
                    // material.SetInt("_CullMode", _tmpCullMode);
                    // means alphaCutOffEnable
                    bool utilSwitch = false;
                    material.SetInt("_BlendMode", 0);
                    material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT"); // ok
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); // ok
                    // 分岐
                    if (utilSwitch)
                    {
                        material.EnableKeyword("_ALPHATEST_ON");
                    }
                    else
                    {
                        material.DisableKeyword("_ALPHATEST_ON");
                    }

                    material.EnableKeyword("_ALPHATEST_ON");
          
                    material.renderQueue = 3000; // ok

                    material.SetInt("_AlphaCutofffEnable", 0); // ok

                    material.SetInt("_AlphaDstBlend", 10); // ok
                    material.SetInt("_DstBlend", 10);
                    material.SetInt("_RenderQueueType", 4);
                    material.SetInt("_SurfaceType", 1);
                    material.SetInt("_ZTestDepthEqualForOpaque", 4);
                    material.SetInt("_ZWrite", 1);

                 

                    
                }

                #endregion // change shader
            }
        }
        Debug.Log("Shaders converted for selected model.");
    }
}
