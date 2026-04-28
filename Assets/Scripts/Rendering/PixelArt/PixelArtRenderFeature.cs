using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Medieval.Rendering.PixelArt
{
    [DisallowMultipleRendererFeature("Pixel Art Post-Opaque Blit")]
    public class PixelArtRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader m_Shader;
        [SerializeField] [Range(8, 512)] int m_PixelWidth = 256;
        [SerializeField] [Range(8, 512)] int m_PixelHeight = 144;

        [FormerlySerializedAs("m_DefaultPalette")]
        [SerializeField] PixelArtColorSettings? m_DefaultColorSettings;

        [FormerlySerializedAs("m_RuntimePalette")]
        [SerializeField] [HideInInspector] PixelArtColorSettings? m_RuntimeColorSettings;

        static PixelArtRenderFeature? s_Instance;

        Material? m_Material;
        PixelArtPass? m_Pass;

        public int PixelWidth => m_PixelWidth;
        public int PixelHeight => m_PixelHeight;

        public static PixelArtRenderFeature? GlobalInstance
        {
            get => s_Instance;
            set => s_Instance = value;
        }

        public override void Create()
        {
            s_Instance = this;
            m_Material = m_Shader != null ? CoreUtils.CreateEngineMaterial(m_Shader) : null;
            m_Pass = m_Material != null ? new PixelArtPass(m_Material) : null;
            m_Pass?.SetResolution(m_PixelWidth, m_PixelHeight);
            m_Pass?.SetRuntimeQuantizeOverride(null, null);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Pass == null || m_Material == null)
                return;
            if (renderingData.cameraData.cameraType != CameraType.Game)
                return;
            m_Pass.SetResolution(m_PixelWidth, m_PixelHeight);
            m_Pass.ConfigureQuantizeFromSettings(ActiveRuntimeColorSettings, m_DefaultColorSettings);
            renderer.EnqueuePass(m_Pass);
        }

        public PixelArtColorSettings? RuntimeColorSettings
        {
            get => m_RuntimeColorSettings != null ? m_RuntimeColorSettings : m_DefaultColorSettings;
            set
            {
                m_RuntimeColorSettings = value;
                m_Pass?.SetRuntimeQuantizeOverride(null, null);
            }
        }

        PixelArtColorSettings? ActiveRuntimeColorSettings => m_RuntimeColorSettings != null ? m_RuntimeColorSettings : null;

        /// <summary>Swap color quantization settings at runtime (biomes, quality presets).</summary>
        public void SetRuntimeColorSettings(PixelArtColorSettings? settings)
        {
            m_RuntimeColorSettings = settings;
            m_Pass?.SetRuntimeQuantizeOverride(null, null);
        }

        /// <summary>Override posterize/dither without a ScriptableObject.</summary>
        public void SetRuntimeQuantize(float? posterizeLevels, float? ditherStrength)
        {
            m_Pass?.SetRuntimeQuantizeOverride(posterizeLevels, ditherStrength);
        }

        /// <summary>Global shortcut when only one <see cref="PixelArtRenderFeature"/> is active in the project.</summary>
        public static void SetGlobalRuntimeColorSettings(PixelArtColorSettings? settings)
        {
            s_Instance?.SetRuntimeColorSettings(settings);
        }

        protected override void Dispose(bool disposing)
        {
            if (s_Instance == this)
                s_Instance = null;
            CoreUtils.Destroy(m_Material);
        }

        void OnValidate()
        {
            m_Pass?.SetResolution(m_PixelWidth, m_PixelHeight);
        }
    }
}
