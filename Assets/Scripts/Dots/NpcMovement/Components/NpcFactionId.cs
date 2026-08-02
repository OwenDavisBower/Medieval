using Unity.Entities;

namespace Medieval.NpcMovement
{
    /// <summary>Indexes <see cref="FactionManager"/> / ECS relationship buffer; matches <see cref="FactionDefinition.FactionID"/>.</summary>
    public struct NpcFactionId : IComponentData
    {
        public int Value;
    }

    /// <summary>Matches <c>Assets/Data/Factions/*.asset</c> defaults.</summary>
    public static class WellKnownFactionIds
    {
        public const int Player = 0;
        public const int Bandit = 1;
        public const int Villager = 2;
        /// <summary>Neutral kingdom; may own settlements and field road patrols.</summary>
        public const int Ravenholt = 3;
        /// <summary>Neutral kingdom; may own settlements and field road patrols.</summary>
        public const int Oakenshield = 4;

        /// <summary>Faction ids that can own settlements and spawn road armies (excludes player/bandit/villager).</summary>
        public static readonly int[] NeutralKingdomIds = { Ravenholt, Oakenshield };

        public static bool IsNeutralKingdom(int factionId)
        {
            for (int i = 0; i < NeutralKingdomIds.Length; i++)
            {
                if (NeutralKingdomIds[i] == factionId)
                    return true;
            }

            return false;
        }
    }
}
