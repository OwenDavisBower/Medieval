using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Medieval.Editor
{
    /// <summary>
    /// URP Lit-style inspector for Water.shader, including Scroll Speed Y.
    /// </summary>
    public class WaterShaderGUI : BaseShaderGUI
    {
        static readonly string[] s_WorkflowModeNames = Enum.GetNames(typeof(LitGUI.WorkflowMode));

        static readonly GUIContent s_DetailInputs = EditorGUIUtility.TrTextContent("Detail Inputs",
            "These settings define the surface details by tiling and overlaying additional maps on the surface.");

        static readonly GUIContent s_DetailMaskText = EditorGUIUtility.TrTextContent("Mask",
            "Select a mask for the Detail map. The mask uses the alpha channel of the selected texture. The Tiling and Offset settings have no effect on the mask.");

        static readonly GUIContent s_DetailAlbedoMapText = EditorGUIUtility.TrTextContent("Base Map",
            "Select the surface detail texture.The alpha of your texture determines surface hue and intensity.");

        static readonly GUIContent s_DetailNormalMapText = EditorGUIUtility.TrTextContent("Normal Map",
            "Designates a Normal Map to create the illusion of bumps and dents in the details of this Material's surface.");

        static readonly GUIContent s_DetailAlbedoMapScaleInfo = EditorGUIUtility.TrTextContent(
            "Setting the scaling factor to a value other than 1 results in a less performant shader variant.");

        static readonly GUIContent s_DetailAlbedoMapFormatError = EditorGUIUtility.TrTextContent(
            "This texture is not in linear space.");

        static readonly GUIContent s_ScrollSpeedY = EditorGUIUtility.TrTextContent("Scroll Speed Y",
            "Scrolling speed for the base UV (V direction). Multiplied by shader time.");

        static readonly GUIContent s_WaveAmplitude = EditorGUIUtility.TrTextContent("Wave Amplitude",
            "Vertical displacement strength for procedural vertex waves.");
        static readonly GUIContent s_WaveFrequency = EditorGUIUtility.TrTextContent("Wave Frequency",
            "Spatial frequency of the wave pattern (world-space XZ).");
        static readonly GUIContent s_WaveSpeed = EditorGUIUtility.TrTextContent("Wave Speed",
            "Animation speed multiplier for waves.");
        static readonly GUIContent s_WaveSecondaryAmp = EditorGUIUtility.TrTextContent("Secondary Wave",
            "Relative strength of the second cross-direction wave for variation.");

        LitGUI.LitProperties m_LitProperties;

        MaterialProperty m_DetailMask;
        MaterialProperty m_DetailAlbedoMapScale;
        MaterialProperty m_DetailAlbedoMap;
        MaterialProperty m_DetailNormalMapScale;
        MaterialProperty m_DetailNormalMap;

        MaterialProperty m_ScrollSpeedY;
        MaterialProperty m_WaveAmplitude;
        MaterialProperty m_WaveFrequency;
        MaterialProperty m_WaveSpeed;
        MaterialProperty m_WaveSecondaryAmp;

        public override void FindProperties(MaterialProperty[] properties)
        {
            base.FindProperties(properties);
            m_LitProperties = new LitGUI.LitProperties(properties);
            m_DetailMask = FindProperty("_DetailMask", properties, false);
            m_DetailAlbedoMapScale = FindProperty("_DetailAlbedoMapScale", properties, false);
            m_DetailAlbedoMap = FindProperty("_DetailAlbedoMap", properties, false);
            m_DetailNormalMapScale = FindProperty("_DetailNormalMapScale", properties, false);
            m_DetailNormalMap = FindProperty("_DetailNormalMap", properties, false);
            m_ScrollSpeedY = FindProperty("_ScrollSpeedY", properties, false);
            m_WaveAmplitude = FindProperty("_WaveAmplitude", properties, false);
            m_WaveFrequency = FindProperty("_WaveFrequency", properties, false);
            m_WaveSpeed = FindProperty("_WaveSpeed", properties, false);
            m_WaveSecondaryAmp = FindProperty("_WaveSecondaryAmp", properties, false);
        }

        public override void FillAdditionalFoldouts(MaterialHeaderScopeList materialScopesList)
        {
            materialScopesList.RegisterHeaderScope(s_DetailInputs, Expandable.Details, _ => DoDetailArea());
        }

        void DoDetailArea()
        {
            if (m_DetailMask == null || m_DetailAlbedoMap == null || m_DetailAlbedoMapScale == null ||
                m_DetailNormalMap == null || m_DetailNormalMapScale == null)
                return;

            materialEditor.TexturePropertySingleLine(s_DetailMaskText, m_DetailMask);
            materialEditor.TexturePropertySingleLine(s_DetailAlbedoMapText, m_DetailAlbedoMap,
                m_DetailAlbedoMap.textureValue != null ? m_DetailAlbedoMapScale : null);
            if (m_DetailAlbedoMapScale.floatValue != 1.0f)
                EditorGUILayout.HelpBox(s_DetailAlbedoMapScaleInfo.text, MessageType.Info, true);

            var detailAlbedoTexture = m_DetailAlbedoMap.textureValue as Texture2D;
            if (detailAlbedoTexture != null && GraphicsFormatUtility.IsSRGBFormat(detailAlbedoTexture.graphicsFormat))
                EditorGUILayout.HelpBox(s_DetailAlbedoMapFormatError.text, MessageType.Warning, true);

            materialEditor.TexturePropertySingleLine(s_DetailNormalMapText, m_DetailNormalMap,
                m_DetailNormalMap.textureValue != null ? m_DetailNormalMapScale : null);
            materialEditor.TextureScaleOffsetProperty(m_DetailAlbedoMap);
        }

        public override void ValidateMaterial(Material material)
        {
            SetMaterialKeywords(material, LitGUI.SetMaterialKeywords, SetDetailMaterialKeywords);
        }

        static void SetDetailMaterialKeywords(Material material)
        {
            if (material.HasProperty("_DetailAlbedoMap") && material.HasProperty("_DetailNormalMap") &&
                material.HasProperty("_DetailAlbedoMapScale"))
            {
                bool isScaled = material.GetFloat("_DetailAlbedoMapScale") != 1.0f;
                bool hasDetailMap = material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap");
                CoreUtils.SetKeyword(material, "_DETAIL_MULX2", !isScaled && hasDetailMap);
                CoreUtils.SetKeyword(material, "_DETAIL_SCALED", isScaled && hasDetailMap);
            }
        }

        public override void DrawSurfaceOptions(Material material)
        {
            EditorGUIUtility.labelWidth = 0f;

            if (m_LitProperties.workflowMode != null)
                DoPopup(LitGUI.Styles.workflowModeText, m_LitProperties.workflowMode, s_WorkflowModeNames);

            base.DrawSurfaceOptions(material);
        }

        public override void DrawSurfaceInputs(Material material)
        {
            base.DrawSurfaceInputs(material);
            LitGUI.Inputs(m_LitProperties, materialEditor, material);
            DrawEmissionProperties(material, true);
            DrawTileOffset(materialEditor, baseMapProp);
            if (m_ScrollSpeedY != null)
                materialEditor.ShaderProperty(m_ScrollSpeedY, s_ScrollSpeedY);
            if (m_WaveAmplitude != null)
                materialEditor.ShaderProperty(m_WaveAmplitude, s_WaveAmplitude);
            if (m_WaveFrequency != null)
                materialEditor.ShaderProperty(m_WaveFrequency, s_WaveFrequency);
            if (m_WaveSpeed != null)
                materialEditor.ShaderProperty(m_WaveSpeed, s_WaveSpeed);
            if (m_WaveSecondaryAmp != null)
                materialEditor.ShaderProperty(m_WaveSecondaryAmp, s_WaveSecondaryAmp);
        }

        public override void DrawAdvancedOptions(Material material)
        {
            if (m_LitProperties.reflections != null && m_LitProperties.highlights != null)
            {
                materialEditor.ShaderProperty(m_LitProperties.highlights, LitGUI.Styles.highlightsText);
                materialEditor.ShaderProperty(m_LitProperties.reflections, LitGUI.Styles.reflectionsText);
            }

            base.DrawAdvancedOptions(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            if (material.HasProperty("_Emission"))
                material.SetColor("_EmissionColor", material.GetColor("_Emission"));

            base.AssignNewShaderToMaterial(material, oldShader, newShader);

            if (oldShader == null || !oldShader.name.Contains("Legacy Shaders/"))
            {
                SetupMaterialBlendMode(material);
                return;
            }

            SurfaceType surfaceType = SurfaceType.Opaque;
            BlendMode blendMode = BlendMode.Alpha;
            if (oldShader.name.Contains("/Transparent/Cutout/"))
            {
                surfaceType = SurfaceType.Opaque;
                material.SetFloat("_AlphaClip", 1);
            }
            else if (oldShader.name.Contains("/Transparent/"))
            {
                surfaceType = SurfaceType.Transparent;
                blendMode = BlendMode.Alpha;
            }

            material.SetFloat("_Blend", (float)blendMode);
            material.SetFloat("_Surface", (float)surfaceType);
            if (surfaceType == SurfaceType.Opaque)
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (oldShader.name.Equals("Standard (Specular setup)"))
            {
                material.SetFloat("_WorkflowMode", (float)LitGUI.WorkflowMode.Specular);
                Texture texture = material.GetTexture("_SpecGlossMap");
                if (texture != null)
                    material.SetTexture("_MetallicSpecGlossMap", texture);
            }
            else
            {
                material.SetFloat("_WorkflowMode", (float)LitGUI.WorkflowMode.Metallic);
                Texture texture = material.GetTexture("_MetallicGlossMap");
                if (texture != null)
                    material.SetTexture("_MetallicSpecGlossMap", texture);
            }
        }
    }
}
