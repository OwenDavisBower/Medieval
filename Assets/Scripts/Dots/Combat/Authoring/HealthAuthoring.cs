using Medieval.DotsCombat;
using Unity.Entities;
using UnityEngine;

namespace Medieval.DotsCombatAuthoring
{
    /// <summary>
    /// Authoring for DOTS health. Intended for entity NPC prefabs / SubScene baking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealthAuthoring : MonoBehaviour
    {
        [Header("Health")]
        [Min(0.01f)] public float maxHealth = 100f;
        [Tooltip("If > 0, pick a random max health in [minRandomMaxHealth, maxHealth] during baking.")]
        [Min(0f)] public float minRandomMaxHealth = 0f;

        [Header("Death")]
        [Tooltip("If > 0, entity is destroyed this many seconds after death.")]
        [Min(0f)] public float despawnDelaySeconds = 3f;

        class Baker : Baker<HealthAuthoring>
        {
            public override void Bake(HealthAuthoring authoring)
            {
                Entity e = GetEntity(TransformUsageFlags.Dynamic);

                float max = Mathf.Max(0.01f, authoring.maxHealth);
                if (authoring.minRandomMaxHealth > 0f && authoring.minRandomMaxHealth < max)
                {
                    // Deterministic per-baked-object randomization.
                    var r = new Unity.Mathematics.Random((uint)(authoring.GetInstanceID() * 2654435761u) | 1u);
                    max = Mathf.Lerp(authoring.minRandomMaxHealth, max, r.NextFloat());
                }

                AddComponent(e, new Health { Current = max, Max = max });
                AddBuffer<DamageEvent>(e);

                if (authoring.despawnDelaySeconds > 0f)
                    AddComponent(e, new DeathConfig { DespawnDelaySeconds = authoring.despawnDelaySeconds });
            }
        }
    }
}

