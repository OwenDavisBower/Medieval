using Unity.Entities;

namespace Medieval.Npcs
{
    public static class NpcEntityDestroyUtility
    {
        public static void DestroyNpcWithLinked(EntityManager em, Entity root)
        {
            if (!em.Exists(root))
                return;
            if (em.HasBuffer<LinkedEntityGroup>(root))
            {
                var buffer = em.GetBuffer<LinkedEntityGroup>(root);
                var copy = new Entity[buffer.Length];
                for (int i = 0; i < buffer.Length; i++)
                    copy[i] = buffer[i].Value;
                for (int i = 0; i < copy.Length; i++)
                {
                    if (em.Exists(copy[i]))
                        em.DestroyEntity(copy[i]);
                }

                return;
            }

            em.DestroyEntity(root);
        }
    }
}
