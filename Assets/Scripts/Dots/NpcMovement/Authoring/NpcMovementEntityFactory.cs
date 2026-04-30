using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Runtime helper used by the <c>TargetSteeringMotor</c> facade to spawn / destroy a backing NPC
    /// movement entity in the default world from a live companion <see cref="GameObject"/>.
    /// </summary>
    public static class NpcMovementEntityFactory
    {
        public static Entity Create(
            Transform companionTransform,
            Rigidbody companionRigidbody,
            INpcFacade facade,
            NpcMovementConfig config,
            NpcMovementMode mode,
            NpcSeparationGroup group)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || companionTransform == null)
                return Entity.Null;

            EntityManager em = world.EntityManager;
            Entity entity = em.CreateEntity();

#if UNITY_EDITOR
            em.SetName(entity, $"Npc_{companionTransform.name}");
#endif

            Vector3 p = companionTransform.position;
            Quaternion r = companionTransform.rotation;

            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                new float3(p.x, p.y, p.z),
                new quaternion(r.x, r.y, r.z, r.w),
                1f));
            em.AddComponent<NpcMovementTag>(entity);
            em.AddComponentData(entity, config);
            uint seed = (uint)UnityEngine.Random.Range(1, int.MaxValue)
                        ^ (uint)companionTransform.GetInstanceID();
            if (seed == 0u)
                seed = 1u;
            em.AddComponentData(entity, new NpcMovementState
            {
                Mode = mode,
                Group = group,
                Rng = new Unity.Mathematics.Random(seed)
            });
            em.AddComponentData(entity, new NpcAnchorTarget());
            em.AddComponentData(entity, new NpcSeekOverride());
            em.AddComponentData(entity, new NpcOverrideFacing());
            em.AddComponentData(entity, new NpcPendingDodge());
            em.AddComponentData(entity, new NpcPathState());
            em.AddBuffer<NpcPathCorner>(entity);
            em.AddComponentObject(entity, new NpcCompanion
            {
                Transform = companionTransform,
                Rigidbody = companionRigidbody,
                Facade = facade
            });

            return entity;
        }

        public static void Destroy(Entity entity)
        {
            if (entity == Entity.Null)
                return;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            EntityManager em = world.EntityManager;
            if (em.Exists(entity))
                em.DestroyEntity(entity);
        }
    }
}
