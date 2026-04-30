using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using Motion = ProjectDawn.Animation.Motion;

namespace ProjectDawn.Animation
{
    public enum RigTransformType
    {
        /// <summary>
        /// Position as float3 and Rotation as quaternion.
        /// </summary>
        [Tooltip("Position as float3 and Rotation as quaternion.")]
        RigidTransform,
        /// <summary>
        /// Position as float3, Rotation as quaternnion and Scale as uniform scale.
        /// </summary>
        [Tooltip("Position as float3, Rotation as quaternnion and Scale as uniform scale.")]
        LocalTransform,
        /// <summary>
        /// Position as float3, Rotation and Scale as float3x3.
        /// </summary>
        [Tooltip("Position as float3, Rotation and Scale as float3x3.")]
        AffineTransform,
    }

    /// <summary>
    /// Rig contains serialized <see cref="Animation.Armature"/> and <see cref="Animation.Motion"/>.
    /// </summary>
    [PreferBinarySerialization]
    [HelpURL("https://lukaschod.github.io/animatron-docs/manual/authoring/rig.html")]
    public unsafe class Rig : ScriptableObject
    {
        public const int DefaultVersion = 9;

#if ANIMATRON_AFFINE_TRANSFORM
        public const RigTransformType DefaultTransformType = RigTransformType.AffineTransform;
#elif ANIMATRON_LOCAL_TRANSFORM
        public const RigTransformType DefaultTransformType = RigTransformType.LocalTransform;
#else
        public const RigTransformType DefaultTransformType = RigTransformType.RigidTransform;
#endif

        [SerializeField, HideInInspector]
        byte[] m_ArmatureData;
        [SerializeField, HideInInspector]
        byte[] m_MotionData;
        [SerializeField]
        int m_Version = DefaultVersion;
        [SerializeField]
        RigTransformType m_TransformType = DefaultTransformType;

        BlobAssetReference<Armature> Armature;
        BlobAssetReference<Motion> Motion;

        /// <summary>
        /// Returns the version this rig was created with.
        /// </summary>
        public int Version => m_Version;

        /// <summary>
        /// Returns the transform type this rig was created with.
        /// </summary>
        public RigTransformType TransformType => m_TransformType;


        /// <summary>
        /// Returns true, if <see cref="Animation.Armature"/> and <see cref="Animation.Motion"/> is created.
        /// </summary>
        public bool IsCreated => Armature.IsCreated && Motion.IsCreated;

        /// <summary>
        /// Amount of bytes is used by <see cref="Animation.Armature"/> and <see cref="Animation.Motion"/>.
        /// </summary>
        public int TotalBytes => m_ArmatureData.Length + m_MotionData.Length;

        void OnDisable()
        {
            Clear();
        }

        void OnValidate()
        {
            Clear();
        }

        public void Clear()
        {
            if (Armature.IsCreated)
                Armature.Dispose();
            if (Motion.IsCreated)
                Motion.Dispose();
        }

        public BlobAssetReference<Armature> GetOrCreateArmature()
        {
            CheckTransformType();
            CheckArmatureData();
            if (!Armature.IsCreated)
                Armature = ReadArmature();
            return Armature;
        }

        public BlobAssetReference<Motion> GetOrCreateMotion()
        {
            CheckTransformType();
            CheckMotionData();
            if (!Motion.IsCreated)
                Motion = ReadMotion();
            return Motion;
        }

        public void WriteArmature(BlobBuilder armature)
        {
            CheckTransformType();
            using (var stream = new MemoryBinaryWriter())
            {
                BlobAssetReference<Armature>.Write(stream, armature, 1);

                m_ArmatureData = new byte[stream.Length];
                fixed (byte* ptr = m_ArmatureData)
                {
                    UnsafeUtility.MemCpy(ptr, stream.Data, stream.Length);
                }
            }
        }

        public BlobAssetReference<Armature> ReadArmature()
        {
            CheckTransformType();
            CheckArmatureData();

            fixed (byte* ptr = m_ArmatureData)
            {
                using (var stream = new MemoryBinaryReader(ptr, m_ArmatureData.Length))
                {
                    BlobAssetReference<Armature>.TryRead(stream, 1, out var result);

                    return result;
                }
            }
        }

        public void WriteMotion(BlobBuilder armature)
        {
            CheckTransformType();
            using (var stream = new MemoryBinaryWriter())
            {
                BlobAssetReference<Motion>.Write(stream, armature, 1);

                m_MotionData = new byte[stream.Length];
                fixed (byte* ptr = m_MotionData)
                {
                    UnsafeUtility.MemCpy(ptr, stream.Data, stream.Length);
                }
            }
        }

        public BlobAssetReference<Motion> ReadMotion()
        {
            CheckTransformType();
            CheckMotionData();
            fixed (byte* ptr = m_MotionData)
            {
                using (var stream = new MemoryBinaryReader(ptr, m_MotionData.Length))
                {
                    BlobAssetReference<Motion>.TryRead(stream, 1, out var result);

                    return result;
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CheckMotionData()
        {
            if (m_MotionData == null)
                throw new InvalidOperationException($"{this} was not baked");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CheckArmatureData()
        {
            if (m_ArmatureData == null)
                throw new InvalidOperationException($"{this} was not baked");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CheckTransformType()
        {
            if (m_TransformType != DefaultTransformType)
                throw new InvalidOperationException($"{this} was serialized with {m_TransformType}. However code uses {DefaultTransformType}, please reimport the asset.");
        }
    }
}