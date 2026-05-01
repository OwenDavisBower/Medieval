using Medieval.NpcMovement;
using ProjectDawn.Animation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>Disables DOTS NPC gameplay on death and starts a death clip on Animatron (see SoldierRig).</summary>
    public static class NpcDeathTransitionUtility
    {
        static readonly FixedString64Bytes[] k_DeathClips =
        {
            "DeathBackward",
            "DeathBackward2",
            "DeathRight",
        };

        public static void TryApply(EntityManager em, Entity npcRoot)
        {
            if (!em.Exists(npcRoot) || !em.HasComponent<NpcCharacterCombatState>(npcRoot))
                return;

            var combat = em.GetComponentData<NpcCharacterCombatState>(npcRoot);
            if (combat.IsDead == 0)
                return;

            if (em.HasComponent<NpcDeadTag>(npcRoot))
                return;

            em.AddComponent<NpcDeadTag>(npcRoot);
            if (em.HasComponent<NpcMovementTag>(npcRoot))
                em.RemoveComponent<NpcMovementTag>(npcRoot);

            TryPlayDeathAnim(em, npcRoot);
        }

        static void TryPlayDeathAnim(EntityManager em, Entity npcRoot)
        {
            FixedString64Bytes clipName = k_DeathClips[Random.Range(0, k_DeathClips.Length)];

            if (em.HasBuffer<LinkedEntityGroup>(npcRoot))
            {
                var buf = em.GetBuffer<LinkedEntityGroup>(npcRoot);
                for (int i = 0; i < buf.Length; i++)
                {
                    if (TryPlayDeathOnEntity(em, buf[i].Value, clipName))
                        return;
                }
            }

            TryPlayDeathOnEntity(em, npcRoot, clipName);
        }

        static bool TryPlayDeathOnEntity(EntityManager em, Entity e, FixedString64Bytes clipName)
        {
            if (!em.HasComponent<Animatron>(e) || !em.HasComponent<MotionRef>(e))
                return false;

            MotionRef motionRef = em.GetSharedComponentManaged<MotionRef>(e);
            ref ProjectDawn.Animation.Motion motion = ref motionRef.Value.Value;
            if (!motion.TryFindAnimationIndex(clipName, out AnimationIndex deathIdx))
                return false;

            var anim = em.GetComponentData<Animatron>(e);
            if (em.HasComponent<CrossFader>(e))
            {
                var cross = em.GetComponentData<CrossFader>(e);
                cross.CrossFade(deathIdx);
                em.SetComponentData(e, cross);
            }
            else
            {
                anim.Speed = 1f;
                anim.Play(deathIdx);
                em.SetComponentData(e, anim);
            }

            return true;
        }
    }
}
