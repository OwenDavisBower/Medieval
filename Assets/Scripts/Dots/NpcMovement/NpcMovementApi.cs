using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Main-thread helpers for configuring NPC movement entities (anchor/seek/facing/dodges/locks).
    /// This replaces the old GameObject-side motor facade.
    /// </summary>
    public static class NpcMovementApi
    {
        const float DefaultFollowerCombatLeash = 15f;
        const float DefaultFollowerTeleportBackDistance = 80f;
        const float DefaultFollowerTeleportBackTargetDistance = 50f;

        /// <summary>
        /// Orbit the player like a party follower: <see cref="NpcSeparationGroup.Followers"/> + Orbit mode,
        /// with catch-up teleport distances. Optionally disables combat seek (escort civilians).
        /// </summary>
        public static void ConfigureAsPlayerFollower(EntityManager em, Entity npc, bool seeksCombat = true)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcMovementState>(npc))
                return;

            var state = em.GetComponentData<NpcMovementState>(npc);
            state.Group = NpcSeparationGroup.Followers;
            state.Mode = NpcMovementMode.Orbit;
            state.RangedMovementLock = 0;
            state.MeleeEngageMovementLock = 0;
            em.SetComponentData(npc, state);

            if (em.HasComponent<NpcSeekOverride>(npc))
            {
                em.SetComponentData(npc, new NpcSeekOverride
                {
                    Position = default,
                    SeekHoldDistance = 0f,
                    HasOverride = 0
                });
            }

            byte seeks = (byte)(seeksCombat ? 1 : 0);
            if (em.HasComponent<NpcCombatSeekConfig>(npc))
            {
                var cfg = em.GetComponentData<NpcCombatSeekConfig>(npc);
                if (cfg.MaxDistanceFromLeader <= 0f)
                    cfg.MaxDistanceFromLeader = DefaultFollowerCombatLeash;
                if (cfg.FollowerTeleportBackDistance <= 0f)
                {
                    cfg.FollowerTeleportBackDistance = DefaultFollowerTeleportBackDistance;
                    cfg.FollowerTeleportBackTargetDistance = DefaultFollowerTeleportBackTargetDistance;
                }
                cfg.SeeksCombatTargets = seeks;
                em.SetComponentData(npc, cfg);
            }
            else
            {
                em.AddComponentData(npc, new NpcCombatSeekConfig
                {
                    AggroRadius = 50f,
                    CombatRange = 20f,
                    RangedStandoffHoldDistance = 0f,
                    EyeHeight = 1.5f,
                    TargetAimHeight = 1f,
                    ObstacleLayerMask = ~0,
                    MaxDistanceFromLeader = DefaultFollowerCombatLeash,
                    FollowerTeleportBackDistance = DefaultFollowerTeleportBackDistance,
                    FollowerTeleportBackTargetDistance = DefaultFollowerTeleportBackTargetDistance,
                    SeeksCombatTargets = seeks
                });
            }
        }

        public static void SetAnchorPosition(EntityManager em, Entity npc, float3 position, float3 linearVelocity = default)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcAnchorTarget>(npc))
                return;

            em.SetComponentData(npc, new NpcAnchorTarget
            {
                Position = position,
                LinearVelocity = linearVelocity,
                HasAnchor = 1
            });
        }

        public static void ClearAnchor(EntityManager em, Entity npc)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcAnchorTarget>(npc))
                return;
            em.SetComponentData(npc, new NpcAnchorTarget());
        }

        public static void SetSeekOverride(EntityManager em, Entity npc, float3 position, float seekHoldDistance = 0f)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcSeekOverride>(npc))
                return;
            em.SetComponentData(npc, new NpcSeekOverride
            {
                Position = position,
                SeekHoldDistance = seekHoldDistance,
                HasOverride = 1
            });
        }

        public static void ClearSeekOverride(EntityManager em, Entity npc, float seekHoldDistance = 0f)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcSeekOverride>(npc))
                return;
            em.SetComponentData(npc, new NpcSeekOverride
            {
                Position = default,
                SeekHoldDistance = seekHoldDistance,
                HasOverride = 0
            });
        }

        public static void SetRangedMovementLock(EntityManager em, Entity npc, bool locked)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcMovementState>(npc))
                return;
            var s = em.GetComponentData<NpcMovementState>(npc);
            s.RangedMovementLock = (byte)(locked ? 1 : 0);
            em.SetComponentData(npc, s);
        }

        public static void SetOverrideFacing(EntityManager em, Entity npc, float3 flatDirection)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcOverrideFacing>(npc))
                return;
            flatDirection.y = 0f;
            em.SetComponentData(npc, new NpcOverrideFacing
            {
                FlatDirection = flatDirection,
                HasOverride = (byte)(math.lengthsq(flatDirection) > 1e-6f ? 1 : 0)
            });
        }

        public static void ClearOverrideFacing(EntityManager em, Entity npc)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcOverrideFacing>(npc))
                return;
            em.SetComponentData(npc, new NpcOverrideFacing());
        }

        public static void ScheduleRangedDodgeImpulse(EntityManager em, Entity npc, float3 referencePosition, float fireTime)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcPendingDodge>(npc))
                return;
            em.SetComponentData(npc, new NpcPendingDodge
            {
                ReferencePosition = referencePosition,
                FireTime = fireTime,
                HasPending = 1
            });
        }
    }
}

