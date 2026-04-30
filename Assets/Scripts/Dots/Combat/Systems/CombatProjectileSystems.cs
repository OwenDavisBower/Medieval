using Medieval.DotsCombat;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.DotsCombatSystems
{
    [BurstCompile]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial struct CombatProjectileSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float now = (float)SystemAPI.Time.ElapsedTime;
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            var tfLookup = SystemAPI.GetComponentLookup<LocalTransform>(isReadOnly: true);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (tfRW, proj, entity) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRW<CombatProjectile>>()
                         .WithEntityAccess())
            {
                ref var p = ref proj.ValueRW;
                if (now >= p.ExpireAtTime)
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                float3 targetPos = p.LastKnownTargetPosition;
                if (p.Target != Entity.Null && tfLookup.HasComponent(p.Target))
                    targetPos = tfLookup[p.Target].Position;
                p.LastKnownTargetPosition = targetPos;

                float3 pos = tfRW.ValueRO.Position;
                float3 to = targetPos - pos;
                float distSq = math.lengthsq(to);

                float r = math.max(0.01f, p.HitRadius);
                if (distSq <= r * r)
                {
                    if (p.Target != Entity.Null)
                        DamageApi.Enqueue(ecb, p.Target, p.Damage, p.Source);
                    ecb.DestroyEntity(entity);
                    continue;
                }

                float dist = math.sqrt(distSq);
                float step = math.max(0f, p.Speed) * dt;

                if (dist <= step + r)
                {
                    tfRW.ValueRW.Position = targetPos;
                    if (p.Target != Entity.Null)
                        DamageApi.Enqueue(ecb, p.Target, p.Damage, p.Source);
                    ecb.DestroyEntity(entity);
                    continue;
                }

                float3 dir = to / math.max(1e-6f, dist);
                tfRW.ValueRW.Position = pos + dir * step;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}

