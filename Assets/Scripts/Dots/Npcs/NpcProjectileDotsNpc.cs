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
        /// <summary>
        /// Horizontal distance at or below which allied NPCs do not take ranged damage (clustered friendly fire).
        /// </summary>
        public const float CloseGroupedAlliedRangedFriendlyFireHorizMeters = 4f;

        /// <summary>
        /// Uniform XZ cell size for projectile vs NPC broadphase (see <see cref="TryFindClosestAlongSegment"/>).
        /// </summary>
        public const float ProjectileNpcSpatialCellSize = 2.5f;

        /// <summary>
        /// Narrowphase along segment using a single broadphase map built for all projectiles this tick.
        /// </summary>
        public static bool TryFindClosestAlongSegment(
            in NativeParallelMultiHashMap<int2, Entity> cellMap,
            float cellSize,
            in ComponentLookup<NpcProfile> profiles,
            in ComponentLookup<NpcCharacterCombatState> combats,
            in ComponentLookup<LocalTransform> transforms,
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

            // Expand segment AABB on XZ by NPC horizontal hit radius so feet in neighbor cells are not missed.
            float horizReach = 0.5f + projectileRadius;
            float minX = math.min(prev.x, cur.x) - horizReach;
            float maxX = math.max(prev.x, cur.x) + horizReach;
            float minZ = math.min(prev.z, cur.z) - horizReach;
            float maxZ = math.max(prev.z, cur.z) + horizReach;

            int cminX = (int)math.floor(minX / cellSize);
            int cmaxX = (int)math.floor(maxX / cellSize);
            int cminZ = (int)math.floor(minZ / cellSize);
            int cmaxZ = (int)math.floor(maxZ / cellSize);

            bool hasShooterProfile = excludeShooterRoot != Entity.Null && profiles.HasComponent(excludeShooterRoot);
            NpcRole shooterRole = default;
            float3 shooterFoot = default;
            if (hasShooterProfile)
            {
                shooterRole = profiles[excludeShooterRoot].Role;
                shooterFoot = transforms[excludeShooterRoot].Position;
            }

            float closeFfSq = CloseGroupedAlliedRangedFriendlyFireHorizMeters *
                CloseGroupedAlliedRangedFriendlyFireHorizMeters;

            for (int cx = cminX; cx <= cmaxX; cx++)
            for (int cz = cminZ; cz <= cmaxZ; cz++)
            {
                var key = new int2(cx, cz);
                if (!cellMap.TryGetFirstValue(key, out Entity npcEntity, out var it))
                    continue;
                do
                {
                    if (excludeShooterRoot != Entity.Null && npcEntity == excludeShooterRoot)
                        continue;
                    if (!combats.HasComponent(npcEntity) || !transforms.HasComponent(npcEntity))
                        continue;

                    var combatState = combats[npcEntity];
                    if (combatState.IsDead != 0 || combatState.CurrentHealth <= 0f)
                        continue;

                    float3 foot = transforms[npcEntity].Position;
                    if (hasShooterProfile && profiles.HasComponent(npcEntity))
                    {
                        var victimRole = profiles[npcEntity].Role;
                        if (NpcCombatRoleHostility.AreAlliedForCloseRangedFriendlyFire(shooterRole, victimRole))
                        {
                            float dx = foot.x - shooterFoot.x;
                            float dz = foot.z - shooterFoot.z;
                            if (dx * dx + dz * dz <= closeFfSq)
                                continue;
                        }
                    }

                    float t = MinTOnSegmentInNpcVolume(prev, cur, foot, projectileRadius);
                    if (t < 0f || t > 1f)
                        continue;
                    float dist = t * segLen;
                    if (dist < closestHitDistanceFromPrev)
                    {
                        closestHitDistanceFromPrev = dist;
                        victim = npcEntity;
                    }
                } while (cellMap.TryGetNextValue(out npcEntity, ref it));
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
