using Medieval.NpcMovement;
using ProjectDawn.Animation;
using Unity.Collections;
using Unity.Entities;

namespace Medieval.Npcs
{
    /// <summary>When combat state marks an NPC dead, strips movement/combat participation and plays death animation.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcCombatSeekSystemGroup))]
    [UpdateBefore(typeof(AnimatronSystemGroup))]
    public partial struct NpcDeathEnterSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            using var pending = new NativeList<Entity>(Allocator.Temp);
            foreach (var (combat, entity) in SystemAPI
                         .Query<RefRO<NpcCharacterCombatState>>()
                         .WithNone<NpcDeadTag>()
                         .WithEntityAccess())
            {
                if (combat.ValueRO.IsDead == 0)
                    continue;
                pending.Add(entity);
            }

            for (int i = 0; i < pending.Length; i++)
                NpcDeathTransitionUtility.TryApply(em, pending[i]);
        }
    }
}
