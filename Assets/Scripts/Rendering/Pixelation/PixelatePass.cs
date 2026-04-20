using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Full-screen pass that samples <c>_CameraOpaqueTexture</c> and writes the pixelated result into the camera color target.
/// </summary>
public sealed class PixelatePass : ScriptableRenderPass
{
    static readonly ProfilingSampler s_Profiler = new ProfilingSampler(nameof(PixelatePass));
    static readonly int s_PixelGridId = Shader.PropertyToID("_PixelGrid");
    static readonly int s_PosterizeId = Shader.PropertyToID("_Posterize");
    static readonly int s_CameraOpaqueTextureId = Shader.PropertyToID("_CameraOpaqueTexture");

    readonly Material m_Material;
    readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();

    public PixelatePass(Material material, RenderPassEvent injectionPoint)
    {
        m_Material = material;
        renderPassEvent = injectionPoint;
        profilingSampler = s_Profiler;
    }

    static bool ShouldRun(CameraType cameraType) =>
        cameraType is CameraType.Game or CameraType.SceneView;

    static void GetCameraResolution(UniversalCameraData cameraData, out int width, out int height)
    {
        width = Mathf.Max(1, cameraData.scaledWidth);
        height = Mathf.Max(1, cameraData.scaledHeight);
    }

    static bool TryGetVolume(out PixelateVolume volume)
    {
        if (VolumeManager.instance == null)
        {
            volume = null;
            return false;
        }

        volume = VolumeManager.instance.stack.GetComponent<PixelateVolume>();
        return volume != null && volume.IsEffectActive;
    }

    static PixelateRuntimeSettings BuildSettings(PixelateVolume volume, int scaledWidth, int scaledHeight)
    {
        int h = Mathf.Max(2, volume.screenHeight.value);
        float aspect = scaledWidth / (float)scaledHeight;
        int w = volume.matchAspectRatio.value
            ? Mathf.Max(2, Mathf.RoundToInt(h * aspect))
            : h;

        float posterize = volume.colorDepth.value > 0.001f
            ? Mathf.Max(2f, volume.colorDepth.value)
            : 0f;

        return new PixelateRuntimeSettings(w, h, posterize);
    }

    void PushBlockConstants(in PixelateRuntimeSettings settings)
    {
        m_Block.Clear();
        m_Block.SetVector(s_PixelGridId, new Vector4(settings.PixelWidth, settings.PixelHeight, 0f, 0f));
        m_Block.SetFloat(s_PosterizeId, settings.Posterize);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        UniversalResourceData resources = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        if (!ShouldRun(cameraData.cameraType))
            return;

        if (m_Material == null)
            return;

        if (!TryGetVolume(out PixelateVolume volume))
            return;

        if (!cameraData.requiresOpaqueTexture)
            return;

        GetCameraResolution(cameraData, out int scaledW, out int scaledH);
        PixelateRuntimeSettings settings = BuildSettings(volume, scaledW, scaledH);

        TextureHandle opaque = resources.cameraOpaqueTexture;
        TextureHandle destination = resources.activeColorTexture;

        PushBlockConstants(in settings);

        var blitParams = new RenderGraphUtils.BlitMaterialParameters(
            opaque,
            destination,
            m_Material,
            0,
            m_Block,
            RenderGraphUtils.FullScreenGeometryType.Mesh,
            s_CameraOpaqueTextureId);

        renderGraph.AddBlitPass(blitParams, nameof(PixelatePass));
    }

    readonly struct PixelateRuntimeSettings
    {
        public PixelateRuntimeSettings(int pixelWidth, int pixelHeight, float posterize)
        {
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            Posterize = posterize;
        }

        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public float Posterize { get; }
    }
}
