using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>Spawns overhead level-up text for NPCs that just gained a level, then clears the FX component.</summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NpcLevelUpFxSystem : SystemBase
    {
        const float HeightOffset = 2.35f;

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;
            var toRemove = new NativeList<Entity>(8, Allocator.Temp);

            foreach (var (fxRw, xp, ltw, entity) in SystemAPI
                         .Query<RefRW<NpcLevelUpFx>, RefRO<NpcExperience>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                ref NpcLevelUpFx fx = ref fxRw.ValueRW;
                if (fx.Spawned == 0)
                {
                    var p = ltw.ValueRO.Position;
                    Vector3 world = new Vector3(p.x, p.y + HeightOffset, p.z);
                    FloatingWorldText.Spawn(world, xp.ValueRO.Level);
                    fx.Spawned = 1;
                }

                fx.SecondsRemaining -= dt;
                if (fx.SecondsRemaining <= 0f)
                    toRemove.Add(entity);
            }

            for (int i = 0; i < toRemove.Length; i++)
            {
                Entity e = toRemove[i];
                if (em.Exists(e) && em.HasComponent<NpcLevelUpFx>(e))
                    em.RemoveComponent<NpcLevelUpFx>(e);
            }

            toRemove.Dispose();
        }
    }
}
