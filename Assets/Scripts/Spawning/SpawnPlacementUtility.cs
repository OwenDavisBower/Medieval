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

    /// <summary>Uniform random XZ on the procedural terrain footprint; Y is zero. Respects <paramref name="edgeMargin"/> inset from each world edge (same convention as <see cref="ClampWorldXZToTerrain"/>).</summary>
    public static Vector3 RandomUniformWorldXZInTerrain(TerrainGenerator gen, float edgeMargin)
    {
        float half = gen.worldSize * 0.5f - edgeMargin;
        if (half <= 0f)
            half = gen.worldSize * 0.25f;

        var o = gen.transform.position;
        float x = Random.Range(o.x - half, o.x + half);
        float z = Random.Range(o.z - half, o.z + half);
        return new Vector3(x, 0f, z);
    }

    /// <summary>
    /// Uniform random XZ inside one logical terrain chunk (see <see cref="TerrainGenerator.chunkCount"/>), intersected with the global terrain footprint inset by <paramref name="edgeMargin"/>.
    /// Chunk axes match <see cref="TerrainLogicalChunkWindow.WorldXZToChunk"/> (X → chunkX, Z → chunkZ).
    /// </summary>
    public static bool TryRandomUniformWorldXZInTerrainChunk(
        TerrainGenerator gen,
        int chunkX,
        int chunkZ,
        float edgeMargin,
        out Vector3 xz)
    {
        xz = default;
        if (gen == null)
            return false;

        int axis = Mathf.Max(1, gen.chunkCount);
        chunkX = Mathf.Clamp(chunkX, 0, axis - 1);
        chunkZ = Mathf.Clamp(chunkZ, 0, axis - 1);

        float ws = gen.worldSize;
        float cw = ws / axis;
        float inset = Mathf.Max(0f, edgeMargin);

        float relMinX = chunkX * cw;
        float relMaxX = (chunkX + 1) * cw;
        float relMinZ = chunkZ * cw;
        float relMaxZ = (chunkZ + 1) * cw;

        relMinX = Mathf.Max(relMinX, inset);
        relMaxX = Mathf.Min(relMaxX, ws - inset);
        relMinZ = Mathf.Max(relMinZ, inset);
        relMaxZ = Mathf.Min(relMaxZ, ws - inset);

        if (relMinX >= relMaxX || relMinZ >= relMaxZ)
            return false;

        var o = gen.transform.position;
        float rx = Random.Range(relMinX, relMaxX);
        float rz = Random.Range(relMinZ, relMaxZ);
        xz = new Vector3(o.x - ws * 0.5f + rx, 0f, o.z - ws * 0.5f + rz);
        return true;
    }

    /// <summary>Axis-aligned bounds check for world XZ inside the procedural terrain footprint.</summary>
    public static bool IsWorldXZInsideTerrain(TerrainGenerator gen, float x, float z)
    {
        float half = gen.worldSize * 0.5f;
        var o = gen.transform.position;
        return x >= o.x - half && x <= o.x + half && z >= o.z - half && z <= o.z + half;
    }
}
