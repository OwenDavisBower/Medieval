using Medieval.NpcMovement;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.Npcs
{
    public static class NpcCombatSpawnUtility
    {
        public static void RollAndAttachCombatState(EntityManager em, Entity npc)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcCharacterBakedStats>(npc))
                return;
            if (em.HasComponent<NpcCharacterCombatState>(npc))
            {
                if (!em.HasComponent<NpcExperience>(npc))
                    em.AddComponentData(npc, NpcExperienceUtility.CreateStarting());
                EnsureDisplayName(em, npc);
                return;
            }

            var bake = em.GetComponentData<NpcCharacterBakedStats>(npc);
            uint seed = 1u;
            if (em.HasComponent<LocalTransform>(npc))
            {
                float3 p = em.GetComponentData<LocalTransform>(npc).Position;
                seed = math.max(1u, math.hash(p) ^ (uint)npc.Index ^ 0x9E3779B9u);
            }

            var rng = new Unity.Mathematics.Random(seed);
            float maxHealth = rng.NextFloat(bake.MinHealth, bake.MaxHealth);
            float strength = rng.NextFloat(bake.MinStrength, bake.MaxStrength);
            float dexterity = rng.NextFloat(bake.MinDexterity, bake.MaxDexterity);
            float focus = rng.NextFloat(bake.MinFocus, bake.MaxFocus);
            float bravery = rng.NextFloat(bake.MinBravery, bake.MaxBravery);

            em.AddComponentData(npc, new NpcCharacterCombatState
            {
                CurrentHealth = maxHealth,
                MaxHealth = maxHealth,
                MeleeDamageMultiplier = StatMultiplier(strength, bake.MinStrength, bake.MaxStrength, 0.78f, 1.22f),
                MovementSpeedMultiplier = StatMultiplier(dexterity, bake.MinDexterity, bake.MaxDexterity, 0.86f, 1.14f),
                RangedAimErrorMultiplier = StatMultiplier(focus, bake.MinFocus, bake.MaxFocus, 1.28f, 0.62f),
                Bravery = bravery,
                AttackStunUntilUnityTime = 0f,
                IsDead = 0
            });

            if (!em.HasComponent<NpcExperience>(npc))
                em.AddComponentData(npc, NpcExperienceUtility.CreateStarting());

            if (!em.HasComponent<NpcDisplayName>(npc))
                em.AddComponentData(npc, new NpcDisplayName { Value = NpcMedievalNameUtility.Generate(ref rng) });
        }

        static void EnsureDisplayName(EntityManager em, Entity npc)
        {
            if (em.HasComponent<NpcDisplayName>(npc))
                return;

            uint seed = math.max(1u, (uint)npc.Index ^ 0xC2B2AE35u);
            if (em.HasComponent<LocalTransform>(npc))
            {
                float3 p = em.GetComponentData<LocalTransform>(npc).Position;
                seed = math.max(1u, math.hash(p) ^ (uint)npc.Index ^ 0xC2B2AE35u);
            }

            em.AddComponentData(npc, new NpcDisplayName { Value = NpcMedievalNameUtility.Generate(seed) });
        }

        /// <summary>Sets <see cref="NpcProfile.Role"/> from spawn and resolves <see cref="NpcProfile.WeaponClass"/> when <see cref="NpcWeaponClass.Unspecified"/>.</summary>
        /// <param name="explicitWeaponClass">If <see cref="NpcWeaponClass.Melee"/> or <see cref="NpcWeaponClass.Ranged"/>, that loadout is used (ranged/melee-only at runtime).</param>
        public static void FinalizeSpawnProfile(EntityManager em, Entity npc, NpcRole role,
            NpcWeaponClass explicitWeaponClass = NpcWeaponClass.Unspecified)
        {
            if (!em.Exists(npc))
                return;

            NpcWeaponClass inferred = ResolveWeaponClass(em, npc);
            NpcWeaponClass resolved;
            if (explicitWeaponClass == NpcWeaponClass.Melee || explicitWeaponClass == NpcWeaponClass.Ranged)
                resolved = explicitWeaponClass;
            else if ((role == NpcRole.Follower || role == NpcRole.Bandit) && inferred == NpcWeaponClass.Both)
                resolved = CoinFlipWeapon(em, npc);
            else
                resolved = inferred;

            if (!em.HasComponent<NpcProfile>(npc))
            {
                em.AddComponentData(npc, new NpcProfile { Role = role, WeaponClass = resolved });
                ApplyFactionForSpawnRole(em, npc, role);
                ApplyWeaponLoadoutVisuals(em, npc);
                return;
            }

            var profile = em.GetComponentData<NpcProfile>(npc);
            profile.Role = role;
            if (explicitWeaponClass == NpcWeaponClass.Melee || explicitWeaponClass == NpcWeaponClass.Ranged)
                profile.WeaponClass = explicitWeaponClass;
            else if (profile.WeaponClass == NpcWeaponClass.Unspecified)
                profile.WeaponClass = resolved;
            else if ((role == NpcRole.Follower || role == NpcRole.Bandit) &&
                     profile.WeaponClass == NpcWeaponClass.Both)
                profile.WeaponClass = CoinFlipWeapon(em, npc);
            em.SetComponentData(npc, profile);
            ApplyFactionForSpawnRole(em, npc, role);
            ApplyWeaponLoadoutVisuals(em, npc);
        }

        static NpcWeaponClass CoinFlipWeapon(EntityManager em, Entity npc)
        {
            uint seed = 1u;
            if (em.HasComponent<LocalTransform>(npc))
            {
                float3 p = em.GetComponentData<LocalTransform>(npc).Position;
                seed = math.max(1u, math.hash(p) ^ (uint)npc.Index ^ 0x85EBCA6Bu);
            }
            else
                seed = math.max(1u, (uint)npc.Index ^ 0x85EBCA6Bu);

            var rng = new Unity.Mathematics.Random(seed);
            return rng.NextFloat() < 0.5f ? NpcWeaponClass.Melee : NpcWeaponClass.Ranged;
        }

        /// <summary>
        /// Disables baked hand weapons that do not match <see cref="NpcProfile.WeaponClass"/>
        /// (ranged NPCs keep bows only; melee keep swords only).
        /// </summary>
        public static void ApplyWeaponLoadoutVisuals(EntityManager em, Entity npc)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcProfile>(npc))
                return;

            NpcWeaponClass weaponClass = em.GetComponentData<NpcProfile>(npc).WeaponClass;
            if (weaponClass == NpcWeaponClass.Both || weaponClass == NpcWeaponClass.Unspecified)
                return;
            if (!em.HasBuffer<LinkedEntityGroup>(npc))
                return;

            var group = em.GetBuffer<LinkedEntityGroup>(npc);
            var toDisable = new NativeList<Entity>(4, Allocator.Temp);
            for (int i = 0; i < group.Length; i++)
            {
                Entity e = group[i].Value;
                if (!em.Exists(e) || !em.HasComponent<NpcWeaponVisual>(e))
                    continue;

                NpcWeaponClass visualClass = em.GetComponentData<NpcWeaponVisual>(e).Class;
                bool keep = weaponClass switch
                {
                    NpcWeaponClass.Melee => visualClass == NpcWeaponClass.Melee,
                    NpcWeaponClass.Ranged => visualClass == NpcWeaponClass.Ranged,
                    NpcWeaponClass.None => false,
                    _ => true
                };
                if (!keep)
                    toDisable.Add(e);
            }

            for (int i = 0; i < toDisable.Length; i++)
                DisableEntityHierarchy(em, toDisable[i]);
            toDisable.Dispose();
        }

        static void DisableEntityHierarchy(EntityManager em, Entity root)
        {
            if (!em.Exists(root))
                return;

            var stack = new NativeList<Entity>(8, Allocator.Temp);
            stack.Add(root);
            while (stack.Length > 0)
            {
                Entity e = stack[stack.Length - 1];
                stack.RemoveAt(stack.Length - 1);
                if (!em.Exists(e))
                    continue;

                if (em.HasBuffer<Child>(e))
                {
                    var children = em.GetBuffer<Child>(e);
                    for (int i = 0; i < children.Length; i++)
                        stack.Add(children[i].Value);
                }

                em.SetEnabled(e, false);
            }

            stack.Dispose();
        }

        /// <summary>Aligns <see cref="NpcFactionId"/> with spawn kind (matches default faction assets).</summary>
        public static void ApplyFactionForSpawnRole(EntityManager em, Entity npc, NpcRole role)
        {
            if (!em.Exists(npc))
                return;
            int id = role switch
            {
                NpcRole.Follower => WellKnownFactionIds.Player,
                NpcRole.Bandit => WellKnownFactionIds.Bandit,
                NpcRole.Villager => WellKnownFactionIds.Villager,
                _ => -1
            };
            if (!em.HasComponent<NpcFactionId>(npc))
                em.AddComponentData(npc, new NpcFactionId { Value = id });
            else
                em.SetComponentData(npc, new NpcFactionId { Value = id });
        }

        /// <summary>Uses combat config presence on this entity (baked root). For configs on child entities, set <see cref="NpcProfile.WeaponClass"/> in authoring.</summary>
        public static NpcWeaponClass ResolveWeaponClass(EntityManager em, Entity npc)
        {
            if (!em.Exists(npc))
                return NpcWeaponClass.None;
            bool melee = em.HasComponent<NpcMeleeCombatConfig>(npc);
            bool ranged = em.HasComponent<NpcRangedCombatConfig>(npc);
            if (melee && ranged)
                return NpcWeaponClass.Both;
            if (melee)
                return NpcWeaponClass.Melee;
            if (ranged)
                return NpcWeaponClass.Ranged;
            return NpcWeaponClass.None;
        }

        static float StatT(float value, float min, float max)
        {
            if (max <= min + 0.001f)
                return 0.5f;
            return math.clamp((value - min) / (max - min), 0f, 1f);
        }

        static float StatMultiplier(float value, float min, float max, float atMin, float atMax)
        {
            return math.lerp(atMin, atMax, StatT(value, min, max));
        }
    }
}
