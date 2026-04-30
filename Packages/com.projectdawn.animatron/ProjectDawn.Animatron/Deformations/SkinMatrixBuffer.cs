using System;
using System.Collections.Generic;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// This is a singleton component that contains Animatron GPU skin matrices.
    /// The code is based on <see cref="Unity.Rendering.SkinningBufferManager"/>.
    /// </summary>
    public class SkinMatrixBuffer : IComponentData, IDisposable
    {
        const int k_ChunkSize = 2048;

        static readonly int k_SkinMatricesBuffer = Shader.PropertyToID("_AnimatronSkinMatrices");
        static readonly int k_MaxSize = (int)math.min(SystemInfo.maxGraphicsBufferSize / UnsafeUtility.SizeOf<float3x4>(), int.MaxValue);

        public static int MaxSize => k_MaxSize;

        FencedGraphicsBuffer m_BufferPool;
        bool m_Mapped;

        public NativeArray<int> m_Length;

        public JobHandle Handle;

        public SkinMatrixBuffer()
        {
            m_BufferPool = new FencedGraphicsBuffer(UnsafeUtility.SizeOf<float3x4>());
            m_Length = new NativeArray<int>(1, Allocator.Persistent);
        }

        public void Dispose()
        {
            m_BufferPool.Dispose();
            m_Length.Dispose();
        }

        public unsafe int* GetLenghtPtr()
        {
            return (int*)m_Length.GetUnsafePtr();
        }

        public bool ResizePassBufferIfRequired(int requiredSize)
        {
            if (m_BufferPool.Size < requiredSize)
            {
                var newSize = ((requiredSize / k_ChunkSize) + 1) * k_ChunkSize;

                if (newSize > k_MaxSize)
                {
                    // Only inform users if the content requires a buffer that is too big.
                    if (requiredSize > k_MaxSize)
                    {
                        Debug.LogWarning("The world contains too many skin matrices to fit into a single GraphicsBuffer. Not all skinned meshes are guaranteed to render correctly. Reduce the number of active deformed meshes.");
                        return false;
                    }
                    // Do not actually resize the buffer if we are already at max capacity.
                    if (m_BufferPool.Size == k_MaxSize)
                        return false;

                    newSize = k_MaxSize;
                }

                m_BufferPool.Resize(newSize);


                return true;
            }

            return false;
        }

        public NativeArray<float3x4> Map(int offset, int count)
        {
            Debug.Assert(!m_Mapped, "Buffer is already mapped!");
            m_Mapped = true;
            m_BufferPool.BeginFrame();
            var buffer = m_BufferPool.GetCurrentFrameBuffer();
            return buffer.LockBufferForWrite<float3x4>(offset, count);
        }

        public void Unmap(int count)
        {
            if (!m_Mapped)
                return;
            m_Mapped = false;
            var buffer = m_BufferPool.GetCurrentFrameBuffer();
            buffer.UnlockBufferAfterWrite<float3x4>(count);
            Shader.SetGlobalBuffer(k_SkinMatricesBuffer, buffer);
            m_BufferPool.EndFrame();
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    unsafe partial struct InitializeSkinMatrixBufferSystem : ISystem
    {
        public const int MaxDummySkinMatrixCount = 256;
        void ISystem.OnCreate(ref SystemState state)
        {
            int size = math.max(MaxDummySkinMatrixCount, AnimatronSettings.ReserveTotalJoints);

            var skinMatrixBuffer = new SkinMatrixBuffer();
            skinMatrixBuffer.ResizePassBufferIfRequired(size);

            // In editor skin systems wont be run, so we can clear first buffer as it will be always used
            if (state.World.Flags == WorldFlags.Editor)
            {
                // For editor we dont animate so we can setup buffer for default
                var data = skinMatrixBuffer.Map(0, size);
                for (int i = 0; i < size; i++)
                    data[i] = SkinMatrix.Default.Value;
                skinMatrixBuffer.Unmap(size);
            }

            state.EntityManager.AddComponentData(state.SystemHandle, skinMatrixBuffer);
        }
    }

    internal class FencedGraphicsBuffer : IDisposable
    {
        List<GraphicsBuffer> m_Buffers;
        int m_CurrentFrameIndex;
        int m_Stride;
        int m_Size;

        public int Size => m_Size;

        public FencedGraphicsBuffer(int stride)
        {
            m_Buffers = new();
            m_CurrentFrameIndex = 0;
            for (int i = 0; i < MaxQueuedFrames; i++)
                m_Buffers.Add(null);
            m_Size = 1;
            m_Stride = stride;
        }

        public void BeginFrame()
        {
            m_CurrentFrameIndex = (m_CurrentFrameIndex + 1) % m_Buffers.Count;
        }

        public void EndFrame()
        {
            
        }

        public GraphicsBuffer GetCurrentFrameBuffer()
        {
            if (m_Buffers[m_CurrentFrameIndex] == null)
                m_Buffers[m_CurrentFrameIndex] = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, m_Size, m_Stride);
            return m_Buffers[m_CurrentFrameIndex];
        }

        public void Resize(int newSize)
        {
            Dispose();
            m_Size = newSize;
            AnimatronStats.TotalJoints = newSize;
            AnimatronStats.SkinMatrixBufferBytes = newSize * m_Buffers.Count * m_Stride;
        }

        public void Dispose()
        {
            for (int i = 0; i < m_Buffers.Count; i++)
            {
                m_Buffers[i]?.Dispose();
                m_Buffers[i] = null;
            }
        }

        internal static int MaxQueuedFrames
        {
            get
            {
                // The number of frames in flight at the same time
                // depends on the Graphics device that we are using.
                // This number tells how long we need to keep the buffers
                // for a given frame alive. For example, if this is 4,
                // we can reclaim the buffers for a frame after 4 frames have passed.
                int numFrames = 0;

                switch (SystemInfo.graphicsDeviceType)
                {
                    case GraphicsDeviceType.Vulkan:
                    case GraphicsDeviceType.Direct3D11:
                    case GraphicsDeviceType.Direct3D12:
                    case GraphicsDeviceType.PlayStation4:
                    case GraphicsDeviceType.PlayStation5:
                    case GraphicsDeviceType.XboxOne:
                    case GraphicsDeviceType.GameCoreXboxOne:
                    case GraphicsDeviceType.GameCoreXboxSeries:
                    case GraphicsDeviceType.OpenGLCore:

                    // OpenGL ES 2.0 is no longer supported in Unity 2023.1 and later
#if !UNITY_2023_1_OR_NEWER
                    case GraphicsDeviceType.OpenGLES2:
#endif

                    case GraphicsDeviceType.OpenGLES3:
                    case GraphicsDeviceType.PlayStation5NGGC:
                        numFrames = 3;
                        break;
                    case GraphicsDeviceType.Switch:
                    case GraphicsDeviceType.Metal:
                    default:
                        numFrames = 4;
                        break;
                }

                // Use at least as many frames as the quality settings have, but use a platform
                // specific lower limit in any case.
                numFrames = math.max(numFrames, QualitySettings.maxQueuedFrames);

                return numFrames;
            }
        }
    }

    public class AnimatronStats
    {
        public static int TotalJoints = 0;
        public static int SkinMatrixBufferBytes = 0;
    }
}
