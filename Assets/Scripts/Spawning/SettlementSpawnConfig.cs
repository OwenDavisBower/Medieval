using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct SettlementLayerAnnulus
{
    [Tooltip("Matches SettlementBuildingSpawnEntry.layer.")]
    [Min(1)]
    public int layer;
    [Tooltip("Minimum horizontal distance from the settlement center (XZ).")]
    public float innerRadius;
    [Tooltip("Maximum horizontal distance from the settlement center (XZ).")]
    public float outerRadius;

    public readonly int EffectiveLayer => layer < 1 ? 1 : layer;
}

[System.Serializable]
public struct SettlementBuildingSpawnEntry
{
    [Tooltip("Prefab instantiated for this building type.")]
    public GameObject prefab;
    [Min(0)]
    public int minCount;
    [Min(0)]
    public int maxCount;
    [Tooltip("Disk radius checked against the procedural placement mask at each candidate site.")]
    public float placementRadius;
    [Tooltip("If true, spawns villagers near each instance (same behavior as former cabin placement).")]
    public bool spawnVillagersHere;
    [Tooltip("Bandit camp only: use Euler (-90, yaw, 0) instead of (0, yaw, 0) when instantiating (common FBX axis fix). Ignored by settlement building spawn.")]
    public bool applyMinus90XRotation;
    [Tooltip("Uniform multiplier for prefab local scale on X, Y, and Z. Defaults to 1; values ≤ 0 are treated as 1 for older serialized entries.")]
    [Min(0.0001f)]
    public float uniformScale;
    [Tooltip("Lower layers are fully placed before higher layers. Within a layer, rolled instances are shuffled so building types interleave randomly.")]
    [Min(1)]
    public int layer;

    /// <summary>Safe multiplier for spawn code (legacy serialized entries may have 0 before re-save).</summary>
    public readonly float EffectiveUniformScale => uniformScale > 0f ? uniformScale : 1f;

    /// <summary>Serialized 0 (older assets) behaves as layer 1.</summary>
    public readonly int EffectiveLayer => layer < 1 ? 1 : layer;
}

/// <summary>
/// Layer-ordered settlement/camp structure placement: expand counts per entry, shuffle within each layer, outer layers after inner.
/// </summary>
public static class SettlementStructureSpawnLayout
{
    /// <summary>Merges authored layer rings into a lookup (duplicate layer indices widen the annulus).</summary>
    public static void MergeLayerAnnuliFromAuthoring(
        IReadOnlyList<SettlementLayerAnnulus> source,
        Dictionary<int, (float inner, float outer)> into)
    {
        into.Clear();
        if (source == null || source.Count == 0)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            var s = source[i];
            int L = s.EffectiveLayer;
            float a = Mathf.Min(s.innerRadius, s.outerRadius);
            float b = Mathf.Max(s.innerRadius, s.outerRadius);

            if (into.TryGetValue(L, out var prev))
                into[L] = (Mathf.Min(prev.inner, a), Mathf.Max(prev.outer, b));
            else
                into[L] = (a, b);
        }
    }

    /// <summary>Builds placement jobs: for each layer ascending, all rolled instances for that layer in random order.</summary>
    public static void BuildShuffledLayerJobQueue(SettlementBuildingSpawnEntry[] entries, List<SettlementBuildingSpawnEntry> outJobs)
    {
        outJobs.Clear();
        if (entries == null || entries.Length == 0)
            return;

        var layerIds = new List<int>();
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].prefab == null)
                continue;
            int L = entries[i].EffectiveLayer;
            if (!layerIds.Contains(L))
                layerIds.Add(L);
        }

        layerIds.Sort();

        for (int li = 0; li < layerIds.Count; li++)
        {
            int L = layerIds[li];
            var layerJobs = new List<SettlementBuildingSpawnEntry>();

            for (int e = 0; e < entries.Length; e++)
            {
                var entry = entries[e];
                if (entry.prefab == null || entry.EffectiveLayer != L)
                    continue;

                int lo = Mathf.Min(entry.minCount, entry.maxCount);
                int hi = Mathf.Max(entry.minCount, entry.maxCount);
                int count = UnityEngine.Random.Range(lo, hi + 1);
                for (int i = 0; i < count; i++)
                    layerJobs.Add(entry);
            }

            Shuffle(layerJobs);
            outJobs.AddRange(layerJobs);
        }
    }

    static void Shuffle(List<SettlementBuildingSpawnEntry> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            SettlementBuildingSpawnEntry tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }
}

