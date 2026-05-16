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
        private SerializedProperty createMaterialBackupsProperty;
        private SerializedProperty disableOutlineOnConvertProperty;

        private void OnEnable()
        {
            targetObjectProperty = serializedObject.FindProperty("targetObject");
            shaderToUseProperty = serializedObject.FindProperty("shaderToUse");
            createMaterialBackupsProperty = serializedObject.FindProperty("createMaterialBackups");
            disableOutlineOnConvertProperty = serializedObject.FindProperty("disableOutlineOnConvert");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(targetObjectProperty);
            EditorGUILayout.PropertyField(shaderToUseProperty);
            EditorGUILayout.PropertyField(createMaterialBackupsProperty, new GUIContent("Also Create Legacy Backups"));
            EditorGUILayout.PropertyField(disableOutlineOnConvertProperty, new GUIContent("Disable Outline After Convert"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            var converter = (LiveToonShaderConverter)target;
            var targetObject = converter.TargetObject;
            var shaderToUse = converter.ShaderToUse;

            if (targetObject == null)
            {
                EditorGUILayout.HelpBox("Target Object is empty.", MessageType.None);
            }

            if (shaderToUse == null)
            {
                EditorGUILayout.HelpBox($"Shader not found: {LiveToonShaderConverter.DefaultShaderName}", MessageType.None);
            }

            using (new EditorGUI.DisabledScope(targetObject == null || shaderToUse == null))
            {
                if (GUILayout.Button("Convert And Assign Material Copies"))
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

        private void ConvertSelectedComponents()
        {
            foreach (LiveToonShaderConverter converter in targets)
            {
                var result = ShaderConverterTool.ConvertShadersForObject(
                    converter.TargetObject,
                    converter.ShaderToUse,
                    converter.CreateMaterialBackups,
                    converter.DisableOutlineOnConvert);
                Debug.Log(result.ToConversionLog(), converter);
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
