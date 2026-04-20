using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Shared resolution math for <see cref="PixelateRenderScaleApplier"/> / <see cref="FollowCam"/> (pixel grid snap)
/// so the virtual pixel grid used for snapping matches the active render resolution.
/// </summary>
public static class PixelateGrid
{
    public static bool TryGetActiveVolume(out PixelateVolume volume)
    {
        volume = null;
        if (VolumeManager.instance == null)
            return false;

        var stack = VolumeManager.instance.stack;
        if (stack == null)
            return false;

        volume = stack.GetComponent<PixelateVolume>();
        return volume != null && volume.IsEffectActive;
    }

    /// <summary>
    /// Best-effort match for URP <c>UniversalCameraData.scaledWidth/Height</c> used when recording the pixelate pass.
    /// </summary>
    public static void GetScaledRenderSize(Camera camera, out int scaledWidth, out int scaledHeight)
    {
        if (camera.targetTexture != null)
        {
            scaledWidth = Mathf.Max(1, camera.targetTexture.width);
            scaledHeight = Mathf.Max(1, camera.targetTexture.height);
            return;
        }

        int pixelWidth = Mathf.Max(1, camera.pixelWidth);
        int pixelHeight = Mathf.Max(1, camera.pixelHeight);

        float renderScale = 1f;
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset urpAsset)
            renderScale = urpAsset.renderScale;

        scaledWidth = Mathf.Max(1, Mathf.RoundToInt(pixelWidth * renderScale));
        scaledHeight = Mathf.Max(1, Mathf.RoundToInt(pixelHeight * renderScale));
    }

    public static void GetLogicalPixelCounts(PixelateVolume volume, int scaledWidth, int scaledHeight, out int logicalWidth, out int logicalHeight)
    {
        int h = Mathf.Max(2, volume.screenHeight.value);
        float aspect = scaledWidth / (float)scaledHeight;
        int w = volume.matchAspectRatio.value
            ? Mathf.Max(2, Mathf.RoundToInt(h * aspect))
            : h;

        logicalWidth = w;
        logicalHeight = h;
    }
}
