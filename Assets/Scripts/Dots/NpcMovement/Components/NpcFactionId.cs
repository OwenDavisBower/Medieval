using Unity.Entities;

namespace Medieval.NpcMovement
{
    /// <summary>Indexes <see cref="FactionManager"/> / ECS relationship buffer; matches <see cref="FactionDefinition.FactionID"/>.</summary>
    public struct NpcFactionId : IComponentData
    {
        public int Value;
    }

    /// <summary>Matches <c>Assets/Data/Factions/*.asset</c> defaults (player 0, bandit 1, villager 2).</summary>
    public static class WellKnownFactionIds
    {
        public const int Player = 0;
        public const int Bandit = 1;
        public const int Villager = 2;
    }
}
