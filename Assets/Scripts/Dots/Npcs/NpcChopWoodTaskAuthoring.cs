using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>Bakes chop-wood task components on NPC roots (e.g. villagers).</summary>
    [DisallowMultipleComponent]
    public sealed class NpcChopWoodTaskAuthoring : MonoBehaviour
    {
        [Min(0.1f)] public float CarryCapacity = 10f;
        [Min(0.01f)] public float WoodGatherPerSecond = 2f;
        [Min(0.1f)] public float ChopInteractDistance = 2.5f;
        [Min(0.05f)] public float DropArriveDistance = 1.75f;
        [Min(0f)] public float DropDurationSeconds = 0.75f;

        [Tooltip("If set, drop-off uses this transform's world position. Otherwise spawn position is used at runtime.")]
        public Transform DropOffPoint;

        class Baker : Baker<NpcChopWoodTaskAuthoring>
        {
            public override void Bake(NpcChopWoodTaskAuthoring authoring)
            {
                Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
                DependsOn(authoring);
                if (authoring.DropOffPoint != null)
                    DependsOn(authoring.DropOffPoint);
                AddComponent<NpcChopWoodTaskTag>(entity);
                AddComponent(entity, new NpcChopWoodConfig
                {
                    CarryCapacity = math.max(0.1f, authoring.CarryCapacity),
                    WoodGatherPerSecond = math.max(0.01f, authoring.WoodGatherPerSecond),
                    ChopInteractDistance = math.max(0.1f, authoring.ChopInteractDistance),
                    DropArriveDistance = math.max(0.05f, authoring.DropArriveDistance),
                    DropDurationSeconds = math.max(0f, authoring.DropDurationSeconds)
                });
                float3 dropWorld = default;
                byte hasDrop = 0;
                if (authoring.DropOffPoint != null)
                {
                    Vector3 w = authoring.DropOffPoint.position;
                    dropWorld = new float3(w.x, w.y, w.z);
                    hasDrop = 1;
                }

                AddComponent(entity, new NpcResourceDropOff
                {
                    WorldPosition = dropWorld,
                    HasPosition = hasDrop
                });
                AddComponent(entity, new NpcTaskChopWoodState
                {
                    Phase = NpcChopWoodPhase.WalkToTree,
                    WoodCarried = 0f,
                    DropTimer = 0f,
                    TargetTreePosition = default,
                    HasTargetTree = 0
                });
            }
        }
    }
}
