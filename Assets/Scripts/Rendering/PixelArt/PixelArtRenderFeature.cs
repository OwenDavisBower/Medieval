using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Medieval.Rendering.PixelArt
{
    [DisallowMultipleRendererFeature("Pixel Art Post-Opaque Blit")]
    public class PixelArtRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader m_Shader;
        [SerializeField] [Range(8, 512)] int m_PixelWidth = 256;
        [SerializeField] [Range(8, 512)] int m_PixelHeight = 144;
        [SerializeField] PixelPalette? m_DefaultPalette;
        [SerializeField] [HideInInspector] PixelPalette? m_RuntimePalette;

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
            m_Pass?.SetPaletteData(null, 0);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Pass == null || m_Material == null)
                return;
            if (renderingData.cameraData.cameraType != CameraType.Game)
                return;
            m_Pass.SetResolution(m_PixelWidth, m_PixelHeight);
            m_Pass.ConfigurePaletteFromSo(ActiveRuntimePalette, m_DefaultPalette);
            renderer.EnqueuePass(m_Pass);
        }

        public PixelPalette? RuntimePalette
        {
            get => m_RuntimePalette != null ? m_RuntimePalette : m_DefaultPalette;
            set
            {
                m_RuntimePalette = value;
                m_Pass?.SetRuntimePaletteOverride(null);
            }
        }

        PixelPalette? ActiveRuntimePalette => m_RuntimePalette != null ? m_RuntimePalette : m_DefaultPalette;

        /// <summary>Swap the palette at runtime (biomes, time of day).</summary>
        public void SetRuntimePalette(PixelPalette? palette)
        {
            m_RuntimePalette = palette;
            m_Pass?.SetRuntimePaletteOverride(null);
        }

        /// <summary>Replace palette with raw colors (e.g. generated in code) without a ScriptableObject.</summary>
        public void SetRuntimeColors(Color[]? colors, int count = -1)
        {
            if (count < 0 && colors != null)
                count = colors.Length;
            m_Pass?.SetRuntimePaletteOverride(count > 0 ? colors : null);
        }

        /// <summary>Global shortcut when only one <see cref="PixelArtRenderFeature"/> is active in the project.</summary>
        public static void SetGlobalRuntimePalette(PixelPalette? palette)
        {
            s_Instance?.SetRuntimePalette(palette);
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
