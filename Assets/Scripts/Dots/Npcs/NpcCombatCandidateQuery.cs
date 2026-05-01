using Medieval.NpcMovement;
using Unity.Entities;
using Unity.Transforms;

namespace Medieval.Npcs
{
    /// <summary>EntityQuery for NPCs that can participate in combat targeting (movement NPCs with profile + combat).</summary>
    public static class NpcCombatCandidateQuery
    {
        public static readonly ComponentType[] All =
        {
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<NpcProfile>(),
            ComponentType.ReadOnly<NpcCharacterCombatState>(),
            ComponentType.ReadOnly<NpcMovementTag>(),
        };

        public static EntityQuery CreateEntityQuery(EntityManager entityManager) =>
            entityManager.CreateEntityQuery(All);
    }
}
