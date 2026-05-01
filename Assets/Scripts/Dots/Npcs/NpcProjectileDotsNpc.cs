using Medieval.Dots.Factions;
using Medieval.NpcMovement;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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
            in ComponentLookup<NpcFactionId> factions,
            in ComponentLookup<NpcCharacterCombatState> combats,
            in ComponentLookup<LocalTransform> transforms,
            in DynamicBuffer<FactionRelationshipCell> relationshipBuf,
            int relationshipMatrixSize,
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

            bool hasShooterFaction = excludeShooterRoot != Entity.Null && factions.HasComponent(excludeShooterRoot);
            int shooterFactionId = -1;
            float3 shooterFoot = default;
            if (hasShooterFaction)
            {
                shooterFactionId = factions[excludeShooterRoot].Value;
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
                    if (hasShooterFaction && shooterFactionId >= 0 && factions.HasComponent(npcEntity) &&
                        relationshipMatrixSize > 0)
                    {
                        int victimFaction = factions[npcEntity].Value;
                        if (victimFaction >= 0 &&
                            FactionRelationshipBufferUtil.IsAllied(in relationshipBuf, relationshipMatrixSize,
                                shooterFactionId, victimFaction))
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

        /// <summary>
        /// Smallest t in [0,1] with P(t)=lerp(a,b,t) inside the same volume as <see cref="PointInHitVolume"/>,
        /// or -1 if the segment misses. Analytic cylinder (XZ) ∩ Y slab — replaces fixed-step sampling.
        /// </summary>
        static float MinTOnSegmentInNpcVolume(float3 a, float3 b, float3 foot, float pr)
        {
            const float horizRadius = 0.5f;
            const float eps = 1e-8f;

            if (PointInHitVolume(a, foot, pr))
                return 0f;

            float R = horizRadius + pr;
            float R2 = R * R;
            float yMin = foot.y - 0.15f;
            float yMax = foot.y + 2.15f + pr;

            float3 d = b - a;
            float dy = d.y;

            // Y slab ∩ [0,1]
            float ty0 = 0f;
            float ty1 = 1f;
            if (math.abs(dy) < eps)
            {
                if (a.y < yMin || a.y > yMax)
                    return -1f;
            }
            else
            {
                float tYa = (yMin - a.y) / dy;
                float tYb = (yMax - a.y) / dy;
                ty0 = math.min(tYa, tYb);
                ty1 = math.max(tYa, tYb);
            }

            float ox = a.x - foot.x;
            float oz = a.z - foot.z;
            float dx = d.x;
            float dz = d.z;
            float aQuad = dx * dx + dz * dz;

            float tc0;
            float tc1;

            if (aQuad < eps)
            {
                // Segment parallel to Y in XZ: fixed column test
                if (ox * ox + oz * oz > R2)
                    return -1f;
                tc0 = 0f;
                tc1 = 1f;
            }
            else
            {
                float bQuad = 2f * (ox * dx + oz * dz);
                float cQuad = ox * ox + oz * oz - R2;
                float disc = bQuad * bQuad - 4f * aQuad * cQuad;
                if (disc < 0f)
                    return -1f;

                float sqrtD = math.sqrt(disc);
                float inv2A = 0.5f / aQuad;
                float tRoot0 = (-bQuad - sqrtD) * inv2A;
                float tRoot1 = (-bQuad + sqrtD) * inv2A;
                tc0 = math.min(tRoot0, tRoot1);
                tc1 = math.max(tRoot0, tRoot1);
            }

            float tLo = math.max(0f, math.max(ty0, tc0));
            float tHi = math.min(1f, math.min(ty1, tc1));
            if (tLo > tHi)
                return -1f;

            return tLo;
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
