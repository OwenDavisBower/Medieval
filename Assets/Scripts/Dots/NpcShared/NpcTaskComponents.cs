using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.Npcs
{
    /// <summary>Singleton tag: streaming tree positions for NPC tasks (filled from world generation).</summary>
    public struct WorldStreamingTreesSingletonTag : IComponentData
    {
    }

    /// <summary>World positions of trees currently in the streaming window.</summary>
    public struct StreamingTreePosition : IBufferElementData
    {
        public float3 Position;
    }

    /// <summary>Marks an NPC that runs the chop-wood task loop.</summary>
    public struct NpcChopWoodTaskTag : IComponentData
    {
    }

    public enum NpcChopWoodPhase : byte
    {
        WalkToTree = 0,
        Chopping = 1,
        WalkToDropOff = 2,
        Dropping = 3
    }

    /// <summary>Runtime state for the chop-wood task.</summary>
    public struct NpcTaskChopWoodState : IComponentData
    {
        public NpcChopWoodPhase Phase;
        public float WoodCarried;
        public float DropTimer;
        public float3 TargetTreePosition;
        public byte HasTargetTree;
    }

    /// <summary>Baked tuning for chop wood.</summary>
    public struct NpcChopWoodConfig : IComponentData
    {
        public float CarryCapacity;
        public float WoodGatherPerSecond;
        public float ChopInteractDistance;
        public float DropArriveDistance;
        public float DropDurationSeconds;
    }

    /// <summary>Where gathered resources are delivered. If <see cref="HasPosition"/> is 0 at spawn, spawn API sets it to the NPC spawn point.</summary>
    public struct NpcResourceDropOff : IComponentData
    {
        public float3 WorldPosition;
        public byte HasPosition;
    }
}
