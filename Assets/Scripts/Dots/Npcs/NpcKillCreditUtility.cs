using Medieval.NpcMovement;
using Unity.Entities;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>Marks bandit deaths caused by the player or party followers for standing / quest / gold credit.</summary>
    public static class NpcKillCreditUtility
    {
        public static bool IsFollower(EntityManager em, Entity npc)
        {
            return npc != Entity.Null && em.Exists(npc) &&
                   em.HasComponent<NpcProfile>(npc) &&
                   em.GetComponentData<NpcProfile>(npc).Role == NpcRole.Follower;
        }

        public static bool IsPlayerSideNpc(EntityManager em, Entity attacker)
        {
            if (attacker == Entity.Null || !em.Exists(attacker))
                return false;

            if (IsFollower(em, attacker))
                return true;

            return em.HasComponent<NpcFactionId>(attacker) &&
                   em.GetComponentData<NpcFactionId>(attacker).Value == WellKnownFactionIds.Player;
        }

        public static bool IsPlayerSideProjectileShooter(
            EntityManager em, Entity shooterRoot, int shooterFactionId, EntityId legacyRootId)
        {
            if (shooterRoot != Entity.Null)
                return IsPlayerSideNpc(em, shooterRoot);

            if (shooterFactionId == WellKnownFactionIds.Player)
                return true;

            Transform playerTf = PlayerAnchorRegistration.Transform;
            return playerTf != null &&
                   playerTf.root != null &&
                   legacyRootId != EntityId.None &&
                   playerTf.root.GetEntityId() == legacyRootId;
        }

        /// <summary>
        /// Records player-side kill credit. When <paramref name="killer"/> is a living follower,
        /// stores them so death loot can pay their wallet instead of a world drop.
        /// </summary>
        public static void TryMarkPlayerSideKill(EntityManager em, Entity victim, bool playerSide, Entity killer)
        {
            if (!playerSide || victim == Entity.Null || !em.Exists(victim) ||
                !em.HasComponent<NpcCharacterCombatState>(victim))
                return;

            var combat = em.GetComponentData<NpcCharacterCombatState>(victim);
            if (combat.IsDead == 0 || combat.KillCreditPlayerSide != 0)
                return;

            combat.KillCreditPlayerSide = 1;
            combat.KillCreditKiller = IsFollower(em, killer) ? killer : Entity.Null;
            em.SetComponentData(victim, combat);
        }
    }
}
