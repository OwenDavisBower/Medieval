using Medieval.DotsCombat;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

namespace Medieval.DotsCombatAuthoring
{
    [DisallowMultipleComponent]
    public sealed class NpcCombatAiAuthoring : MonoBehaviour
    {
        [Header("Role")]
        public CombatRole role = CombatRole.Ranged;

        [Header("Targeting")]
        public float aggroRadius = 50f;
        public float combatRange = 20f;

        [Header("Attack")]
        public float attackInterval = 1.1f;
        public float damage = 14f;

        [Header("Ranged")]
        public RangedAttackMode rangedMode = RangedAttackMode.DirectDamage;
        public float rangedWindupSeconds = 0.12f;
        [Tooltip("Used only when rangedMode = Projectile.")]
        public GameObject projectilePrefab;
        [Min(0.01f)] public float projectileSpeed = 24f;
        [Min(0.01f)] public float projectileHitRadius = 0.35f;
        [Min(0.01f)] public float projectileMaxLifetimeSeconds = 4f;
        public Vector3 projectileSpawnOffset = new Vector3(0f, 1.35f, 0.5f);

        [Header("Melee")]
        public float meleeRange = 1.12f;
        public float meleeStunSeconds = 0.28f;

        class Baker : Baker<NpcCombatAiAuthoring>
        {
            public override void Bake(NpcCombatAiAuthoring authoring)
            {
                Entity e = GetEntity(TransformUsageFlags.Dynamic);
                Entity projectilePrefabEntity = Entity.Null;
                if (authoring.projectilePrefab != null)
                    projectilePrefabEntity = GetEntity(authoring.projectilePrefab, TransformUsageFlags.Dynamic);

                AddComponent(e, new NpcCombatConfig
                {
                    Role = authoring.role,
                    RangedMode = authoring.rangedMode,
                    AggroRadius = Mathf.Max(0f, authoring.aggroRadius),
                    CombatRange = Mathf.Max(0f, authoring.combatRange),
                    AttackInterval = Mathf.Max(0.02f, authoring.attackInterval),
                    Damage = Mathf.Max(0f, authoring.damage),
                    RangedWindupSeconds = Mathf.Max(0f, authoring.rangedWindupSeconds),
                    ProjectilePrefab = projectilePrefabEntity,
                    ProjectileSpeed = Mathf.Max(0.01f, authoring.projectileSpeed),
                    ProjectileHitRadius = Mathf.Max(0.01f, authoring.projectileHitRadius),
                    ProjectileMaxLifetimeSeconds = Mathf.Max(0.01f, authoring.projectileMaxLifetimeSeconds),
                    ProjectileSpawnOffset = new float3(authoring.projectileSpawnOffset.x, authoring.projectileSpawnOffset.y, authoring.projectileSpawnOffset.z),
                    MeleeRange = Mathf.Max(0f, authoring.meleeRange),
                    MeleeStunSeconds = Mathf.Max(0f, authoring.meleeStunSeconds)
                });
                AddComponent(e, new NpcCombatState());
                AddComponent(e, new NpcCurrentTarget());
            }
        }
    }
}

