using Unity.Entities;

namespace Medieval.Dots.Factions
{
    /// <summary>Singleton: last copied <see cref="FactionManager"/> relationship matrix size.</summary>
    public struct FactionRelationshipState : IComponentData
    {
        public int MatrixSize;
        public int SourceVersion;
    }

    /// <summary>Row-major flattening: index = <c>a * MatrixSize + b</c> → <see cref="Relationship"/> as byte.</summary>
    public struct FactionRelationshipCell : IBufferElementData
    {
        public byte Value;
    }
}
