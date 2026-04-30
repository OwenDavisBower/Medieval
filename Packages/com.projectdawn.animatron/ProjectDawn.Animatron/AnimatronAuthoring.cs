using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using ProjectDawn.Animation.Entities;
using UnityEngine;
using System;
using ProjectDawn.Rendering;
using UnityEngine.Rendering;
using ProjectDawn.Animation.Hybrid;
using Unity.Mathematics;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Animatron is the main component of the package responsible for animating an object.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Miscellaneous/Animatron")]
    [HelpURL("https://lukaschod.github.io/animatron-docs/manual/authoring/animatron.html")]
    public class AnimatronAuthoring : EntityBehaviour
    {
        [SerializeField]
        protected Rig m_Rig;

        [SerializeField]
        protected Skin[] m_Skins;

        [SerializeField]
        protected Player m_Player = Player.Default;

        [SerializeField]
        protected CullingMode m_CullingMode = CullingMode.AlwaysAnimate;

        public Rig Rig
        {
            get => m_Rig;
            set
            {
                if (Application.isPlaying)
                    throw new InvalidOperationException("Rig can only changed in editor");
                m_Rig = value;
            }
        }

        /// <summary>
        /// Total time passed since the last <see cref="Play"/>.
        /// </summary>
        public float Time
        {
            get
            {
                if (World == null)
                    return 0.0f;
                return EntityAnimatron.Time;
            }

            set
            {
                if (World == null)
                    return;
                EntityAnimatron.Time = value;
            }
        }

        /// <summary>
        /// Animation play speed.
        /// </summary>
        public float Speed
        {
            get => m_Player.Speed;

            set
            {
                m_Player.Speed = value;
                if (World == null)
                    return;
                EntityAnimatron.Speed = value;
            }
        }

        public Skin[] Skins
        {
            get => m_Skins;
            set
            {
                if (Application.isPlaying)
                    throw new InvalidOperationException("Rig can only changed in editor");
                m_Skins = value;
            }
        }

        /// <summary>
        /// Returns true, if any animation is currently played.
        /// </summary>
        public bool IsPlaying =>
            m_Rig.GetOrCreateMotion().Value.GetNormalizedTime(EntityAnimatron.AnimationIndex, EntityAnimatron.Time) != 1.0f;

        /// <summary>
        /// Returns <see cref="Animatron"/> component that is connected to this game object.
        /// </summary>
        public ref Animatron EntityAnimatron =>
            ref World.EntityManager.GetComponentDataRW<Animatron>(m_Entity).ValueRW;

        /// <summary>
        /// Returns <see cref="JointPose"/> buffer that is connected to this game object.
        /// The current pose of this game object.
        /// </summary>
        public DynamicBuffer<JointPose> EntityPose => 
            World.EntityManager.GetBuffer<JointPose>(m_Entity);

        /// <summary>
        /// Returns animation index with given name.
        /// </summary>
        /// <exception cref="System.Exception"></exception>
        public AnimationIndex FindAnimationIndex(string name) => m_Rig.GetOrCreateMotion().Value.FindAnimationIndex(name);

        /// <summary>
        /// Returns animation index with given name.
        /// </summary>
        /// <exception cref="System.Exception"></exception>
        public bool TryFindAnimationIndex(string name, out AnimationIndex index) => m_Rig.GetOrCreateMotion().Value.TryFindAnimationIndex(name, out index);

        /// <summary>
        /// Play animation at given index.
        /// </summary>
        public void Play(AnimationIndex animationIndex)
        {
            EntityAnimatron.Play(animationIndex);
        }

        /// <summary>
        /// Returns joint index with given name.
        /// </summary>
        /// <exception cref="System.Exception"></exception>
        public int FindJointIndex(string name) => m_Rig.GetOrCreateArmature().Value.FindJointIndex(name);

        /// <summary>
        /// Returns joint index with given name.
        /// </summary>
        /// <exception cref="System.Exception"></exception>
        public bool TryFindJointIndex(string name, out int index) => m_Rig.GetOrCreateArmature().Value.TryFindJointIndex(name, out index);

        /// <summary>
        /// Returns the joint's rigid transform in world space.
        /// This is quite useful for attachments.
        /// </summary>
        public RigidTransform GetJointWorldTransform(int jointIndex)
        {
            var jointPoseLS = EntityPose[jointIndex];
#if ANIMATRON_AFFINE_TRANSFORM
            var jointPoseWS = math.mul(transform.localToWorldMatrix, jointPoseLS.Value);
            return RigidTransform.FromPositionRotation(jointPoseWS.Translation(), jointPoseWS.Rotation());
#elif ANIMATRON_LOCAL_TRANSFORM
            var jointPoseWS = LocalTransform.FromMatrix(transform.localToWorldMatrix).TransformTransform(jointPoseLS.Value);
            return RigidTransform.FromPositionRotation(jointPoseWS.Position, jointPoseWS.Rotation);
#else
            return RigidTransform.FromMatrix(transform.localToWorldMatrix).TransformTransform(jointPoseLS.Value);
#endif
        }

        /// <summary>
        /// Attempts to return event data, if it is available at current pose.
        /// </summary>
        public bool TryGetEvent<T>(out T e) where T : unmanaged, IEventData
        {
            ref var animatron = ref EntityAnimatron;
            ref var motion = ref m_Rig.GetOrCreateMotion().Value;
            return motion.TryGetEvent<T>(animatron.AnimationIndex, animatron.PlayedPoses, out e);
        }

        /// <summary>
        /// Returns true, if event is available at current pose.
        /// </summary>
        public bool HasEvent<T>() where T : unmanaged, IEventData
        {
            ref var animatron = ref EntityAnimatron;
            ref var motion = ref m_Rig.GetOrCreateMotion().Value;
            return motion.HasEvent<T>(animatron.AnimationIndex, animatron.PlayedPoses);
        }

        unsafe void Awake()
        {
            var entity = GetOrCreateEntity();

            if (m_Rig == null)
            {
                Debug.LogWarning($"Animatron {this} is missing rig!");
                return;
            }

            var entityManager = World.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            if (m_CullingMode == CullingMode.CullCompletely)
            {
                ecb.AddComponent<Culled>(entity);
                ecb.SetComponentEnabled<Culled>(entity, false);
            }

            var armature = m_Rig.GetOrCreateArmature();
            ecb.AddSharedComponent(entity, new ArmatureRef { Value = armature });

            var motion = m_Rig.GetOrCreateMotion();
            ecb.AddSharedComponent(entity, new MotionRef { Value = motion });

            int jointCount = armature.Value.JointCount;
            var currentPose = ecb.AddBuffer<JointPose>(entity);
            currentPose.ResizeUninitialized(jointCount);
            for (int i = 0; i < jointCount; i++)
                currentPose[i] = JointPose.Default;

            if (m_Player.PreviousPose)
            {
                var previousPose = ecb.AddBuffer<JointPreviousPose>(entity);
                previousPose.ResizeUninitialized(jointCount);
                for (int i = 0; i < jointCount; i++)
                    previousPose[i] = JointPreviousPose.Default;
            }

            if (m_Player.Enabled)
            {
                ecb.AddComponent(entity, new Animatron
                {
                    Speed = m_Player.Speed,
                    SamplingMode = m_Player.SamplingMode,
                });
            }

            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(transform.localPosition, transform.localRotation, transform.localScale.x));
            ecb.AddComponent(entity, new LocalToWorld { Value = transform.localToWorldMatrix });
            ecb.AddComponent(entity, transform);

            // Here we add to each renderer components needed for deformation
            var linkedEntityGroup = ecb.AddBuffer<LinkedEntityGroup>(entity);
            for (int i = 0; i < m_Skins.Length; i++)
            {
                var child = m_Skins[i].GameObject;
                var skinIndex = m_Skins[i].BindingIndex;
                if (child == null)
                {
                    Debug.LogWarning($"Animatron {this} at skin index {i} the game object is missing!");
                    continue;
                }

                if (skinIndex >= armature.Value.Skins.Length)
                {
                    Debug.LogError($"Animatron {this} at skin index {i} has invalid binding index {skinIndex}!");
                    continue;
                }
                var skin = armature.Value.Skins[skinIndex];

                Entity childEntity;
                if (child.TryGetComponent(out RenderMeshArrayAuthoring renderMeshArray))
                {
                    childEntity = renderMeshArray.GetOrCreateEntity();

                    entityManager.AddComponentData(childEntity, LocalTransform.FromPositionRotation(child.transform.localPosition, child.transform.localRotation));

                    if (IsDynamicBlendWeightCount(renderMeshArray, out int boneCount))
                        entityManager.AddComponentData(childEntity, new SkinBlendWeightCount { Value = boneCount });
                }
                else if (child.TryGetComponent(out SkinnedMeshRenderer skinnedMeshRenderer))
                {
                    Debug.LogError("Currently SkinnedMeshRenderer is not supported in hybrid path only in subscene!");
                    continue;
                }
                else
                {
                    continue;
                }

                if (m_CullingMode == CullingMode.CullCompletely)
                {
                    entityManager.AddComponent<Culled>(childEntity);
                    entityManager.SetComponentEnabled<Culled>(childEntity, false);
                }

                var childSkinMatrix = entityManager.AddBuffer<SkinMatrix>(childEntity);
                childSkinMatrix.ResizeUninitialized(skin.Length);
                for (int skinMatrixIndex = 0; skinMatrixIndex < skin.Length; skinMatrixIndex++)
                    childSkinMatrix[skinMatrixIndex] = SkinMatrix.Default;

                entityManager.AddComponentData(childEntity, SkinMatrixBufferIndex.Invalid);
                entityManager.AddSharedComponent(childEntity, new SkinRef { Armature = armature, SkinIndex = skinIndex });

                entityManager.AddComponentData(childEntity, new Parent { Value = entity });

                linkedEntityGroup.Add(childEntity);
            }

#if MODULE_AGENTS_NAVIGATION
            if (transform.parent?.TryGetComponent(out Navigation.Hybrid.AgentAuthoring agent) ?? false)
            {
                entityManager.AddComponentData(m_Entity, new Parent { Value = agent.GetOrCreateEntity() });
                entityManager.AddComponentData(agent.GetOrCreateEntity(), new LocalToWorld { });
            }
#endif

            ecb.Playback(entityManager);
        }

        [System.Serializable]
        public struct Skin
        {
            /// <summary>
            /// Index at which the renderer was assigned in <see cref="Animation.Rig"/>.
            /// </summary>
            [Tooltip("Index at which the renderer was assigned in Rig.")]
            public int BindingIndex;

            /// <summary>
            /// GameObject that contains the rendering component.
            /// </summary>
            [Tooltip("GameObject that contains the rendering component.")]
            public GameObject GameObject;

        }

        [System.Serializable]
        public struct Player
        {
            /// <summary>
            /// Controls whether the default player should be added.
            /// </summary>
            [Tooltip("Controls whether the default player should be added.")]
            public bool Enabled;

            /// <summary>
            /// Animation playback speed factor. Acts as a multiplier for time progression.
            /// </summary>
            [Tooltip("Animation playback speed factor. Acts as a multiplier for time progression.")]
            public float Speed;

            /// <summary>
            /// Controls the pose sample interpolation mode.
            /// </summary>
            [Tooltip("Controls the pose sample interpolation mode.")]
            public SamplingMode SamplingMode;

            public bool PreviousPose;

            public static Player Default => new()
            {
                Enabled = true,
                Speed = 1.0f,
                SamplingMode = SamplingMode.Interpolated,
            };
        }

        class Baker : Baker<AnimatronAuthoring>
        {
            public override void Bake(AnimatronAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                if (authoring.m_Rig == null)
                {
                    Debug.LogWarning($"Animatron {this} is missing rig!");
                    return;
                }

                DependsOn(authoring.m_Rig);

                if (authoring.m_CullingMode == CullingMode.CullCompletely)
                {
                    AddComponent<Culled>(entity);
                    SetComponentEnabled<Culled>(entity, false);
                }

                var armatureHash = new Unity.Entities.Hash128((uint)authoring.m_Rig.GetHashCode(), 0, 0, 0);
                if (!TryGetBlobAssetReference(armatureHash,
                    out BlobAssetReference<Armature> armature))
                {
                    armature = authoring.m_Rig.ReadArmature();
                    AddBlobAssetWithCustomHash(ref armature, armatureHash);
                }
                AddSharedComponent(entity, new ArmatureRef { Value = armature });

                var motionHash = new Unity.Entities.Hash128((uint)authoring.m_Rig.GetHashCode(), 1, 0, 0);
                if (!TryGetBlobAssetReference(motionHash,
                    out BlobAssetReference<Motion> motion))
                {
                    motion = authoring.m_Rig.ReadMotion();
                    AddBlobAssetWithCustomHash(ref motion, motionHash);
                }
                AddSharedComponent(entity, new MotionRef { Value = motion });

                int jointCount = armature.Value.JointCount;
                var currentPose = AddBuffer<JointPose>(entity);
                currentPose.ResizeUninitialized(jointCount);
                for (int i = 0; i < jointCount; i++)
                    currentPose[i] = JointPose.Default;

                if (authoring.m_Player.PreviousPose)
                {
                    var previousPose = AddBuffer<JointPreviousPose>(entity);
                    previousPose.ResizeUninitialized(jointCount);
                    for (int i = 0; i < jointCount; i++)
                        previousPose[i] = JointPreviousPose.Default;
                }

                if (authoring.m_Player.Enabled)
                {
                    AddComponent(entity, new Animation.Animatron
                    {
                        Speed = authoring.m_Player.Speed,
                        SamplingMode = authoring.m_Player.SamplingMode,
                    });
                }

                // In subscene baking we cant modify other game objects, to workaround it we add here ApplySkin component
                // As result in later steps ApplySkinSystem will add required skinning components to renderers similar like in hybrid pass
                var applySkin = AddBuffer<ApplySkin>(entity);
                for (int i = 0; i < authoring.m_Skins.Length; i++)
                {
                    var child = authoring.m_Skins[i].GameObject;
                    var skinIndex = authoring.m_Skins[i].BindingIndex;
                    var skin = armature.Value.Skins[skinIndex];

                    if (child == null)
                    {
                        Debug.LogWarning($"Animatron {authoring} at skin index {i} the game object is missing!");
                        continue;
                    }

                    if (child.TryGetComponent(out RenderMeshArrayAuthoring renderMeshArray))
                    {
                        IsDynamicBlendWeightCount(renderMeshArray, out int boneCount);

                        applySkin.Add(new ApplySkin
                        {
                            Value = GetEntity(renderMeshArray, TransformUsageFlags.Renderable),
                            SkinIndex = i,
                            CullingMode = authoring.m_CullingMode,
                            BoneCount = boneCount,
                        });
                    }
                    else if (child.TryGetComponent(out SkinnedMeshRenderer skinnedMeshRenderer))
                    {
                        IsDynamicBlendWeightCount(skinnedMeshRenderer, out int boneCount);

                        applySkin.Add(new ApplySkin
                        {
                            Value = GetEntity(renderMeshArray, TransformUsageFlags.Renderable),
                            SkinIndex = i,
                            CullingMode = authoring.m_CullingMode,
                            BoneCount = boneCount,
                        });
                    }
                }
            }
        }

        static bool IsDynamicBlendWeightCount(RenderMeshArrayAuthoring renderMeshArray, out int boneCount)
        {
            bool isFlex = false;
            boneCount = -1;
            foreach (var instance in renderMeshArray.Instances)
            {
                if (instance.Material == null || instance.Mesh == null)
                    continue;
                isFlex |= instance.Material.HasProperty("_SkinBlendWeightCount");
                boneCount = instance.Mesh.GetVertexAttributeDimension(VertexAttribute.BlendIndices);
                if (!isFlex && boneCount != 4)
                    Debug.LogError($"Mesh {instance.Mesh} does not have 4 blend weights ({boneCount}), but shader does not have automatic deformation set!"); 
            }
            return isFlex;
        }

        static bool IsDynamicBlendWeightCount(SkinnedMeshRenderer skinnedMeshRenderer, out int boneCount)
        {
            bool isFlex = false;
            boneCount = -1;
            var materials = UnityEngine.Pool.ListPool<Material>.Get();
            skinnedMeshRenderer.GetSharedMaterials(materials);
            var mesh = skinnedMeshRenderer.sharedMesh;
            foreach (var material in materials)
            {
                if (material == null || mesh == null)
                    continue;
                isFlex |= material.HasProperty("_SkinBlendWeightCount");
                boneCount = mesh.GetVertexAttributeDimension(VertexAttribute.BlendIndices);
                if (!isFlex && boneCount != 4)
                    Debug.LogError($"Mesh {mesh} does not have 4 blend weights ({boneCount}), but shader does not have automatic deformation set!");
            }
            UnityEngine.Pool.ListPool<Material>.Release(materials);
            return isFlex;
        }
    }
}

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Controls pose sample interpolation.
    /// </summary>
    public enum SamplingMode
    {
        /// <summary>
        /// No interpolation at all.
        /// </summary>
        Nearest,
        /// <summary>
        /// Linear interpolation good for mechanical armatures.
        /// </summary>
        Interpolated,
        /// <summary>
        /// Uniform-S interpolation good for organic armatures.
        /// </summary>
        [Obsolete("Uniform-S is currently not usable as the key frames are baked.")]
        UniformS,
    }

    /// <summary>
    /// Controls animation playing.
    /// </summary>
    public struct Animatron : IComponentData
    {
        /// <summary>
        /// Index of the animation to be played.
        /// </summary>
        public AnimationIndex AnimationIndex;

        /// <summary>
        /// Total time passed since the last <see cref="Play"/> call.
        /// </summary>
        public float Time;

        /// <summary>
        /// Animation playback speed factor. Acts as a multiplier for time progression.
        /// </summary>
        public float Speed;

        /// <summary>
        /// Controls pose sample interpolation mode.
        /// </summary>
        public SamplingMode SamplingMode;

        /// <summary>
        /// The range of poses that were played during this frame’s execution.
        /// Since each animation can have a different frame rate, and the game itself may run at any frame rate,
        /// it is common for several poses to be skipped in a single frame.
        /// This value stores the pose index from the previous execution and the current execution,
        /// making it useful for systems like events that need to know which poses were executed to determine which events to trigger.
        /// </summary>
        public int2 PlayedPoses;


        public static Animatron Default => new()
        {
            Speed = 1.0f,
            SamplingMode = SamplingMode.Interpolated,
        };

        /// <summary>
        /// Play animation at given index.
        /// </summary>
        public void Play(AnimationIndex animationIndex)
        {
            AnimationIndex = animationIndex;
            Time = 0;
        }
    }

    struct ApplySkin : IBufferElementData
    {
        public Entity Value;
        public int SkinIndex;
        public CullingMode CullingMode;
        public int BoneCount;
    }

    /// <summary>
    /// Applies skin components to render mesh array
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    partial struct ApplySkinSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (armature, applySkins, entity) in
                SystemAPI.Query<ArmatureRef, DynamicBuffer<ApplySkin>>()
                .WithEntityAccess()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
            {
                foreach (var applySkin in applySkins)
                {
                    var skinIndex = applySkin.SkinIndex;
                    var skin = armature.Value.Value.Skins[skinIndex];
                    var childEntity = applySkin.Value;

                    var childSkinMatrix = ecb.AddBuffer<SkinMatrix>(childEntity);
                    childSkinMatrix.ResizeUninitialized(skin.Length);
                    for (int skinMatrixIndex = 0; skinMatrixIndex < skin.Length; skinMatrixIndex++)
                        childSkinMatrix[skinMatrixIndex] = SkinMatrix.Default;

                    ecb.AddComponent(childEntity, SkinMatrixBufferIndex.Invalid);
                    ecb.AddSharedComponent(childEntity, new SkinRef { Armature = armature.Value, SkinIndex = skinIndex });

                    if (applySkin.BoneCount != -1)
                    {
                        ecb.AddComponent(childEntity, new SkinBlendWeightCount { Value = applySkin.BoneCount });
                    }
                    if (applySkin.CullingMode == CullingMode.CullCompletely)
                    {
                        ecb.AddComponent<Culled>(childEntity);
                        ecb.SetComponentEnabled<Culled>(childEntity, false);
                    }
                }
                ecb.RemoveComponent<ApplySkin>(entity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
