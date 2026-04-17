using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public class DayNightCycle : MonoBehaviour
{
    [SerializeField] float cycleDurationSeconds = 60f;
    [SerializeField] float dayIntensity = 2f;
    [SerializeField] float nightIntensity = 0.3f;
    [SerializeField] float dayColorTemperature = 5000f;
    [SerializeField] float nightColorTemperature = 3200f;
    [SerializeField] float dayBlendSunDownDot = -0.15f;
    [SerializeField] float dayBlendSunUpDot = 0.55f;

    Light _light;
    Quaternion _baseRotation;

    void Awake()
    {
        _light = GetComponent<Light>();
        _baseRotation = transform.rotation;
    }

    void Update()
    {
        float t = Mathf.Repeat(Time.time, cycleDurationSeconds) / cycleDurationSeconds;
        transform.rotation = _baseRotation * Quaternion.AngleAxis(t * 360f, Vector3.right);

        // Directional light rays follow transform.forward; compare to "down" for day vs night.
        float sunDown = Vector3.Dot(transform.forward, Vector3.down);
        float dayBlend = Mathf.InverseLerp(dayBlendSunDownDot, dayBlendSunUpDot, sunDown);
        dayBlend = Mathf.Clamp01(dayBlend);

        _light.intensity = Mathf.Lerp(nightIntensity, dayIntensity, dayBlend);
        if (_light.useColorTemperature)
            _light.colorTemperature = Mathf.Lerp(nightColorTemperature, dayColorTemperature, dayBlend);
    }
}
