using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Medieval.Rendering.PixelArt
{
    /// <summary>
    /// Downscales the camera color target with cell-centre sampling, then upscales with point filtering and CIE Lab palette quantization.
    /// </summary>
    public class PixelArtPass : ScriptableRenderPass
    {
        const int k_MaxPalette = 48;
        const string k_PassDown = "PixelArt Downsample";
        const string k_PassUp = "PixelArt Lab & Upsample";
        const string k_LowTexName = "_PixelArt_LowRes";

        static readonly int s_IdPixelGrid = Shader.PropertyToID("_PixelGrid");
        static readonly int s_IdPaletteCount = Shader.PropertyToID("_PaletteCount");
        static readonly int s_IdPalette = Shader.PropertyToID("_Palette");
        static readonly int s_IdPaletteLab = Shader.PropertyToID("_PaletteLab");

        static readonly List<Vector4> s_PaletteScratch = new List<Vector4>(k_MaxPalette);
        static readonly List<Vector4> s_PaletteLabScratch = new List<Vector4>(k_MaxPalette);

        readonly Vector4 m_ScaleBias = new Vector4(1f, 1f, 0f, 0f);

        Material m_Material;
        int m_Width = 256;
        int m_Height = 144;

        int m_PaletteCount;
        Color[]? m_PaletteOverride;

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

        /// <summary>Replaces the palette (runtime). Pass null to use the renderer feature default asset.</summary>
        public void SetRuntimePaletteOverride(Color[]? colors)
        {
            m_PaletteOverride = colors;
        }

        public void SetPaletteData(Color[]? colors, int count)
        {
            m_PaletteCount = 0;
            s_PaletteScratch.Clear();
            s_PaletteLabScratch.Clear();
            if (colors == null || count <= 0)
            {
                PushDefaultPalette();
                return;
            }
            int n = Mathf.Min(count, k_MaxPalette);
            for (int i = 0; i < n; i++)
            {
                Color lin = colors[i].linear;
                s_PaletteScratch.Add(new Vector4(lin.r, lin.g, lin.b, lin.a));
                Vector3 lab = CieLabColor.LinearRgbToLab(lin);
                s_PaletteLabScratch.Add(new Vector4(lab.x, lab.y, lab.z, 0f));
            }
            m_PaletteCount = s_PaletteScratch.Count;
            ApplyListToMaterial();
        }

        void PushDefaultPalette()
        {
            s_PaletteScratch.Clear();
            s_PaletteLabScratch.Clear();
            m_PaletteCount = 3;
            var c0 = Color.white.linear;
            var c1 = Color.black.linear;
            var c2 = new Color(0.5f, 0.5f, 0.5f, 1f).linear;
            s_PaletteScratch.Add(new Vector4(c0.r, c0.g, c0.b, c0.a));
            s_PaletteScratch.Add(new Vector4(c1.r, c1.g, c1.b, c1.a));
            s_PaletteScratch.Add(new Vector4(c2.r, c2.g, c2.b, c2.a));
            Vector3 l0 = CieLabColor.LinearRgbToLab(c0);
            Vector3 l1 = CieLabColor.LinearRgbToLab(c1);
            Vector3 l2 = CieLabColor.LinearRgbToLab(c2);
            s_PaletteLabScratch.Add(new Vector4(l0.x, l0.y, l0.z, 0f));
            s_PaletteLabScratch.Add(new Vector4(l1.x, l1.y, l1.z, 0f));
            s_PaletteLabScratch.Add(new Vector4(l2.x, l2.y, l2.z, 0f));
            ApplyListToMaterial();
        }

        void ApplyListToMaterial()
        {
            if (m_Material == null)
                return;
            m_Material.SetInt(s_IdPaletteCount, m_PaletteCount);
            while (s_PaletteScratch.Count < k_MaxPalette)
                s_PaletteScratch.Add(Vector4.zero);
            while (s_PaletteLabScratch.Count < k_MaxPalette)
                s_PaletteLabScratch.Add(Vector4.zero);
            m_Material.SetVectorArray(s_IdPalette, s_PaletteScratch);
            m_Material.SetVectorArray(s_IdPaletteLab, s_PaletteLabScratch);
        }

        public void ConfigurePaletteFromSo(PixelPalette? palette, PixelPalette? defaultPalette)
        {
            Color[]? useOverride = m_PaletteOverride;
            if (useOverride != null)
            {
                SetPaletteData(useOverride, useOverride.Length);
                return;
            }
            var src = palette != null && palette.Count > 0 ? palette : defaultPalette;
            if (src == null || src.Count == 0)
            {
                SetPaletteData(null, 0);
                return;
            }
            var cols = src.Colors;
            SetPaletteData(cols, cols != null ? cols.Length : 0);
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
            if (m_PaletteCount == 0)
                PushDefaultPalette();
            else
                ApplyListToMaterial();


            // Downscale
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

            // Quantize in Lab + upscale to active color
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
