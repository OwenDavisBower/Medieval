using ProjectDawn.Animation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// After transforms and joint poses are updated, places attachment entities at the baked skin joint pose in world space,
    /// expressed as <see cref="LocalTransform"/> under their <see cref="Parent"/> (same convention as Animatron GetJointWorldTransform).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class AnimatedJointAttachmentSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRO<JointPose>();

            foreach (var (ltRW, parent, att) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRO<Parent>, RefRO<AnimatedJointAttachment>>())
            {
                Entity src = att.ValueRO.SourceArmatureEntity;
                if (src == Entity.Null || !EntityManager.Exists(src) || !EntityManager.HasBuffer<JointPose>(src))
                    continue;

                if (!EntityManager.HasComponent<LocalToWorld>(src) ||
                    !EntityManager.HasComponent<LocalToWorld>(parent.ValueRO.Value))
                    continue;

                var pose = EntityManager.GetBuffer<JointPose>(src);
                int j = att.ValueRO.JointIndex;
                if (j < 0 || j >= pose.Length)
                    continue;

                float4x4 armatureLtw = EntityManager.GetComponentData<LocalToWorld>(src).Value;
                float4x4 parentLtw = EntityManager.GetComponentData<LocalToWorld>(parent.ValueRO.Value).Value;

                float4x4 handWorld;
#if ANIMATRON_LOCAL_TRANSFORM
                var jointWs = LocalTransform.FromMatrix(armatureLtw).TransformTransform(pose[j].Value);
                handWorld = float4x4.TRS(jointWs.Position, jointWs.Rotation, jointWs.Scale);
#else
                var jointWs = RigidTransform.FromMatrix(armatureLtw).TransformTransform(pose[j].Value);
                handWorld = float4x4.TRS(jointWs.Position, jointWs.Rotation, 1f);
#endif

                float4x4 localMat = math.mul(math.inverse(parentLtw), handWorld);
                ltRW.ValueRW = LocalTransform.FromMatrix(localMat);
            }
        }
    }
}
