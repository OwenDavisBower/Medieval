using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives <see cref="UniversalRenderPipelineAsset.renderScale"/> from <see cref="PixelateVolume"/> so the game
/// renders at a lower internal resolution, and sets <see cref="UniversalRenderPipelineAsset.upscalingFilter"/> to
/// <see cref="UpscalingFilterSelection.Point"/> while active for crisp upscale. Restores prior scale and filter when the volume is off.
/// </summary>
static class PixelateRenderScaleApplier
{
    const float MinRenderScale = 0.1f;
    const float MaxRenderScale = 2f;

    static bool s_WasPixelating;
    static float s_BaselineRenderScale = 1f;
    static UpscalingFilterSelection s_BaselineUpscalingFilter;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
    }

    static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!Application.isPlaying)
            return;

        var pipeline = GetActivePipeline();
        if (pipeline == null)
            return;

        bool active = PixelateGrid.TryGetActiveVolume(out PixelateVolume volume);
        if (active)
        {
            Camera reference = FindReferenceCamera(cameras);
            if (reference == null)
                return;

            if (!s_WasPixelating)
            {
                s_BaselineRenderScale = pipeline.renderScale;
                s_BaselineUpscalingFilter = pipeline.upscalingFilter;
            }

            pipeline.renderScale = ComputeRenderScale(volume, reference);
            pipeline.upscalingFilter = UpscalingFilterSelection.Point;
            s_WasPixelating = true;
        }
        else
        {
            if (s_WasPixelating)
            {
                pipeline.renderScale = s_BaselineRenderScale;
                pipeline.upscalingFilter = s_BaselineUpscalingFilter;
                s_WasPixelating = false;
            }
        }
    }

    static Camera FindReferenceCamera(List<Camera> cameras)
    {
        for (int i = 0; i < cameras.Count; i++)
        {
            Camera c = cameras[i];
            if (c == null || c.cameraType != CameraType.Game)
                continue;
            if (!c.TryGetComponent<UniversalAdditionalCameraData>(out var uacd))
                continue;
            if (uacd.renderType == CameraRenderType.Base)
                return c;
        }

        for (int i = 0; i < cameras.Count; i++)
        {
            Camera c = cameras[i];
            if (c != null && c.cameraType == CameraType.Game)
                return c;
        }

        return null;
    }

    static float ComputeRenderScale(PixelateVolume volume, Camera camera)
    {
        int pixelHeight = camera.targetTexture != null
            ? Mathf.Max(1, camera.targetTexture.height)
            : Mathf.Max(1, camera.pixelHeight);

        int targetH = Mathf.Max(2, volume.screenHeight.value);
        float scale = targetH / (float)pixelHeight;
        return Mathf.Clamp(scale, MinRenderScale, MaxRenderScale);
    }

    static UniversalRenderPipelineAsset GetActivePipeline()
    {
        RenderPipelineAsset asset = QualitySettings.renderPipeline != null
            ? QualitySettings.renderPipeline
            : GraphicsSettings.defaultRenderPipeline;
        return asset as UniversalRenderPipelineAsset;
    }
}
