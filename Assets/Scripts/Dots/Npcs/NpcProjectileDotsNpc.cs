using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Medieval.NpcMovement;

namespace Medieval.Npcs
{
    /// <summary>Projectile hits against baked NPCs that have no GameObject colliders in the physics scene.</summary>
    public static class NpcProjectileDotsNpc
    {
        public static bool TryFindClosestAlongSegment(
            EntityManager em,
            float3 prev,
            float3 cur,
            float projectileRadius,
            Entity excludeShooterRoot,
            out Entity victim,
            out float closestHitDistanceFromPrev)
        {
            victim = Entity.Null;
            closestHitDistanceFromPrev = float.MaxValue;
            float segLen = math.distance(prev, cur);
            if (segLen < 1e-6f)
                return false;

            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<NpcCharacterCombatState>(),
                ComponentType.ReadOnly<NpcMovementTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var states = query.ToComponentDataArray<NpcCharacterCombatState>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (excludeShooterRoot != Entity.Null && entities[i] == excludeShooterRoot)
                    continue;
                if (states[i].IsDead != 0 || states[i].CurrentHealth <= 0f)
                    continue;
                float3 foot = transforms[i].Position;
                float t = MinTOnSegmentInNpcVolume(prev, cur, foot, projectileRadius);
                if (t < 0f || t > 1f)
                    continue;
                float dist = t * segLen;
                if (dist < closestHitDistanceFromPrev)
                {
                    closestHitDistanceFromPrev = dist;
                    victim = entities[i];
                }
            }

            return victim != Entity.Null;
        }

        static float MinTOnSegmentInNpcVolume(float3 a, float3 b, float3 foot, float pr)
        {
            float bestT = 2f;
            for (int s = 0; s <= 24; s++)
            {
                float t = s / 24f;
                float3 p = math.lerp(a, b, t);
                if (PointInHitVolume(p, foot, pr))
                    bestT = math.min(bestT, t);
            }

            return bestT <= 1f ? bestT : -1f;
        }

        static bool PointInHitVolume(float3 p, float3 foot, float pr)
        {
            float r = 0.5f + pr;
            float dx = p.x - foot.x;
            float dz = p.z - foot.z;
            if (dx * dx + dz * dz > r * r)
                return false;
            return p.y >= foot.y - 0.15f && p.y <= foot.y + 2.15f + pr;
        }

        public static void ApplyProjectileDamage(EntityManager em, Entity npc, float damageAmount)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcCharacterCombatState>(npc))
                return;
            var c = em.GetComponentData<NpcCharacterCombatState>(npc);
            if (c.IsDead != 0)
                return;
            c.CurrentHealth -= damageAmount;
            if (c.CurrentHealth <= 0f)
            {
                c.CurrentHealth = 0f;
                c.IsDead = 1;
            }

            em.SetComponentData(npc, c);
        }
    }
}
