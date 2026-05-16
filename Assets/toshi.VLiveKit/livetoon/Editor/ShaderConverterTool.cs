using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VLiveKit.LiveToon.Editor
{
    internal struct LiveToonMaterialConversionResult
    {
        public int ConvertedCount;
        public int AssignedCount;
        public int SkippedCount;
        public int BackupCount;
        public int RestoredCount;
        public int MissingCount;

        public string ToConversionLog()
        {
            return $"LiveToon shader conversion complete. Created copies: {ConvertedCount}, assigned slots: {AssignedCount}, skipped: {SkippedCount}, backups: {BackupCount}";
        }

        public string ToOriginalAssignmentRestoreLog()
        {
            return $"LiveToon original material assignment restore complete. Restored slots: {RestoredCount}, missing originals: {MissingCount}";
        }

        public string ToLegacyBackupRestoreLog()
        {
            return $"LiveToon material backup restore complete. Restored: {RestoredCount}, missing backups: {MissingCount}";
        }
    }

    internal struct LiveToonOutlineMaterialState
    {
        public float WidthMode;
        public float ColorMode;
        public float Width;
        public float ScaledMaxDistance;
        public float LightingMix;
        public float CullMode;
        public Texture WidthTexture;
        public Color Color;
    }

    public sealed class ShaderConverterTool : EditorWindow
    {
        private const string DefaultShaderName = LiveToonShaderConverter.DefaultShaderName;
        private const string BackupDirectoryName = "LiveToonMaterialBackups";
        private const string BackupSuffix = "_LiveToonBackup";
        private const string ConvertedDirectoryName = "LiveToonMaterials";
        private const string ConvertedSuffix = "_LiveToon";
        private const string GeneratedMaterialsDirectory = "Assets/VLiveKitGenerated/LiveToonMaterials";
        private const string SourceMaterialPathPrefix = "VLiveKit.LiveToon.SourceMaterialPath=";
        private const string TransparentDepthPrepassName = "TransparentDepthPrepass";
        private const string TransparentDepthPostpassName = "TransparentDepthPostpass";
        private const string KeyOutlineWidthWorld = "MTOON_OUTLINE_WIDTH_WORLD";
        private const string KeyOutlineWidthScreen = "MTOON_OUTLINE_WIDTH_SCREEN";
        private const string KeyOutlineColorFixed = "MTOON_OUTLINE_COLOR_FIXED";
        private const string KeyOutlineColorMixed = "MTOON_OUTLINE_COLOR_MIXED";
        private const int TransparentWithZWriteQueue = 2501;
        private const float DefaultIndirectLightIntensity = 0.35f;
        private const float DefaultReflectionProbeIntensity = 0.25f;
        private const float DefaultReflectionProbeSmoothness = 0.35f;
        private static readonly float BlendZero = (float)UnityEngine.Rendering.BlendMode.Zero;
        private static readonly float BlendOne = (float)UnityEngine.Rendering.BlendMode.One;
        private static readonly float BlendSrcAlpha = (float)UnityEngine.Rendering.BlendMode.SrcAlpha;
        private static readonly float BlendOneMinusSrcAlpha = (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
        private static readonly float ZTestLessEqual = (float)UnityEngine.Rendering.CompareFunction.LessEqual;

        private GameObject selectedObject;
        private Shader shaderToUse;
        private bool createMaterialBackups;
        private bool disableOutlineOnConvert;

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
            createMaterialBackups = EditorGUILayout.ToggleLeft("Also create legacy backup copies", createMaterialBackups);
            disableOutlineOnConvert = EditorGUILayout.ToggleLeft("Disable outline after converting", disableOutlineOnConvert);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(selectedObject == null || shaderToUse == null))
            {
                if (GUILayout.Button("Convert And Assign Material Copies"))
                {
                    ConvertShadersForSelectedModel();
                }
            }

            using (new EditorGUI.DisabledScope(selectedObject == null))
            {
                if (GUILayout.Button("Restore Original Material Assignments"))
                {
                    RestoreOriginalMaterialAssignments();
                }

                if (GUILayout.Button("Restore Legacy Material Assets From Backups"))
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

            var result = ConvertShadersForObject(selectedObject, shaderToUse, createMaterialBackups, disableOutlineOnConvert);
            ShowNotification(new GUIContent($"Converted {result.ConvertedCount}, assigned {result.AssignedCount}"));
            Debug.Log(result.ToConversionLog());
        }

        private void RestoreOriginalMaterialAssignments()
        {
            if (selectedObject == null)
            {
                ShowNotification(new GUIContent("Select a model first."));
                return;
            }

            var result = RestoreOriginalMaterialAssignments(selectedObject);
            ShowNotification(new GUIContent($"Restored assignments {result.RestoredCount}, missing {result.MissingCount}"));
            Debug.Log(result.ToOriginalAssignmentRestoreLog());
        }

        private void RestoreMaterialsFromBackups()
        {
            if (selectedObject == null)
            {
                ShowNotification(new GUIContent("Select a model first."));
                return;
            }

            var result = RestoreMaterialAssetsFromBackups(selectedObject);
            ShowNotification(new GUIContent($"Restored {result.RestoredCount}, missing {result.MissingCount}"));
            Debug.Log(result.ToLegacyBackupRestoreLog());
        }

        internal static LiveToonMaterialConversionResult ConvertShadersForObject(
            GameObject root,
            Shader targetShader,
            bool createMaterialBackups,
            bool disableOutlineOnConvert)
        {
            var result = new LiveToonMaterialConversionResult();
            if (root == null || targetShader == null)
            {
                return result;
            }

            var conversionMap = new Dictionary<Material, Material>();
            var skippedMaterials = new HashSet<Material>();
            var backupMaterials = new HashSet<Material>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                var sharedMaterials = renderer.sharedMaterials;
                var changed = false;

                for (var materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    var materialInSlot = sharedMaterials[materialIndex];
                    if (materialInSlot == null)
                    {
                        continue;
                    }

                    var originalForConvertedCopy = FindOriginalMaterialForConvertedCopy(materialInSlot);
                    var sourceMaterial = originalForConvertedCopy != null ? originalForConvertedCopy : materialInSlot;

                    if (!conversionMap.TryGetValue(sourceMaterial, out var convertedMaterial))
                    {
                        if (createMaterialBackups && backupMaterials.Add(sourceMaterial) && TryCreateMaterialBackup(sourceMaterial))
                        {
                            result.BackupCount++;
                        }

                        convertedMaterial = CreateConvertedMaterial(sourceMaterial);
                        if (convertedMaterial == null)
                        {
                            if (skippedMaterials.Add(sourceMaterial))
                            {
                                result.SkippedCount++;
                            }

                            continue;
                        }

                        ConvertMaterialLikeLoadModel(convertedMaterial, targetShader);
                        if (disableOutlineOnConvert)
                        {
                            DisableOutline(convertedMaterial);
                        }

                        EditorUtility.SetDirty(convertedMaterial);
                        conversionMap[sourceMaterial] = convertedMaterial;
                        result.ConvertedCount++;
                    }

                    if (sharedMaterials[materialIndex] != convertedMaterial)
                    {
                        sharedMaterials[materialIndex] = convertedMaterial;
                        changed = true;
                        result.AssignedCount++;
                    }
                }

                if (changed)
                {
                    Undo.RecordObject(renderer, "Assign LiveToon Material Copies");
                    renderer.sharedMaterials = sharedMaterials;
                    EditorUtility.SetDirty(renderer);
                }
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        internal static LiveToonMaterialConversionResult RestoreOriginalMaterialAssignments(GameObject root)
        {
            var result = new LiveToonMaterialConversionResult();
            if (root == null)
            {
                return result;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                var sharedMaterials = renderer.sharedMaterials;
                var changed = false;

                for (var materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    var material = sharedMaterials[materialIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    var originalMaterial = FindOriginalMaterialForConvertedCopy(material);
                    if (originalMaterial == null)
                    {
                        if (IsConvertedMaterial(material))
                        {
                            result.MissingCount++;
                        }

                        continue;
                    }

                    sharedMaterials[materialIndex] = originalMaterial;
                    changed = true;
                    result.RestoredCount++;
                }

                if (changed)
                {
                    Undo.RecordObject(renderer, "Restore Original Material Assignments");
                    renderer.sharedMaterials = sharedMaterials;
                    EditorUtility.SetDirty(renderer);
                }
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        internal static LiveToonMaterialConversionResult RestoreMaterialAssetsFromBackups(GameObject root)
        {
            var result = new LiveToonMaterialConversionResult();
            if (root == null)
            {
                return result;
            }

            var materials = CollectUniqueMaterials(root);

            foreach (var material in materials)
            {
                var backup = FindMaterialBackup(material);
                if (backup == null)
                {
                    result.MissingCount++;
                    continue;
                }

                Undo.RecordObject(material, "Restore LiveToon Material Backup");
                var materialName = material.name;
                EditorUtility.CopySerialized(backup, material);
                material.name = materialName;
                EditorUtility.SetDirty(material);
                result.RestoredCount++;
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        private static void ConvertMaterialLikeLoadModel(Material material, Shader targetShader)
        {
            var blendMode = GetFloat(material, "_BlendMode", 0f);
            var color = GetColor(material, "_Color", Color.white);
            var mainTexture = GetTexture(material, "_MainTex");
            var shadeTexture = GetTexture(material, "_ShadeTexture");
            var outlineState = CaptureOutlineState(material);

            material.shader = targetShader;

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
            RestoreOutlineProperties(material, outlineState);
            ApplyOutlineModeState(material);
            ApplyEnvironmentLightingDefaults(material);
            LiveToonDefaultAssets.EnsureDefaultJitterTexture(material);
        }

        private static void ApplyEnvironmentLightingDefaults(Material material)
        {
            SetFloatIfPresent(material, "_IndirectLightIntensity", DefaultIndirectLightIntensity);
            SetFloatIfPresent(material, "_ReflectionProbeIntensity", DefaultReflectionProbeIntensity);
            SetFloatIfPresent(material, "_ReflectionProbeSmoothness", DefaultReflectionProbeSmoothness);
        }

        private static LiveToonOutlineMaterialState CaptureOutlineState(Material material)
        {
            return new LiveToonOutlineMaterialState
            {
                WidthMode = GetFloat(material, "_OutlineWidthMode", 0f),
                ColorMode = GetFloat(material, "_OutlineColorMode", 0f),
                Width = GetFloat(material, "_OutlineWidth", 0f),
                ScaledMaxDistance = GetFloat(material, "_OutlineScaledMaxDistance", 1f),
                LightingMix = GetFloat(material, "_OutlineLightingMix", 1f),
                CullMode = GetFloat(material, "_OutlineCullMode", 1f),
                WidthTexture = GetTexture(material, "_OutlineWidthTexture"),
                Color = GetColor(material, "_OutlineColor", Color.black)
            };
        }

        private static void RestoreOutlineProperties(Material material, LiveToonOutlineMaterialState outlineState)
        {
            SetFloatIfPresent(material, "_OutlineWidthMode", outlineState.WidthMode);
            SetFloatIfPresent(material, "_OutlineColorMode", outlineState.ColorMode);
            SetFloatIfPresent(material, "_OutlineWidth", outlineState.Width);
            SetFloatIfPresent(material, "_OutlineScaledMaxDistance", outlineState.ScaledMaxDistance);
            SetFloatIfPresent(material, "_OutlineLightingMix", outlineState.LightingMix);
            SetFloatIfPresent(material, "_OutlineCullMode", outlineState.CullMode);

            if (material.HasProperty("_OutlineWidthTexture"))
            {
                material.SetTexture("_OutlineWidthTexture", outlineState.WidthTexture);
            }

            if (material.HasProperty("_OutlineColor"))
            {
                material.SetColor("_OutlineColor", outlineState.Color);
            }
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
                    SetTransparentDepthPasses(material, false);
                    material.renderQueue = 2225;
                    break;
                case 1:
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    SetRenderStateFloats(material, BlendOne, BlendZero, zWrite: 1f, alphaToMask: 0f);
                    SetFloatIfPresent(material, "_ZTeForLiOpa", ZTestLessEqual);
                    SetTransparentDepthPasses(material, false);
                    material.renderQueue = 2450;
                    break;
                case 3:
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    SetRenderStateFloats(material, BlendSrcAlpha, BlendOneMinusSrcAlpha, zWrite: 1f, alphaToMask: 0f);
                    SetFloatIfPresent(material, "_ZTeForLiOpa", ZTestLessEqual);
                    SetTransparentDepthPasses(material, true);
                    material.renderQueue = TransparentWithZWriteQueue;
                    break;
                default:
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    SetRenderStateFloats(material, BlendSrcAlpha, BlendOneMinusSrcAlpha, zWrite: 0f, alphaToMask: 0f);
                    SetFloatIfPresent(material, "_ZTeForLiOpa", ZTestLessEqual);
                    SetTransparentDepthPasses(material, false);
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

        private static void SetTransparentDepthPasses(Material material, bool enabled)
        {
            material.SetShaderPassEnabled(TransparentDepthPrepassName, enabled);
            material.SetShaderPassEnabled(TransparentDepthPostpassName, enabled);
        }

        private static void DisableOutline(Material material)
        {
            SetFloatIfPresent(material, "_OutlineWidthMode", 0f);
            SetFloatIfPresent(material, "_OutlineWidth", 0f);
            material.DisableKeyword(KeyOutlineWidthWorld);
            material.DisableKeyword(KeyOutlineWidthScreen);
            material.DisableKeyword(KeyOutlineColorFixed);
            material.DisableKeyword(KeyOutlineColorMixed);
        }

        private static void ApplyOutlineModeState(Material material)
        {
            var outlineWidthMode = Mathf.RoundToInt(GetFloat(material, "_OutlineWidthMode", 0f));
            var outlineColorMode = Mathf.RoundToInt(GetFloat(material, "_OutlineColorMode", 0f));
            SetFloatIfPresent(material, "_OutlineWidthMode", outlineWidthMode);
            SetFloatIfPresent(material, "_OutlineColorMode", outlineColorMode);

            var usesWorldWidth = outlineWidthMode == 1;
            var usesScreenWidth = outlineWidthMode == 2;
            var usesOutline = usesWorldWidth || usesScreenWidth;
            SetKeyword(material, KeyOutlineWidthWorld, usesWorldWidth);
            SetKeyword(material, KeyOutlineWidthScreen, usesScreenWidth);
            SetKeyword(material, KeyOutlineColorFixed, usesOutline && outlineColorMode == 0);
            SetKeyword(material, KeyOutlineColorMixed, usesOutline && outlineColorMode == 1);
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
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

        private static Material CreateConvertedMaterial(Material sourceMaterial)
        {
            var sourceAssetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(sourceMaterial));
            var convertedPath = GetConvertedMaterialPath(sourceMaterial, sourceAssetPath);
            if (string.IsNullOrEmpty(convertedPath))
            {
                return null;
            }

            convertedPath = AssetDatabase.GenerateUniqueAssetPath(convertedPath);

            var convertedDirectory = NormalizeAssetPath(Path.GetDirectoryName(convertedPath));
            EnsureFolder(convertedDirectory);

            var convertedMaterial = new Material(sourceMaterial)
            {
                name = $"{sourceMaterial.name}{ConvertedSuffix}"
            };

            AssetDatabase.CreateAsset(convertedMaterial, convertedPath);
            Undo.RegisterCreatedObjectUndo(convertedMaterial, "Create LiveToon Material Copy");
            SetConvertedMaterialSourcePath(convertedPath, sourceAssetPath);
            return convertedMaterial;
        }

        private static string GetConvertedMaterialPath(Material sourceMaterial, string sourceAssetPath)
        {
            if (!string.IsNullOrEmpty(sourceAssetPath) && IsConvertedMaterialPath(sourceAssetPath))
            {
                var convertedSourceDirectory = NormalizeAssetPath(Path.GetDirectoryName(sourceAssetPath));
                var convertedSourceName = Path.GetFileNameWithoutExtension(sourceAssetPath);
                var originalLikeName = RemoveConvertedSuffix(convertedSourceName) ?? convertedSourceName;
                var convertedSourceExtension = GetMaterialAssetExtension(sourceAssetPath);
                return $"{convertedSourceDirectory}/{SanitizeAssetFileName(originalLikeName)}{ConvertedSuffix}{convertedSourceExtension}";
            }

            var sourceFileName = string.IsNullOrEmpty(sourceAssetPath)
                ? SanitizeAssetFileName(sourceMaterial.name)
                : SanitizeAssetFileName(Path.GetFileNameWithoutExtension(sourceAssetPath));
            var extension = GetMaterialAssetExtension(sourceAssetPath);
            var convertedFileName = $"{sourceFileName}{ConvertedSuffix}{extension}";

            if (!string.IsNullOrEmpty(sourceAssetPath) && sourceAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var sourceDirectory = NormalizeAssetPath(Path.GetDirectoryName(sourceAssetPath));
                if (!string.IsNullOrEmpty(sourceDirectory))
                {
                    return $"{sourceDirectory}/{ConvertedDirectoryName}/{convertedFileName}";
                }
            }

            var sourceGuid = string.IsNullOrEmpty(sourceAssetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(sourceAssetPath);
            if (!string.IsNullOrEmpty(sourceGuid) && sourceGuid.Length >= 8)
            {
                convertedFileName = $"{sourceFileName}{ConvertedSuffix}_{sourceGuid.Substring(0, 8)}{extension}";
            }

            return $"{GeneratedMaterialsDirectory}/{convertedFileName}";
        }

        private static string GetMaterialAssetExtension(string assetPath)
        {
            var extension = string.IsNullOrEmpty(assetPath) ? ".mat" : Path.GetExtension(assetPath);
            return extension == ".asset" || extension == ".mat" ? extension : ".mat";
        }

        private static Material FindOriginalMaterialForConvertedCopy(Material material)
        {
            var convertedPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(material));
            if (string.IsNullOrEmpty(convertedPath))
            {
                return null;
            }

            var sourcePath = GetConvertedMaterialSourcePath(convertedPath);
            if (!string.IsNullOrEmpty(sourcePath))
            {
                var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
                if (sourceMaterial != null)
                {
                    return sourceMaterial;
                }
            }

            return FindOriginalMaterialByConvertedPath(convertedPath);
        }

        private static Material FindOriginalMaterialByConvertedPath(string convertedPath)
        {
            if (string.IsNullOrEmpty(convertedPath))
            {
                return null;
            }

            if (convertedPath.StartsWith($"{GeneratedMaterialsDirectory}/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var marker = $"/{ConvertedDirectoryName}/";
            var markerIndex = convertedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var convertedFileName = Path.GetFileNameWithoutExtension(convertedPath);
            var sourceFileName = RemoveConvertedSuffix(convertedFileName);
            if (string.IsNullOrEmpty(sourceFileName))
            {
                return null;
            }

            var sourceDirectory = convertedPath.Substring(0, markerIndex);
            var extension = GetMaterialAssetExtension(convertedPath);
            var candidates = new[]
            {
                $"{sourceDirectory}/{sourceFileName}{extension}",
                $"{sourceDirectory}/{sourceFileName}.asset",
                $"{sourceDirectory}/{sourceFileName}.mat"
            };
            var seen = new HashSet<string>();

            foreach (var candidate in candidates)
            {
                if (!seen.Add(candidate))
                {
                    continue;
                }

                var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(candidate);
                if (sourceMaterial != null)
                {
                    return sourceMaterial;
                }
            }

            return null;
        }

        private static string RemoveConvertedSuffix(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            if (fileName.EndsWith(ConvertedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - ConvertedSuffix.Length);
            }

            var generatedMarker = $"{ConvertedSuffix}_";
            var markerIndex = fileName.LastIndexOf(generatedMarker, StringComparison.OrdinalIgnoreCase);
            return markerIndex >= 0 ? fileName.Substring(0, markerIndex) : null;
        }

        private static bool IsConvertedMaterial(Material material)
        {
            var assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(material));
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            return !string.IsNullOrEmpty(GetConvertedMaterialSourcePath(assetPath)) || IsConvertedMaterialPath(assetPath);
        }

        private static bool IsConvertedMaterialPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var normalizedPath = NormalizeAssetPath(assetPath);
            var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
            return normalizedPath.IndexOf($"/{ConvertedDirectoryName}/", StringComparison.OrdinalIgnoreCase) >= 0
                && (fileName.EndsWith(ConvertedSuffix, StringComparison.OrdinalIgnoreCase)
                    || fileName.IndexOf($"{ConvertedSuffix}_", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void SetConvertedMaterialSourcePath(string convertedPath, string sourceAssetPath)
        {
            if (string.IsNullOrEmpty(convertedPath) || string.IsNullOrEmpty(sourceAssetPath))
            {
                return;
            }

            var importer = AssetImporter.GetAtPath(convertedPath);
            if (importer == null)
            {
                return;
            }

            var sourceLine = $"{SourceMaterialPathPrefix}{NormalizeAssetPath(sourceAssetPath)}";
            var userData = importer.userData ?? string.Empty;
            var lines = userData.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var updatedLines = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith(SourceMaterialPathPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                updatedLines.Add(line);
            }

            updatedLines.Add(sourceLine);
            var updatedUserData = string.Join("\n", updatedLines);
            if (string.Equals(userData, updatedUserData, StringComparison.Ordinal))
            {
                return;
            }

            importer.userData = updatedUserData;
            importer.SaveAndReimport();
        }

        private static string GetConvertedMaterialSourcePath(string convertedPath)
        {
            if (string.IsNullOrEmpty(convertedPath))
            {
                return null;
            }

            var importer = AssetImporter.GetAtPath(convertedPath);
            if (importer == null || string.IsNullOrEmpty(importer.userData))
            {
                return null;
            }

            var lines = importer.userData.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith(SourceMaterialPathPrefix, StringComparison.Ordinal))
                {
                    return NormalizeAssetPath(line.Substring(SourceMaterialPathPrefix.Length));
                }
            }

            return null;
        }

        private static string SanitizeAssetFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "Material";
            }

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(fileName) ? "Material" : fileName;
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
            folderPath = NormalizeAssetPath(folderPath);
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

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

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }
    }
}
