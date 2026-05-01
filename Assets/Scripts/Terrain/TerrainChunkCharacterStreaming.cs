#nullable enable
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// When terrain streaming moves the logical chunk window, disables bandit and villager roots inside
/// chunks that left the window and re-enables those previously disabled when a chunk re-enters.
/// Uses an axis-aligned physics box over the chunk XZ cell (full vertical span) for GameObject NPCs;
/// <see cref="TerrainChunkDotsNpcStreaming"/> applies the same policy to baked DOTS NPC entities.
/// </summary>
public static class TerrainChunkCharacterStreaming
{
    static readonly HashSet<Vector2Int> OldWindowScratch = new();
    static readonly HashSet<Vector2Int> NewWindowScratch = new();
    static readonly HashSet<Vector2Int> DiffScratch = new();
    static readonly HashSet<GameObject> RootsThisChunkScratch = new();

    /// <summary>Roots we disabled per logical chunk; cleared when the chunk is re-streamed in.</summary>
    static readonly Dictionary<Vector2Int, HashSet<GameObject>> DisabledRootsByChunk = new();

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

        Vector3 origin = terrain.transform.position;
        float ws = terrain.worldSize;
        float verticalHalf = Mathf.Max(128f, terrain.maxHeightVariation * 2f + Mathf.Abs(terrain.baseHeight) + 32f);
        float centerY = origin.y + terrain.baseHeight + terrain.maxHeightVariation * 0.5f;

        DiffScratch.Clear();
        foreach (Vector2Int c in OldWindowScratch)
        {
            if (!NewWindowScratch.Contains(c))
                DiffScratch.Add(c);
        }
        foreach (Vector2Int c in DiffScratch)
        {
            DisableUnitsInChunk(c, origin, ws, axis, centerY, verticalHalf);
            TerrainChunkDotsNpcStreaming.DisableNpcsInChunk(c, terrain);
        }

        DiffScratch.Clear();
        foreach (Vector2Int c in NewWindowScratch)
        {
            if (!OldWindowScratch.Contains(c))
                DiffScratch.Add(c);
        }
        foreach (Vector2Int c in DiffScratch)
        {
            EnableUnitsForChunk(c);
            TerrainChunkDotsNpcStreaming.EnableNpcsForChunk(c);
        }
    }

    static void DisableUnitsInChunk(
        Vector2Int chunk,
        Vector3 worldOrigin,
        float worldSize,
        int logicalChunkAxis,
        float boxCenterY,
        float verticalHalfExtent)
    {
        GetChunkBox(worldOrigin, worldSize, logicalChunkAxis, chunk.x, chunk.y, boxCenterY, verticalHalfExtent, out Vector3 center, out Vector3 halfExtents);

        // maxDistance 0: only colliders overlapping the box at the start pose (sweep degenerates to overlap).
        RaycastHit[] hits = Physics.BoxCastAll(
            center,
            halfExtents,
            Vector3.forward,
            Quaternion.identity,
            0f,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return;

        RootsThisChunkScratch.Clear();
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;
            Transform t = col.transform;
            while (t != null)
            {
                if (t.GetComponent<BanditController>() != null || t.GetComponent<VillagerController>() != null)
                {
                    RootsThisChunkScratch.Add(t.gameObject);
                    break;
                }
                t = t.parent;
            }
        }

        if (RootsThisChunkScratch.Count == 0)
            return;

        if (!DisabledRootsByChunk.TryGetValue(chunk, out HashSet<GameObject>? bucket))
        {
            bucket = new HashSet<GameObject>();
            DisabledRootsByChunk[chunk] = bucket;
        }

        foreach (GameObject root in RootsThisChunkScratch)
        {
            if (root == null || !root.activeSelf)
                continue;
            root.SetActive(false);
            bucket.Add(root);
        }
    }

    static void EnableUnitsForChunk(Vector2Int chunk)
    {
        if (!DisabledRootsByChunk.TryGetValue(chunk, out HashSet<GameObject>? bucket))
            return;

        foreach (GameObject go in bucket)
        {
            if (go != null)
                go.SetActive(true);
        }

        DisabledRootsByChunk.Remove(chunk);
    }

    static void GetChunkBox(
        Vector3 terrainWorldOrigin,
        float worldSize,
        int logicalChunkAxis,
        int cx,
        int cz,
        float centerY,
        float verticalHalfExtent,
        out Vector3 center,
        out Vector3 halfExtents)
    {
        logicalChunkAxis = Mathf.Max(1, logicalChunkAxis);
        float chunkWorld = worldSize / logicalChunkAxis;
        float minX = terrainWorldOrigin.x - worldSize * 0.5f + cx * chunkWorld;
        float minZ = terrainWorldOrigin.z - worldSize * 0.5f + cz * chunkWorld;
        center = new Vector3(minX + chunkWorld * 0.5f, centerY, minZ + chunkWorld * 0.5f);
        halfExtents = new Vector3(chunkWorld * 0.5f, verticalHalfExtent, chunkWorld * 0.5f);
    }

    /// <summary>Domain reload safety in editor.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OldWindowScratch.Clear();
        NewWindowScratch.Clear();
        DiffScratch.Clear();
        RootsThisChunkScratch.Clear();
        DisabledRootsByChunk.Clear();
    }
}
