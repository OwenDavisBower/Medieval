using Unity.Entities;

namespace Medieval.DotsCombat
{
    /// <summary>Basic health state for DOTS entities.</summary>
    public struct Health : IComponentData
    {
        public float Current;
        public float Max;
    }

    /// <summary>Damage events queued for an entity and consumed by <c>ApplyDamageSystem</c>.</summary>
    public struct DamageEvent : IBufferElementData
    {
        public float Amount;
        public Entity Source;
    }

    /// <summary>Added once when health reaches zero.</summary>
    public struct DeadTag : IComponentData
    {
    }

    /// <summary>Optional configuration controlling what happens on death.</summary>
    public struct DeathConfig : IComponentData
    {
        public float DespawnDelaySeconds;
    }

    /// <summary>Runtime death state.</summary>
    public struct DeathState : IComponentData
    {
        public float TimeOfDeath;
        public float DespawnAtTime;
    }

    /// <summary>Simple "cannot attack until time" gate, independent of animation.</summary>
    public struct AttackStun : IComponentData
    {
        public float StunnedUntilTime;
    }
}

