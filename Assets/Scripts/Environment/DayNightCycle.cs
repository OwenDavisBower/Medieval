using System;
using UnityEngine;

/// <summary>
/// World day/night driver. Lives on the main camera; rotates and dims the sun light,
/// shifts ambient lighting, and exposes night state for gameplay systems.
/// </summary>
[DisallowMultipleComponent]
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    /// <summary>0 at full day, 1 at full night.</summary>
    public static float NightFactor => Instance != null ? Instance._nightFactor : 0f;

    public static bool IsNight => Instance != null && Instance._isNight;

    /// <summary>0..1 progress through the full cycle.</summary>
    public static float Cycle01 => Instance != null ? Instance._cycle01 : 0f;

    /// <summary>1 at full day, 0 at full night.</summary>
    public static float DayBlend => Instance != null ? Instance._dayBlend : 1f;

    public static event Action<bool> NightChanged;

    [Header("Timing")]
    [SerializeField] float cycleDurationSeconds = 150f;
    [Tooltip("0 midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset.")]
    [SerializeField] [Range(0f, 1f)] float startCycle01 = 0.4f;

    [Header("Sun")]
    [SerializeField] Light sun;
    [SerializeField] float dayIntensity = 2f;
    [SerializeField] float nightIntensity = 0.28f;
    [SerializeField] float dayColorTemperature = 5000f;
    [SerializeField] float nightColorTemperature = 2800f;
    [Tooltip("Peak sun altitude above the southern horizon (never reaches overhead).")]
    [SerializeField] [Range(25f, 75f)] float maxElevationDegrees = 55f;
    [SerializeField] float dayBlendSunDownDot = -0.15f;
    [SerializeField] float dayBlendSunUpDot = 0.55f;
    [Tooltip("DayBlend at or below this counts as night for gameplay.")]
    [SerializeField] float nightThreshold = 0.35f;

    [Header("Ambient")]
    [SerializeField] Color dayAmbientSky = new Color(0.55f, 0.62f, 0.75f);
    [SerializeField] Color nightAmbientSky = new Color(0.05f, 0.07f, 0.14f);
    [SerializeField] Color dayAmbientEquator = new Color(0.35f, 0.38f, 0.42f);
    [SerializeField] Color nightAmbientEquator = new Color(0.04f, 0.05f, 0.08f);
    [SerializeField] Color dayAmbientGround = new Color(0.18f, 0.16f, 0.12f);
    [SerializeField] Color nightAmbientGround = new Color(0.02f, 0.02f, 0.03f);
    [SerializeField] float dayAmbientIntensity = 1f;
    [SerializeField] float nightAmbientIntensity = 0.35f;

    [Header("Fog")]
    [SerializeField] bool driveFog = true;
    [SerializeField] Color dayFogColor = new Color(0.72f, 0.78f, 0.85f);
    [SerializeField] Color nightFogColor = new Color(0.04f, 0.05f, 0.09f);
    [SerializeField] float dayFogDensity = 0.004f;
    [SerializeField] float nightFogDensity = 0.014f;

    [Header("Notices")]
    [SerializeField] bool announcePhaseChanges = true;

    float _cycle01;
    float _dayBlend = 1f;
    float _nightFactor;
    bool _isNight;
    bool _hasAnnouncedInitialPhase;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveSun();
        if (sun != null)
            RenderSettings.sun = sun;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnValidate()
    {
        cycleDurationSeconds = Mathf.Max(10f, cycleDurationSeconds);
        maxElevationDegrees = Mathf.Clamp(maxElevationDegrees, 25f, 75f);
        nightThreshold = Mathf.Clamp01(nightThreshold);
    }

    void Update()
    {
        if (sun == null)
        {
            ResolveSun();
            if (sun == null)
                return;
            RenderSettings.sun = sun;
        }

        float duration = Mathf.Max(10f, cycleDurationSeconds);
        _cycle01 = Mathf.Repeat(Time.time / duration + startCycle01, 1f);
        sun.transform.rotation = Quaternion.LookRotation(
            SunLightForward(_cycle01, maxElevationDegrees), Vector3.up);

        // Directional light rays follow transform.forward; compare to "down" for day vs night.
        float sunDown = Vector3.Dot(sun.transform.forward, Vector3.down);
        _dayBlend = Mathf.Clamp01(Mathf.InverseLerp(dayBlendSunDownDot, dayBlendSunUpDot, sunDown));
        _nightFactor = 1f - _dayBlend;

        sun.intensity = Mathf.Lerp(nightIntensity, dayIntensity, _dayBlend);
        if (sun.useColorTemperature)
            sun.colorTemperature = Mathf.Lerp(nightColorTemperature, dayColorTemperature, _dayBlend);

        ApplyAmbient();
        if (driveFog)
            ApplyFog();

        bool night = _dayBlend <= nightThreshold;
        if (night != _isNight || !_hasAnnouncedInitialPhase)
        {
            bool wasNight = _isNight;
            _isNight = night;
            if (_hasAnnouncedInitialPhase && wasNight != _isNight)
            {
                NightChanged?.Invoke(_isNight);
                if (announcePhaseChanges)
                    GameplayEvents.RaiseToast(_isNight ? "Night falls…" : "Dawn breaks.");
            }

            _hasAnnouncedInitialPhase = true;
        }
    }

    void ResolveSun()
    {
        if (sun != null)
            return;

        var lights = FindObjectsByType<Light>();
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
            {
                sun = lights[i];
                return;
            }
        }
    }

    /// <summary>
    /// Northern-hemisphere path: rises east, arcs across the southern sky, sets west.
    /// Unity axes: +X east, +Y up, +Z north. Light travels along the returned forward.
    /// </summary>
    static Vector3 SunLightForward(float cycle01, float maxElevationDegrees)
    {
        // 0.25 sunrise, 0.5 noon, 0.75 sunset; elevation is negative at night.
        float elevationRad = Mathf.Sin((cycle01 - 0.25f) * Mathf.PI * 2f)
            * maxElevationDegrees * Mathf.Deg2Rad;
        // 0.25 → east (90°), 0.5 → south (180°), 0.75 → west (270°).
        float azimuthRad = cycle01 * Mathf.PI * 2f;

        float cosEl = Mathf.Cos(elevationRad);
        Vector3 toSun = new Vector3(
            Mathf.Sin(azimuthRad) * cosEl,
            Mathf.Sin(elevationRad),
            Mathf.Cos(azimuthRad) * cosEl);

        return -toSun.normalized;
    }

    void ApplyAmbient()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(nightAmbientSky, dayAmbientSky, _dayBlend);
        RenderSettings.ambientEquatorColor = Color.Lerp(nightAmbientEquator, dayAmbientEquator, _dayBlend);
        RenderSettings.ambientGroundColor = Color.Lerp(nightAmbientGround, dayAmbientGround, _dayBlend);
        RenderSettings.ambientIntensity = Mathf.Lerp(nightAmbientIntensity, dayAmbientIntensity, _dayBlend);
    }

    void ApplyFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.Lerp(nightFogColor, dayFogColor, _dayBlend);
        RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, dayFogDensity, _dayBlend);
    }
}
