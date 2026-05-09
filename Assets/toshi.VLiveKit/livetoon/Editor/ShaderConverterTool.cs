using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VLiveKit.LiveToon.Editor
{
    public sealed class ShaderConverterTool : EditorWindow
    {
        private const string DefaultShaderName = "toshi/VLiveKit/livetoon";
        private const string BackupDirectoryName = "LiveToonMaterialBackups";
        private const string BackupSuffix = "_LiveToonBackup";
        private static readonly float BlendZero = (float)UnityEngine.Rendering.BlendMode.Zero;
        private static readonly float BlendOne = (float)UnityEngine.Rendering.BlendMode.One;
        private static readonly float BlendSrcAlpha = (float)UnityEngine.Rendering.BlendMode.SrcAlpha;
        private static readonly float BlendOneMinusSrcAlpha = (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
        private static readonly float ZTestLessEqual = (float)UnityEngine.Rendering.CompareFunction.LessEqual;

        private GameObject selectedObject;
        private Shader shaderToUse;
        private bool createMaterialBackups = true;
        private bool skipConvertedMaterials = true;
        private bool disableOutlineOnConvert = true;

        [MenuItem("toshi/VLiveKit/LiveToon/Shader Converter")]
        public static void ShowWindow()
        {
            GetWindow<ShaderConverterTool>("LiveToon Shader Converter");
        }

        private void OnEnable()
        {
            if (shaderToUse == null)
            {
                shaderToUse = Shader.Find(DefaultShaderName);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Base Settings", EditorStyles.boldLabel);
            selectedObject = (GameObject)EditorGUILayout.ObjectField("Selected Object", selectedObject, typeof(GameObject), true);
            shaderToUse = (Shader)EditorGUILayout.ObjectField("Shader To Use", shaderToUse, typeof(Shader), false);

            EditorGUILayout.Space();
            createMaterialBackups = EditorGUILayout.ToggleLeft("Create material backups before converting", createMaterialBackups);
            skipConvertedMaterials = EditorGUILayout.ToggleLeft("Skip materials already using the target shader", skipConvertedMaterials);
            disableOutlineOnConvert = EditorGUILayout.ToggleLeft("Disable outline while converting", disableOutlineOnConvert);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(selectedObject == null || shaderToUse == null))
            {
                if (GUILayout.Button("Convert Shaders"))
                {
                    ConvertShadersForSelectedModel();
                }
            }

            using (new EditorGUI.DisabledScope(selectedObject == null))
            {
                if (GUILayout.Button("Restore Materials From Backups"))
                {
                    RestoreMaterialsFromBackups();
                }
            }
        }

        private void ConvertShadersForSelectedModel()
        {
            if (selectedObject == null)
            {
                ShowNotification(new GUIContent("Select a model first."));
                return;
            }

            if (shaderToUse == null)
            {
                ShowNotification(new GUIContent("Select a target shader first."));
                return;
            }

            var convertedCount = 0;
            var skippedCount = 0;
            var backupCount = 0;
            var materials = CollectUniqueMaterials(selectedObject);

            foreach (var material in materials)
            {
                if (material == null)
                {
                    continue;
                }

                if (skipConvertedMaterials && material.shader == shaderToUse)
                {
                    Undo.RecordObject(material, "Repair LiveToon Render Mode");
                    ApplyRenderModeState(material, GetFloat(material, "_BlendMode", 0f));

                    if (disableOutlineOnConvert)
                    {
                        DisableOutline(material);
                    }

                    EditorUtility.SetDirty(material);
                    skippedCount++;
                    continue;
                }

                if (createMaterialBackups && TryCreateMaterialBackup(material))
                {
                    backupCount++;
                }

                Undo.RecordObject(material, "Convert LiveToon Shader");
                ConvertMaterialLikeLoadModel(material);
                if (disableOutlineOnConvert)
                {
                    DisableOutline(material);
                }

                EditorUtility.SetDirty(material);
                convertedCount++;
            }

            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent($"Converted {convertedCount}, skipped {skippedCount}"));
            Debug.Log($"LiveToon shader conversion complete. Converted: {convertedCount}, skipped: {skippedCount}, backups: {backupCount}");
        }

        private void RestoreMaterialsFromBackups()
        {
            if (selectedObject == null)
            {
                ShowNotification(new GUIContent("Select a model first."));
                return;
            }

            var restoredCount = 0;
            var missingCount = 0;
            var materials = CollectUniqueMaterials(selectedObject);

            foreach (var material in materials)
            {
                var backup = FindMaterialBackup(material);
                if (backup == null)
                {
                    missingCount++;
                    continue;
                }

                Undo.RecordObject(material, "Restore LiveToon Material Backup");
                var materialName = material.name;
                EditorUtility.CopySerialized(backup, material);
                material.name = materialName;
                EditorUtility.SetDirty(material);
                restoredCount++;
            }

            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent($"Restored {restoredCount}, missing {missingCount}"));
            Debug.Log($"LiveToon material backup restore complete. Restored: {restoredCount}, missing backups: {missingCount}");
        }

        private void ConvertMaterialLikeLoadModel(Material material)
        {
            var blendMode = GetFloat(material, "_BlendMode", 0f);
            var color = GetColor(material, "_Color", Color.white);
            var mainTexture = GetTexture(material, "_MainTex");
            var shadeTexture = GetTexture(material, "_ShadeTexture");

            material.shader = shaderToUse;

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (mainTexture != null && shadeTexture == null && material.HasProperty("_ShadeTexture"))
            {
                material.SetTexture("_ShadeTexture", mainTexture);
            }

            if (material.HasProperty("_BlendMode"))
            {
                material.SetFloat("_BlendMode", blendMode);
            }

            ApplyRenderModeState(material, blendMode);
        }

        private static void ApplyRenderModeState(Material material, float blendMode)
        {
            switch ((int)blendMode)
            {
                case 0:
                    material.SetOverrideTag("RenderType", "");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    SetRenderStateFloats(material, BlendOne, BlendZero, zWrite: 1f, alphaToMask: 0f);
                    SetFloatIfPresent(material, "_ZTeForLiOpa", ZTestLessEqual);
                    material.renderQueue = 2225;
                    break;
                case 1:
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    SetRenderStateFloats(material, BlendOne, BlendZero, zWrite: 1f, alphaToMask: 0f);
                    SetFloatIfPresent(material, "_ZTeForLiOpa", ZTestLessEqual);
                    material.renderQueue = 2450;
                    break;
                case 3:
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    SetRenderStateFloats(material, BlendSrcAlpha, BlendOneMinusSrcAlpha, zWrite: 1f, alphaToMask: 0f);
                    SetFloatIfPresent(material, "_ZTeForLiOpa", ZTestLessEqual);
                    material.renderQueue = 3000;
                    break;
                default:
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    SetRenderStateFloats(material, BlendSrcAlpha, BlendOneMinusSrcAlpha, zWrite: 0f, alphaToMask: 0f);
                    SetFloatIfPresent(material, "_ZTeForLiOpa", ZTestLessEqual);
                    material.renderQueue = 3000;
                    break;
            }
        }

        private static void SetRenderStateFloats(Material material, float srcBlend, float dstBlend, float zWrite, float alphaToMask)
        {
            SetFloatIfPresent(material, "_SrcBlend", srcBlend);
            SetFloatIfPresent(material, "_DstBlend", dstBlend);
            SetFloatIfPresent(material, "_ZWrite", zWrite);
            SetFloatIfPresent(material, "_AlphaToMask", alphaToMask);
        }

        private static void DisableOutline(Material material)
        {
            SetFloatIfPresent(material, "_OutlineWidthMode", 0f);
            SetFloatIfPresent(material, "_OutlineWidth", 0f);
            material.DisableKeyword("MTOON_OUTLINE_WIDTH_WORLD");
            material.DisableKeyword("MTOON_OUTLINE_WIDTH_SCREEN");
        }

        private static List<Material> CollectUniqueMaterials(GameObject root)
        {
            var result = new List<Material>();
            var seen = new HashSet<Material>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null && seen.Add(material))
                    {
                        result.Add(material);
                    }
                }
            }

            return result;
        }

        private static float GetFloat(Material material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static Texture GetTexture(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
        }

        private static Color GetColor(Material material, string propertyName, Color fallback)
        {
            return material.HasProperty(propertyName) ? material.GetColor(propertyName) : fallback;
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static bool TryCreateMaterialBackup(Material material)
        {
            var assetPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            var extension = Path.GetExtension(assetPath);
            if (extension != ".mat" && extension != ".asset")
            {
                return false;
            }

            var backupDirectory = $"{directory}/{BackupDirectoryName}";
            EnsureFolder(backupDirectory);

            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            var backupPath = $"{backupDirectory}/{fileName}{BackupSuffix}{extension}";
            if (AssetDatabase.LoadAssetAtPath<Material>(backupPath) != null)
            {
                return false;
            }

            return AssetDatabase.CopyAsset(assetPath, backupPath);
        }

        private static Material FindMaterialBackup(Material material)
        {
            var assetPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var extension = Path.GetExtension(assetPath);
            if (extension != ".mat" && extension != ".asset")
            {
                return null;
            }

            var directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            var backupDirectory = $"{directory}/{BackupDirectoryName}";
            var backupPath = $"{backupDirectory}/{fileName}{BackupSuffix}{extension}";
            var backup = AssetDatabase.LoadAssetAtPath<Material>(backupPath);
            if (backup != null)
            {
                return backup;
            }

            return FindMostRecentLegacyBackup(backupDirectory, fileName, extension);
        }

        private static Material FindMostRecentLegacyBackup(string backupDirectory, string fileName, string extension)
        {
            var absoluteDirectory = ToProjectAbsolutePath(backupDirectory);
            if (string.IsNullOrEmpty(absoluteDirectory) || !Directory.Exists(absoluteDirectory))
            {
                return null;
            }

            var pattern = $"{fileName}_backup*{extension}";
            var backupFiles = Directory.GetFiles(absoluteDirectory, pattern, SearchOption.TopDirectoryOnly);
            if (backupFiles.Length == 0)
            {
                return null;
            }

            var newestPath = backupFiles[0];
            var newestWriteTime = File.GetLastWriteTimeUtc(newestPath);
            for (var i = 1; i < backupFiles.Length; i++)
            {
                var writeTime = File.GetLastWriteTimeUtc(backupFiles[i]);
                if (writeTime > newestWriteTime)
                {
                    newestPath = backupFiles[i];
                    newestWriteTime = writeTime;
                }
            }

            var assetPath = ToProjectRelativePath(newestPath);
            return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        }

        private static string ToProjectAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return null;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            projectRoot += Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(absolutePath);
            if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return fullPath.Substring(projectRoot.Length).Replace('\\', '/');
        }

        private static void EnsureFolder(string folderPath)
        {
            var parts = folderPath.Split('/');
            if (parts.Length == 0)
            {
                return;
            }

            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
