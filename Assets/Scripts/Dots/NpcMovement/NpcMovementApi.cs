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

