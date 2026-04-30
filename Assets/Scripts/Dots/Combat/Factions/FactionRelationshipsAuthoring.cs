using System;
using Medieval.DotsCombat;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes the classic <c>FactionRelationshipTable</c> into a DOTS blob singleton.
/// Put this on a GameObject inside a baked scene/subscene.
/// </summary>
[DisallowMultipleComponent]
public sealed class FactionRelationshipsAuthoring : MonoBehaviour
{
    [SerializeField] FactionRelationshipTable relationshipTable;
    [Tooltip("Extra faction definitions to include in sizing (optional).")]
    [SerializeField] FactionDefinition[] registeredFactions = Array.Empty<FactionDefinition>();

    class Baker : Baker<FactionRelationshipsAuthoring>
    {
        public override void Bake(FactionRelationshipsAuthoring authoring)
        {
            Entity e = GetEntity(TransformUsageFlags.None);

            int size = 0;
            if (authoring.relationshipTable != null)
                size = Mathf.Max(0, authoring.relationshipTable.ComputeRequiredMatrixSize(authoring.registeredFactions));

            // Default: 0..size-1, allied on diagonal, neutral elsewhere.
            var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref var root = ref builder.ConstructRoot<FactionRelationshipMatrixBlob>();
            root.Size = size;
            var values = builder.Allocate(ref root.Values, size * size);
            for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                values[r * size + c] = (byte)(r == c ? Relationship.Allied : Relationship.Neutral);

            if (authoring.relationshipTable != null)
            {
                // Apply explicit pairs from the ScriptableObject (already symmetric by authoring semantics).
                var pairs = authoring.relationshipTable.ExplicitPairs;
                if (pairs != null)
                {
                    for (int i = 0; i < pairs.Count; i++)
                    {
                        var p = pairs[i];
                        if (p.FactionA == null || p.FactionB == null)
                            continue;
                        int a = p.FactionA.FactionID;
                        int b = p.FactionB.FactionID;
                        if ((uint)a >= (uint)size || (uint)b >= (uint)size)
                            continue;
                        byte rel = (byte)(p.Relationship == global::Relationship.Enemy
                            ? Relationship.Enemy
                            : p.Relationship == global::Relationship.Allied
                                ? Relationship.Allied
                                : Relationship.Neutral);
                        values[a * size + b] = rel;
                        values[b * size + a] = rel;
                    }
                }
            }

            var blob = builder.CreateBlobAssetReference<FactionRelationshipMatrixBlob>(Unity.Collections.Allocator.Persistent);
            builder.Dispose();

            AddComponent(e, new FactionRelationships { Matrix = blob });
        }
    }
}

