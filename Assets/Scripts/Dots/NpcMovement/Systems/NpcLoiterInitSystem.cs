using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// One-shot initialization for orbit / wander state. Consumes <see cref="NpcLoiterInitTag"/> and
    /// randomizes base angle/radius/noise (and wander repick timer) using the per-entity RNG.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct NpcLoiterInitSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcLoiterInitTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new InitJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct InitJob : IJobEntity
        {
            public void Execute(ref NpcMovementState mstate, in NpcMovementConfig cfg, in NpcLoiterInitTag _)
            {
                if (mstate.Mode != NpcMovementMode.Orbit && mstate.Mode != NpcMovementMode.WanderAroundTarget)
                    return;

                var rng = mstate.Rng;
                mstate.BaseAngle = rng.NextFloat(0f, math.PI * 2f);
                mstate.NoiseA = rng.NextFloat(0f, 100f);
                mstate.NoiseB = rng.NextFloat(0f, 100f);

                if (mstate.Mode == NpcMovementMode.Orbit)
                {
                    float minR = math.max(0f, cfg.MinLoiterRadius);
                    float maxR = math.max(minR, cfg.MaxLoiterRadius);
                    mstate.BaseRadius = rng.NextFloat(minR, maxR);
                }
                else
                {
                    mstate.BaseRadius = rng.NextFloat(0f, math.max(0f, cfg.WanderRadius));
                    // Randomize initial repick to avoid "everybody repick on the same frame".
                    mstate.NextWanderPickTime = rng.NextFloat(0f, 1f);
                }

                mstate.Rng = rng;
            }
        }
    }
}

