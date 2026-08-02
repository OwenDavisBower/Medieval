using Unity.Collections;
using Unity.Entities;

namespace Medieval.Npcs
{
    /// <summary>Persistent display name for a DOTS NPC (assigned at spawn).</summary>
    public struct NpcDisplayName : IComponentData
    {
        public FixedString64Bytes Value;
    }
}
