using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.Projectiles
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial struct ProjectileMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ProjectileSimSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            float gravity = SystemAPI.GetSingleton<ProjectileSimSettings>().Gravity;

            state.Dependency = new ProjectileMovementJob
            {
                DeltaTime = dt,
                Gravity = gravity
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct ProjectileMovementJob : IJobEntity
        {
            public float DeltaTime;
            public float Gravity;

            public void Execute(
                ref LocalTransform tf,
                ref ProjectileVelocity velocity,
                ref ProjectileMotionState motion,
                RefRO<ProjectileTag> _)
            {
                motion.PreviousPosition = tf.Position;

                float3 v = velocity.Value;
                v.y -= Gravity * DeltaTime;
                velocity.Value = v;

                tf.Position += v * DeltaTime;

                if (math.lengthsq(v) > 1e-4f)
                {
                    quaternion look = quaternion.LookRotationSafe(math.normalize(v), new float3(0f, 1f, 0f));
                    tf.Rotation = look;
                }
            }
        }
    }
}
