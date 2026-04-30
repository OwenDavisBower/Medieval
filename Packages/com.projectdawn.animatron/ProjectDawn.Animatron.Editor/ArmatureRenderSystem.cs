using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ProjectDawn.Animation.Editor
{
    /// <summary>
    /// Useful for debugging the pose.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct ArmatureRenderSystem : ISystem
    {
        void ISystem.OnUpdate(ref SystemState state)
        {
            foreach (var (currentPose, armatureHandle, transform) in 
                SystemAPI.Query<DynamicBuffer<JointPose>, ArmatureRef, LocalToWorld>())
            {
                ref var armature = ref armatureHandle.Value.Value;
                for (int jointIndex = 0; jointIndex < armature.JointCount; jointIndex++)
                {
                    int parentIndex = armature.JointParentIndices[jointIndex];
                    if (parentIndex == -1)
                        continue;
                    var parentJointPose = currentPose[parentIndex];
                    var jointPose = currentPose[jointIndex];

#if ANIMATRON_AFFINE_TRANSFORM
                    float3 from = transform.Value.TransformPoint(parentJointPose.Value.t);
                    float3 to = transform.Value.TransformPoint(jointPose.Value.t);
#else
                    float3 from = transform.Value.TransformPoint(parentJointPose.Value.Position);
                    float3 to = transform.Value.TransformPoint(jointPose.Value.Position);
#endif
                    Debug.DrawLine(from, to, Color.red);
                }
            }
        }
    }
}