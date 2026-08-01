using UnityEngine;

/// <summary>Shared projectile math utilities (lobbed/arc shots).</summary>
public static class ProjectileBallistics
{
    /// <summary>
    /// Minimum horizontal aim distance used when computing lob velocity.
    /// Closer targets are aimed <em>through</em> so muzzle speed stays high.
    /// </summary>
    public const float DefaultMinAimHorizontalDistance = 14f;

    /// <summary>
    /// Computes a reasonable lob flight time purely from horizontal distance.
    /// (Matches existing arrow/tower heuristics.)
    /// </summary>
    public static float LobbedFlightTime(Vector3 from, Vector3 to, float distanceDivisor = 20f, float minSeconds = 0.32f,
        float maxSeconds = 1.6f, float minHorizontalDistance = 0.05f,
        float minAimHorizontalDistance = DefaultMinAimHorizontalDistance)
    {
        AimThroughIfNeeded(from, ref to, minHorizontalDistance, minAimHorizontalDistance);
        Vector3 displacement = to - from;
        Vector3 horizontal = new Vector3(displacement.x, 0f, displacement.z);
        float h = Mathf.Max(minHorizontalDistance, horizontal.magnitude);
        float div = Mathf.Max(0.01f, distanceDivisor);
        return Mathf.Clamp(h / div, minSeconds, maxSeconds);
    }

    /// <summary>
    /// Computes an initial velocity that will reach <paramref name="to"/> in the chosen flight time using gravity.
    /// Nearby aim points are extended through the target so close-range shots keep a fast muzzle speed.
    /// </summary>
    public static Vector3 LobbedLaunchVelocity(Vector3 from, Vector3 to, out float flightTime, float distanceDivisor = 20f,
        float minSeconds = 0.32f, float maxSeconds = 1.6f, float minHorizontalDistance = 0.05f,
        float minAimHorizontalDistance = DefaultMinAimHorizontalDistance)
    {
        AimThroughIfNeeded(from, ref to, minHorizontalDistance, minAimHorizontalDistance);

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

    /// <summary>
    /// If the aim point is closer than <paramref name="minAimHorizontalDistance"/>, push it along the
    /// horizontal shot line (same height) so velocity is solved for a farther landing point through the target.
    /// </summary>
    static void AimThroughIfNeeded(Vector3 from, ref Vector3 to, float minHorizontalDistance, float minAimHorizontalDistance)
    {
        if (minAimHorizontalDistance <= 0f)
            return;

        Vector3 horizontal = new Vector3(to.x - from.x, 0f, to.z - from.z);
        float h = horizontal.magnitude;
        if (h >= minAimHorizontalDistance)
            return;

        Vector3 dir = h > minHorizontalDistance ? horizontal / h : Vector3.forward;
        to = new Vector3(from.x + dir.x * minAimHorizontalDistance, to.y, from.z + dir.z * minAimHorizontalDistance);
    }
}
