using UnityEngine;

/// <summary>Horizontal (XZ) helpers and transform hierarchy checks used by combat and steering.</summary>
public static class SpatialMath
{
    public static float FlatSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    /// <summary>True if <paramref name="t"/> is <paramref name="root"/> or a descendant of it.</summary>
    public static bool IsTransformUnderHierarchy(Transform t, Transform root)
    {
        if (root == null || t == null)
            return false;
        for (Transform cur = t; cur != null; cur = cur.parent)
        {
            if (cur == root)
                return true;
        }

        return false;
    }
}
