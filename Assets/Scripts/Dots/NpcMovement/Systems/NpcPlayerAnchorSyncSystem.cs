using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Main-thread bridge from the player GameObject to a singleton <see cref="NpcPlayerAnchor"/> component.
    /// This is not a per-NPC companion/writeback; it's a single read-only anchor for DOTS followers.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NpcFollowersAnchorSystem))]
    public partial class NpcPlayerAnchorSyncSystem : SystemBase
    {
        EntityQuery _q;
        Transform _playerTransform;
        Rigidbody _playerRb;

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
            if (_playerTransform == null)
            {
                // Keep this system free of compile-time dependencies on gameplay assemblies (e.g. PlayerController)
                // so it can live inside a DOTS-focused asmdef.
                GameObject go = null;
                try { go = GameObject.FindWithTag("Player"); }
                catch (UnityException) { /* Tag doesn't exist in project. */ }
                if (go == null)
                    go = GameObject.Find("Player");

                _playerTransform = go != null ? go.transform : null;
                _playerRb = go != null ? go.GetComponent<Rigidbody>() : null;
            }

            var anchor = new NpcPlayerAnchor();
            if (_playerTransform != null)
            {
                Vector3 p = _playerTransform.position;
                Vector3 v = _playerRb != null ? _playerRb.linearVelocity : Vector3.zero;
                anchor.Position = new float3(p.x, p.y, p.z);
                anchor.LinearVelocity = new float3(v.x, v.y, v.z);
                anchor.HasPlayer = 1;
            }

            Entity eSingleton = _q.GetSingletonEntity();
            EntityManager.SetComponentData(eSingleton, anchor);
        }
    }
}

