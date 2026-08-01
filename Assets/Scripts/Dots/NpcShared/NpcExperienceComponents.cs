using Unity.Entities;

namespace Medieval.Npcs
{
    /// <summary>Combat progression for DOTS NPCs; attached at spawn with <see cref="NpcCharacterCombatState"/>.</summary>
    public struct NpcExperience : IComponentData
    {
        public int Level;
        public float CurrentXp;
        public float XpToNextLevel;
    }

    /// <summary>Requests short-lived overhead "Level Up!" presentation; removed when the timer expires.</summary>
    public struct NpcLevelUpFx : IComponentData
    {
        public float SecondsRemaining;
        /// <summary>0 until a floating label has been spawned for this request.</summary>
        public byte Spawned;
        /// <summary>How many levels were gained in the grant that created this FX.</summary>
        public byte LevelsGained;
    }
}
