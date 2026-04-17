using UnityEngine;

/// <summary>Raycast line-of-sight with self-colliders skipped along the segment.</summary>
public static class LineOfSightUtility
{
    const float Skin = 0.4f;
    const float MinRayLength = 0.02f;
    const float AdvanceEpsilon = 0.002f;
    const int MaxSegments = 32;

    /// <param name="observerFeetWorld">Base position of the observer (e.g. transform.position).</param>
    /// <param name="ignoreHitsUnderHierarchy">Hits whose colliders are under this transform are skipped (e.g. observer root).</param>
    public static bool HasClearLineOfSight(
        Vector3 observerFeetWorld,
        Transform target,
        float eyeHeight,
        float targetHeight,
        LayerMask obstacleLayers,
        Transform ignoreHitsUnderHierarchy)
    {
        if (target == null)
            return false;

        Vector3 eye = observerFeetWorld + Vector3.up * eyeHeight;
        Vector3 tgt = target.position + Vector3.up * targetHeight;
        Vector3 delta = tgt - eye;
        float distSq = delta.sqrMagnitude;
        float minSq = MinRayLength * MinRayLength;
        if (distSq < minSq)
            return true;

        float dist = Mathf.Sqrt(distSq);
        Vector3 dir = delta / dist;
        Vector3 origin = eye + dir * Skin;
        float remain = dist - Skin;
        if (remain <= 0.01f)
            return true;

        for (int seg = 0; seg < MaxSegments; seg++)
        {
            if (remain <= AdvanceEpsilon)
                return true;

            if (!Physics.Raycast(origin, dir, out RaycastHit hit, remain, obstacleLayers, QueryTriggerInteraction.Ignore))
                return true;

            Transform ht = hit.collider.transform;

            if (ignoreHitsUnderHierarchy != null && SpatialMath.IsTransformUnderHierarchy(ht, ignoreHitsUnderHierarchy))
            {
                float adv = Mathf.Max(hit.distance + AdvanceEpsilon, AdvanceEpsilon);
                if (adv >= remain - 1e-5f)
                    return true;
                origin += dir * adv;
                remain -= adv;
                continue;
            }

            if (SpatialMath.IsTransformUnderHierarchy(ht, target))
                return true;

            return false;
        }

        return true;
    }
}
