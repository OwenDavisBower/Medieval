using Unity.Entities;
using UnityEngine;

namespace Medieval.Projectiles
{
    /// <summary>Creates a singleton with gravity matching <see cref="Physics.gravity"/> once per world.</summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct ProjectileSimSettingsBootstrapSystem : ISystem
    {
        /// <summary>Physics casts for projectiles hit these layers; Character is excluded so ECS resolves unit hits.</summary>
        public static int DefaultStaticEnvironmentLayerMask()
        {
            int mask = LayerMask.GetMask("Default", "Water", "Building", "Tree");
            if (mask == 0)
                mask = ~0;
            int character = LayerMask.NameToLayer("Character");
            if (character >= 0)
                mask &= ~(1 << character);
            return mask;
        }

        public void OnCreate(ref SystemState state)
        {
            using var q = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileSimSettings>());
            if (q.CalculateEntityCount() != 0)
                return;

            Entity e = state.EntityManager.CreateEntity();
            float g = -Physics.gravity.y;
            if (g < 0.01f)
                g = 9.81f;
            state.EntityManager.AddComponentData(e, new ProjectileSimSettings
            {
                Gravity = g,
                StaticEnvironmentLayerMask = DefaultStaticEnvironmentLayerMask()
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;
        }
    }
}
