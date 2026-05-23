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
        private const string SourceMaterialGlobalObjectIdPrefix = "VLiveKit.LiveToon.SourceMaterialGlobalObjectId=";
        private const string SourceMaterialNamePrefix = "VLiveKit.LiveToon.SourceMaterialName=";
        private const string SourceMaterialShaderNamePrefix = "VLiveKit.LiveToon.SourceMaterialShaderName=";
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
        private const float MmdOutlineWidthScale = 100f;
        private static readonly char[] AdditionalInvalidAssetFileNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
        private static readonly float BlendZero = (float)UnityEngine.Rendering.BlendMode.Zero;
        private static readonly float BlendOne = (float)UnityEngine.Rendering.BlendMode.One;
        private static readonly float BlendSrcAlpha = (float)UnityEngine.Rendering.BlendMode.SrcAlpha;
        private static readonly float BlendOneMinusSrcAlpha = (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
        private static readonly float ZTestLessEqual = (float)UnityEngine.Rendering.CompareFunction.LessEqual;

        private GameObject selectedObject;
        private Shader shaderToUse;
        private LiveToonShaderConversionSource conversionSource;
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
            conversionSource = (LiveToonShaderConversionSource)EditorGUILayout.EnumPopup("Conversion Source", conversionSource);

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

            var result = ConvertShadersForObject(selectedObject, shaderToUse, createMaterialBackups, disableOutlineOnConvert, conversionSource);
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
            return ConvertShadersForObject(
                root,
                targetShader,
                createMaterialBackups,
                disableOutlineOnConvert,
                LiveToonShaderConversionSource.MToon);
        }

        internal static LiveToonMaterialConversionResult ConvertShadersForObject(
            GameObject root,
            Shader targetShader,
            bool createMaterialBackups,
            bool disableOutlineOnConvert,
            LiveToonShaderConversionSource conversionSource)
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

                        ConvertMaterial(convertedMaterial, targetShader, conversionSource);
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

        private static void ConvertMaterial(
            Material material,
            Shader targetShader,
            LiveToonShaderConversionSource conversionSource)
        {
            switch (conversionSource)
            {
                case LiveToonShaderConversionSource.MMD4Mecanim:
                    ConvertMaterialFromMmd4Mecanim(material, targetShader);
                    break;
                default:
                    ConvertMaterialLikeLoadModel(material, targetShader);
                    break;
            }
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

        private static void ConvertMaterialFromMmd4Mecanim(Material material, Shader targetShader)
        {
            var materialName = GetMmdSourceMaterialName(material);
            var sourceShaderName = GetMmdSourceShaderName(material);
            var sourceRenderQueue = GetMmdRenderQueue(material);
            var blendMode = GetMmdBlendMode(material, materialName, sourceShaderName);
            var color = GetMmdBaseColor(material);
            var shadeColor = GetMmdShadeColor(material, color);
            var mainTexture = GetMmdMainTexture(material);
            var sphereAddTexture = GetTexture(material, "_SphereAdd");
            var outlineState = CaptureMmdOutlineState(material, materialName, sourceShaderName);
            var cullMode = GetMmdCullMode(sourceShaderName);
            var emissionMap = GetMmdEmissionMap(material);
            var emissionColor = GetMmdEmissionColor(material, emissionMap);

            material.shader = targetShader;

            SetColorIfPresent(material, "_Color", color);
            SetColorIfPresent(material, "_BaseColor", color);
            SetColorIfPresent(material, "_ShadeColor", shadeColor);
            SetTextureIfPresent(material, "_MainTex", mainTexture);
            SetTextureIfPresent(material, "_ShadeTexture", mainTexture);
            SetTextureIfPresent(material, "_SphereAdd", sphereAddTexture is Texture2D ? sphereAddTexture : null);
            ApplyMmdEmission(material, emissionMap, emissionColor);

            SetFloatIfPresent(material, "_BlendMode", blendMode);
            SetFloatIfPresent(material, "_ShadeToony", 0.9f);
            SetFloatIfPresent(material, "_ShadeShift", -0.25f);
            SetFloatIfPresent(material, "_ReceiveShadowRate", 0.35f);
            SetFloatIfPresent(material, "_CullMode", cullMode);

            ApplyRenderModeState(material, blendMode);
            ApplyMmdRenderQueueState(material, blendMode, sourceRenderQueue);
            RestoreOutlineProperties(material, outlineState);
            ApplyOutlineModeState(material);
            ApplyEnvironmentLightingDefaults(material);
            LiveToonDefaultAssets.EnsureDefaultJitterTexture(material);
        }

        private static string GetMmdSourceShaderName(Material material)
        {
            if (material == null)
            {
                return string.Empty;
            }

            var shaderName = material.shader != null ? material.shader.name : string.Empty;
            if (!string.Equals(shaderName, DefaultShaderName, StringComparison.Ordinal))
            {
                return shaderName;
            }

            var materialPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(material));
            return GetConvertedMaterialSourceShaderName(materialPath) ?? shaderName;
        }

        private static string GetMmdSourceMaterialName(Material material)
        {
            var materialName = material != null ? material.name : string.Empty;
            return RemoveConvertedSuffix(materialName) ?? materialName;
        }

        private static Color GetMmdBaseColor(Material material)
        {
            var legacyColor = GetColor(material, "_Color", Color.white);
            var baseColor = GetColor(material, "_BaseColor", legacyColor);
            if (!HasVisibleRgb(baseColor))
            {
                baseColor = legacyColor;
            }

            baseColor.a = legacyColor.a;
            return ClampColor01(baseColor);
        }

        private static Texture GetMmdMainTexture(Material material)
        {
            var mainTexture = GetTexture(material, "_MainTex");
            return mainTexture != null ? mainTexture : GetTexture(material, "_BaseColorMap");
        }

        private static Texture GetMmdEmissionMap(Material material)
        {
            var emissionMap = GetTexture(material, "_EmissionMap");
            return emissionMap != null ? emissionMap : GetTexture(material, "_EmissiveColorMap");
        }

        private static float GetMmdBlendMode(Material material, string materialName, string sourceShaderName)
        {
            if (IsMmdTransparentMaterial(materialName, sourceShaderName))
            {
                return GetMmdTransparentBlendMode(materialName);
            }

            var blendMode = GetFloat(material, "_BlendMode", float.NaN);
            if (!float.IsNaN(blendMode) && blendMode > 0f)
            {
                return blendMode;
            }

            var mode = GetFloat(material, "_Mode", 0f);
            var renderQueue = GetFloat(material, "_RenderQueue", material.renderQueue);
            var color = GetColor(material, "_Color", Color.white);
            if (mode >= 3f || renderQueue >= 3000f || color.a < 0.99f)
            {
                return 2f;
            }

            if (mode >= 1f || renderQueue >= 2450f)
            {
                return 1f;
            }

            return 0f;
        }

        private static bool IsMmdTransparentMaterial(string materialName, string sourceShaderName)
        {
            if (ShaderNameContains(sourceShaderName, "Transparent"))
            {
                return true;
            }

            return IsMmdOverlayTransparentMaterial(materialName);
        }

        private static float GetMmdTransparentBlendMode(string materialName)
        {
            return IsMmdOverlayTransparentMaterial(materialName)
                ? 2f
                : 1f;
        }

        private static bool IsMmdOverlayTransparentMaterial(string materialName)
        {
            materialName = materialName ?? string.Empty;
            return materialName.IndexOf("hairshadow", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("eye_hi", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("eyehi", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("cheek", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("lens", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float GetMmdCullMode(string sourceShaderName)
        {
            return ShaderNameContains(sourceShaderName, "BothFaces") ? 0f : 2f;
        }

        private static float GetMmdRenderQueue(Material material)
        {
            var renderQueue = GetFloat(material, "_RenderQueue", -1f);
            return renderQueue >= 0f ? renderQueue : material.renderQueue;
        }

        private static void ApplyMmdRenderQueueState(Material material, float blendMode, float sourceRenderQueue)
        {
            var sourceQueueOffset = GetMmdRenderQueueOffset(sourceRenderQueue);
            switch ((int)blendMode)
            {
                case 1:
                    material.renderQueue = 2450 + sourceQueueOffset;
                    break;
                case 2:
                    material.renderQueue = 3000 + sourceQueueOffset;
                    break;
                case 3:
                    material.renderQueue = TransparentWithZWriteQueue + sourceQueueOffset;
                    break;
            }
        }

        private static int GetMmdRenderQueueOffset(float sourceRenderQueue)
        {
            if (sourceRenderQueue < 0f)
            {
                return 0;
            }

            var roundedRenderQueue = Mathf.RoundToInt(sourceRenderQueue);
            if (roundedRenderQueue >= 2000 && roundedRenderQueue < 2500)
            {
                return Mathf.Clamp(roundedRenderQueue - 2000, 0, 99);
            }

            if (roundedRenderQueue >= 3000 && roundedRenderQueue < 3100)
            {
                return roundedRenderQueue - 3000;
            }

            return 0;
        }

        private static Color GetMmdShadeColor(Material material, Color litColor)
        {
            var fallbackAmbient = new Color(litColor.r * 0.65f, litColor.g * 0.65f, litColor.b * 0.65f, litColor.a);
            var ambient = GetColor(material, "_Ambient", fallbackAmbient);
            var shadeColor = Color.Lerp(litColor, ambient, 0.35f);
            shadeColor.a = litColor.a;
            return ClampColor01(shadeColor);
        }

        private static LiveToonOutlineMaterialState CaptureMmdOutlineState(Material material, string materialName, string sourceShaderName)
        {
            var edgeSize = GetFloat(material, "_EdgeSize", 0f);
            var edgeColor = GetColor(material, "_EdgeColor", Color.black);
            var usesOutline = UsesMmdOutline(materialName, sourceShaderName) && edgeSize > 0f && edgeColor.a > 0.001f;

            return new LiveToonOutlineMaterialState
            {
                WidthMode = usesOutline ? 1f : 0f,
                ColorMode = 0f,
                Width = usesOutline ? Mathf.Clamp(edgeSize * MmdOutlineWidthScale, 0.01f, 1f) : 0f,
                ScaledMaxDistance = 1f,
                LightingMix = 0f,
                CullMode = 1f,
                WidthTexture = null,
                Color = edgeColor
            };
        }

        private static bool UsesMmdOutline(string materialName, string sourceShaderName)
        {
            if (ShaderNameContains(sourceShaderName, "Edge"))
            {
                return true;
            }

            materialName = materialName ?? string.Empty;
            if (materialName.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("eye_hi", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("eyehi", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("hairshadow", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("cheek", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("lens", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("megane", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("face01", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("hair01", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return materialName.IndexOf("body_", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("face00", StringComparison.OrdinalIgnoreCase) >= 0
                || materialName.IndexOf("hair00", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShaderNameContains(string shaderName, string token)
        {
            return !string.IsNullOrEmpty(shaderName)
                && shaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static void ApplyMmdEmission(Material material, Texture emissionMap, Color emissionColor)
        {
            var hasEmission = HasVisibleRgb(emissionColor);
            SetColorIfPresent(material, "_EmissionColor", hasEmission ? emissionColor : Color.black);
            SetColorIfPresent(material, "_EmissiveColor", hasEmission ? emissionColor : Color.black);
            SetColorIfPresent(material, "_EmissiveColorLDR", hasEmission ? ClampColor01(emissionColor) : Color.black);
            SetTextureIfPresent(material, "_EmissionMap", hasEmission && emissionMap != null ? emissionMap : null);
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

        private static Material CreateConvertedMaterial(Material sourceMaterial)
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
                Debug.LogWarning($"LiveToon shader conversion skipped material '{sourceMaterial.name}' because converted asset path is invalid: {convertedPath}", sourceMaterial);
                return null;
            }

            EnsureFolder(convertedDirectory);
            convertedPath = AssetDatabase.GenerateUniqueAssetPath(convertedPath);
            if (string.IsNullOrEmpty(convertedPath))
            {
                Debug.LogWarning($"LiveToon shader conversion skipped material '{sourceMaterial.name}' because Unity could not generate a material path under: {convertedDirectory}", sourceMaterial);
                return null;
            }

            var convertedMaterial = new Material(sourceMaterial)
            {
                name = $"{sourceMaterial.name}{ConvertedSuffix}"
            };

            AssetDatabase.CreateAsset(convertedMaterial, convertedPath);
            Undo.RegisterCreatedObjectUndo(convertedMaterial, "Create LiveToon Material Copy");
            SetConvertedMaterialSourceIdentity(convertedPath, sourceMaterial, sourceAssetPath);
            return convertedMaterial;
        }

        private static string GetConvertedMaterialPath(Material sourceMaterial, string sourceAssetPath)
        {
            if (!string.IsNullOrEmpty(sourceAssetPath) && IsConvertedMaterialPath(sourceAssetPath))
            {
                var convertedSourceDirectory = GetAssetDirectoryName(sourceAssetPath);
                var convertedSourceName = GetAssetFileNameWithoutExtension(sourceAssetPath);
                var originalLikeName = RemoveConvertedSuffix(convertedSourceName) ?? convertedSourceName;
                var convertedSourceExtension = GetMaterialAssetExtension(sourceAssetPath);
                return $"{convertedSourceDirectory}/{SanitizeAssetFileName(originalLikeName)}{ConvertedSuffix}{convertedSourceExtension}";
            }

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

        private static string GetMaterialAssetExtension(string assetPath)
        {
            var extension = string.IsNullOrEmpty(assetPath) ? ".mat" : GetAssetExtension(assetPath);
            return string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase)
                ? extension
                : ".mat";
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
                sourceMaterial = LoadSourceMaterialFromPath(sourcePath, GetConvertedMaterialSourceName(convertedPath), material);
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
            var fileName = GetAssetFileNameWithoutExtension(normalizedPath);
            return normalizedPath.IndexOf($"/{ConvertedDirectoryName}/", StringComparison.OrdinalIgnoreCase) >= 0
                && (fileName.EndsWith(ConvertedSuffix, StringComparison.OrdinalIgnoreCase)
                    || fileName.IndexOf($"{ConvertedSuffix}_", StringComparison.OrdinalIgnoreCase) >= 0);
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
            var sourceShaderNameLine = $"{SourceMaterialShaderNamePrefix}{GetMmdSourceShaderName(sourceMaterial)}";
            var userData = importer.userData ?? string.Empty;
            var lines = userData.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var updatedLines = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith(SourceMaterialPathPrefix, StringComparison.Ordinal)
                    || line.StartsWith(SourceMaterialGlobalObjectIdPrefix, StringComparison.Ordinal)
                    || line.StartsWith(SourceMaterialNamePrefix, StringComparison.Ordinal)
                    || line.StartsWith(SourceMaterialShaderNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                updatedLines.Add(line);
            }

            updatedLines.Add(sourceLine);
            updatedLines.Add(sourceGlobalIdLine);
            updatedLines.Add(sourceNameLine);
            updatedLines.Add(sourceShaderNameLine);
            var updatedUserData = string.Join("\n", updatedLines);
            if (string.Equals(userData, updatedUserData, StringComparison.Ordinal))
            {
                return;
            }

            importer.userData = updatedUserData;
            importer.SaveAndReimport();
        }

        private static Material GetConvertedMaterialSourceByGlobalObjectId(string convertedPath)
        {
            var globalObjectId = GetConvertedMaterialSourceGlobalObjectId(convertedPath);
            if (string.IsNullOrEmpty(globalObjectId))
            {
                return null;
            }

            if (!GlobalObjectId.TryParse(globalObjectId, out var parsedGlobalObjectId))
            {
                return null;
            }

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedGlobalObjectId) as Material;
        }

        private static string GetConvertedMaterialSourceGlobalObjectId(string convertedPath)
        {
            return GetConvertedMaterialSourceUserDataValue(convertedPath, SourceMaterialGlobalObjectIdPrefix);
        }

        private static string GetConvertedMaterialSourcePath(string convertedPath)
        {
            return NormalizeAssetPath(GetConvertedMaterialSourceUserDataValue(convertedPath, SourceMaterialPathPrefix));
        }

        private static string GetConvertedMaterialSourceName(string convertedPath)
        {
            return GetConvertedMaterialSourceUserDataValue(convertedPath, SourceMaterialNamePrefix);
        }

        private static string GetConvertedMaterialSourceShaderName(string convertedPath)
        {
            return GetConvertedMaterialSourceUserDataValue(convertedPath, SourceMaterialShaderNamePrefix);
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

        private static Material LoadSourceMaterialFromPath(string sourcePath, string sourceName, Material convertedMaterial)
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

            var matchedMaterial = FindSourceMaterialByConvertedProperties(assets, convertedMaterial);
            if (matchedMaterial != null)
            {
                return matchedMaterial;
            }

            if (string.Equals(GetMaterialAssetExtension(sourcePath), GetAssetExtension(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            }

            var materialCount = 0;
            Material onlyMaterial = null;
            foreach (var asset in assets)
            {
                if (!(asset is Material material))
                {
                    continue;
                }

                onlyMaterial = material;
                materialCount++;
                if (materialCount > 1)
                {
                    return null;
                }
            }

            return onlyMaterial;
        }

        private static Material FindSourceMaterialByConvertedProperties(UnityEngine.Object[] sourceAssets, Material convertedMaterial)
        {
            if (sourceAssets == null || convertedMaterial == null)
            {
                return null;
            }

            var convertedRenderQueue = GetFloat(convertedMaterial, "_RenderQueue", float.NaN);
            if (!float.IsNaN(convertedRenderQueue))
            {
                foreach (var asset in sourceAssets)
                {
                    if (asset is Material material
                        && Mathf.Approximately(GetFloat(material, "_RenderQueue", float.NaN), convertedRenderQueue))
                    {
                        return material;
                    }
                }
            }

            var convertedMainTexture = GetMmdMainTexture(convertedMaterial);
            if (convertedMainTexture == null)
            {
                return null;
            }

            Material matchedMaterial = null;
            foreach (var asset in sourceAssets)
            {
                if (!(asset is Material material) || GetMmdMainTexture(material) != convertedMainTexture)
                {
                    continue;
                }

                if (matchedMaterial != null)
                {
                    return null;
                }

                matchedMaterial = material;
            }

            return matchedMaterial;
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

        private static bool TryCreateMaterialBackup(Material material)
        {
            var assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(material));
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var directory = GetAssetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            var extension = GetAssetExtension(assetPath);
            if (!string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var backupDirectory = $"{directory}/{BackupDirectoryName}";
            EnsureFolder(backupDirectory);

            var fileName = GetAssetFileNameWithoutExtension(assetPath);
            var backupPath = $"{backupDirectory}/{fileName}{BackupSuffix}{extension}";
            if (AssetDatabase.LoadAssetAtPath<Material>(backupPath) != null)
            {
                return false;
            }

            return AssetDatabase.CopyAsset(assetPath, backupPath);
        }

        private static Material FindMaterialBackup(Material material)
        {
            var assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(material));
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var extension = GetAssetExtension(assetPath);
            if (!string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var directory = GetAssetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var fileName = GetAssetFileNameWithoutExtension(assetPath);
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
    }
}
