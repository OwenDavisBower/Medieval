using UnityEngine;

/// <summary>Shared projectile math utilities (lobbed/arc shots).</summary>
public static class ProjectileBallistics
{
    /// <summary>
    /// Computes a reasonable lob flight time purely from horizontal distance.
    /// (Matches existing arrow/tower heuristics.)
    /// </summary>
    public static float LobbedFlightTime(Vector3 from, Vector3 to, float distanceDivisor = 12f, float minSeconds = 0.55f,
        float maxSeconds = 2.2f, float minHorizontalDistance = 0.05f)
    {
        Vector3 displacement = to - from;
        Vector3 horizontal = new Vector3(displacement.x, 0f, displacement.z);
        float h = horizontal.magnitude;
        if (h < minHorizontalDistance)
            h = minHorizontalDistance;
        float div = Mathf.Max(0.01f, distanceDivisor);
        return Mathf.Clamp(h / div, minSeconds, maxSeconds);
    }

    /// <summary>
    /// Computes an initial velocity that will reach <paramref name="to"/> in the chosen flight time using gravity.
    /// </summary>
    public static Vector3 LobbedLaunchVelocity(Vector3 from, Vector3 to, out float flightTime, float distanceDivisor = 12f,
        float minSeconds = 0.55f, float maxSeconds = 2.2f, float minHorizontalDistance = 0.05f)
    {
        Vector3 displacement = to - from;
        Vector3 horizontal = new Vector3(displacement.x, 0f, displacement.z);
        float h = horizontal.magnitude;
        if (h < minHorizontalDistance)
            h = minHorizontalDistance;

        float dh = displacement.y;
        float g = -Physics.gravity.y;
        if (g < 0.01f)
            g = 9.81f;

        float div = Mathf.Max(0.01f, distanceDivisor);
        flightTime = Mathf.Clamp(h / div, minSeconds, maxSeconds);
        float t = flightTime;

        float vy = (dh + 0.5f * g * t * t) / t;
        Vector3 vHoriz = horizontal.normalized * (h / t);
        return new Vector3(vHoriz.x, vy, vHoriz.z);
    }

    /// <summary>Convenience overload when flight time isn't needed.</summary>
    public static Vector3 LobbedLaunchVelocity(Vector3 from, Vector3 to)
    {
        return LobbedLaunchVelocity(from, to, out _);
    }
}

