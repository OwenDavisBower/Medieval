using Unity.Entities;
using UnityEngine;

namespace Medieval.Projectiles
{
    /// <summary>Creates a singleton with gravity matching <see cref="Physics.gravity"/> once per world.</summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct ProjectileSimSettingsBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            using var q = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileSimSettings>());
            if (q.CalculateEntityCount() != 0)
                return;

            Entity e = state.EntityManager.CreateEntity();
            float g = -Physics.gravity.y;
            if (g < 0.01f)
                g = 9.81f;
            state.EntityManager.AddComponentData(e, new ProjectileSimSettings { Gravity = g });
        }

        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;
        }
    }
}
