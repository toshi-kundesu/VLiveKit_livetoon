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

    internal struct LiveToonSpecularMaterialState
    {
        public Color Color;
        public float Intensity;
        public float Power;
    }

    public sealed class ShaderConverterTool : EditorWindow
    {
        private const string DefaultShaderName = LiveToonShaderConverter.DefaultShaderName;
        private const string OfficialHdrpMmdShaderName = LiveToonShaderConverter.OfficialHdrpMmdShaderName;
        private const string BackupDirectoryName = "LiveToonMaterialBackups";
        private const string BackupSuffix = "_LiveToonBackup";
        private const string ConvertedDirectoryName = "LiveToonMaterials";
        private const string ConvertedSuffix = "_LiveToon";
        private const string GeneratedMaterialsDirectory = "Assets/VLiveKitGenerated/LiveToonMaterials";
        private const string OfficialHdrpMmdConvertedDirectoryName = "OfficialHDRPMMDMaterials";
        private const string OfficialHdrpMmdConvertedSuffix = "_OfficialHDRPMMD";
        private const string OfficialHdrpMmdGeneratedMaterialsDirectory = "Assets/VLiveKitGenerated/OfficialHDRPMMDMaterials";
        private const string SourceMaterialPathPrefix = "VLiveKit.LiveToon.SourceMaterialPath=";
        private const string SourceMaterialGlobalObjectIdPrefix = "VLiveKit.LiveToon.SourceMaterialGlobalObjectId=";
        private const string SourceMaterialNamePrefix = "VLiveKit.LiveToon.SourceMaterialName=";
        private const string SourceMaterialShaderNamePrefix = "VLiveKit.LiveToon.SourceMaterialShaderName=";
        private const string SourceMaterialRenderQueuePrefix = "VLiveKit.LiveToon.SourceMaterialRenderQueue=";
        private const string TransparentDepthPrepassName = "TransparentDepthPrepass";
        private const string TransparentDepthPostpassName = "TransparentDepthPostpass";
        private const string KeyOutlineWidthWorld = "MTOON_OUTLINE_WIDTH_WORLD";
        private const string KeyOutlineWidthScreen = "MTOON_OUTLINE_WIDTH_SCREEN";
        private const string KeyOutlineColorFixed = "MTOON_OUTLINE_COLOR_FIXED";
        private const string KeyOutlineColorMixed = "MTOON_OUTLINE_COLOR_MIXED";
        private const int GeometryQueue = 2000;
        private const int GeometryLastQueue = 2500;
        private const int LiveToonFogBaseQueue = 2225;
        private const int AlphaTestQueue = 2450;
        private const int TransparentQueue = 3000;
        private const int TransparentWithZWriteQueue = 2501;
        private const int MmdOpaqueDefaultSourceQueue = GeometryQueue + 1;
        private const int MmdTransparentDefaultSourceQueue = GeometryQueue + 2;
        private const int MToonTransparentQueueSpan = 50;
        private const int MmdHdrpTransparentQueueSpan = GeometryLastQueue - GeometryQueue + 1;
        private const float DefaultIndirectLightIntensity = 0.35f;
        private const float DefaultReflectionProbeIntensity = 0.25f;
        private const float DefaultReflectionProbeSmoothness = 0.35f;
        private const float MmdOutlineWidthScale = 100f;
        private const float MmdTransparentForwardOffsetFactor = -0.1f;
        private const float MmdTransparentForwardOffsetUnits = -1f;
        private const float MmdDefaultForwardOffsetFactor = 0f;
        private const float MmdDefaultForwardOffsetUnits = 0f;
        private const float MmdOutlineOffsetFactor = 0.1f;
        private const float MmdOutlineOffsetUnits = 1f;
        private const float MmdDefaultOutlineOffsetFactor = 1f;
        private const float MmdDefaultOutlineOffsetUnits = 1f;
        private const float MmdTransparentClipThreshold = 1f / 255f;
        private const float MmdTransparentFogAlphaWeight = 0f;
        private const float MmdTransparentFogIntensity = 0f;
        private const float MmdColorMaskRgb = 14f;
        private const float MmdColorMaskRgba = 15f;
        private const float MmdDefaultSpecularPower = 64f;
        private const float MmdVisibleSpecularThreshold = 0.001f;
        private static readonly char[] AdditionalInvalidAssetFileNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
        private static readonly float BlendZero = (float)UnityEngine.Rendering.BlendMode.Zero;
        private static readonly float BlendOne = (float)UnityEngine.Rendering.BlendMode.One;
        private static readonly float BlendSrcAlpha = (float)UnityEngine.Rendering.BlendMode.SrcAlpha;
        private static readonly float BlendOneMinusSrcAlpha = (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
        private static readonly float ZTestLessEqual = (float)UnityEngine.Rendering.CompareFunction.LessEqual;

        private GameObject selectedObject;
        private Shader shaderToUse;
        private LiveToonShaderConversionSource conversionSource;
        private MmdTransparentFogMode mmdTransparentFogMode = MmdTransparentFogMode.HdrpMmdStackRangeWithSurfaceFog;
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
            conversionSource = (LiveToonShaderConversionSource)EditorGUILayout.EnumPopup("Conversion Mode", conversionSource);
            using (new EditorGUI.DisabledScope(!UsesMmdTransparentFogOption(conversionSource)))
            {
                mmdTransparentFogMode = (MmdTransparentFogMode)EditorGUILayout.EnumPopup("MMD Transparent Path", mmdTransparentFogMode);
            }

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

            var result = ConvertShadersForObject(selectedObject, shaderToUse, createMaterialBackups, disableOutlineOnConvert, conversionSource, mmdTransparentFogMode);
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
            return ConvertShadersForObject(
                root,
                targetShader,
                createMaterialBackups,
                disableOutlineOnConvert,
                conversionSource,
                MmdTransparentFogMode.HdrpMmdStackRangeWithSurfaceFog);
        }

        internal static LiveToonMaterialConversionResult ConvertShadersForObject(
            GameObject root,
            Shader targetShader,
            bool createMaterialBackups,
            bool disableOutlineOnConvert,
            LiveToonShaderConversionSource conversionSource,
            MmdTransparentFogMode mmdTransparentFogMode)
        {
            var result = new LiveToonMaterialConversionResult();
            targetShader = ResolveTargetShader(targetShader, conversionSource);
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

                        convertedMaterial = CreateConvertedMaterial(sourceMaterial, conversionSource);
                        if (convertedMaterial == null)
                        {
                            if (skippedMaterials.Add(sourceMaterial))
                            {
                                result.SkippedCount++;
                            }

                            continue;
                        }

                        ConvertMaterial(convertedMaterial, targetShader, conversionSource, mmdTransparentFogMode);
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

        private static Shader ResolveTargetShader(Shader targetShader, LiveToonShaderConversionSource conversionSource)
        {
            if (conversionSource == LiveToonShaderConversionSource.OfficialHDRPMMD)
            {
                return Shader.Find(OfficialHdrpMmdShaderName);
            }

            return targetShader;
        }

        private static void ConvertMaterial(
            Material material,
            Shader targetShader,
            LiveToonShaderConversionSource conversionSource,
            MmdTransparentFogMode mmdTransparentFogMode)
        {
            switch (conversionSource)
            {
                case LiveToonShaderConversionSource.MMD4Mecanim:
                    ConvertMaterialFromMmd4Mecanim(material, targetShader, mmdTransparentFogMode);
                    break;
                case LiveToonShaderConversionSource.OfficialHDRPMMD:
                    ConvertMaterialToOfficialHdrpMmd(material, targetShader);
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
            ApplyMToonSpecularState(material);
            RestoreOutlineProperties(material, outlineState);
            ApplyOutlineModeState(material);
            ApplyEnvironmentLightingDefaults(material);
            LiveToonDefaultAssets.EnsureDefaultJitterTexture(material);
        }

        private static void ConvertMaterialFromMmd4Mecanim(
            Material material,
            Shader targetShader,
            MmdTransparentFogMode mmdTransparentFogMode)
        {
            var sourceShaderName = GetMmdSourceShaderName(material);
            var sourceRenderQueue = GetMmdSourceRenderQueue(material);
            var blendMode = GetMmdBlendMode(material, sourceShaderName);
            var usesMmdTransparentShader = IsMmdTransparentShader(sourceShaderName);
            var color = GetMmdBaseColor(material);
            var mainTexture = GetMmdMainTexture(material);
            var shadeColor = GetMmdShadeColor(material, color);
            var sphereAddTexture = GetTexture(material, "_SphereAdd");
            var outlineState = CaptureMmdOutlineState(material, sourceShaderName, allowPropertyOnlyOutline: false);
            var usesMmdOutline = outlineState.WidthMode > 0.5f;
            var cullMode = GetMmdCullMode(sourceShaderName);
            var emissionMap = GetMmdEmissionMap(material);
            var emissionColor = GetMmdEmissionColor(material, emissionMap);
            var specularState = CaptureMmdSpecularState(material);
            var noShadowCasting = GetFloat(material, "_NoShadowCasting", ShaderNameContains(sourceShaderName, "NoShadowCasting") ? 1f : 0f);

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
            SetFloatIfPresent(material, "_NoShadowCasting", noShadowCasting);

            ApplyRenderModeState(material, blendMode);
            ApplyMmdSpecularState(material, specularState);
            ApplyMmdAlphaState(material, blendMode, usesMmdTransparentShader, mmdTransparentFogMode);
            ApplyMmdDepthOffsetState(material, usesMmdTransparentShader, usesMmdOutline);
            ApplyMmdRenderQueueState(material, blendMode, sourceRenderQueue, usesMmdTransparentShader, mmdTransparentFogMode);
            RestoreOutlineProperties(material, outlineState);
            ApplyOutlineModeState(material);
            ApplyEnvironmentLightingDefaults(material);
            LiveToonDefaultAssets.EnsureDefaultJitterTexture(material);
        }

        private static void ConvertMaterialToOfficialHdrpMmd(Material material, Shader targetShader)
        {
            var sourceShaderName = GetMmdSourceShaderName(material);
            var sourceRenderQueue = GetMmdSourceRenderQueue(material);
            var blendMode = GetOfficialHdrpMmdTargetBlendMode(material, sourceShaderName, sourceRenderQueue);
            var isTransparent = Mathf.RoundToInt(blendMode) == 2;
            var isCutout = Mathf.RoundToInt(blendMode) == 1;
            var cullMode = GetMmdCullMode(sourceShaderName);
            var diffuse = GetMmdBaseColor(material);
            var specular = GetColor(material, "_Specular", Color.black);
            var ambient = GetColor(material, "_Ambient", new Color(0.5f, 0.5f, 0.5f, 1f));
            var shininess = GetFloat(material, "_Shininess", 80f);
            var shadowLum = GetFloat(material, "_ShadowLum", 1.5f);
            var ambientToDiffuse = GetFloat(material, "_AmbientToDiffuse", 5f);
            var edgeColor = GetColor(material, "_EdgeColor", Color.black);
            var edgeScale = GetFloat(material, "_EdgeScale", 0f);
            var edgeSize = GetFloat(material, "_EdgeSize", 0f);
            var mainTexture = GetMmdMainTexture(material);
            var toonTexture = GetTexture(material, "_ToonTex");
            var sphereCube = GetTexture(material, "_SphereCube");
            var emissive = GetColor(material, "_Emissive", Color.black);
            var autoLuminousPower = GetFloat(material, "_ALPower", 0f);
            var emissionColor = GetMmdEmissionColor(material, null);
            var toonTone = GetVector(material, "_ToonTone", new Vector4(1f, 0.5f, 0.5f, 0f));
            var noShadowCasting = GetFloat(material, "_NoShadowCasting", ShaderNameContains(sourceShaderName, "NoShadowCasting") ? 1f : 0f);
            var hasSpecular = HasVisibleRgb(specular) || material.IsKeywordEnabled("SPECULAR_ON");
            var hasEmission = HasVisibleRgb(emissive) || material.IsKeywordEnabled("EMISSIVE_ON");
            var hasSphereMul = material.IsKeywordEnabled("SPHEREMAP_MUL");
            var hasSphereAdd = material.IsKeywordEnabled("SPHEREMAP_ADD") || (!hasSphereMul && sphereCube != null);
            var hasSelfShadow = material.IsKeywordEnabled("SELFSHADOW_ON");
            var hasAmbientToDiffuse = material.IsKeywordEnabled("AMB2DIFF_ON");

            material.shader = targetShader;

            SetColorIfPresent(material, "_Diffuse", diffuse);
            SetColorIfPresent(material, "_Color", diffuse);
            SetColorIfPresent(material, "_BaseColor", diffuse);
            SetColorIfPresent(material, "_Specular", specular);
            SetColorIfPresent(material, "_SpecularColor", specular);
            SetColorIfPresent(material, "_Ambient", ambient);
            SetColorIfPresent(material, "_EdgeColor", edgeColor);
            SetTextureIfPresent(material, "_MainTex", mainTexture);
            SetTextureIfPresent(material, "_BaseColorMap", mainTexture);
            SetTextureIfPresent(material, "_ToonTex", toonTexture);
            if (sphereCube is Cubemap)
            {
                SetTextureIfPresent(material, "_SphereCube", sphereCube);
            }

            SetColorIfPresent(material, "_Emissive", emissive);
            SetColorIfPresent(material, "_EmissiveColor", emissionColor);
            SetColorIfPresent(material, "_EmissiveColorLDR", ClampColor01(emissionColor));
            SetFloatIfPresent(material, "_ALPower", autoLuminousPower);
            SetFloatIfPresent(material, "_Shininess", shininess);
            SetFloatIfPresent(material, "_ShadowLum", shadowLum);
            SetFloatIfPresent(material, "_AmbientToDiffuse", ambientToDiffuse);
            SetFloatIfPresent(material, "_EdgeScale", edgeScale);
            SetFloatIfPresent(material, "_EdgeSize", edgeSize);
            SetFloatIfPresent(material, "_NoShadowCasting", noShadowCasting);
            SetVectorIfPresent(material, "_ToonTone", toonTone);

            ApplyOfficialHdrpMmdRenderState(material, blendMode, cullMode, sourceRenderQueue);

            SetKeyword(material, "_TOON", true);
            SetKeyword(material, "SPECULAR_ON", hasSpecular);
            SetKeyword(material, "EMISSIVE_ON", hasEmission);
            SetKeyword(material, "SPHEREMAP_MUL", hasSphereMul);
            SetKeyword(material, "SPHEREMAP_ADD", hasSphereAdd && !hasSphereMul);
            SetKeyword(material, "SELFSHADOW_ON", hasSelfShadow);
            SetKeyword(material, "AMB2DIFF_ON", hasAmbientToDiffuse);
            SetKeyword(material, "_ALPHATEST_ON", isCutout);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", isTransparent);
            SetKeyword(material, "_BLENDMODE_ALPHA", isTransparent);
            SetKeyword(material, "_BLENDMODE_ADD", false);
            SetKeyword(material, "_BLENDMODE_PRE_MULTIPLY", false);
            SetKeyword(material, "_ENABLE_FOG_ON_TRANSPARENT", isTransparent);
            SetKeyword(material, "_DOUBLESIDED_ON", Mathf.RoundToInt(cullMode) == 0);
            SetKeyword(material, "_EMISSIVE_COLOR_MAP", false);
            SetKeyword(material, "_MATERIAL_FEATURE_SPECULAR_COLOR", hasSpecular);
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

        private static float GetMmdBlendMode(Material material, string sourceShaderName)
        {
            if (IsMmdTransparentShader(sourceShaderName))
            {
                return 2f;
            }

            if (IsMmd4MecanimShader(sourceShaderName))
            {
                return 0f;
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

        private static float GetOfficialHdrpMmdTargetBlendMode(Material material, string sourceShaderName, float sourceRenderQueue)
        {
            if (IsMmdTransparentShader(sourceShaderName))
            {
                return 2f;
            }

            var renderQueue = sourceRenderQueue >= 0f ? sourceRenderQueue : material.renderQueue;
            var surfaceType = GetFloat(material, "_SurfaceType", 0f);
            var srcBlend = Mathf.RoundToInt(GetFloat(material, "_SrcBlend", BlendOne));
            var dstBlend = Mathf.RoundToInt(GetFloat(material, "_DstBlend", BlendZero));
            var isAlphaBlendState = srcBlend == Mathf.RoundToInt(BlendSrcAlpha)
                || dstBlend == Mathf.RoundToInt(BlendOneMinusSrcAlpha);
            var alphaCutoffEnabled = GetFloat(material, "_AlphaCutoffEnable", 0f) > 0.5f
                || material.IsKeywordEnabled("_ALPHATEST_ON");
            var color = GetMmdBaseColor(material);

            if (surfaceType >= 0.5f || isAlphaBlendState || renderQueue >= TransparentQueue || color.a < 0.99f)
            {
                // Official HDRP MMD uses one shader for opaque and transparent materials.
                // Recover transparent state from HDRP/Lit-style properties when shader name no longer contains "Transparent".
                return 2f;
            }

            if (alphaCutoffEnabled || renderQueue >= AlphaTestQueue)
            {
                return 1f;
            }

            return 0f;
        }

        private static bool IsMmdTransparentShader(string sourceShaderName)
        {
            return ShaderNameContains(sourceShaderName, "Transparent");
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

        private static float GetMmdSourceRenderQueue(Material material)
        {
            var renderQueue = GetMmdRenderQueue(material);
            if (renderQueue >= 0f && renderQueue != material.renderQueue)
            {
                return renderQueue;
            }

            var materialPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(material));
            var storedRenderQueue = GetConvertedMaterialSourceRenderQueue(materialPath);
            return storedRenderQueue >= 0f ? storedRenderQueue : renderQueue;
        }

        private static void ApplyOfficialHdrpMmdRenderState(Material material, float blendMode, float cullMode, float sourceRenderQueue)
        {
            var blendModeInt = Mathf.RoundToInt(blendMode);
            var isCutout = blendModeInt == 1;
            var isTransparent = blendModeInt == 2;
            var renderQueue = sourceRenderQueue >= 0f
                ? Mathf.RoundToInt(sourceRenderQueue)
                : isTransparent
                    ? TransparentQueue
                    : isCutout
                        ? AlphaTestQueue
                        : GeometryQueue;

            SetFloatIfPresent(material, "_SurfaceType", isTransparent ? 1f : 0f);
            SetFloatIfPresent(material, "_BlendMode", isTransparent ? 0f : 0f);
            SetFloatIfPresent(material, "_SrcBlend", isTransparent ? BlendSrcAlpha : BlendOne);
            SetFloatIfPresent(material, "_DstBlend", isTransparent ? BlendOneMinusSrcAlpha : BlendZero);
            SetFloatIfPresent(material, "_AlphaSrcBlend", BlendOne);
            SetFloatIfPresent(material, "_AlphaDstBlend", isTransparent ? BlendOneMinusSrcAlpha : BlendZero);
            SetFloatIfPresent(material, "_ZWrite", isTransparent ? 0f : 1f);
            SetFloatIfPresent(material, "_TransparentZWrite", 0f);
            SetFloatIfPresent(material, "_CullMode", cullMode);
            SetFloatIfPresent(material, "_CullModeForward", cullMode);
            SetFloatIfPresent(material, "_TransparentCullMode", cullMode);
            SetFloatIfPresent(material, "_OpaqueCullMode", cullMode);
            SetFloatIfPresent(material, "_EnableFogOnTransparent", isTransparent ? 1f : 0f);
            SetFloatIfPresent(material, "_AlphaCutoffEnable", isCutout ? 1f : 0f);
            SetFloatIfPresent(material, "_AlphaToMask", 0f);
            SetFloatIfPresent(material, "_TransparentDepthPrepassEnable", 0f);
            SetFloatIfPresent(material, "_TransparentDepthPostpassEnable", 0f);
            SetFloatIfPresent(material, "_TransparentBackfaceEnable", 0f);
            SetFloatIfPresent(material, "_RenderQueue", renderQueue);
            material.renderQueue = renderQueue;
        }

        private static void ApplyMmdRenderQueueState(
            Material material,
            float blendMode,
            float sourceRenderQueue,
            bool usesMmdTransparentShader,
            MmdTransparentFogMode mmdTransparentFogMode)
        {
            if (UsesHdrpMmdStackRange(mmdTransparentFogMode))
            {
                var fallbackSourceQueue = Mathf.RoundToInt(blendMode) == 2
                    ? MmdTransparentDefaultSourceQueue
                    : MmdOpaqueDefaultSourceQueue;

                material.renderQueue = GetHdrpQueueFromMmdSourceQueue(sourceRenderQueue, fallbackSourceQueue);
                return;
            }

            if (usesMmdTransparentShader
                && Mathf.RoundToInt(blendMode) == 2
                && UsesHdrpTransparentRangeForMmd(mmdTransparentFogMode))
            {
                material.renderQueue = GetHdrpTransparentQueueFromMmdSourceQueue(sourceRenderQueue);
                return;
            }

            var mappedSourceRenderQueue = GetLiveToonQueueFromMmdSourceQueue(sourceRenderQueue);
            if (mappedSourceRenderQueue >= 0)
            {
                material.renderQueue = mappedSourceRenderQueue;
                return;
            }

            switch ((int)blendMode)
            {
                case 0:
                    material.renderQueue = GetLiveToonQueueFromMmdSourceQueue(MmdOpaqueDefaultSourceQueue);
                    break;
                case 1:
                    material.renderQueue = AlphaTestQueue;
                    break;
                case 2:
                    material.renderQueue = usesMmdTransparentShader && sourceRenderQueue < 0f
                        ? GetLiveToonQueueFromMmdSourceQueue(MmdTransparentDefaultSourceQueue)
                        : TransparentQueue + GetMmdTransparentRenderQueueOffset(sourceRenderQueue);
                    break;
                case 3:
                    material.renderQueue = TransparentWithZWriteQueue + GetMmdTransparentWithZWriteRenderQueueOffset(sourceRenderQueue);
                    break;
            }
        }

        private static int GetLiveToonQueueFromMmdSourceQueue(float sourceRenderQueue)
        {
            if (sourceRenderQueue < 0f)
            {
                return -1;
            }

            var roundedRenderQueue = Mathf.RoundToInt(sourceRenderQueue);
            if (roundedRenderQueue >= GeometryQueue && roundedRenderQueue < AlphaTestQueue)
            {
                // MMD4Mecanim conversion: preserve the exact Geometry+N queue because MMD interleaves opaque and transparent materials there.
                return roundedRenderQueue;
            }

            return -1;
        }

        private static int GetHdrpTransparentQueueFromMmdSourceQueue(float sourceRenderQueue)
        {
            return GetHdrpQueueFromMmdSourceQueue(sourceRenderQueue, MmdTransparentDefaultSourceQueue);
        }

        private static int GetHdrpQueueFromMmdSourceQueue(float sourceRenderQueue, int fallbackSourceQueue)
        {
            var roundedRenderQueue = sourceRenderQueue >= 0f
                ? Mathf.RoundToInt(sourceRenderQueue)
                : fallbackSourceQueue;

            if (roundedRenderQueue >= GeometryQueue && roundedRenderQueue <= GeometryLastQueue)
            {
                // MMD4Mecanim conversion: move the whole Geometry+N stack after HDRP opaque fog while preserving its relative order.
                // Moving only transparent materials breaks models that interleave opaque and transparent sleeves in the same MMD queue band.
                return TransparentQueue + Mathf.Clamp(roundedRenderQueue - GeometryQueue, 0, MmdHdrpTransparentQueueSpan - 1);
            }

            if (roundedRenderQueue >= TransparentQueue - MmdHdrpTransparentQueueSpan
                && roundedRenderQueue <= TransparentQueue + MmdHdrpTransparentQueueSpan)
            {
                return roundedRenderQueue;
            }

            return TransparentQueue + Mathf.Clamp(fallbackSourceQueue - GeometryQueue, 0, MmdHdrpTransparentQueueSpan - 1);
        }

        private static bool UsesHdrpMmdStackRange(MmdTransparentFogMode mmdTransparentFogMode)
        {
            return mmdTransparentFogMode == MmdTransparentFogMode.HdrpMmdStackRangeWithSurfaceFog
                || mmdTransparentFogMode == MmdTransparentFogMode.HdrpMmdStackRangeNoSurfaceFog;
        }

        private static bool UsesHdrpTransparentRangeForMmd(MmdTransparentFogMode mmdTransparentFogMode)
        {
            return mmdTransparentFogMode == MmdTransparentFogMode.HdrpTransparentRangeNoSurfaceFog
                || mmdTransparentFogMode == MmdTransparentFogMode.HdrpTransparentRangeWithSurfaceFog;
        }

        private static bool UsesMmdSurfaceFog(float blendMode, bool usesMmdTransparentShader, MmdTransparentFogMode mmdTransparentFogMode)
        {
            if (mmdTransparentFogMode == MmdTransparentFogMode.HdrpMmdStackRangeWithSurfaceFog)
            {
                return true;
            }

            return usesMmdTransparentShader
                && Mathf.RoundToInt(blendMode) == 2
                && (mmdTransparentFogMode == MmdTransparentFogMode.PreserveMmdQueueWithSurfaceFog
                    || mmdTransparentFogMode == MmdTransparentFogMode.HdrpTransparentRangeWithSurfaceFog);
        }

        private static int GetMmdTransparentRenderQueueOffset(float sourceRenderQueue)
        {
            if (sourceRenderQueue < 0f)
            {
                return 0;
            }

            var roundedRenderQueue = Mathf.RoundToInt(sourceRenderQueue);
            if (roundedRenderQueue >= TransparentQueue - MToonTransparentQueueSpan + 1 && roundedRenderQueue <= TransparentQueue)
            {
                return roundedRenderQueue - TransparentQueue;
            }

            return 0;
        }

        private static int GetMmdTransparentWithZWriteRenderQueueOffset(float sourceRenderQueue)
        {
            if (sourceRenderQueue < 0f)
            {
                return 0;
            }

            var roundedRenderQueue = Mathf.RoundToInt(sourceRenderQueue);
            if (roundedRenderQueue >= 2000 && roundedRenderQueue < 2500)
            {
                return Mathf.Clamp(roundedRenderQueue - 2000, 0, MToonTransparentQueueSpan - 1);
            }

            if (roundedRenderQueue >= TransparentWithZWriteQueue
                && roundedRenderQueue < TransparentWithZWriteQueue + MToonTransparentQueueSpan)
            {
                return roundedRenderQueue - TransparentWithZWriteQueue;
            }

            return 0;
        }

        private static void ApplyMmdAlphaState(
            Material material,
            float blendMode,
            bool usesMmdTransparentShader,
            MmdTransparentFogMode mmdTransparentFogMode)
        {
            var roundedBlendMode = Mathf.RoundToInt(blendMode);
            var usesMmdSurfaceFog = UsesMmdSurfaceFog(blendMode, usesMmdTransparentShader, mmdTransparentFogMode);
            SetFloatIfPresent(material, "_TransparentThreshold", 0f);
            // MMD4Mecanim conversion: MMDLit clips transparent fragments below 1/255 before blending/depth writes.
            SetFloatIfPresent(material, "_TransparentClipThreshold", usesMmdTransparentShader ? MmdTransparentClipThreshold : 0.001f);
            SetFloatIfPresent(material, "_TransparentFogAlphaWeight", usesMmdTransparentShader ? MmdTransparentFogAlphaWeight : 0f);
            // MMD4Mecanim conversion: only fog in-shader when the material is rendered after HDRP's opaque fog pass.
            SetFloatIfPresent(material, "_TransparentFogIntensity", usesMmdSurfaceFog ? 1f : 0f);
            SetFloatIfPresent(material, "_MmdTransparentDepthWrite", 0f);
            SetKeyword(material, "_ENABLE_FOG_ON_TRANSPARENT", usesMmdSurfaceFog);

            if (usesMmdTransparentShader && roundedBlendMode == 2)
            {
                if (usesMmdSurfaceFog)
                {
                    // MMD4Mecanim conversion: keep the MMD Offset/ZWrite path, but fog in the forward pass after HDRP's opaque fog.
                    SetFloatIfPresent(material, "_TransparentFogIntensity", 1f);
                }
                else
                {
                    // MMD4Mecanim conversion: preserve the original Geometry+N-style path and let HDRP opaque fog handle non-shifted queues.
                    SetFloatIfPresent(material, "_TransparentFogIntensity", MmdTransparentFogIntensity);
                }

                // MMD4Mecanim conversion: keep MMD's forward ZWrite + Offset path for sleeve and hair ordering.
                SetFloatIfPresent(material, "_MmdTransparentDepthWrite", 0f);
                SetRenderStateFloats(material, BlendSrcAlpha, BlendOneMinusSrcAlpha, zWrite: 1f, alphaToMask: 0f);
                SetTransparentDepthPasses(material, false);
                return;
            }

            if (Mathf.RoundToInt(blendMode) == 3)
            {
                SetFloatIfPresent(material, "_AlphaCutoffPrepass", 0.001f);
                SetFloatIfPresent(material, "_AlphaCutoffPostpass", 0.001f);
            }
        }

        private static void ApplyMmdDepthOffsetState(Material material, bool usesMmdTransparentShader, bool usesMmdOutline)
        {
            // MMD4Mecanim conversion: MMDLit-Transparent uses Offset -0.1, -1 to pull transparent depth slightly forward.
            SetFloatIfPresent(material, "_MmdForwardOffsetFactor", usesMmdTransparentShader ? MmdTransparentForwardOffsetFactor : MmdDefaultForwardOffsetFactor);
            SetFloatIfPresent(material, "_MmdForwardOffsetUnits", usesMmdTransparentShader ? MmdTransparentForwardOffsetUnits : MmdDefaultForwardOffsetUnits);

            // MMD4Mecanim conversion: MMDLit-Edge uses Offset 0.1, 1, while LiveToon defaults to its original 1, 1.
            SetFloatIfPresent(material, "_MmdOutlineOffsetFactor", usesMmdOutline ? MmdOutlineOffsetFactor : MmdDefaultOutlineOffsetFactor);
            SetFloatIfPresent(material, "_MmdOutlineOffsetUnits", usesMmdOutline ? MmdOutlineOffsetUnits : MmdDefaultOutlineOffsetUnits);

            // MMD4Mecanim conversion: MMDLit transparent/edge passes use ColorMask RGB, which avoids writing transparent silhouettes into HDRP's alpha buffer.
            SetFloatIfPresent(material, "_MmdForwardColorMask", usesMmdTransparentShader ? MmdColorMaskRgb : MmdColorMaskRgba);
            SetFloatIfPresent(material, "_MmdOutlineColorMask", usesMmdOutline ? MmdColorMaskRgb : MmdColorMaskRgba);
        }

        private static Color GetMmdShadeColor(Material material, Color litColor)
        {
            var fallbackAmbient = new Color(litColor.r * 0.65f, litColor.g * 0.65f, litColor.b * 0.65f, litColor.a);
            var ambient = GetColor(material, "_Ambient", fallbackAmbient);
            var shadeColor = Color.Lerp(litColor, ambient, 0.35f);
            shadeColor.a = litColor.a;
            return ClampColor01(shadeColor);
        }

        private static LiveToonOutlineMaterialState CaptureMmdOutlineState(
            Material material,
            string sourceShaderName,
            bool allowPropertyOnlyOutline)
        {
            var edgeSize = GetFloat(material, "_EdgeSize", 0f);
            var edgeColor = GetColor(material, "_EdgeColor", Color.black);
            var usesOutline = (allowPropertyOnlyOutline || UsesMmdOutline(sourceShaderName)) && edgeSize > 0f && edgeColor.a > 0.001f;

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

        private static bool UsesMmdOutline(string sourceShaderName)
        {
            return ShaderNameContains(sourceShaderName, "Edge");
        }

        private static bool UsesMmdTransparentFogOption(LiveToonShaderConversionSource conversionSource)
        {
            return conversionSource == LiveToonShaderConversionSource.MMD4Mecanim;
        }

        private static bool ShaderNameContains(string shaderName, string token)
        {
            return !string.IsNullOrEmpty(shaderName)
                && shaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMmd4MecanimShader(string shaderName)
        {
            return ShaderNameContains(shaderName, "MMD4Mecanim/");
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

        private static LiveToonSpecularMaterialState CaptureMmdSpecularState(Material material)
        {
            var sourceSpecular = GetColor(material, "_Specular", Color.black);
            var maxSpecular = Mathf.Max(sourceSpecular.r, Mathf.Max(sourceSpecular.g, sourceSpecular.b));
            var specularHighlights = GetFloat(material, "_SpecularHighlights", 1f);
            var specularPower = Mathf.Clamp(GetFloat(material, "_Shininess", MmdDefaultSpecularPower), 1f, 256f);
            if (specularHighlights <= 0f || maxSpecular <= MmdVisibleSpecularThreshold)
            {
                return new LiveToonSpecularMaterialState
                {
                    Color = Color.black,
                    Intensity = 0f,
                    Power = specularPower
                };
            }

            return new LiveToonSpecularMaterialState
            {
                Color = ClampColor01(new Color(
                    sourceSpecular.r / maxSpecular,
                    sourceSpecular.g / maxSpecular,
                    sourceSpecular.b / maxSpecular,
                    1f)),
                Intensity = Mathf.Clamp01(maxSpecular),
                Power = specularPower
            };
        }

        private static void ApplyMToonSpecularState(Material material)
        {
            SetColorIfPresent(material, "_SpecColor", Color.black);
            SetColorIfPresent(material, "_MmdSpecularColor", Color.black);
            SetFloatIfPresent(material, "_Intensity", 0f);
            SetFloatIfPresent(material, "_MmdSpecularIntensity", 0f);
            SetFloatIfPresent(material, "_MmdSpecularPower", MmdDefaultSpecularPower);
        }

        private static void ApplyMmdSpecularState(Material material, LiveToonSpecularMaterialState specularState)
        {
            SetColorIfPresent(material, "_SpecColor", specularState.Color);
            SetColorIfPresent(material, "_MmdSpecularColor", specularState.Color);
            SetFloatIfPresent(material, "_Intensity", specularState.Intensity);
            SetFloatIfPresent(material, "_MmdSpecularIntensity", specularState.Intensity);
            SetFloatIfPresent(material, "_Shininess", specularState.Power);
            SetFloatIfPresent(material, "_Sharpness", Mathf.Clamp(specularState.Power * 0.45f, 8f, 80f));
            SetFloatIfPresent(material, "_MmdSpecularPower", specularState.Power);
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
                    SetFloatIfPresent(material, "_TransparentFogAlphaWeight", 0f);
                    SetFloatIfPresent(material, "_TransparentFogIntensity", 0f);
                    SetRenderStateFloats(material, BlendOne, BlendZero, zWrite: 1f, alphaToMask: 0f);
                    SetFloatIfPresent(material, "_ZTeForLiOpa", ZTestLessEqual);
                    SetTransparentDepthPasses(material, false);
                    material.renderQueue = LiveToonFogBaseQueue;
                    break;
                case 1:
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    SetFloatIfPresent(material, "_TransparentFogAlphaWeight", 0f);
                    SetFloatIfPresent(material, "_TransparentFogIntensity", 0f);
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
                    // MToon conversion: reset stale MMD fog state so TransparentWithZWrite is fogged by LiveToon/HDRP.
                    SetFloatIfPresent(material, "_TransparentFogAlphaWeight", 0f);
                    SetFloatIfPresent(material, "_TransparentFogIntensity", 1f);
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
                    // MToon conversion: reset stale MMD fog state so Transparent is fogged by LiveToon/HDRP.
                    SetFloatIfPresent(material, "_TransparentFogAlphaWeight", 0f);
                    SetFloatIfPresent(material, "_TransparentFogIntensity", 1f);
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
            SetFloatIfPresent(material, "_TransparentDepthPrepassEnable", enabled ? 1f : 0f);
            SetFloatIfPresent(material, "_TransparentDepthPostpassEnable", enabled ? 1f : 0f);
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

        private static Vector4 GetVector(Material material, string propertyName, Vector4 fallback)
        {
            return material.HasProperty(propertyName) ? material.GetVector(propertyName) : fallback;
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

        private static void SetVectorIfPresent(Material material, string propertyName, Vector4 value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetVector(propertyName, value);
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

        private static Material CreateConvertedMaterial(Material sourceMaterial, LiveToonShaderConversionSource conversionSource)
        {
            var sourceAssetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(sourceMaterial));
            var convertedPath = GetConvertedMaterialPath(sourceMaterial, sourceAssetPath, conversionSource);
            var convertedSuffix = GetConvertedSuffix(conversionSource);
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
            var existingConvertedMaterial = AssetDatabase.LoadAssetAtPath<Material>(convertedPath);
            if (existingConvertedMaterial != null)
            {
                Undo.RecordObject(existingConvertedMaterial, "Update LiveToon Material Copy");
                EditorUtility.CopySerialized(sourceMaterial, existingConvertedMaterial);
                existingConvertedMaterial.name = $"{sourceMaterial.name}{convertedSuffix}";
                EditorUtility.SetDirty(existingConvertedMaterial);
                SetConvertedMaterialSourceIdentity(convertedPath, sourceMaterial, sourceAssetPath);
                return existingConvertedMaterial;
            }

            convertedPath = AssetDatabase.GenerateUniqueAssetPath(convertedPath);
            if (string.IsNullOrEmpty(convertedPath))
            {
                Debug.LogWarning($"LiveToon shader conversion skipped material '{sourceMaterial.name}' because Unity could not generate a material path under: {convertedDirectory}", sourceMaterial);
                return null;
            }

            var convertedMaterial = new Material(sourceMaterial)
            {
                name = $"{sourceMaterial.name}{convertedSuffix}"
            };

            AssetDatabase.CreateAsset(convertedMaterial, convertedPath);
            Undo.RegisterCreatedObjectUndo(convertedMaterial, "Create LiveToon Material Copy");
            SetConvertedMaterialSourceIdentity(convertedPath, sourceMaterial, sourceAssetPath);
            return convertedMaterial;
        }

        private static string GetConvertedMaterialPath(
            Material sourceMaterial,
            string sourceAssetPath,
            LiveToonShaderConversionSource conversionSource)
        {
            var convertedDirectoryName = GetConvertedDirectoryName(conversionSource);
            var convertedSuffix = GetConvertedSuffix(conversionSource);
            var generatedMaterialsDirectory = GetGeneratedMaterialsDirectory(conversionSource);

            if (!string.IsNullOrEmpty(sourceAssetPath) && IsConvertedMaterialPath(sourceAssetPath))
            {
                var convertedSourceDirectory = GetAssetDirectoryName(sourceAssetPath);
                var convertedSourceName = GetAssetFileNameWithoutExtension(sourceAssetPath);
                var originalLikeName = RemoveConvertedSuffix(convertedSourceName) ?? convertedSourceName;
                var convertedSourceExtension = GetMaterialAssetExtension(sourceAssetPath);
                return $"{convertedSourceDirectory}/{SanitizeAssetFileName(originalLikeName)}{convertedSuffix}{convertedSourceExtension}";
            }

            var sourceFileName = UseSourceMaterialNameForConvertedAsset(sourceMaterial, sourceAssetPath)
                ? SanitizeAssetFileName(sourceMaterial.name)
                : SanitizeAssetFileName(GetAssetFileNameWithoutExtension(sourceAssetPath));
            var extension = GetMaterialAssetExtension(sourceAssetPath);
            var convertedFileName = $"{sourceFileName}{convertedSuffix}{extension}";

            if (!string.IsNullOrEmpty(sourceAssetPath) && sourceAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var sourceDirectory = GetAssetDirectoryName(sourceAssetPath);
                if (!string.IsNullOrEmpty(sourceDirectory))
                {
                    return $"{sourceDirectory}/{convertedDirectoryName}/{convertedFileName}";
                }
            }

            var sourceGuid = string.IsNullOrEmpty(sourceAssetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(sourceAssetPath);
            if (!string.IsNullOrEmpty(sourceGuid) && sourceGuid.Length >= 8)
            {
                convertedFileName = $"{sourceFileName}{convertedSuffix}_{sourceGuid.Substring(0, 8)}{extension}";
            }

            return $"{generatedMaterialsDirectory}/{convertedFileName}";
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

            if (convertedPath.StartsWith($"{GeneratedMaterialsDirectory}/", StringComparison.OrdinalIgnoreCase)
                || convertedPath.StartsWith($"{OfficialHdrpMmdGeneratedMaterialsDirectory}/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var markerIndex = GetConvertedDirectoryMarkerIndex(convertedPath);
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

            foreach (var suffix in GetConvertedSuffixes())
            {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return fileName.Substring(0, fileName.Length - suffix.Length);
                }

                var generatedMarker = $"{suffix}_";
                var markerIndex = fileName.LastIndexOf(generatedMarker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0)
                {
                    return fileName.Substring(0, markerIndex);
                }
            }

            return null;
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
            return GetConvertedDirectoryMarkerIndex(normalizedPath) >= 0
                && !string.IsNullOrEmpty(RemoveConvertedSuffix(fileName));
        }

        private static string GetConvertedDirectoryName(LiveToonShaderConversionSource conversionSource)
        {
            return conversionSource == LiveToonShaderConversionSource.OfficialHDRPMMD
                ? OfficialHdrpMmdConvertedDirectoryName
                : ConvertedDirectoryName;
        }

        private static string GetConvertedSuffix(LiveToonShaderConversionSource conversionSource)
        {
            return conversionSource == LiveToonShaderConversionSource.OfficialHDRPMMD
                ? OfficialHdrpMmdConvertedSuffix
                : ConvertedSuffix;
        }

        private static string GetGeneratedMaterialsDirectory(LiveToonShaderConversionSource conversionSource)
        {
            return conversionSource == LiveToonShaderConversionSource.OfficialHDRPMMD
                ? OfficialHdrpMmdGeneratedMaterialsDirectory
                : GeneratedMaterialsDirectory;
        }

        private static string[] GetConvertedSuffixes()
        {
            return new[] { ConvertedSuffix, OfficialHdrpMmdConvertedSuffix };
        }

        private static int GetConvertedDirectoryMarkerIndex(string normalizedPath)
        {
            foreach (var directoryName in new[] { ConvertedDirectoryName, OfficialHdrpMmdConvertedDirectoryName })
            {
                var marker = $"/{directoryName}/";
                var markerIndex = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0)
                {
                    return markerIndex;
                }
            }

            return -1;
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
            var sourceRenderQueueLine = $"{SourceMaterialRenderQueuePrefix}{GetMmdRenderQueue(sourceMaterial)}";
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

        private static float GetConvertedMaterialSourceRenderQueue(string convertedPath)
        {
            var value = GetConvertedMaterialSourceUserDataValue(convertedPath, SourceMaterialRenderQueuePrefix);
            return float.TryParse(value, out var renderQueue) ? renderQueue : -1f;
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
