#nullable enable
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// When terrain streaming moves the logical chunk window, enables/disables DOTS NPC entities for chunks
/// that left or entered the window. GameObject NPC streaming was removed; ECS uses <see cref="TerrainChunkDotsNpcStreaming"/>.
/// </summary>
public static class TerrainChunkCharacterStreaming
{
    static readonly HashSet<Vector2Int> OldWindowScratch = new();
    static readonly HashSet<Vector2Int> NewWindowScratch = new();
    static readonly HashSet<Vector2Int> DiffScratch = new();

    /// <summary>Call when the terrain streaming window origin changes (same 3×3 window as <see cref="TerrainGenerator"/>).</summary>
    public static void OnTerrainStreamingWindowMoved(
        TerrainGenerator terrain,
        Vector2Int previousWindowOrigin,
        Vector2Int newWindowOrigin,
        int poolSide)
    {
        if (terrain == null || !Application.isPlaying)
            return;

        int axis = Mathf.Max(1, terrain.chunkCount);
        poolSide = Mathf.Max(1, poolSide);

        OldWindowScratch.Clear();
        NewWindowScratch.Clear();
        if (previousWindowOrigin.x != int.MinValue)
            TerrainLogicalChunkWindow.CollectWindowChunks(previousWindowOrigin.x, previousWindowOrigin.y, poolSide, axis, OldWindowScratch);
        TerrainLogicalChunkWindow.CollectWindowChunks(newWindowOrigin.x, newWindowOrigin.y, poolSide, axis, NewWindowScratch);

        DiffScratch.Clear();
        foreach (Vector2Int c in OldWindowScratch)
        {
            if (!NewWindowScratch.Contains(c))
                DiffScratch.Add(c);
        }
        foreach (Vector2Int c in DiffScratch)
            TerrainChunkDotsNpcStreaming.DisableNpcsInChunk(c, terrain);

        DiffScratch.Clear();
        foreach (Vector2Int c in NewWindowScratch)
        {
            if (!OldWindowScratch.Contains(c))
                DiffScratch.Add(c);
        }
        foreach (Vector2Int c in DiffScratch)
            TerrainChunkDotsNpcStreaming.EnableNpcsForChunk(c);
    }

    /// <summary>Domain reload safety in editor.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OldWindowScratch.Clear();
        NewWindowScratch.Clear();
        DiffScratch.Clear();
    }
}
