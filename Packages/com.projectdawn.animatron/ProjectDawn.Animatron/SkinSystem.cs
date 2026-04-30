using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Updates skin matrices from joint poses.
    /// </summary>
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateAfter(typeof(PoseSystem))]
    [UpdateBefore(typeof(CullingSystem))]
    [UpdateInGroup(typeof(AnimatronSystemGroup))]
    public partial struct SkinSystem : ISystem
    {
        BufferLookup<JointPose> JointCurrentPoseLookup;

        [WithNone(typeof(Culled))]
        [BurstCompile]
        partial struct SkinJob : IJobEntity
        {
            void Execute(ref DynamicBuffer<SkinMatrix> skinMatrices, in DynamicBuffer<JointPose> pose, in ArmatureRef armatureReference)
            {
                ref var armature = ref armatureReference.Value.Value;
                var skin = armature.Skins[0];
                for (int bindIndex = skin.Begin; bindIndex < skin.End; bindIndex++)
                {
                    int skinMatrixIndex = armature.SkinJointIndices[bindIndex];
                    if (skinMatrixIndex == -1)
                    {
                        skinMatrices.ElementAt(bindIndex - skin.Begin).Value = ToFloat3x4(float4x4.identity);
                        continue;
                    }

#if ANIMATRON_AFFINE_TRANSFORM
                    skinMatrices.ElementAt(bindIndex - skin.Begin).Value = ToFloat3x4(math.mul(math.float4x4(pose[skinMatrixIndex].Value), armature.SkinBindPoses[bindIndex]));
#else
                    skinMatrices.ElementAt(bindIndex - skin.Begin).Value = ToFloat3x4(math.mul(pose[skinMatrixIndex].Value.ToMatrix(), armature.SkinBindPoses[bindIndex]));
#endif
                }
            }
        }

        [WithNone(typeof(Culled))]
        [BurstCompile]
        partial struct SkinParentJob : IJobEntity
        {
            [ReadOnly]
            public BufferLookup<JointPose> JointCurrentPoseLookup;
            void Execute(ref DynamicBuffer<SkinMatrix> skinMatrices, ref LocalTransform transform, in Parent parent, in SkinRef skinReference)
            {
                if (!JointCurrentPoseLookup.TryGetBuffer(parent.Value, out var pose))
                    return;
                ref var armature = ref skinReference.Armature.Value;
                var skin = armature.Skins[skinReference.SkinIndex];

                // Here we apply root join transform for skin object and later move skin matrices from object to root space
                // This is needed for render bounds function correctly as they have to use the transform of root
                var localToRoot = pose[skin.Root];
#if ANIMATRON_AFFINE_TRANSFORM
                var rootToLocal = math.inverse(localToRoot.Value);
                transform.Position = localToRoot.Value.t;
                transform.Rotation = math.normalize(math.quaternion(localToRoot.Value.rs));
                transform.Scale = math.length(localToRoot.Value.rs.c0);
#elif ANIMATRON_LOCAL_TRANSFORM
                var rootToLocal = localToRoot.Value.ToInverseMatrix();
                transform.Position = localToRoot.Value.Position;
                transform.Rotation = localToRoot.Value.Rotation;
                transform.Scale = localToRoot.Value.Scale;
#else
                var rootToLocal = localToRoot.Value.ToInverseMatrix();
                transform.Position = localToRoot.Value.Position;
                transform.Rotation = localToRoot.Value.Rotation;
#endif

                for (int bindIndex = skin.Begin; bindIndex < skin.End; bindIndex++)
                {
                    int skinMatrixIndex = armature.SkinJointIndices[bindIndex];
                    if (skinMatrixIndex == -1)
                    {
                        skinMatrices.ElementAt(bindIndex - skin.Begin).Value = ToFloat3x4(rootToLocal);
                        continue;
                    }

#if ANIMATRON_AFFINE_TRANSFORM
                    var poseTransform = math.mul(rootToLocal, pose[skinMatrixIndex].Value);
                    skinMatrices.ElementAt(bindIndex - skin.Begin).Value = ToFloat3x4(math.mul(math.float4x4(poseTransform), armature.SkinBindPoses[bindIndex]));
#else
                    var poseTransform = math.mul(rootToLocal, pose[skinMatrixIndex].Value.ToMatrix());
                    skinMatrices.ElementAt(bindIndex - skin.Begin).Value = ToFloat3x4(math.mul(poseTransform, armature.SkinBindPoses[bindIndex]));
#endif
                }
            }
        }

        [BurstCompile]
        void ISystem.OnCreate(ref SystemState state)
        {
            JointCurrentPoseLookup = SystemAPI.GetBufferLookup<JointPose>(true);
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            JointCurrentPoseLookup.Update(ref state);

            state.Dependency = new SkinJob().ScheduleParallel(state.Dependency);
            state.Dependency = new SkinParentJob { JointCurrentPoseLookup = JointCurrentPoseLookup }.ScheduleParallel(state.Dependency);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3x4 ToFloat3x4(float4x4 value) => new(value.c0.xyz, value.c1.xyz, value.c2.xyz, value.c3.xyz);
    }
}
