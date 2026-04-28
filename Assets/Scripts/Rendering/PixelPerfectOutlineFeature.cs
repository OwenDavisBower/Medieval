using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Medieval.Rendering
{
    /// <summary>
    /// Pixel-grid outlines from depth (silhouettes) and normals (creases), gated by a layer mask RT (R=silhouette, G=crease).
    /// List this feature below Pixel Art on the URP renderer so low-res quantization happens before outlines.
    /// Requires depth texture and (for creases) transparent normals / depth-normals prepass per URP renderer settings.
    /// </summary>
    [DisallowMultipleRendererFeature("Pixel Perfect Outline")]
    public sealed class PixelPerfectOutlineFeature : ScriptableRendererFeature
    {
        public enum MaskChannelMode
        {
            SilhouetteOnly = 0,
            CreaseOnly = 1,
            Both = 2
        }

        [SerializeField] Shader m_CompositeShader;
        [SerializeField] Shader m_MaskShader;

        [SerializeField] LayerMask m_OutlineLayers;

        [SerializeField] MaskChannelMode m_MaskChannels = MaskChannelMode.Both;

        [Tooltip("Linear eye-space depth delta across one game-pixel neighbor.")]
        [SerializeField] float m_DepthThreshold = 0.02f;

        [Tooltip("Creases where abs(dot(n0,n1)) is below this value.")]
        [SerializeField] [Range(0f, 1f)] float m_NormalThreshold = 0.92f;

        [SerializeField] bool m_FillNeighbor = true;

        [SerializeField] Color m_OutlineSilhouetteColor = Color.black;

        [SerializeField] Color m_OutlineCreaseColor = Color.black;

        [SerializeField] bool m_UsePixelArtGrid = true;

        [SerializeField] [Min(8)] int m_PixelWidthOverride = 256;

        [SerializeField] [Min(8)] int m_PixelHeightOverride = 144;

        Material m_CompositeMaterial;
        Material m_MaskMaterial;
        PixelPerfectOutlinePass m_Pass;

        public override void Create()
        {
            m_CompositeMaterial = m_CompositeShader != null ? CoreUtils.CreateEngineMaterial(m_CompositeShader) : null;
            m_MaskMaterial = m_MaskShader != null ? CoreUtils.CreateEngineMaterial(m_MaskShader) : null;
            m_Pass = m_CompositeMaterial != null && m_MaskMaterial != null
                ? new PixelPerfectOutlinePass(m_CompositeMaterial, m_MaskMaterial)
                : null;
            SyncPassSettings();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Pass == null)
                return;
            SyncPassSettings();
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_CompositeMaterial);
            CoreUtils.Destroy(m_MaskMaterial);
        }

        void OnValidate()
        {
            SyncPassSettings();
        }

        void SyncPassSettings()
        {
            m_Pass?.ApplySettings(
                m_MaskChannels,
                m_OutlineLayers,
                m_DepthThreshold,
                m_NormalThreshold,
                m_FillNeighbor,
                m_OutlineSilhouetteColor,
                m_OutlineCreaseColor,
                m_UsePixelArtGrid,
                m_PixelWidthOverride,
                m_PixelHeightOverride);
        }
    }
}
