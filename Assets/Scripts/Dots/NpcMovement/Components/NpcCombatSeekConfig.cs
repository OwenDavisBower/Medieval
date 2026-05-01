using Unity.Entities;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Tunables for <see cref="Medieval.Npcs.NpcCombatSeekSystem"/> (aggro, hold range, LOS probe).
    /// Baked from <see cref="NpcMovementAuthoring"/>.
    /// </summary>
    public struct NpcCombatSeekConfig : IComponentData
    {
        public float AggroRadius;
        public float CombatRange;
        public float EyeHeight;
        public float TargetAimHeight;
        public int ObstacleLayerMask;
        /// <summary>Followers: no combat seek beyond this horizontal distance from the player. 0 = disabled.</summary>
        public float MaxDistanceFromLeader;
        /// <summary>Followers: horizontal distance from the player past which this entity snaps closer. 0 = off.</summary>
        public float FollowerTeleportBackDistance;
        /// <summary>Followers: after snap, horizontal radius from the player on the same radial line (must be ≥ 0 when teleport is on).</summary>
        public float FollowerTeleportBackTargetDistance;
    }
}
