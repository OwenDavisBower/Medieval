#nullable enable
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Medieval.Dots.VAT
{
    /// <summary>Per-entity VAT playback controls.</summary>
    public struct VatAnimator : IComponentData
    {
        public int ClipIndex;
        public float Time;
        public float Speed;
    }

    /// <summary>Atlas-wide data baked from a <see cref="Medieval.VAT.VatAtlasAsset"/>.</summary>
    public struct VatAtlasInfo : IComponentData
    {
        public int VertexCount;
        public int Fps;
        public int TotalFrames;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
    }

    /// <summary>Per-clip baked data (dynamic buffer so we can switch clips at runtime).</summary>
    public struct VatClipElement : IBufferElementData
    {
        public int StartFrame;
        public int FrameCount;
        public float LengthSeconds;
        public byte Loop;
    }

    /// <summary>Internal runtime state (used to detect clip switches).</summary>
    public struct VatAnimatorRuntime : IComponentData
    {
        public int LastClipIndex;
    }

    // Material property overrides pushed by Entities Graphics.
    [MaterialProperty("_VatFrame")]
    public struct VatFrameProperty : IComponentData
    {
        public float Value;
    }
}

