namespace Medieval.Npcs
{
    /// <summary>Role-based hostility for DOTS NPC combat (seeking, friendly-fire, etc.).</summary>
    public static class NpcCombatRoleHostility
    {
        public static bool IsHostilePair(NpcRole self, NpcRole other)
        {
            if (self == NpcRole.Follower && other == NpcRole.Bandit)
                return true;
            if (self == NpcRole.Bandit && (other == NpcRole.Follower || other == NpcRole.Villager))
                return true;
            return false;
        }

        /// <summary>
        /// Symmetric ally check for close-range ranged friendly-fire: neither role is hostile to the other.
        /// </summary>
        public static bool AreAlliedForCloseRangedFriendlyFire(NpcRole a, NpcRole b)
        {
            return !IsHostilePair(a, b) && !IsHostilePair(b, a);
        }
    }
}
