using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VLiveKit.LiveToon.Editor
{
    internal struct Mmd4MecanimToMToonConversionResult
    {
        public int ConvertedCount;
        public int AssignedCount;
        public int SkippedCount;
        public int RestoredCount;
        public int MissingCount;

        public string ToConversionLog()
        {
            return $"MMD4Mecanim to MToon conversion complete. Created or updated copies: {ConvertedCount}, assigned slots: {AssignedCount}, skipped: {SkippedCount}";
        }

        public string ToRestoreLog()
        {
            return $"MMD4Mecanim to MToon original material assignment restore complete. Restored slots: {RestoredCount}, missing originals: {MissingCount}";
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(Mmd4MecanimToMToonConverter))]
    public sealed class Mmd4MecanimToMToonConverterEditor : UnityEditor.Editor
    {
        private SerializedProperty targetObjectProperty;
        private SerializedProperty shaderToUseProperty;
        private SerializedProperty includeInactiveProperty;
        private SerializedProperty preserveMmdRenderQueueProperty;
        private SerializedProperty overwriteExistingCopiesProperty;
        private SerializedProperty disableOutlineOnConvertProperty;

        private void OnEnable()
        {
            targetObjectProperty = serializedObject.FindProperty("targetObject");
            shaderToUseProperty = serializedObject.FindProperty("shaderToUse");
            includeInactiveProperty = serializedObject.FindProperty("includeInactive");
            preserveMmdRenderQueueProperty = serializedObject.FindProperty("preserveMmdRenderQueue");
            overwriteExistingCopiesProperty = serializedObject.FindProperty("overwriteExistingCopies");
            disableOutlineOnConvertProperty = serializedObject.FindProperty("disableOutlineOnConvert");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(targetObjectProperty);
            EditorGUILayout.PropertyField(shaderToUseProperty);
            EditorGUILayout.PropertyField(includeInactiveProperty);
            EditorGUILayout.PropertyField(preserveMmdRenderQueueProperty);
            EditorGUILayout.PropertyField(overwriteExistingCopiesProperty);
            EditorGUILayout.PropertyField(disableOutlineOnConvertProperty);

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            var converter = (Mmd4MecanimToMToonConverter)target;
            var targetObject = converter.TargetObject;
            var shaderToUse = converter.ShaderToUse;

            if (targetObject == null)
            {
                EditorGUILayout.HelpBox("Target Object is empty.", MessageType.None);
            }

            if (shaderToUse == null)
            {
                EditorGUILayout.HelpBox($"Shader not found: {Mmd4MecanimToMToonConverter.DefaultShaderName}", MessageType.None);
            }

            using (new EditorGUI.DisabledScope(targetObject == null || shaderToUse == null))
            {
                if (GUILayout.Button("Convert MMD4Mecanim To MToon And Assign"))
                {
                    ConvertSelectedComponents();
                }
            }

            using (new EditorGUI.DisabledScope(targetObject == null))
            {
                if (GUILayout.Button("Restore Original Material Assignments"))
                {
                    RestoreOriginalAssignmentsForSelectedComponents();
                }
            }
        }

        private void ConvertSelectedComponents()
        {
            foreach (Mmd4MecanimToMToonConverter converter in targets)
            {
                var result = Mmd4MecanimToMToonConverterTool.ConvertAndAssign(
                    converter.TargetObject,
                    converter.ShaderToUse,
                    converter.IncludeInactive,
                    converter.PreserveMmdRenderQueue,
                    converter.OverwriteExistingCopies,
                    converter.DisableOutlineOnConvert);
                Debug.Log(result.ToConversionLog(), converter);
            }
        }

        private void RestoreOriginalAssignmentsForSelectedComponents()
        {
            foreach (Mmd4MecanimToMToonConverter converter in targets)
            {
                var result = Mmd4MecanimToMToonConverterTool.RestoreOriginalAssignments(
                    converter.TargetObject,
                    converter.IncludeInactive);
                Debug.Log(result.ToRestoreLog(), converter);
            }
        }
    }

    internal static class Mmd4MecanimToMToonConverterTool
    {
        private const string ConvertedDirectoryName = "MToonMaterials";
        private const string ConvertedSuffix = "_MToon";
        private const string GeneratedMaterialsDirectory = "Assets/VLiveKitGenerated/MToonMaterials";
        private const string SourceMaterialPathPrefix = "VLiveKit.Mmd4MecanimToMToon.SourceMaterialPath=";
        private const string SourceMaterialGlobalObjectIdPrefix = "VLiveKit.Mmd4MecanimToMToon.SourceMaterialGlobalObjectId=";
        private const string SourceMaterialNamePrefix = "VLiveKit.Mmd4MecanimToMToon.SourceMaterialName=";
        private const string SourceMaterialShaderNamePrefix = "VLiveKit.Mmd4MecanimToMToon.SourceMaterialShaderName=";
        private const string SourceMaterialRenderQueuePrefix = "VLiveKit.Mmd4MecanimToMToon.SourceMaterialRenderQueue=";
        private const int GeometryQueue = 2000;
        private const int MmdOpaqueDefaultSourceQueue = GeometryQueue + 1;
        private const int MmdTransparentDefaultSourceQueue = GeometryQueue + 2;
        private const float MmdOutlineWidthScale = 1f / (0.003f * 1.5f);
        private const float MmdEdgeScaleFallbackToEdgeSize = 0.35f;
        private const float MmdTransparentClipThreshold = 1f / 255f;
        private static readonly char[] AdditionalInvalidAssetFileNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        public static Mmd4MecanimToMToonConversionResult ConvertAndAssign(
            GameObject root,
            Shader targetShader,
            bool includeInactive,
            bool preserveMmdRenderQueue,
            bool overwriteExistingCopies,
            bool disableOutlineOnConvert)
        {
            var result = new Mmd4MecanimToMToonConversionResult();
            if (root == null || targetShader == null)
            {
                return result;
            }

            var conversionMap = new Dictionary<Material, Material>();
            var skippedMaterials = new HashSet<Material>();
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive);

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
                    if (!IsMmd4MecanimMaterial(sourceMaterial))
                    {
                        if (skippedMaterials.Add(sourceMaterial))
                        {
                            result.SkippedCount++;
                        }

                        continue;
                    }

                    if (!conversionMap.TryGetValue(sourceMaterial, out var convertedMaterial))
                    {
                        convertedMaterial = CreateConvertedMaterial(sourceMaterial, overwriteExistingCopies);
                        if (convertedMaterial == null)
                        {
                            if (skippedMaterials.Add(sourceMaterial))
                            {
                                result.SkippedCount++;
                            }

                            continue;
                        }

                        ConvertMaterialToMToon(convertedMaterial, sourceMaterial, targetShader, preserveMmdRenderQueue, disableOutlineOnConvert);
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
                    Undo.RecordObject(renderer, "Assign MToon Material Copies");
                    renderer.sharedMaterials = sharedMaterials;
                    EditorUtility.SetDirty(renderer);
                }
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        public static Mmd4MecanimToMToonConversionResult RestoreOriginalAssignments(GameObject root, bool includeInactive)
        {
            var result = new Mmd4MecanimToMToonConversionResult();
            if (root == null)
            {
                return result;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive);
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

        private static void ConvertMaterialToMToon(
            Material material,
            Material sourceMaterial,
            Shader targetShader,
            bool preserveMmdRenderQueue,
            bool disableOutlineOnConvert)
        {
            var state = CaptureMmdState(sourceMaterial);
            material.shader = targetShader;

            SetColorIfPresent(material, "_Color", state.BaseColor);
            SetColorIfPresent(material, "_ShadeColor", state.ShadeColor);
            SetTextureIfPresent(material, "_MainTex", state.MainTexture);
            SetTextureIfPresent(material, "_ShadeTexture", state.MainTexture);
            SetTextureIfPresent(material, "_EmissionMap", state.EmissionMap);
            SetColorIfPresent(material, "_EmissionColor", state.EmissionColor);
            SetTextureIfPresent(material, "_SphereAdd", state.SphereAddTexture);
            SetFloatIfPresent(material, "_Cutoff", MmdTransparentClipThreshold);
            SetFloatIfPresent(material, "_ReceiveShadowRate", 0.35f);
            SetFloatIfPresent(material, "_ShadingGradeRate", 1f);
            SetFloatIfPresent(material, "_ShadeShift", -0.25f);
            SetFloatIfPresent(material, "_ShadeToony", 0.9f);
            SetFloatIfPresent(material, "_LightColorAttenuation", 0f);
            SetFloatIfPresent(material, "_IndirectLightIntensity", 0.35f);
            SetFloatIfPresent(material, "_RimLightingMix", 0f);
            SetFloatIfPresent(material, "_RimFresnelPower", 1f);
            SetFloatIfPresent(material, "_RimLift", 0f);
            SetColorIfPresent(material, "_RimColor", Color.black);
            SetFloatIfPresent(material, "_MToonVersion", 39f);
            SetFloatIfPresent(material, "_DebugMode", 0f);

            if (state.MainTexture != null && material.HasProperty("_MainTex"))
            {
                material.SetTextureScale("_MainTex", state.MainTextureScale);
                material.SetTextureOffset("_MainTex", state.MainTextureOffset);
            }

            ApplyMToonRenderMode(material, state, preserveMmdRenderQueue);
            ApplyMToonOutline(material, state.Outline, disableOutlineOnConvert);

            // MMD4Mecanim conversion: do not call MToon.Utils.ValidateProperties here.
            // It clamps TransparentWithZWrite to the MToon queue band and breaks MMD's Geometry+N ordering.
            material.renderQueue = preserveMmdRenderQueue ? state.SourceRenderQueue : material.renderQueue;
        }

        private static MmdMaterialState CaptureMmdState(Material material)
        {
            var sourceShaderName = GetSourceShaderName(material);
            var sourceRenderQueue = GetSourceRenderQueue(material, sourceShaderName);
            var baseColor = GetMmdBaseColor(material);
            var mainTexture = GetMmdMainTexture(material);
            var emissionMap = GetTexture(material, "_EmissionMap");

            return new MmdMaterialState
            {
                SourceRenderQueue = sourceRenderQueue,
                IsTransparent = IsMmdTransparentMaterial(material, sourceShaderName, sourceRenderQueue),
                CullMode = GetMmdCullMode(material, sourceShaderName),
                BaseColor = baseColor,
                ShadeColor = GetMmdShadeColor(material, baseColor),
                MainTexture = mainTexture,
                MainTextureScale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one,
                MainTextureOffset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero,
                SphereAddTexture = GetTexture(material, "_SphereAdd") as Texture2D,
                EmissionMap = emissionMap,
                EmissionColor = GetMmdEmissionColor(material, emissionMap),
                Outline = CaptureMmdOutline(material, sourceShaderName)
            };
        }

        private static void ApplyMToonRenderMode(Material material, MmdMaterialState state, bool preserveMmdRenderQueue)
        {
            if (state.IsTransparent)
            {
                SetFloatIfPresent(material, "_BlendMode", 3f);
                SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloatIfPresent(material, "_ZWrite", 1f);
                SetFloatIfPresent(material, "_AlphaToMask", 0f);
                SetFloatIfPresent(material, "_CullMode", state.CullMode);
                SetFloatIfPresent(material, "_OutlineCullMode", (float)CullMode.Front);
                material.SetOverrideTag("RenderType", "Transparent");
                SetKeyword(material, "_ALPHATEST_ON", false);
                SetKeyword(material, "_ALPHABLEND_ON", true);
                SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
                material.renderQueue = preserveMmdRenderQueue ? state.SourceRenderQueue : 2501;
                return;
            }

            SetFloatIfPresent(material, "_BlendMode", 0f);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloatIfPresent(material, "_ZWrite", 1f);
            SetFloatIfPresent(material, "_AlphaToMask", 0f);
            SetFloatIfPresent(material, "_CullMode", state.CullMode);
            SetFloatIfPresent(material, "_OutlineCullMode", (float)CullMode.Front);
            material.SetOverrideTag("RenderType", "Opaque");
            SetKeyword(material, "_ALPHATEST_ON", false);
            SetKeyword(material, "_ALPHABLEND_ON", false);
            SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
            material.renderQueue = preserveMmdRenderQueue ? state.SourceRenderQueue : -1;
        }

        private static void ApplyMToonOutline(Material material, MmdOutlineState outline, bool disableOutline)
        {
            var usesOutline = !disableOutline && outline.Width > 0f && outline.Color.a > 0.001f;
            SetFloatIfPresent(material, "_OutlineWidthMode", usesOutline ? 1f : 0f);
            SetFloatIfPresent(material, "_OutlineColorMode", 0f);
            SetFloatIfPresent(material, "_OutlineWidth", usesOutline ? outline.Width : 0f);
            SetFloatIfPresent(material, "_OutlineScaledMaxDistance", 1f);
            SetFloatIfPresent(material, "_OutlineLightingMix", 0f);
            SetColorIfPresent(material, "_OutlineColor", usesOutline ? outline.Color : Color.black);
            SetKeyword(material, "MTOON_OUTLINE_WIDTH_WORLD", usesOutline);
            SetKeyword(material, "MTOON_OUTLINE_WIDTH_SCREEN", false);
            SetKeyword(material, "MTOON_OUTLINE_COLOR_FIXED", usesOutline);
            SetKeyword(material, "MTOON_OUTLINE_COLOR_MIXED", false);
        }

        private static Material CreateConvertedMaterial(Material sourceMaterial, bool overwriteExistingCopies)
        {
            var sourceAssetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(sourceMaterial));
            var convertedPath = GetConvertedMaterialPath(sourceMaterial, sourceAssetPath);
            if (string.IsNullOrEmpty(convertedPath))
            {
                return null;
            }

            var convertedDirectory = GetAssetDirectoryName(convertedPath);
            if (string.IsNullOrEmpty(convertedDirectory))
            {
                Debug.LogWarning($"MMD4Mecanim to MToon conversion skipped material '{sourceMaterial.name}' because converted asset path is invalid: {convertedPath}", sourceMaterial);
                return null;
            }

            EnsureFolder(convertedDirectory);
            var existingConvertedMaterial = overwriteExistingCopies ? AssetDatabase.LoadAssetAtPath<Material>(convertedPath) : null;
            if (existingConvertedMaterial != null)
            {
                Undo.RecordObject(existingConvertedMaterial, "Update MToon Material Copy");
                EditorUtility.CopySerialized(sourceMaterial, existingConvertedMaterial);
                existingConvertedMaterial.name = $"{sourceMaterial.name}{ConvertedSuffix}";
                EditorUtility.SetDirty(existingConvertedMaterial);
                SetConvertedMaterialSourceIdentity(convertedPath, sourceMaterial, sourceAssetPath);
                return existingConvertedMaterial;
            }

            convertedPath = AssetDatabase.GenerateUniqueAssetPath(convertedPath);
            if (string.IsNullOrEmpty(convertedPath))
            {
                Debug.LogWarning($"MMD4Mecanim to MToon conversion skipped material '{sourceMaterial.name}' because Unity could not generate a material path under: {convertedDirectory}", sourceMaterial);
                return null;
            }

            var convertedMaterial = new Material(sourceMaterial)
            {
                name = $"{sourceMaterial.name}{ConvertedSuffix}"
            };

            AssetDatabase.CreateAsset(convertedMaterial, convertedPath);
            Undo.RegisterCreatedObjectUndo(convertedMaterial, "Create MToon Material Copy");
            SetConvertedMaterialSourceIdentity(convertedPath, sourceMaterial, sourceAssetPath);
            return convertedMaterial;
        }

        private static string GetConvertedMaterialPath(Material sourceMaterial, string sourceAssetPath)
        {
            var sourceFileName = UseSourceMaterialNameForConvertedAsset(sourceMaterial, sourceAssetPath)
                ? SanitizeAssetFileName(sourceMaterial.name)
                : SanitizeAssetFileName(GetAssetFileNameWithoutExtension(sourceAssetPath));
            var extension = GetMaterialAssetExtension(sourceAssetPath);
            var convertedFileName = $"{sourceFileName}{ConvertedSuffix}{extension}";

            if (!string.IsNullOrEmpty(sourceAssetPath) && sourceAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var sourceDirectory = GetAssetDirectoryName(sourceAssetPath);
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

        private static bool UseSourceMaterialNameForConvertedAsset(Material sourceMaterial, string sourceAssetPath)
        {
            if (sourceMaterial == null || string.IsNullOrEmpty(sourceMaterial.name))
            {
                return false;
            }

            if (string.IsNullOrEmpty(sourceAssetPath))
            {
                return true;
            }

            var extension = GetAssetExtension(sourceAssetPath);
            return !string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase);
        }

        private static Material FindOriginalMaterialForConvertedCopy(Material material)
        {
            var convertedPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(material));
            if (string.IsNullOrEmpty(convertedPath))
            {
                return null;
            }

            var sourceMaterial = GetConvertedMaterialSourceByGlobalObjectId(convertedPath);
            if (sourceMaterial != null)
            {
                return sourceMaterial;
            }

            var sourcePath = GetConvertedMaterialSourcePath(convertedPath);
            if (!string.IsNullOrEmpty(sourcePath))
            {
                sourceMaterial = LoadSourceMaterialFromPath(sourcePath, GetConvertedMaterialSourceName(convertedPath));
                if (sourceMaterial != null)
                {
                    return sourceMaterial;
                }
            }

            return FindOriginalMaterialByConvertedPath(convertedPath);
        }

        private static Material GetConvertedMaterialSourceByGlobalObjectId(string convertedPath)
        {
            var globalObjectId = GetConvertedMaterialSourceUserDataValue(convertedPath, SourceMaterialGlobalObjectIdPrefix);
            if (string.IsNullOrEmpty(globalObjectId) || !GlobalObjectId.TryParse(globalObjectId, out var parsedGlobalObjectId))
            {
                return null;
            }

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedGlobalObjectId) as Material;
        }

        private static string GetConvertedMaterialSourcePath(string convertedPath)
        {
            return NormalizeAssetPath(GetConvertedMaterialSourceUserDataValue(convertedPath, SourceMaterialPathPrefix));
        }

        private static string GetConvertedMaterialSourceName(string convertedPath)
        {
            return GetConvertedMaterialSourceUserDataValue(convertedPath, SourceMaterialNamePrefix);
        }

        private static string GetConvertedMaterialSourceUserDataValue(string convertedPath, string prefix)
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
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return line.Substring(prefix.Length);
                }
            }

            return null;
        }

        private static Material LoadSourceMaterialFromPath(string sourcePath, string sourceName)
        {
            sourcePath = NormalizeAssetPath(sourcePath);
            if (string.IsNullOrEmpty(sourcePath))
            {
                return null;
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath(sourcePath);
            if (!string.IsNullOrEmpty(sourceName))
            {
                foreach (var asset in assets)
                {
                    if (asset is Material material && string.Equals(material.name, sourceName, StringComparison.Ordinal))
                    {
                        return material;
                    }
                }
            }

            return AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        }

        private static Material FindOriginalMaterialByConvertedPath(string convertedPath)
        {
            if (string.IsNullOrEmpty(convertedPath) || convertedPath.StartsWith($"{GeneratedMaterialsDirectory}/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var marker = $"/{ConvertedDirectoryName}/";
            var markerIndex = convertedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var convertedFileName = GetAssetFileNameWithoutExtension(convertedPath);
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

            foreach (var candidate in candidates)
            {
                var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(candidate);
                if (sourceMaterial != null)
                {
                    return sourceMaterial;
                }
            }

            return null;
        }

        private static void SetConvertedMaterialSourceIdentity(string convertedPath, Material sourceMaterial, string sourceAssetPath)
        {
            if (string.IsNullOrEmpty(convertedPath) || sourceMaterial == null || string.IsNullOrEmpty(sourceAssetPath))
            {
                return;
            }

            var importer = AssetImporter.GetAtPath(convertedPath);
            if (importer == null)
            {
                return;
            }

            var sourceLine = $"{SourceMaterialPathPrefix}{NormalizeAssetPath(sourceAssetPath)}";
            var sourceGlobalIdLine = $"{SourceMaterialGlobalObjectIdPrefix}{GlobalObjectId.GetGlobalObjectIdSlow(sourceMaterial)}";
            var sourceNameLine = $"{SourceMaterialNamePrefix}{sourceMaterial.name}";
            var sourceShaderNameLine = $"{SourceMaterialShaderNamePrefix}{GetSourceShaderName(sourceMaterial)}";
            var sourceRenderQueueLine = $"{SourceMaterialRenderQueuePrefix}{GetSourceRenderQueue(sourceMaterial, GetSourceShaderName(sourceMaterial))}";
            var userData = importer.userData ?? string.Empty;
            var lines = userData.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var updatedLines = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith(SourceMaterialPathPrefix, StringComparison.Ordinal)
                    || line.StartsWith(SourceMaterialGlobalObjectIdPrefix, StringComparison.Ordinal)
                    || line.StartsWith(SourceMaterialNamePrefix, StringComparison.Ordinal)
                    || line.StartsWith(SourceMaterialShaderNamePrefix, StringComparison.Ordinal)
                    || line.StartsWith(SourceMaterialRenderQueuePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                updatedLines.Add(line);
            }

            updatedLines.Add(sourceLine);
            updatedLines.Add(sourceGlobalIdLine);
            updatedLines.Add(sourceNameLine);
            updatedLines.Add(sourceShaderNameLine);
            updatedLines.Add(sourceRenderQueueLine);
            var updatedUserData = string.Join("\n", updatedLines);
            if (string.Equals(userData, updatedUserData, StringComparison.Ordinal))
            {
                return;
            }

            importer.userData = updatedUserData;
            importer.SaveAndReimport();
        }

        private static bool IsConvertedMaterial(Material material)
        {
            var assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(material));
            return !string.IsNullOrEmpty(assetPath)
                && (!string.IsNullOrEmpty(GetConvertedMaterialSourcePath(assetPath)) || IsConvertedMaterialPath(assetPath));
        }

        private static bool IsConvertedMaterialPath(string assetPath)
        {
            var normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            var marker = $"/{ConvertedDirectoryName}/";
            return normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0
                && !string.IsNullOrEmpty(RemoveConvertedSuffix(GetAssetFileNameWithoutExtension(normalizedPath)));
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

        private static bool IsMmd4MecanimMaterial(Material material)
        {
            return material != null && IsMmd4MecanimShader(GetSourceShaderName(material));
        }

        private static bool IsMmd4MecanimShader(string shaderName)
        {
            return !string.IsNullOrEmpty(shaderName)
                && shaderName.IndexOf("MMD4Mecanim/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMmdTransparentMaterial(Material material, string shaderName, int sourceRenderQueue)
        {
            if (ShaderNameContains(shaderName, "Transparent"))
            {
                return true;
            }

            var srcBlend = Mathf.RoundToInt(GetFloat(material, "_SrcBlend", (float)BlendMode.One));
            var dstBlend = Mathf.RoundToInt(GetFloat(material, "_DstBlend", (float)BlendMode.Zero));
            if (srcBlend == (int)BlendMode.SrcAlpha || dstBlend == (int)BlendMode.OneMinusSrcAlpha)
            {
                return true;
            }

            if (GetFloat(material, "_SurfaceType", 0f) > 0.5f)
            {
                return true;
            }

            return sourceRenderQueue >= 3000 || GetMmdBaseColor(material).a < 0.99f;
        }

        private static bool ShaderNameContains(string shaderName, string token)
        {
            return !string.IsNullOrEmpty(shaderName)
                && shaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetSourceShaderName(Material material)
        {
            return material != null && material.shader != null ? material.shader.name : string.Empty;
        }

        private static int GetSourceRenderQueue(Material material, string sourceShaderName)
        {
            var renderQueueProperty = GetFloat(material, "_RenderQueue", -1f);
            if (renderQueueProperty >= 0f)
            {
                return Mathf.RoundToInt(renderQueueProperty);
            }

            if (material.renderQueue >= 0)
            {
                return material.renderQueue;
            }

            return GetDefaultMmdRenderQueue(sourceShaderName);
        }

        private static int GetDefaultMmdRenderQueue(string sourceShaderName)
        {
            return ShaderNameContains(sourceShaderName, "Transparent")
                ? MmdTransparentDefaultSourceQueue
                : MmdOpaqueDefaultSourceQueue;
        }

        private static float GetMmdCullMode(Material material, string sourceShaderName)
        {
            var cullMode = GetFloat(material, "_CullMode", float.NaN);
            if (!float.IsNaN(cullMode))
            {
                return Mathf.Clamp(Mathf.Round(cullMode), (float)CullMode.Off, (float)CullMode.Back);
            }

            return ShaderNameContains(sourceShaderName, "BothFaces") ? (float)CullMode.Off : (float)CullMode.Back;
        }

        private static Color GetMmdBaseColor(Material material)
        {
            var diffuseColor = GetColor(material, "_Diffuse", Color.white);
            var legacyColor = GetColor(material, "_Color", diffuseColor);
            var baseColor = GetColor(material, "_BaseColor", legacyColor);
            if (!HasVisibleRgb(baseColor))
            {
                baseColor = HasVisibleRgb(legacyColor) ? legacyColor : diffuseColor;
            }

            baseColor.a = material.HasProperty("_Diffuse") ? diffuseColor.a : legacyColor.a;
            return ClampColor01(baseColor);
        }

        private static Color GetMmdShadeColor(Material material, Color litColor)
        {
            var fallbackAmbient = new Color(litColor.r * 0.65f, litColor.g * 0.65f, litColor.b * 0.65f, litColor.a);
            var ambient = GetColor(material, "_Ambient", fallbackAmbient);
            var shadeColor = Color.Lerp(litColor, ambient, 0.35f);
            shadeColor.a = litColor.a;
            return ClampColor01(shadeColor);
        }

        private static Texture GetMmdMainTexture(Material material)
        {
            var mainTexture = GetTexture(material, "_MainTex");
            return mainTexture != null ? mainTexture : GetTexture(material, "_BaseColorMap");
        }

        private static Color GetMmdEmissionColor(Material material, Texture emissionMap)
        {
            var emissive = GetColor(material, "_Emissive", Color.black);
            if (HasVisibleRgb(emissive))
            {
                var autoLuminousPower = Mathf.Max(1f, GetFloat(material, "_ALPower", 1f));
                return ScaleColor(emissive, autoLuminousPower);
            }

            if (emissionMap != null)
            {
                return GetColor(material, "_EmissionColor", Color.white);
            }

            return Color.black;
        }

        private static MmdOutlineState CaptureMmdOutline(Material material, string sourceShaderName)
        {
            var edgeSize = GetFloat(material, "_EdgeSize", 0f);
            var edgeScale = GetFloat(material, "_EdgeScale", 0f);
            var effectiveEdgeSize = edgeSize > 0f ? edgeSize : Mathf.Max(0f, edgeScale * MmdEdgeScaleFallbackToEdgeSize);
            var edgeColor = GetColor(material, "_EdgeColor", Color.black);
            var usesOutline = ShaderNameContains(sourceShaderName, "Edge") && effectiveEdgeSize > 0f && edgeColor.a > 0.001f;

            return new MmdOutlineState
            {
                Width = usesOutline ? Mathf.Clamp(effectiveEdgeSize * MmdOutlineWidthScale, 0.01f, 1f) : 0f,
                Color = edgeColor
            };
        }

        private static float GetFloat(Material material, string propertyName, float fallback)
        {
            return material != null && material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static Texture GetTexture(Material material, string propertyName)
        {
            return material != null && material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
        }

        private static Color GetColor(Material material, string propertyName, Color fallback)
        {
            return material != null && material.HasProperty(propertyName) ? material.GetColor(propertyName) : fallback;
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetTextureIfPresent(Material material, string propertyName, Texture value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetTexture(propertyName, value);
            }
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
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

        private static bool HasVisibleRgb(Color color)
        {
            return color.r > 0.001f || color.g > 0.001f || color.b > 0.001f;
        }

        private static Color ScaleColor(Color color, float scale)
        {
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }

        private static Color ClampColor01(Color color)
        {
            return new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a));
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

            foreach (var invalidChar in AdditionalInvalidAssetFileNameChars)
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            for (var i = 0; i < fileName.Length; i++)
            {
                if (char.IsControl(fileName[i]))
                {
                    fileName = fileName.Replace(fileName[i], '_');
                }
            }

            fileName = fileName.Trim().Trim('.');
            return string.IsNullOrWhiteSpace(fileName) ? "Material" : fileName;
        }

        private static string GetMaterialAssetExtension(string assetPath)
        {
            var extension = string.IsNullOrEmpty(assetPath) ? ".mat" : GetAssetExtension(assetPath);
            return string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase)
                ? extension
                : ".mat";
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

        private static string GetAssetDirectoryName(string assetPath)
        {
            var normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return null;
            }

            var separatorIndex = normalizedPath.LastIndexOf('/');
            return separatorIndex <= 0 ? null : normalizedPath.Substring(0, separatorIndex);
        }

        private static string GetAssetFileNameWithoutExtension(string assetPath)
        {
            var fileName = GetAssetFileName(assetPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return fileName;
            }

            var extensionIndex = fileName.LastIndexOf('.');
            return extensionIndex > 0 ? fileName.Substring(0, extensionIndex) : fileName;
        }

        private static string GetAssetExtension(string assetPath)
        {
            var fileName = GetAssetFileName(assetPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            var extensionIndex = fileName.LastIndexOf('.');
            return extensionIndex > 0 ? fileName.Substring(extensionIndex) : string.Empty;
        }

        private static string GetAssetFileName(string assetPath)
        {
            var normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return normalizedPath;
            }

            var separatorIndex = normalizedPath.LastIndexOf('/');
            return separatorIndex >= 0 ? normalizedPath.Substring(separatorIndex + 1) : normalizedPath;
        }

        private struct MmdMaterialState
        {
            public int SourceRenderQueue;
            public bool IsTransparent;
            public float CullMode;
            public Color BaseColor;
            public Color ShadeColor;
            public Texture MainTexture;
            public Vector2 MainTextureScale;
            public Vector2 MainTextureOffset;
            public Texture2D SphereAddTexture;
            public Texture EmissionMap;
            public Color EmissionColor;
            public MmdOutlineState Outline;
        }

        private struct MmdOutlineState
        {
            public float Width;
            public Color Color;
        }
    }
}
