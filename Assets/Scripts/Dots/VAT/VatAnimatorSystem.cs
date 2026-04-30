#nullable enable
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.Dots.VAT
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VatAnimatorSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VatAnimator>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = SystemAPI.Time.DeltaTime;

            foreach (var (anim, runtime, atlasInfo, clips, frameProp) in
                     SystemAPI.Query<RefRW<VatAnimator>, RefRW<VatAnimatorRuntime>, RefRO<VatAtlasInfo>, DynamicBuffer<VatClipElement>, RefRW<VatFrameProperty>>())
            {
                if (clips.Length == 0)
                    continue;

                var clipIndex = math.clamp(anim.ValueRO.ClipIndex, 0, clips.Length - 1);
                if (clipIndex != anim.ValueRO.ClipIndex)
                    anim.ValueRW.ClipIndex = clipIndex;

                if (runtime.ValueRO.LastClipIndex != clipIndex)
                {
                    anim.ValueRW.Time = 0f;
                    runtime.ValueRW.LastClipIndex = clipIndex;
                }

                var clip = clips[clipIndex];
                if (clip.FrameCount <= 0 || atlasInfo.ValueRO.Fps <= 0)
                {
                    frameProp.ValueRW.Value = 0f;
                    continue;
                }

                var time = anim.ValueRO.Time + dt * math.max(0f, anim.ValueRO.Speed);
                var length = math.max(1e-4f, clip.LengthSeconds);

                if (clip.Loop != 0)
                {
                    time = time % length;
                    if (time < 0f) time += length;
                }
                else
                {
                    time = math.min(time, length);
                }

                anim.ValueRW.Time = time;

                var localFrame = (int)math.floor(time * atlasInfo.ValueRO.Fps);
                if (clip.Loop != 0)
                    localFrame = localFrame % clip.FrameCount;
                else
                    localFrame = math.clamp(localFrame, 0, clip.FrameCount - 1);

                var frame = clip.StartFrame + localFrame;
                frame = math.clamp(frame, 0, atlasInfo.ValueRO.TotalFrames - 1);

                frameProp.ValueRW.Value = frame;
            }
        }
    }
}

