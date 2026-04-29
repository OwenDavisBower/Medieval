#nullable enable
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared 3×3 (or poolSide×poolSide) logical chunk window math for terrain streaming and world content.
/// </summary>
public static class TerrainLogicalChunkWindow
{
    public const int DefaultStreamingPoolSide = 3;

    /// <summary>
    /// Clamps a streaming window origin so a poolSide×poolSide region stays inside a logicalChunkAxis×logicalChunkAxis grid.
    /// </summary>
    public static Vector2Int ComputeWindowOrigin(
        Vector3 anchorWorld,
        Vector3 worldOrigin,
        float worldSize,
        int logicalChunkAxis,
        int poolSide)
    {
        logicalChunkAxis = Mathf.Max(1, logicalChunkAxis);
        poolSide = Mathf.Max(1, poolSide);
        float chunkWorld = worldSize / logicalChunkAxis;
        float relX = anchorWorld.x - worldOrigin.x + worldSize * 0.5f;
        float relZ = anchorWorld.z - worldOrigin.z + worldSize * 0.5f;
        int pcx = Mathf.FloorToInt(relX / Mathf.Max(1e-4f, chunkWorld));
        int pcz = Mathf.FloorToInt(relZ / Mathf.Max(1e-4f, chunkWorld));
        pcx = Mathf.Clamp(pcx, 0, Mathf.Max(0, logicalChunkAxis - 1));
        pcz = Mathf.Clamp(pcz, 0, Mathf.Max(0, logicalChunkAxis - 1));

        int half = (poolSide - 1) / 2;
        int maxOrigin = Mathf.Max(0, logicalChunkAxis - poolSide);
        int ox = Mathf.Clamp(pcx - half, 0, maxOrigin);
        int oz = Mathf.Clamp(pcz - half, 0, maxOrigin);
        return new Vector2Int(ox, oz);
    }

    /// <summary>Logical chunk indices (clamped) containing world XZ.</summary>
    public static Vector2Int WorldXZToChunk(
        Vector3 worldOrigin,
        float worldSize,
        int logicalChunkAxis,
        float worldX,
        float worldZ)
    {
        logicalChunkAxis = Mathf.Max(1, logicalChunkAxis);
        float chunkWorld = worldSize / logicalChunkAxis;
        float relX = worldX - worldOrigin.x + worldSize * 0.5f;
        float relZ = worldZ - worldOrigin.z + worldSize * 0.5f;
        int pcx = Mathf.FloorToInt(relX / Mathf.Max(1e-4f, chunkWorld));
        int pcz = Mathf.FloorToInt(relZ / Mathf.Max(1e-4f, chunkWorld));
        pcx = Mathf.Clamp(pcx, 0, Mathf.Max(0, logicalChunkAxis - 1));
        pcz = Mathf.Clamp(pcz, 0, Mathf.Max(0, logicalChunkAxis - 1));
        return new Vector2Int(pcx, pcz);
    }

    /// <summary>Fills <paramref name="into"/> with logical chunks covered by the window [ox,ox+poolSide) × [oz,oz+poolSide).</summary>
    public static void CollectWindowChunks(int ox, int oz, int poolSide, int logicalChunkAxis, HashSet<Vector2Int> into)
    {
        into.Clear();
        poolSide = Mathf.Max(1, poolSide);
        logicalChunkAxis = Mathf.Max(1, logicalChunkAxis);
        for (int z = 0; z < poolSide; z++)
        {
            for (int x = 0; x < poolSide; x++)
            {
                int cx = ox + x;
                int cz = oz + z;
                if (cx >= 0 && cz >= 0 && cx < logicalChunkAxis && cz < logicalChunkAxis)
                    into.Add(new Vector2Int(cx, cz));
            }
        }
    }
}
