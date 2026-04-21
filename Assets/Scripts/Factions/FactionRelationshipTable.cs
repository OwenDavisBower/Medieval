using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One directed or undirected pair as authored in a <see cref="FactionRelationshipTable"/>.</summary>
[Serializable]
public struct FactionRelationshipEntry
{
    public FactionDefinition FactionA;
    public FactionDefinition FactionB;
    public Relationship Relationship;
}

/// <summary>
/// Authoring asset for baseline faction pairs. At runtime, <see cref="FactionManager"/> expands this
/// into a dense symmetric matrix for O(1) queries. Pairs missing from the list use <see cref="DefaultBetweenUnlisted"/>.
/// </summary>
[CreateAssetMenu(fileName = "FactionRelationshipTable", menuName = "Medieval/Factions/Faction Relationship Table")]
public class FactionRelationshipTable : ScriptableObject
{
    [Tooltip("Used when rebuilding the matrix if a pair is not covered by explicit entries or bootstrap rules.")]
    [SerializeField] Relationship defaultBetweenUnlisted = Relationship.Neutral;

    [Tooltip("Optional fixed capacity for the runtime matrix (max exclusive index). If 0, capacity is inferred from IDs.")]
    [SerializeField] int matrixCapacityHint;

    [SerializeField] List<FactionRelationshipEntry> explicitPairs = new List<FactionRelationshipEntry>();

    public Relationship DefaultBetweenUnlisted => defaultBetweenUnlisted;
    public IReadOnlyList<FactionRelationshipEntry> ExplicitPairs => explicitPairs;
    public int MatrixCapacityHint => Mathf.Max(0, matrixCapacityHint);

    /// <summary>Largest faction id referenced by this table plus one (minimum 1).</summary>
    public int ComputeRequiredMatrixSize(IEnumerable<FactionDefinition> extraDefinitions)
    {
        int max = -1;
        void ConsiderDef(FactionDefinition d)
        {
            if (d == null)
                return;
            max = Mathf.Max(max, d.FactionID);
        }

        if (explicitPairs != null)
        {
            for (int i = 0; i < explicitPairs.Count; i++)
            {
                ConsiderDef(explicitPairs[i].FactionA);
                ConsiderDef(explicitPairs[i].FactionB);
            }
        }

        if (extraDefinitions != null)
        {
            foreach (FactionDefinition d in extraDefinitions)
                ConsiderDef(d);
        }

        int inferred = max + 1;
        if (inferred < 1)
            inferred = 1;
        return Mathf.Max(matrixCapacityHint, inferred);
    }
}
