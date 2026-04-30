using Unity.Entities;

namespace Medieval.DotsCombat
{
    public static class DamageApi
    {
        /// <summary>
        /// Enqueues damage on a victim entity. Victim is expected to already have a <see cref="DamageEvent"/> buffer.
        /// </summary>
        public static void Enqueue(EntityCommandBuffer ecb, Entity victim, float amount, Entity source = default)
        {
            if (amount <= 0f)
                return;
            if (victim == Entity.Null)
                return;

            ecb.AppendToBuffer(victim, new DamageEvent { Amount = amount, Source = source });
        }
    }
}

