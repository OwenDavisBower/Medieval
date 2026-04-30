using Unity.Entities;

namespace Medieval.DotsCombat
{
    /// <summary>Faction identity for DOTS entities. Matches <c>FactionDefinition.FactionID</c> semantics.</summary>
    public struct Faction : IComponentData
    {
        public int Id;
    }

    /// <summary>Relationship values, mirroring the classic faction system.</summary>
    public enum Relationship : byte
    {
        Allied = 0,
        Neutral = 1,
        Enemy = 2
    }

    /// <summary>Singleton holding a blob relationship matrix.</summary>
    public struct FactionRelationships : IComponentData
    {
        public BlobAssetReference<FactionRelationshipMatrixBlob> Matrix;
    }

    public struct FactionRelationshipMatrixBlob
    {
        public int Size;
        public BlobArray<byte> Values; // size*size, row-major
    }
}

