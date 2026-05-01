using Unity.Burst;
using Unity.Entities;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Copies the singleton <see cref="NpcPlayerAnchor"/> into each follower's <see cref="NpcAnchorTarget"/>.
    /// Keeps followers orbiting/chasing the player without any GameObject companion.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct NpcFollowersAnchorSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcPlayerAnchor>();
            state.RequireForUpdate<NpcMovementTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            NpcPlayerAnchor player = SystemAPI.GetSingleton<NpcPlayerAnchor>();
            if (player.HasPlayer == 0)
                return;

            state.Dependency = new CopyJob
            {
                Player = player
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(NpcMovementTag))]
        partial struct CopyJob : IJobEntity
        {
            public NpcPlayerAnchor Player;

            public void Execute(ref NpcAnchorTarget anchor, in NpcMovementState mstate)
            {
                if (mstate.Group != NpcSeparationGroup.Followers)
                    return;

                anchor.Position = Player.Position;
                anchor.LinearVelocity = Player.LinearVelocity;
                anchor.HasAnchor = 1;
            }
        }
    }
}

