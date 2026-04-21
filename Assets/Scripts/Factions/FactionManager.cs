using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global faction relationship service. Builds a dense symmetric matrix from a <see cref="FactionRelationshipTable"/>
/// and optional bootstrap assignments so <see cref="GetRelationship"/> is O(1). Runtime edits apply immediately.
/// </summary>
public sealed class FactionManager : MonoBehaviour
{
    public static FactionManager Instance { get; private set; }

    [Header("Data")]
    [SerializeField] FactionRelationshipTable relationshipTable;
    [Tooltip("If null, bootstrap rules below are skipped (table-only init).")]
    [SerializeField] FactionDefinition playerFaction;
    [SerializeField] FactionDefinition banditFaction;
    [SerializeField] FactionDefinition villagerFaction;

    [Header("Lifecycle")]
    [SerializeField] bool dontDestroyOnLoad = true;

    Relationship[,] _matrix;
    int _size;
    Relationship _defaultNeutral;

    public FactionDefinition PlayerFaction => playerFaction;
    public FactionDefinition BanditFaction => banditFaction;
    public FactionDefinition VillagerFaction => villagerFaction;

    void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Multiple {nameof(FactionManager)} instances; destroying duplicate on '{name}'.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        RebuildMatrixFromTableAndBootstrap();
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Rebuilds the internal matrix from the serialized table and default faction bootstrap.</summary>
    public void RebuildMatrixFromTableAndBootstrap()
    {
        var extras = new List<FactionDefinition>(3);
        if (playerFaction != null)
            extras.Add(playerFaction);
        if (banditFaction != null)
            extras.Add(banditFaction);
        if (villagerFaction != null)
            extras.Add(villagerFaction);

        _defaultNeutral = relationshipTable != null
            ? relationshipTable.DefaultBetweenUnlisted
            : Relationship.Neutral;

        int newSize = relationshipTable != null
            ? relationshipTable.ComputeRequiredMatrixSize(extras)
            : ComputeMaxId(extras) + 1;

        if (newSize < 1)
            newSize = 1;

        _matrix = new Relationship[newSize, newSize];
        _size = newSize;

        for (int y = 0; y < _size; y++)
        for (int x = 0; x < _size; x++)
            _matrix[y, x] = y == x ? Relationship.Allied : _defaultNeutral;

        if (relationshipTable != null)
        {
            IReadOnlyList<FactionRelationshipEntry> pairs = relationshipTable.ExplicitPairs;
            for (int i = 0; i < pairs.Count; i++)
            {
                FactionRelationshipEntry e = pairs[i];
                if (e.FactionA == null || e.FactionB == null)
                    continue;
                WriteSymmetric(e.FactionA.FactionID, e.FactionB.FactionID, e.Relationship);
            }
        }

        ApplyBootstrapDefaults();
    }

    void ApplyBootstrapDefaults()
    {
        if (playerFaction == null || banditFaction == null || villagerFaction == null)
            return;

        int p = playerFaction.FactionID;
        int b = banditFaction.FactionID;
        int v = villagerFaction.FactionID;

        // Spec: player ↔ villager neutral; bandit enemy to both.
        WriteSymmetric(p, v, Relationship.Neutral);
        WriteSymmetric(p, b, Relationship.Enemy);
        WriteSymmetric(v, b, Relationship.Enemy);
    }

    static int ComputeMaxId(List<FactionDefinition> defs)
    {
        int max = -1;
        for (int i = 0; i < defs.Count; i++)
        {
            if (defs[i] != null)
                max = Mathf.Max(max, defs[i].FactionID);
        }

        return max;
    }

    /// <summary>O(1) symmetric lookup. Same id returns <see cref="Relationship.Allied"/>.</summary>
    public Relationship GetRelationship(int factionA, int factionB)
    {
        if (factionA == factionB)
            return Relationship.Allied;
        if ((uint)factionA >= (uint)_size || (uint)factionB >= (uint)_size)
            return _defaultNeutral;
        return _matrix[factionA, factionB];
    }

    public Relationship GetRelationship(FactionDefinition a, FactionDefinition b)
    {
        if (a == null || b == null)
            return _defaultNeutral;
        return GetRelationship(a.FactionID, b.FactionID);
    }

    /// <summary>Updates both directions immediately; expands the matrix if ids exceed current capacity.</summary>
    public void SetRelationship(int factionA, int factionB, Relationship relationship)
    {
        if (factionA == factionB)
            return;

        EnsureCapacity(Mathf.Max(factionA, factionB) + 1);
        WriteSymmetricUnchecked(factionA, factionB, relationship);
    }

    public void SetRelationship(FactionDefinition a, FactionDefinition b, Relationship relationship)
    {
        if (a == null || b == null)
            return;
        SetRelationship(a.FactionID, b.FactionID, relationship);
    }

    void WriteSymmetric(int a, int b, Relationship r)
    {
        EnsureCapacity(Mathf.Max(a, b) + 1);
        WriteSymmetricUnchecked(a, b, r);
    }

    void WriteSymmetricUnchecked(int a, int b, Relationship r)
    {
        _matrix[a, b] = r;
        _matrix[b, a] = r;
    }

    void EnsureCapacity(int requiredSize)
    {
        if (_matrix == null)
            RebuildMatrixFromTableAndBootstrap();
        if (requiredSize <= _size)
            return;

        var next = new Relationship[requiredSize, requiredSize];
        for (int y = 0; y < requiredSize; y++)
        for (int x = 0; x < requiredSize; x++)
        {
            if (x < _size && y < _size)
                next[y, x] = _matrix[y, x];
            else if (x == y)
                next[y, x] = Relationship.Allied;
            else
                next[y, x] = _defaultNeutral;
        }

        _matrix = next;
        _size = requiredSize;
    }
}
