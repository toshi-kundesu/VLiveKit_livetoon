// VLiveKit is all Unlicense.
// unlicense: https://unlicense.org/
// this comment & namespace can be removed. you can use this code freely.
// last update: 2025/05/14

using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class ShaderGlobalValueSetter : MonoBehaviour
{
    public List<FloatProperty> floatProperties = new();
    public List<VectorProperty> vectorProperties = new();
    public List<ColorProperty> colorProperties = new();
    public List<TextureProperty> textureProperties = new();

    [System.Serializable]
    public class FloatProperty
    {
        public string propertyName = "_MyFloat";
        public float value = 0f;
    }

    [System.Serializable]
    public class VectorProperty
    {
        public string propertyName = "_MyVector";
        public Vector4 value = Vector4.zero;
    }

    [System.Serializable]
    public class ColorProperty
    {
        public string propertyName = "_MyColor";
        public Color value = Color.white;
    }

    [System.Serializable]
    public class TextureProperty
    {
        public string propertyName = "_MyTexture";
        public Texture value = null;
    }

    void Update()
    {
        foreach (var f in floatProperties)
        {
            if (!string.IsNullOrEmpty(f.propertyName))
                Shader.SetGlobalFloat(f.propertyName, f.value);
        }

        foreach (var v in vectorProperties)
        {
            if (!string.IsNullOrEmpty(v.propertyName))
                Shader.SetGlobalVector(v.propertyName, v.value);
        }

        foreach (var c in colorProperties)
        {
            if (!string.IsNullOrEmpty(c.propertyName))
                Shader.SetGlobalColor(c.propertyName, c.value);
        }

        foreach (var t in textureProperties)
        {
            if (!string.IsNullOrEmpty(t.propertyName) && t.value != null)
                Shader.SetGlobalTexture(t.propertyName, t.value);
        }
    }
}
