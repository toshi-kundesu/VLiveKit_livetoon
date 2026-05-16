using UnityEditor;
using UnityEngine;

namespace VLiveKit.LiveToon.Editor
{
    internal static class LiveToonDefaultAssets
    {
        internal const string JitterTexturePropertyName = "_JitterTex";

        private const string DefaultJitterTextureGuid = "71402ec0b7cb2da439b4f350f95b2254";
        private const string DefaultJitterTexturePackagePath = "Packages/com.toshi.vlivekit.livetoon/Shader/jitter.png";
        private const string DefaultJitterTextureDevelopmentPath = "Packages/VLiveKit_LiveToon/Assets/toshi.VLiveKit/livetoon/Shader/jitter.png";

        private static Texture2D defaultJitterTexture;

        internal static bool NeedsDefaultJitterTexture(Material material)
        {
            return material != null
                && material.HasProperty(JitterTexturePropertyName)
                && material.GetTexture(JitterTexturePropertyName) == null;
        }

        internal static bool EnsureDefaultJitterTexture(Material material)
        {
            if (!NeedsDefaultJitterTexture(material))
            {
                return false;
            }

            var texture = LoadDefaultJitterTexture();
            if (texture == null)
            {
                return false;
            }

            material.SetTexture(JitterTexturePropertyName, texture);
            return true;
        }

        internal static Texture2D LoadDefaultJitterTexture()
        {
            if (defaultJitterTexture != null)
            {
                return defaultJitterTexture;
            }

            defaultJitterTexture = LoadTextureAtPath(AssetDatabase.GUIDToAssetPath(DefaultJitterTextureGuid));
            if (defaultJitterTexture != null)
            {
                return defaultJitterTexture;
            }

            defaultJitterTexture = LoadTextureAtPath(DefaultJitterTexturePackagePath);
            if (defaultJitterTexture != null)
            {
                return defaultJitterTexture;
            }

            defaultJitterTexture = LoadTextureAtPath(DefaultJitterTextureDevelopmentPath);
            return defaultJitterTexture;
        }

        private static Texture2D LoadTextureAtPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