[CreateAssetMenu(fileName = "SettlementSpawnConfig", menuName = "Medieval/Spawning/Settlement Spawn Config")]
public class SettlementSpawnConfig : ScriptableObject
{
    [SerializeField] SettlementBuildingSpawnEntry[] buildings;
    [Tooltip("Horizontal annulus (XZ) from settlement center for each layer. Building entries reference layers by index.")]
    [SerializeField] SettlementLayerAnnulus[] structureLayers;
    [Tooltip("How many settlements to try placing in each logical terrain chunk (see TerrainGenerator.chunkCount). 0 disables settlement planning.")]
    [SerializeField, Min(0)] int settlementsPerLogicalChunk = 1;
    [Tooltip("Inset from procedural terrain edges when picking settlement centers (XZ).")]
    [SerializeField] float terrainEdgeMargin = 64f;
    [Tooltip("Minimum distance between settlement centers (XZ).")]
    [SerializeField] float minSettlementSeparation = 30f;
    [SerializeField] int maxSpawnAttemptsPerSettlement = 120;
    [Tooltip("XZ disk radius for procedural placement mask: settlement center must be free of path and prior burns.")]
    [SerializeField] float settlementCenterFootprintRadius = 14f;
    [Tooltip("Minimum horizontal distance from the nearest path centerline (meters). Keeps settlements off the path mesh; should be ≥ path occupancy stamp.")]
    [SerializeField] float minDistanceFromPathMeters = 12f;
    [Tooltip("Maximum horizontal distance from the nearest path (meters). Settlements must lie within this radius to count as near a path.")]
    [SerializeField] float maxDistanceFromPathMeters = 56f;

    [Header("Settlement paths (splat R)")]
    [Tooltip("Path ring runs this far outside each structure's horizontal bounds (world meters).")]
    [SerializeField] float pathRingOutsideFootprint = 1.25f;
    [Tooltip("Approximate spacing between samples along connecting paths (world meters).")]
    [SerializeField] float pathSegmentStepMeters = 1.4f;
    [Tooltip("Max lateral wobble for organic corridors (world meters).")]
    [SerializeField] float pathWobbleAmplitude = 1.1f;

    public IReadOnlyList<SettlementBuildingSpawnEntry> Buildings => buildings;
    public IReadOnlyList<SettlementLayerAnnulus> StructureLayers => structureLayers;
    public int SettlementsPerLogicalChunk => settlementsPerLogicalChunk;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinSettlementSeparation => minSettlementSeparation;
    public int MaxSpawnAttemptsPerSettlement => maxSpawnAttemptsPerSettlement;
    public float SettlementCenterFootprintRadius => settlementCenterFootprintRadius;
    /// <summary>Lower bound for path-distance ring; falls back if unset in older assets.</summary>
    public float MinDistanceFromPathMeters => minDistanceFromPathMeters > 0f ? minDistanceFromPathMeters : 12f;
    /// <summary>Upper bound for path-distance ring; falls back if unset in older assets.</summary>
    public float MaxDistanceFromPathMeters => maxDistanceFromPathMeters > 0f ? maxDistanceFromPathMeters : 56f;

    public float PathRingOutsideFootprint => pathRingOutsideFootprint > 0f ? pathRingOutsideFootprint : 1.25f;
    public float PathSegmentStepMeters => pathSegmentStepMeters > 0f ? pathSegmentStepMeters : 1.4f;
    public float PathWobbleAmplitude => pathWobbleAmplitude >= 0f ? pathWobbleAmplitude : 1.1f;
}
