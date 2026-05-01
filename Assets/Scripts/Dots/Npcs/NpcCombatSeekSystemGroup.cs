using Medieval.NpcMovement;
using Unity.Entities;

namespace Medieval.Npcs
{
    /// <summary>
    /// Runs after follower anchors are synced, before separation/pathfind: picks combat targets and writes
    /// <see cref="NpcSeekOverride"/>, facing, and movement lock for the DOTS NPC pipeline.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcFollowersAnchorSystem))]
    [UpdateBefore(typeof(NpcSeparationSystem))]
    public partial class NpcCombatSeekSystemGroup : ComponentSystemGroup
    {
    }
}
