using Unity.Burst;
using Unity.Mathematics;

namespace Medieval.Npcs
{
    /// <summary>Burst-friendly queries mirroring <see cref="Character"/> combat helpers.</summary>
    public static class NpcCombatStateQueries
    {
        /// <summary>Same rule as <see cref="Character.ShouldFleeFromCombatThreat"/> using baked flee fractions and bravery range.</summary>
        [BurstCompile]
        public static bool ShouldFleeFromCombatThreat(in NpcCharacterCombatState state, in NpcCharacterBakedStats bake)
        {
            if (state.MaxHealth <= 0.0001f || state.IsDead != 0)
                return false;
            float t = StatT(state.Bravery, bake.MinBravery, bake.MaxBravery);
            float fleeBelow = math.lerp(bake.FleeFracLowBravery, bake.FleeFracHighBravery, t);
            return (state.CurrentHealth / state.MaxHealth) <= fleeBelow;
        }

        [BurstCompile]
        static float StatT(float value, float min, float max)
        {
            if (max <= min + 0.001f)
                return 0.5f;
            return math.clamp((value - min) / (max - min), 0f, 1f);
        }
    }
}
