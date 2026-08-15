using UnityEditor;
using UnityEngine;

namespace VLiveKit.LiveToon.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(LiveToonShaderConverter))]
    public sealed class LiveToonShaderConverterEditor : UnityEditor.Editor
    {
        private SerializedProperty targetObjectProperty;
        private SerializedProperty shaderToUseProperty;
        private SerializedProperty conversionSourceProperty;
        private SerializedProperty mmdTransparentFogModeProperty;
        private SerializedProperty createMaterialBackupsProperty;
        private SerializedProperty disableOutlineOnConvertProperty;

        private void OnEnable()
        {
            targetObjectProperty = serializedObject.FindProperty("targetObject");
            shaderToUseProperty = serializedObject.FindProperty("shaderToUse");
            conversionSourceProperty = serializedObject.FindProperty("conversionSource");
            mmdTransparentFogModeProperty = serializedObject.FindProperty("mmdTransparentFogMode");
            createMaterialBackupsProperty = serializedObject.FindProperty("createMaterialBackups");
            disableOutlineOnConvertProperty = serializedObject.FindProperty("disableOutlineOnConvert");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(targetObjectProperty);
            EditorGUILayout.PropertyField(conversionSourceProperty, new GUIContent("Conversion Mode"));
            var conversionSource = (LiveToonShaderConversionSource)conversionSourceProperty.enumValueIndex;
            var requiresTargetShader = RequiresTargetShader(conversionSource);
            if (requiresTargetShader)
            {
                EditorGUILayout.PropertyField(shaderToUseProperty);
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("MToon Shader", ResolveMToonShader(), typeof(Shader), false);
                }
            }

            using (new EditorGUI.DisabledScope(!UsesMmdTransparentFogOption(conversionSource)))
            {
                EditorGUILayout.PropertyField(mmdTransparentFogModeProperty, new GUIContent("MMD Transparent Path"));
            }

            using (new EditorGUI.DisabledScope(!requiresTargetShader))
            {
                EditorGUILayout.PropertyField(createMaterialBackupsProperty, new GUIContent("Also Create Legacy Backups"));
                EditorGUILayout.PropertyField(disableOutlineOnConvertProperty, new GUIContent("Disable Outline After Convert"));
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            var converter = (LiveToonShaderConverter)target;
            var targetObject = converter.TargetObject;
            var shaderToUse = converter.ShaderToUse;

            if (targetObject == null)
            {
                EditorGUILayout.HelpBox("Target Object is empty.", MessageType.None);
            }

            if (requiresTargetShader && shaderToUse == null)
            {
                EditorGUILayout.HelpBox($"Shader not found: {LiveToonShaderConverter.DefaultShaderName}", MessageType.None);
            }

            using (new EditorGUI.DisabledScope(targetObject == null || (requiresTargetShader && shaderToUse == null)))
            {
                if (GUILayout.Button(GetConvertButtonLabel(conversionSource)))
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

                if (GUILayout.Button("Restore Legacy Material Assets From Backups"))
                {
                    RestoreLegacyBackupsForSelectedComponents();
                }
            }
        }

        private static bool UsesMmdTransparentFogOption(LiveToonShaderConversionSource conversionSource)
        {
            return conversionSource == LiveToonShaderConversionSource.MMD4Mecanim;
        }

        private static bool RequiresTargetShader(LiveToonShaderConversionSource conversionSource)
        {
            return conversionSource != LiveToonShaderConversionSource.LiveToonToMToon;
        }

        private static string GetConvertButtonLabel(LiveToonShaderConversionSource conversionSource)
        {
            return conversionSource == LiveToonShaderConversionSource.LiveToonToMToon
                ? "Create And Assign MToon Copies"
                : "Convert And Assign Material Copies";
        }

        private static Shader ResolveMToonShader()
        {
            return Shader.Find(LiveToonShaderConverter.MToonShaderName)
                ?? Shader.Find(LiveToonShaderConverter.MToon10ShaderName)
                ?? Shader.Find(LiveToonShaderConverter.MToon10BuiltinShaderName);
        }

        private void ConvertSelectedComponents()
        {
            foreach (LiveToonShaderConverter converter in targets)
            {
                var result = ShaderConverterTool.ConvertShadersForObject(
                    converter.TargetObject,
                    converter.ShaderToUse,
                    converter.CreateMaterialBackups,
                    converter.DisableOutlineOnConvert,
                    converter.ConversionSource,
                    converter.MmdTransparentFogMode);
                Debug.Log(converter.ConversionSource == LiveToonShaderConversionSource.LiveToonToMToon
                    ? result.ToLiveToonToMToonLog()
                    : result.ToConversionLog(), converter);
            }
        }

        private void RestoreOriginalAssignmentsForSelectedComponents()
        {
            foreach (LiveToonShaderConverter converter in targets)
            {
                var result = ShaderConverterTool.RestoreOriginalMaterialAssignments(converter.TargetObject);
                Debug.Log(result.ToOriginalAssignmentRestoreLog(), converter);
            }
        }

        private void RestoreLegacyBackupsForSelectedComponents()
        {
            foreach (LiveToonShaderConverter converter in targets)
            {
                var result = ShaderConverterTool.RestoreMaterialAssetsFromBackups(converter.TargetObject);
                Debug.Log(result.ToLegacyBackupRestoreLog(), converter);
            }
        }
    }
}
