using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global faction relationship service. Builds a dense symmetric matrix from a <see cref="FactionRelationshipTable"/>
/// plus registered factions so their ids are included in capacity; <see cref="GetRelationship"/> is O(1).
/// Runtime edits apply immediately.
/// </summary>
public sealed class FactionManager : MonoBehaviour
{
    public static FactionManager Instance { get; private set; }

    [Header("Data")]
    [SerializeField] FactionRelationshipTable relationshipTable;
    [Tooltip("Factions to include in matrix sizing (e.g. all factions used in the scene). Null entries are ignored.")]
    [SerializeField] List<FactionDefinition> registeredFactions = new List<FactionDefinition>();

    [Header("Lifecycle")]
    [SerializeField] bool dontDestroyOnLoad = true;

    Relationship[,] _matrix;
    int _size;
    Relationship _defaultNeutral;

    /// <summary>Increments when the relationship matrix is rebuilt or edited; DOTS bridge copies use this to detect changes.</summary>
    public int RelationshipMatrixVersion { get; private set; }

    public IReadOnlyList<FactionDefinition> RegisteredFactions => registeredFactions;

    /// <summary>Current matrix dimension (max faction id + 1). 0 if not initialized.</summary>
    public int RelationshipMatrixSize => _matrix != null ? _size : 0;

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

        RebuildRelationshipMatrix();
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Rebuilds the internal matrix from the relationship table and the inspector faction list.</summary>
    public void RebuildRelationshipMatrix()
    {
        var extras = new List<FactionDefinition>();
        if (registeredFactions != null)
        {
            for (int i = 0; i < registeredFactions.Count; i++)
            {
                if (registeredFactions[i] != null)
                    extras.Add(registeredFactions[i]);
            }
        }

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
        RelationshipMatrixVersion++;

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
        RelationshipMatrixVersion++;
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
            RebuildRelationshipMatrix();
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

    /// <summary>Writes row-major <paramref name="a"/> major × <paramref name="b"/> entries as enum bytes for ECS.</summary>
    public void CopyRelationshipMatrixBytes(System.Span<byte> destination)
    {
        if (_matrix == null || destination.Length < _size * _size)
            return;
        int i = 0;
        for (int a = 0; a < _size; a++)
        for (int b = 0; b < _size; b++)
            destination[i++] = (byte)_matrix[a, b];
    }
}
