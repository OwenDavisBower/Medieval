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
        /// <summary>
        /// Followers: if &gt; <see cref="AggroRadius"/>, a second pass acquires the nearest visible hostile out to this
        /// distance when the aggro pass finds nothing (e.g. stragglers just outside aggro). 0 = disabled.
        /// </summary>
        public float LosChaseRadius;
        /// <summary>Max horizontal distance at which ranged attacks are allowed; should be greater than standoff hold distance.</summary>
        public float CombatRange;
        /// <summary>
        /// Ranged / Both: distance from target at which movement stops advancing (standoff). Must stay below
        /// <see cref="CombatRange"/> so units still shoot from their hold ring. 0 = use ~72% of <see cref="CombatRange"/>.
        /// </summary>
        public float RangedStandoffHoldDistance;
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
