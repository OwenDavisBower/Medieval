using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Volume-driven settings for <see cref="PixelateRendererFeature"/>. Add this to a Volume Profile on a
/// Global Volume (or local volume) so each scene can tune pixelation independently.
/// </summary>
[VolumeComponentMenu("Rendering/Pixelate")]
public sealed class PixelateVolume : VolumeComponent
{
    [Tooltip("Master toggle for the renderer feature pass.")]
    public BoolParameter isActive = new BoolParameter(false);

    [Tooltip("Target vertical resolution in pixels (grid rows used for UV quantization).")]
    public ClampedIntParameter screenHeight = new ClampedIntParameter(240, 8, 2160);

    [Tooltip("When enabled, horizontal resolution is derived from the camera aspect so pixels stay square.")]
    public BoolParameter matchAspectRatio = new BoolParameter(true);

    [Tooltip("Per-channel posterization steps. 0 disables posterization; higher values keep more shades.")]
    public ClampedFloatParameter colorDepth = new ClampedFloatParameter(0f, 0f, 256f);

    /// <summary>
    /// True when the volume override is enabled and parameters request a valid pixel grid.
    /// Uses the base <see cref="VolumeComponent.active"/> toggle plus <see cref="isActive"/>.
    /// </summary>
    public bool IsEffectActive => active && isActive.value && screenHeight.value >= 2;
}
