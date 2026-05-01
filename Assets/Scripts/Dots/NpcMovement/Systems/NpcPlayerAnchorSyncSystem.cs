using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Main-thread bridge from <see cref="PlayerAnchorRegistration"/> to a singleton <see cref="NpcPlayerAnchor"/>.
    /// Player transform/velocity are registered once from <c>PlayerController</c>; this system does not use Find/tag discovery.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NpcFollowersAnchorSystem))]
    public partial class NpcPlayerAnchorSyncSystem : SystemBase
    {
        EntityQuery _q;

        protected override void OnCreate()
        {
            _q = GetEntityQuery(ComponentType.ReadWrite<NpcPlayerAnchor>());
            if (_q.CalculateEntityCount() == 0)
            {
                Entity e = EntityManager.CreateEntity(typeof(NpcPlayerAnchor));
#if UNITY_EDITOR
                EntityManager.SetName(e, "NpcPlayerAnchorSingleton");
#endif
            }
        }

        protected override void OnUpdate()
        {
            var anchor = new NpcPlayerAnchor();
            if (PlayerAnchorRegistration.HasPlayer)
            {
                Transform t = PlayerAnchorRegistration.Transform;
                Rigidbody rb = PlayerAnchorRegistration.Rigidbody;
                Vector3 p = t.position;
                Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero;
                anchor.Position = new float3(p.x, p.y, p.z);
                anchor.LinearVelocity = new float3(v.x, v.y, v.z);
                anchor.HasPlayer = 1;
                anchor.PlayerFactionId = PlayerAnchorRegistration.PlayerFactionId;
            }
            else
                anchor.PlayerFactionId = -1;

            Entity eSingleton = _q.GetSingletonEntity();
            EntityManager.SetComponentData(eSingleton, anchor);
        }
    }
}

