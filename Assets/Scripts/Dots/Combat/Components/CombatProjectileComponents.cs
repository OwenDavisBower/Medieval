using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.DotsCombat
{
    public struct CombatProjectile : IComponentData
    {
        public Entity Target;
        public float3 LastKnownTargetPosition;

        public Entity Source;
        public float Damage;

        public float Speed;
        public float HitRadius;
        public float ExpireAtTime;
    }
}

