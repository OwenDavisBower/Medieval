using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Volume-driven settings for <see cref="PixelateRenderScaleApplier"/>, which lowers URP render scale from the
/// target vertical pixel count. Add to a Volume Profile (global or local). Pair with <see cref="FollowCam"/>
/// (pixel grid snap) on the game camera to reduce sub-pixel crawl while moving.
/// </summary>
[VolumeComponentMenu("Rendering/Pixelate")]
public sealed class PixelateVolume : VolumeComponent
{
    [Tooltip("Master toggle for the renderer feature pass.")]
    public BoolParameter isActive = new BoolParameter(false);

    [Tooltip("Target vertical resolution in pixels (grid rows used for UV quantization).")]
    public ClampedIntParameter screenHeight = new ClampedIntParameter(240, 8, 2160);

    [Tooltip("When enabled, horizontal resolution is derived from the camera aspect so pixels stay square. "
             + "Render-scale pixelation uses a uniform scale only; this affects FollowCam grid math, not URP scale.")]
    public BoolParameter matchAspectRatio = new BoolParameter(true);

    [Tooltip("Reserved: posterization was applied by the old fullscreen pass and is not used on the render-scale path.")]
    public ClampedFloatParameter colorDepth = new ClampedFloatParameter(0f, 0f, 256f);

    /// <summary>
    /// True when the volume override is enabled and parameters request a valid pixel grid.
    /// Uses the base <see cref="VolumeComponent.active"/> toggle plus <see cref="isActive"/>.
    /// </summary>
    public bool IsEffectActive => active && isActive.value && screenHeight.value >= 2;
}
