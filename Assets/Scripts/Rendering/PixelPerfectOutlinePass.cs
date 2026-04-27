using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using Medieval.Rendering.PixelArt;

namespace Medieval.Rendering
{
    /// <summary>
    /// Layer mask render (after depth exists), then edge composite read/write color via a temp copy.
    /// Place this renderer feature below Pixel Art in the URP renderer list so quantization runs before outlines.
    /// </summary>
    public sealed class PixelPerfectOutlinePass : ScriptableRenderPass
    {
        const string k_MaskPassName = "Pixel Perfect Outline Mask";
        const string k_CopyPassName = "Pixel Perfect Outline Copy";
        const string k_OutlinePassName = "Pixel Perfect Outline Composite";


        static readonly int s_IdPixelGrid = Shader.PropertyToID("_PixelGrid");
        static readonly int s_IdDepthThreshold = Shader.PropertyToID("_DepthThreshold");
        static readonly int s_IdNormalThreshold = Shader.PropertyToID("_NormalThreshold");
        static readonly int s_IdFillNeighbor = Shader.PropertyToID("_FillNeighbor");
        static readonly int s_IdOutlineSilColor = Shader.PropertyToID("_OutlineSilColor");
        static readonly int s_IdOutlineCreaseColor = Shader.PropertyToID("_OutlineCreaseColor");
        static readonly int s_IdCameraDepth = Shader.PropertyToID("_CameraDepthTexture");
        static readonly int s_IdSceneNormals = Shader.PropertyToID("_SceneNormalsTexture");
        static readonly int s_IdMaskTex = Shader.PropertyToID("_MaskTex");
        static readonly int s_IdSilhouetteWeight = Shader.PropertyToID("_SilhouetteWeight");
        static readonly int s_IdCreaseWeight = Shader.PropertyToID("_CreaseWeight");

        readonly ProfilingSampler m_ProfilingMask = new ProfilingSampler(k_MaskPassName);
        readonly ProfilingSampler m_ProfilingOutline = new ProfilingSampler(k_OutlinePassName);

        readonly Vector4 m_ScaleBias = new Vector4(1f, 1f, 0f, 0f);

        Material m_CompositeMaterial;
        Material m_MaskMaterial;

        PixelPerfectOutlineFeature.MaskChannelMode m_MaskMode = PixelPerfectOutlineFeature.MaskChannelMode.Both;
        LayerMask m_OutlineLayers = 0;
        float m_DepthThreshold = 0.02f;
        float m_NormalThreshold = 0.92f;
        bool m_FillNeighbor = true;
        Color m_OutlineSilColor = Color.black;
        Color m_OutlineCreaseColor = new Color(0f, 0f, 0f, 1f);
        bool m_UsePixelArtGrid = true;
        int m_PixelWidthOverride = 256;
        int m_PixelHeightOverride = 144;

        sealed class MaskPassData
        {
            internal RendererListHandle RendererList;
            internal Material MaskMaterial;
        }

        sealed class OutlinePassData
        {
            internal Material CompositeMaterial;
            internal TextureHandle SrcColor;
            internal TextureHandle Depth;
            internal TextureHandle Normals;
            internal bool NormalsValid;
            internal TextureHandle Mask;
            internal Vector4 PixelGrid;
            internal Vector4 ScaleBias;
            internal float DepthThreshold;
            internal float NormalThreshold;
            internal float FillNeighbor;
            internal Color SilColor;
            internal Color CreaseColor;
        }

