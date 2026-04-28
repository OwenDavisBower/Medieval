using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Medieval.Rendering.PixelArt
{
    /// <summary>
    /// Downscales the camera color target with cell-centre sampling, then upscales with point filtering,
    /// optional per-channel posterization and ordered Bayer dither (matches legacy PixelateScreen behaviour).
    /// </summary>
    public class PixelArtPass : ScriptableRenderPass
    {
        const string k_PassDown = "PixelArt Downsample";
        const string k_PassUp = "PixelArt Upscale & Posterize";
        const string k_LowTexName = "_PixelArt_LowRes";

        static readonly int s_IdPixelGrid = Shader.PropertyToID("_PixelGrid");
        static readonly int s_IdPosterize = Shader.PropertyToID("_Posterize");
        static readonly int s_IdPosterizeDitherStrength = Shader.PropertyToID("_PosterizeDitherStrength");

        readonly Vector4 m_ScaleBias = new Vector4(1f, 1f, 0f, 0f);

        Material m_Material;
        int m_Width = 256;
        int m_Height = 144;

        float? m_RuntimePosterizeOverride;
        float? m_RuntimeDitherOverride;

        class PassData
        {
            internal Material material;
            internal TextureHandle src;
        }

        public PixelArtPass(Material material)
        {
            m_Material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            profilingSampler = new ProfilingSampler(nameof(PixelArtPass));
            requiresIntermediateTexture = true;
        }

        public void SetResolution(int width, int height)
        {
            m_Width = Mathf.Max(8, width);
            m_Height = Mathf.Max(8, height);
        }

        /// <summary>Optional runtime overrides; null uses values from <see cref="PixelArtColorSettings"/>.</summary>
        public void SetRuntimeQuantizeOverride(float? posterizeLevels, float? ditherStrength)
        {
            m_RuntimePosterizeOverride = posterizeLevels;
            m_RuntimeDitherOverride = ditherStrength;
        }

        static void ResolveQuantize(PixelArtColorSettings? settings, float? posterizeOverride, float? ditherOverride,
            out float posterize, out float dither)
        {
            float depth = posterizeOverride ?? (settings != null ? settings.ColorDepth : 0f);
            posterize = depth > 0.001f ? Mathf.Max(2f, depth) : 0f;

            float rawDither = ditherOverride ?? (settings != null ? settings.PosterizeDitherStrength : 0f);
            dither = posterize > 0.001f && rawDither > 0.001f ? Mathf.Clamp01(rawDither) : 0f;
        }

        public void ConfigureQuantizeFromSettings(PixelArtColorSettings? runtimeSo, PixelArtColorSettings? defaultSo)
        {
            var src = runtimeSo != null ? runtimeSo : defaultSo;
            ResolveQuantize(src, m_RuntimePosterizeOverride, m_RuntimeDitherOverride, out float p, out float d);

            if (m_Material != null)
            {
                m_Material.SetFloat(s_IdPosterize, p);
                m_Material.SetFloat(s_IdPosterizeDitherStrength, d);
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;
            if (cameraData.cameraType != CameraType.Game)
                return;

            TextureHandle srcCamColor = resourceData.activeColorTexture;
            if (!srcCamColor.IsValid())
                return;

            RenderTextureDescriptor d = cameraData.cameraTargetDescriptor;
            d.width = m_Width;
            d.height = m_Height;
            d.msaaSamples = 1;
            d.depthBufferBits = 0;
            d.graphicsFormat = cameraData.cameraTargetDescriptor.graphicsFormat;
            d.depthStencilFormat = GraphicsFormat.None;

            TextureHandle low = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, d, k_LowTexName, false, FilterMode.Point, TextureWrapMode.Clamp);
            if (!low.IsValid())
                return;

            m_Material.SetVector(s_IdPixelGrid, new Vector4(m_Width, m_Height, 0f, 0f));

            using (var b = renderGraph.AddRasterRenderPass<PassData>(k_PassDown, out var pass0, profilingSampler))
            {
                pass0.material = m_Material;
                pass0.src = srcCamColor;
                b.UseTexture(srcCamColor, AccessFlags.Read);
                b.SetRenderAttachment(low, 0, AccessFlags.WriteAll);
                b.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    RTHandle srcH = data.src;
                    Blitter.BlitTexture(ctx.cmd, srcH, m_ScaleBias, data.material, 0);
                });
            }

            using (var b2 = renderGraph.AddRasterRenderPass<PassData>(k_PassUp, out var pass1, profilingSampler))
            {
                pass1.material = m_Material;
                pass1.src = low;
                b2.UseTexture(low, AccessFlags.Read);
                b2.SetRenderAttachment(srcCamColor, 0, AccessFlags.WriteAll);
                b2.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    RTHandle srcH = data.src;
                    Blitter.BlitTexture(ctx.cmd, srcH, m_ScaleBias, data.material, 1);
                });
            }
        }
    }
}
