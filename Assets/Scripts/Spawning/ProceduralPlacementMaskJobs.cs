using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

/// <summary>
/// Burst-parallel helpers for <see cref="ProceduralPlacementMask"/> CPU cache and word copies.
/// </summary>
public static class ProceduralPlacementMaskJobs
{
    const int MinParallelFreeBytesCells = 4096;
    const int MinParallelWordOrCopy = 512;

    /// <summary>Rebuilds R8 free grid: 255 = free, 0 = blocked from path|dynamic bits.</summary>
    [BurstCompile]
    public struct RebuildCpuFreeBytesParallelJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<uint> PathWords;
        [ReadOnly] public NativeArray<uint> DynamicWords;
        [WriteOnly] public NativeArray<byte> CpuFreeBytes;

        public void Execute(int index)
        {
            int word = index >> 5;
            int bit = index & 31;
            uint combined = PathWords[word] | DynamicWords[word];
            CpuFreeBytes[index] = (combined & (1u << bit)) != 0 ? (byte)0 : (byte)255;
        }
    }

    /// <summary>Writes destination[i] = path[i] | dynamic[i].</summary>
    [BurstCompile]
    public struct OrMergeOccupancyWordsParallelJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<uint> PathWords;
        [ReadOnly] public NativeArray<uint> DynamicWords;
        [WriteOnly] public NativeArray<uint> Destination;

        public void Execute(int index) =>
            Destination[index] = PathWords[index] | DynamicWords[index];
    }

    public static bool ShouldParallelRebuildCpuFreeBytes(int cellCount) =>
        cellCount >= MinParallelFreeBytesCells;

    public static bool ShouldParallelOrCopyWords(int wordCount) =>
        wordCount >= MinParallelWordOrCopy;
}
