using System.Collections.Generic;
using UnityEngine;

/// <summary>Uniform horizontal disks, separation tests, and terrain XZ bounds for world placement.</summary>
public static class SpawnPlacementUtility
{
    /// <summary>Uniform random point in a disk on the world XZ plane; Y is zero.</summary>
    public static Vector3 RandomUniformDiskOffsetXZ(float radius)
    {
        float ang = Random.Range(0f, Mathf.PI * 2f);
        float r = radius * Mathf.Sqrt(Random.value);
        return new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
    }

    /// <summary>Uniform disk offset using x = sin(angle)*r, z = cos(angle)*r (matches some steering code).</summary>
    public static Vector3 RandomUniformDiskOffsetXZ_SinXCosZ(float radius)
    {
        float ang = Random.Range(0f, Mathf.PI * 2f);
        float r = radius * Mathf.Sqrt(Random.value);
        return new Vector3(Mathf.Sin(ang) * r, 0f, Mathf.Cos(ang) * r);
    }

    /// <summary>Uniform random offset in an annulus on XZ; Y is zero.</summary>
    public static Vector3 RandomAnnulusOffsetXZ(float rMin, float rMax)
    {
        float ang = Random.Range(0f, Mathf.PI * 2f);
        float rad = Random.Range(rMin, rMax);
        return new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
    }

    public static bool IsFarEnoughFromAllXZ(Vector3 candidate, IReadOnlyList<Vector3> placed, float minSepSq)
    {
        float cx = candidate.x;
        float cz = candidate.z;
        for (int i = 0; i < placed.Count; i++)
        {
            float dx = cx - placed[i].x;
            float dz = cz - placed[i].z;
            if (dx * dx + dz * dz < minSepSq)
                return false;
        }

        return true;
    }

    public static Vector3 ClampWorldXZToTerrain(TerrainGenerator gen, Vector3 worldPos, float margin)
    {
        float half = gen.worldSize * 0.5f - margin;
        if (half <= 0f)
            half = gen.worldSize * 0.25f;

        var o = gen.transform.position;
        float x = Mathf.Clamp(worldPos.x, o.x - half, o.x + half);
        float z = Mathf.Clamp(worldPos.z, o.z - half, o.z + half);
        return new Vector3(x, worldPos.y, z);
    }

    /// <summary>Axis-aligned bounds check for world XZ inside the procedural terrain footprint.</summary>
    public static bool IsWorldXZInsideTerrain(TerrainGenerator gen, float x, float z)
    {
        float half = gen.worldSize * 0.5f;
        var o = gen.transform.position;
        return x >= o.x - half && x <= o.x + half && z >= o.z - half && z <= o.z + half;
    }
}
