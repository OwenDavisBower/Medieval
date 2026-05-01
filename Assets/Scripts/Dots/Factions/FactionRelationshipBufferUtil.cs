using Unity.Entities;

namespace Medieval.Dots.Factions
{
    /// <summary>Burst-safe style helpers for the copied faction matrix buffer (no managed calls).</summary>
    public static class FactionRelationshipBufferUtil
    {
        public static bool TryGetRelationship(in DynamicBuffer<FactionRelationshipCell> buf, int matrixSize, int a, int b,
            out Relationship relationship)
        {
            relationship = Relationship.Neutral;
            if (matrixSize <= 0 || buf.Length < matrixSize * matrixSize)
                return false;
            if (a < 0 || b < 0)
                return false;
            if ((uint)a >= (uint)matrixSize || (uint)b >= (uint)matrixSize)
                return false;
            relationship = (Relationship)buf[a * matrixSize + b].Value;
            return true;
        }

        public static bool IsHostile(in DynamicBuffer<FactionRelationshipCell> buf, int matrixSize, int a, int b)
        {
            return TryGetRelationship(in buf, matrixSize, a, b, out var r) && r == Relationship.Enemy;
        }

        public static bool IsAllied(in DynamicBuffer<FactionRelationshipCell> buf, int matrixSize, int a, int b)
        {
            return TryGetRelationship(in buf, matrixSize, a, b, out var r) && r == Relationship.Allied;
        }
    }
}
