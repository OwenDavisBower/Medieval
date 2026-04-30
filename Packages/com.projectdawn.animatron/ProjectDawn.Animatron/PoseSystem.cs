using Unity.Burst;
using Unity.Entities;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Transforms joint pose from parent space to root space.
    /// </summary>
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateBefore(typeof(CullingSystem))]
    [UpdateInGroup(typeof(AnimatronSystemGroup))]
    public partial struct PoseSystem : ISystem
    {
        [WithNone(typeof(Culled))]
        [BurstCompile]
        partial struct JointParentToRootJob : IJobEntity
        {
            void Execute(ref DynamicBuffer<JointPose> pose, in ArmatureRef armatureReference)
            {
                ref var armature = ref armatureReference.Value.Value;
                for (int jointIndex = 0; jointIndex < armature.JointCount; jointIndex++)
                {
                    int parentIndex = armature.JointParentIndices[jointIndex];
                    if (parentIndex == -1)
                        continue;
#if ANIMATRON_AFFINE_TRANSFORM
                    pose.ElementAt(jointIndex).Value = Unity.Mathematics.math.mul(pose[parentIndex].Value, pose[jointIndex].Value);
#else
                    pose.ElementAt(jointIndex).Value = pose[parentIndex].Value.TransformTransform(pose[jointIndex].Value);
#endif
                }
            }
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            new JointParentToRootJob().ScheduleParallel();
        }
    }
}
