using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VLiveKit.LiveToon.Editor;

namespace toshi.VLiveKit.livetoon.Editor
{
    public sealed class LiveToonInspector_MToonBase : ShaderGUI
    {
        private const string MToonInspectorTypeName = "MToon.MToonInspector";

        private ShaderGUI mtoonInspector;
        private bool showLiveToonOptions = true;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            var baseInspector = GetMToonInspector();
            if (baseInspector != null)
            {
                baseInspector.OnGUI(materialEditor, properties);
            }
            else
            {
                base.OnGUI(materialEditor, properties);
            }

            DrawLiveToonOptions(materialEditor, properties);
        }

        private ShaderGUI GetMToonInspector()
        {
            if (mtoonInspector != null)
            {
                return mtoonInspector;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(MToonInspectorTypeName);
                if (type == null || !typeof(ShaderGUI).IsAssignableFrom(type))
                {
                    continue;
                }

                mtoonInspector = Activator.CreateInstance(type) as ShaderGUI;
                break;
            }

            return mtoonInspector;
        }

        private void DrawLiveToonOptions(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUILayout.Space(8);
            showLiveToonOptions = EditorGUILayout.BeginFoldoutHeaderGroup(showLiveToonOptions, "LiveToon Options");
            if (showLiveToonOptions)
            {
                EditorGUI.indentLevel++;

                DrawSectionLabel("Environment Lighting");
                DrawProperty(materialEditor, properties, "_IndirectLightIntensity");
                DrawProperty(materialEditor, properties, "_ReflectionProbeIntensity");
                DrawProperty(materialEditor, properties, "_ReflectionProbeSmoothness");

                DrawSectionLabel("Local Lights");
                DrawProperty(materialEditor, properties, "_PunctualLightIntensity");
                DrawProperty(materialEditor, properties, "_FallbackLightIntensity");
                DrawProperty(materialEditor, properties, "_FallbackLightColor");
                DrawProperty(materialEditor, properties, "_CustomRimIntensity");

                DrawSectionLabel("Wet Skin");
                DrawProperty(materialEditor, properties, "_SweatIntensity");
                DrawProperty(materialEditor, properties, "_SweatScale");
                DrawProperty(materialEditor, properties, "_SweatSpeed");
                DrawProperty(materialEditor, properties, "_SweatHighlight");
                DrawProperty(materialEditor, properties, "_SweatTrail");
                DrawProperty(materialEditor, properties, "_SweatColor");
                DrawProperty(materialEditor, properties, "_WetHairOverlayTex");
                DrawProperty(materialEditor, properties, "_WetHairOverlayColor");
                DrawProperty(materialEditor, properties, "_WetHairOverlayIntensity");
                DrawProperty(materialEditor, properties, "_WetHairOverlayGloss");

                DrawSectionLabel("Hair Specular");
                DrawProperty(materialEditor, properties, "_SpecColor");
                DrawProperty(materialEditor, properties, "_Intensity");
                DrawProperty(materialEditor, properties, "_Sharpness");
                DrawProperty(materialEditor, properties, "_Position");
                DrawProperty(materialEditor, properties, "_JitterIntensity");
                DrawProperty(materialEditor, properties, "_JitterTex");
                DrawDefaultJitterTextureButton(materialEditor);

                DrawSectionLabel("MMD Specular");
                DrawProperty(materialEditor, properties, "_MmdSpecularColor");
                DrawProperty(materialEditor, properties, "_MmdSpecularIntensity");
                DrawProperty(materialEditor, properties, "_MmdSpecularPower");

                DrawSectionLabel("MMD Texture Effects");
                DrawProperty(materialEditor, properties, "_MmdToonTex");
                DrawProperty(materialEditor, properties, "_MmdToonTexIntensity");
                DrawProperty(materialEditor, properties, "_MmdShadowLum");
                DrawProperty(materialEditor, properties, "_MmdToonTone");
                DrawProperty(materialEditor, properties, "_MmdSphereCube");
                DrawProperty(materialEditor, properties, "_MmdSphereMode");
                DrawProperty(materialEditor, properties, "_MmdSphereIntensity");

                DrawSectionLabel("Perspective");
                DrawProperty(materialEditor, properties, "_LiveToonPerspectiveCorrectionIntensity");
                DrawProperty(materialEditor, properties, "_LiveToonPerspectiveCorrectionCenterWS");
                DrawProperty(materialEditor, properties, "_LiveToonPerspectiveCorrectionGroundY");
                DrawProperty(materialEditor, properties, "_LiveToonPerspectiveCorrectionHeight");
                DrawProperty(materialEditor, properties, "_LiveToonPerspectiveCorrectionHeightPower");

                DrawSectionLabel("Material Role");
                DrawProperty(materialEditor, properties, "_IsFace");
                DrawProperty(materialEditor, properties, "_isHair");

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawSectionLabel(string label)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        private static void DrawProperty(MaterialEditor materialEditor, MaterialProperty[] properties, string name)
        {
            var property = FindPropertyOptional(properties, name);
            if (property == null)
            {
                return;
            }

            materialEditor.ShaderProperty(property, property.displayName);
        }

        private static void DrawDefaultJitterTextureButton(MaterialEditor materialEditor)
        {
            var missingMaterials = CollectMissingJitterTextureMaterials(materialEditor);
            if (missingMaterials.Count == 0 || LiveToonDefaultAssets.LoadDefaultJitterTexture() == null)
            {
                return;
            }

            if (!GUILayout.Button("Assign Default Jitter Texture"))
            {
                return;
            }

            Undo.RecordObjects(missingMaterials.ToArray(), "Assign LiveToon Jitter Texture");
            foreach (var material in missingMaterials)
            {
                if (LiveToonDefaultAssets.EnsureDefaultJitterTexture(material))
                {
                    EditorUtility.SetDirty(material);
                }
            }
        }

        private static List<Material> CollectMissingJitterTextureMaterials(MaterialEditor materialEditor)
        {
            var materials = new List<Material>();
            foreach (var target in materialEditor.targets)
            {
                var material = target as Material;
                if (material == null || !LiveToonDefaultAssets.NeedsDefaultJitterTexture(material))
                {
                    continue;
                }

                materials.Add(material);
            }

            return materials;
        }

        private static MaterialProperty FindPropertyOptional(MaterialProperty[] properties, string name)
        {
            for (var i = 0; i < properties.Length; i++)
            {
                var property = properties[i];
                if (property != null && property.name == name)
                {
                    return property;
                }
            }

            return null;
        }
    }
}
