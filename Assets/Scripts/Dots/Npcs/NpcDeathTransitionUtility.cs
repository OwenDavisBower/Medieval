using Medieval.NpcMovement;
using ProjectDawn.Animation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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

        static readonly Color FollowerGoldLabelColor = new Color(1f, 0.88f, 0.28f, 1f);

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
            if (em.HasComponent<NpcMovementState>(npcRoot))
            {
                var move = em.GetComponentData<NpcMovementState>(npcRoot);
                move.CurrentHorizontalVelocity = float3.zero;
                move.SmoothTargetVel = float3.zero;
                move.SeparationAccum = float3.zero;
                move.ObstacleDeflectDir = float3.zero;
                move.DodgeImpulseThisFrame = 0;
                em.SetComponentData(npcRoot, move);
            }

            if (em.HasComponent<NpcMovementTag>(npcRoot))
                em.RemoveComponent<NpcMovementTag>(npcRoot);

            TrySpawnLootAndSignal(em, npcRoot);
            TryDropFollowerCarriedGold(em, npcRoot);
            TryPlayDeathAnim(em, npcRoot);
        }

        static void TrySpawnLootAndSignal(EntityManager em, Entity npcRoot)
        {
            if (!IsEnemyNpc(em, npcRoot))
                return;

            if (!TryGetWorldPosition(em, npcRoot, out Vector3 worldPos))
                return;

            var combat = em.GetComponentData<NpcCharacterCombatState>(npcRoot);
            int amount = GoldDrop.RollAmount(GoldDrop.DefaultMinAmount, GoldDrop.DefaultMaxAmountInclusive + 2);
            if (!TryPayKillGoldToFollower(em, combat.KillCreditKiller, amount, worldPos))
                GoldDrop.Spawn(worldPos, amount);

            // Occasional food ration from bandit packs.
            if (UnityEngine.Random.value < 0.28f && PlayerInventory.Instance != null)
            {
                PlayerInventory.Instance.AddFood(1);
                FloatingWorldText.Spawn(
                    worldPos + Vector3.up * 1.9f,
                    "+1 Food",
                    new Color(0.55f, 0.95f, 0.45f, 1f));
            }

            bool byPlayerOrFollower = combat.KillCreditPlayerSide != 0;
            GameplayEvents.RaiseEnemyKilled(worldPos, WellKnownFactionIds.Bandit, byPlayerOrFollower);
        }

        /// <summary>Follower kills credit gold to their wallet instead of a world pickup.</summary>
        static bool TryPayKillGoldToFollower(EntityManager em, Entity killer, int amount, Vector3 corpsePos)
        {
            if (amount <= 0 || !NpcKillCreditUtility.IsFollower(em, killer) ||
                !em.HasComponent<NpcWallet>(killer))
                return false;

            if (em.HasComponent<NpcCharacterCombatState>(killer))
            {
                var killerCombat = em.GetComponentData<NpcCharacterCombatState>(killer);
                if (killerCombat.IsDead != 0)
                    return false;
            }

            var wallet = em.GetComponentData<NpcWallet>(killer);
            wallet.Gold += amount;
            em.SetComponentData(killer, wallet);

            Vector3 labelPos = corpsePos + Vector3.up * 1.9f;
            if (TryGetWorldPosition(em, killer, out Vector3 killerPos))
                labelPos = killerPos + Vector3.up * 1.9f;

            FloatingWorldText.Spawn(labelPos, $"+{amount} Gold", FollowerGoldLabelColor);
            return true;
        }

        static void TryDropFollowerCarriedGold(EntityManager em, Entity npcRoot)
        {
            if (!NpcKillCreditUtility.IsFollower(em, npcRoot) || !em.HasComponent<NpcWallet>(npcRoot))
                return;

            var wallet = em.GetComponentData<NpcWallet>(npcRoot);
            if (wallet.Gold <= 0)
                return;

            if (!TryGetWorldPosition(em, npcRoot, out Vector3 worldPos))
                return;

            GoldDrop.Spawn(worldPos, wallet.Gold);
            wallet.Gold = 0;
            em.SetComponentData(npcRoot, wallet);
        }

        static bool TryGetWorldPosition(EntityManager em, Entity npcRoot, out Vector3 worldPos)
        {
            worldPos = default;
            if (em.HasComponent<LocalToWorld>(npcRoot))
            {
                float3 pos = em.GetComponentData<LocalToWorld>(npcRoot).Position;
                worldPos = new Vector3(pos.x, pos.y, pos.z);
                return true;
            }

            if (em.HasComponent<LocalTransform>(npcRoot))
            {
                float3 pos = em.GetComponentData<LocalTransform>(npcRoot).Position;
                worldPos = new Vector3(pos.x, pos.y, pos.z);
                return true;
            }

            return false;
        }

        static bool IsEnemyNpc(EntityManager em, Entity npcRoot)
        {
            if (em.HasComponent<NpcProfile>(npcRoot) &&
                em.GetComponentData<NpcProfile>(npcRoot).Role == NpcRole.Bandit)
                return true;

            return em.HasComponent<NpcFactionId>(npcRoot) &&
                   em.GetComponentData<NpcFactionId>(npcRoot).Value == WellKnownFactionIds.Bandit;
        }

        static void TryPlayDeathAnim(EntityManager em, Entity npcRoot)
        {
            FixedString64Bytes clipName = k_DeathClips[UnityEngine.Random.Range(0, k_DeathClips.Length)];

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