        public PixelPerfectOutlinePass(Material compositeMaterial, Material maskMaterial)
        {
            m_CompositeMaterial = compositeMaterial;
            m_MaskMaterial = maskMaterial;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            profilingSampler = m_ProfilingOutline;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public void ApplySettings(
            PixelPerfectOutlineFeature.MaskChannelMode maskMode,
            LayerMask outlineLayers,
            float depthThreshold,
            float normalThreshold,
            bool fillNeighbor,
            Color silColor,
            Color creaseColor,
            bool usePixelArtGrid,
            int pixelWidthOverride,
            int pixelHeightOverride)
        {
            m_MaskMode = maskMode;
            m_OutlineLayers = outlineLayers;
            m_DepthThreshold = depthThreshold;
            m_NormalThreshold = normalThreshold;
            m_FillNeighbor = fillNeighbor;
            m_OutlineSilColor = silColor;
            m_OutlineCreaseColor = creaseColor;
            m_UsePixelArtGrid = usePixelArtGrid;
            m_PixelWidthOverride = pixelWidthOverride;
            m_PixelHeightOverride = pixelHeightOverride;
        }

        void GetPixelGrid(out int w, out int h)
        {
            if (m_UsePixelArtGrid && PixelArtRenderFeature.GlobalInstance != null)
            {
                w = Mathf.Max(8, PixelArtRenderFeature.GlobalInstance.PixelWidth);
                h = Mathf.Max(8, PixelArtRenderFeature.GlobalInstance.PixelHeight);
                return;
            }

            w = Mathf.Max(8, m_PixelWidthOverride);
            h = Mathf.Max(8, m_PixelHeightOverride);
        }

        void ApplyMaskMaterialWeights()
        {
            if (m_MaskMaterial == null)
                return;
            switch (m_MaskMode)
            {
                case PixelPerfectOutlineFeature.MaskChannelMode.SilhouetteOnly:
                    m_MaskMaterial.SetFloat(s_IdSilhouetteWeight, 1f);
                    m_MaskMaterial.SetFloat(s_IdCreaseWeight, 0f);
                    break;
                case PixelPerfectOutlineFeature.MaskChannelMode.CreaseOnly:
                    m_MaskMaterial.SetFloat(s_IdSilhouetteWeight, 0f);
                    m_MaskMaterial.SetFloat(s_IdCreaseWeight, 1f);
                    break;
                default:
                    m_MaskMaterial.SetFloat(s_IdSilhouetteWeight, 1f);
                    m_MaskMaterial.SetFloat(s_IdCreaseWeight, 1f);
                    break;
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_CompositeMaterial == null || m_MaskMaterial == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;
            if (cameraData.cameraType != CameraType.Game)
                return;

            TextureHandle color = resourceData.activeColorTexture;
            TextureHandle depth = resourceData.cameraDepthTexture;
            TextureHandle normals = resourceData.cameraNormalsTexture;

            if (!color.IsValid() || !depth.IsValid())
                return;

            GetPixelGrid(out int pxW, out int pxH);
            Vector4 pixelGrid = new Vector4(pxW, pxH, 1f / pxW, 1f / pxH);

            TextureHandle mask = RenderOutlineMask(renderGraph, frameData, renderingData, cameraData, resourceData);
            if (!mask.IsValid())
                return;

            RenderTextureDescriptor tempDesc = cameraData.cameraTargetDescriptor;
            tempDesc.msaaSamples = 1;
            tempDesc.depthStencilFormat = GraphicsFormat.None;
            tempDesc.depthBufferBits = 0;

            TextureHandle tempColor = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, tempDesc, "_OutlineSceneCopy", false, FilterMode.Point, TextureWrapMode.Clamp);
            if (!tempColor.IsValid())
                return;

            // AddBlitPass uses an optimized copy when possible; avoid RasterCommandBuffer.CopyTexture (not on RasterCommandBuffer in Unity 6).
            renderGraph.AddBlitPass(color, tempColor, Vector2.one, Vector2.zero, passName: k_CopyPassName);

            bool normalsValid = normals.IsValid();

            using (var builder = renderGraph.AddRasterRenderPass<OutlinePassData>(k_OutlinePassName, out var outlinePass, m_ProfilingOutline))
            {
                outlinePass.CompositeMaterial = m_CompositeMaterial;
                outlinePass.SrcColor = tempColor;
                outlinePass.Depth = depth;
                outlinePass.Normals = normals;
                outlinePass.NormalsValid = normalsValid;
                outlinePass.Mask = mask;
                outlinePass.PixelGrid = pixelGrid;
                outlinePass.ScaleBias = m_ScaleBias;
                outlinePass.DepthThreshold = m_DepthThreshold;
                outlinePass.NormalThreshold = normalsValid ? m_NormalThreshold : 1.01f;
                outlinePass.FillNeighbor = m_FillNeighbor ? 1f : 0f;
                outlinePass.SilColor = m_OutlineSilColor;
                outlinePass.CreaseColor = m_OutlineCreaseColor;

                builder.UseTexture(tempColor, AccessFlags.Read);
                builder.UseTexture(depth, AccessFlags.Read);
                if (normalsValid)
                    builder.UseTexture(normals, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(color, 0, AccessFlags.WriteAll);

                builder.SetRenderFunc(static (OutlinePassData data, RasterGraphContext ctx) =>
                {
                    Material mat = data.CompositeMaterial;
                    mat.SetVector(s_IdPixelGrid, data.PixelGrid);
                    mat.SetFloat(s_IdDepthThreshold, data.DepthThreshold);
                    mat.SetFloat(s_IdNormalThreshold, data.NormalThreshold);
                    mat.SetFloat(s_IdFillNeighbor, data.FillNeighbor);
                    mat.SetColor(s_IdOutlineSilColor, data.SilColor);
                    mat.SetColor(s_IdOutlineCreaseColor, data.CreaseColor);
                    mat.SetTexture(s_IdCameraDepth, data.Depth);
                    mat.SetTexture(s_IdMaskTex, data.Mask);
                    if (data.NormalsValid)
                        mat.SetTexture(s_IdSceneNormals, data.Normals);

                    Blitter.BlitTexture(ctx.cmd, data.SrcColor, data.ScaleBias, mat, 0);
                });
            }
        }

        TextureHandle RenderOutlineMask(
            RenderGraph renderGraph,
            ContextContainer frameData,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalResourceData resourceData)
        {
            if (m_OutlineLayers == 0)
                return TextureHandle.nullHandle;

            ApplyMaskMaterialWeights();

            RenderTextureDescriptor maskDesc = cameraData.cameraTargetDescriptor;
            maskDesc.msaaSamples = 1;
            maskDesc.depthStencilFormat = GraphicsFormat.None;
            maskDesc.depthBufferBits = 0;
            maskDesc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;

            TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, maskDesc, "_OutlineMask", false, FilterMode.Point, TextureWrapMode.Clamp);
            if (!mask.IsValid())
                return TextureHandle.nullHandle;

            SortingSettings sortingSettings = new SortingSettings(cameraData.camera)
            {
                criteria = cameraData.defaultOpaqueSortFlags
            };
            DrawingSettings drawingSettings = new DrawingSettings(new ShaderTagId("UniversalForward"), sortingSettings)
            {
                overrideMaterial = m_MaskMaterial,
                overrideMaterialPassIndex = 0,
                enableDynamicBatching = true,
                enableInstancing = true
            };

            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque)
            {
                layerMask = m_OutlineLayers
            };

            RendererListParams rendererListParams = new RendererListParams(
                renderingData.cullResults,
                drawingSettings,
                filteringSettings);

            RendererListHandle rendererList = renderGraph.CreateRendererList(rendererListParams);

            TextureHandle depthRead = resourceData.activeDepthTexture;

            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(k_MaskPassName, out var passData, m_ProfilingMask))
            {
                passData.RendererList = rendererList;
                passData.MaskMaterial = m_MaskMaterial;
                builder.UseRendererList(rendererList);
                builder.SetRenderAttachment(mask, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depthRead, AccessFlags.ReadWrite);

                builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0u);
                    ctx.cmd.DrawRendererList(data.RendererList);
                });
            }

            return mask;
        }
    }
}
