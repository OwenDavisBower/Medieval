using Medieval.DotsCombat;
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
        public float rangedWindupSeconds = 0.12f;

        [Header("Melee")]
        public float meleeRange = 1.12f;
        public float meleeStunSeconds = 0.28f;

        class Baker : Baker<NpcCombatAiAuthoring>
        {
            public override void Bake(NpcCombatAiAuthoring authoring)
            {
                Entity e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new NpcCombatConfig
                {
                    Role = authoring.role,
                    AggroRadius = Mathf.Max(0f, authoring.aggroRadius),
                    CombatRange = Mathf.Max(0f, authoring.combatRange),
                    AttackInterval = Mathf.Max(0.02f, authoring.attackInterval),
                    Damage = Mathf.Max(0f, authoring.damage),
                    RangedWindupSeconds = Mathf.Max(0f, authoring.rangedWindupSeconds),
                    MeleeRange = Mathf.Max(0f, authoring.meleeRange),
                    MeleeStunSeconds = Mathf.Max(0f, authoring.meleeStunSeconds)
                });
                AddComponent(e, new NpcCombatState());
                AddComponent(e, new NpcCurrentTarget());
            }
        }
    }
}

