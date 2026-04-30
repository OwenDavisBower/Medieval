using Medieval.DotsCombat;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.DotsCombatSystems
{
    /// <summary>Applies queued damage and transitions entities into the dead state.</summary>
    [BurstCompile]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial struct ApplyDamageSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float now = (float)SystemAPI.Time.ElapsedTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (healthRW, damage, entity) in SystemAPI
                         .Query<RefRW<Health>, DynamicBuffer<DamageEvent>>()
                         .WithNone<DeadTag>()
                         .WithEntityAccess())
            {
                if (damage.Length == 0)
                    continue;

                float total = 0f;
                for (int i = 0; i < damage.Length; i++)
                {
                    float a = damage[i].Amount;
                    if (a > 0f)
                        total += a;
                }
                damage.Clear();

                if (total <= 0f)
                    continue;

                var h = healthRW.ValueRW;
                h.Current = math.max(0f, h.Current - total);
                healthRW.ValueRW = h;

                if (h.Current > 0f)
                    continue;

                ecb.AddComponent<DeadTag>(entity);

                float despawnDelay = 0f;
                if (SystemAPI.HasComponent<DeathConfig>(entity))
                    despawnDelay = math.max(0f, SystemAPI.GetComponent<DeathConfig>(entity).DespawnDelaySeconds);

                ecb.AddComponent(entity, new DeathState
                {
                    TimeOfDeath = now,
                    DespawnAtTime = now + despawnDelay
                });
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    /// <summary>Destroys entities whose death timer has elapsed.</summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    public partial struct DeathDespawnSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float now = (float)SystemAPI.Time.ElapsedTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (death, entity) in SystemAPI
                         .Query<RefRO<DeathState>>()
                         .WithAll<DeadTag>()
                         .WithEntityAccess())
            {
                if (now >= death.ValueRO.DespawnAtTime)
                    ecb.DestroyEntity(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}

