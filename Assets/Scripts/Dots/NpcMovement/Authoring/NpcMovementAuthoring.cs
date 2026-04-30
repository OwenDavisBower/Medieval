using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Authoring component mirroring the serialized fields of the legacy <c>TargetSteeringMotor</c> so
    /// the DOTS NPC movement pipeline can be configured either by baking a prefab or by reading this
    /// component off the GameObject at runtime (see <see cref="NpcMovementEntityFactory"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcMovementAuthoring : MonoBehaviour
    {
        [Header("Mode")]
        public NpcMovementMode Mode = NpcMovementMode.Orbit;
        public NpcSeparationGroup SeparationGroup = NpcSeparationGroup.None;

        [Header("Motion")]
        public float MoveSpeed = 5f;
        public float MoveSpeedScale = 1f;
        public float ArriveThreshold = 0.15f;
        public float Acceleration = 14f;
        public float FacingTurnSpeedDegreesPerSecond = 720f;
        public float FacingMinHorizontalSpeed = 1f;
        public float PostRangedDodgeImpulse = 3.6f;
        [Range(0f, 1f)] public float PostRangedDodgeRetreatRatio = 0.28f;
        public float PostRangedDodgeDelay = 0.14f;
        public float RangedDodgeCooldown = 0.42f;

        [Header("Orbit (annulus around anchor)")]
        public float MinLoiterRadius = 2.5f;
        public float MaxLoiterRadius = 5.5f;

        [Header("Orbit - trail behind moving anchor")]
        public float TrailBehindStrength = 0.35f;
        public float MaxTrailOffset = 2f;

        [Header("Wander (disk around anchor)")]
        public float WanderRadius = 20f;
        public float RepickWanderInterval = 4f;

        [Header("Organic motion")]
        public float TargetSmoothTime = 0.35f;
        public float NoiseFrequency = 0.2f;
        public float AngleWobbleDegrees = 38f;
        public float RadiusWobble = 2f;

        [Header("Pathfinding & avoidance")]
        public bool UseNavMeshWhenAvailable = true;
        public float NavMeshSampleMaxDistance = 2f;
        public float MinCornerAdvanceDistance = 0.35f;
        public float SeparationRadius = 1.1f;
        public float SeparationStrength = 4f;
        public float ObstacleProbeRadius = 0.35f;
        public float ObstacleProbeDistance = 1.25f;

        [Header("Path refresh")]
        [Tooltip("Seconds between pathfinding attempts when the goal has not moved significantly.")]
        public float RepathInterval = 0.35f;
        [Tooltip("Distance the goal must move before forcing an early repath.")]
        public float RepathGoalShiftDistance = 2f;

        [Header("Ground alignment")]
        public bool GroundSnapEnabled = true;
        public float GroundRaycastStartHeight = 1.25f;
        public float GroundRaycastMaxDistance = 5f;
        public float GroundSnapHeightOffset;
        public float GroundSnapSmoothTime = 0.1f;
        public LayerMask GroundSnapLayers = -1;

        public NpcMovementConfig ToConfig()
        {
            return new NpcMovementConfig
            {
                MoveSpeed = MoveSpeed,
                MoveSpeedScale = math.max(0.05f, MoveSpeedScale),
                ArriveThreshold = ArriveThreshold,
                Acceleration = Acceleration,
                FacingTurnSpeedDegreesPerSecond = FacingTurnSpeedDegreesPerSecond,
                FacingMinHorizontalSpeed = FacingMinHorizontalSpeed,
                PostRangedDodgeImpulse = PostRangedDodgeImpulse,
                PostRangedDodgeRetreatRatio = PostRangedDodgeRetreatRatio,
                PostRangedDodgeDelay = PostRangedDodgeDelay,
                RangedDodgeCooldown = RangedDodgeCooldown,
                MinLoiterRadius = MinLoiterRadius,
                MaxLoiterRadius = MaxLoiterRadius,
                TrailBehindStrength = TrailBehindStrength,
                MaxTrailOffset = MaxTrailOffset,
                WanderRadius = WanderRadius,
                RepickWanderInterval = RepickWanderInterval,
                TargetSmoothTime = TargetSmoothTime,
                NoiseFrequency = NoiseFrequency,
                AngleWobbleDegrees = AngleWobbleDegrees,
                RadiusWobble = RadiusWobble,
                UseNavMeshWhenAvailable = (byte)(UseNavMeshWhenAvailable ? 1 : 0),
                NavMeshSampleMaxDistance = NavMeshSampleMaxDistance,
                MinCornerAdvanceDistance = MinCornerAdvanceDistance,
                SeparationRadius = SeparationRadius,
                SeparationStrength = SeparationStrength,
                ObstacleProbeRadius = ObstacleProbeRadius,
                ObstacleProbeDistance = ObstacleProbeDistance,
                RepathInterval = math.max(0.05f, RepathInterval),
                RepathGoalShiftSqr = RepathGoalShiftDistance * RepathGoalShiftDistance,
                GroundSnapEnabled = (byte)(GroundSnapEnabled ? 1 : 0),
                GroundRaycastStartHeight = GroundRaycastStartHeight,
                GroundRaycastMaxDistance = GroundRaycastMaxDistance,
                GroundSnapHeightOffset = GroundSnapHeightOffset,
                GroundSnapSmoothTime = GroundSnapSmoothTime,
                GroundSnapLayerMask = GroundSnapLayers.value != 0 ? GroundSnapLayers.value : -1
            };
        }

        class NpcMovementBaker : Baker<NpcMovementAuthoring>
        {
            public override void Bake(NpcMovementAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                var t = authoring.transform;
                AddComponent(entity, LocalTransform.FromPositionRotationScale(
                    new float3(t.position.x, t.position.y, t.position.z),
                    new quaternion(t.rotation.x, t.rotation.y, t.rotation.z, t.rotation.w),
                    1f));
                AddComponent<NpcMovementTag>(entity);
                AddComponent(entity, authoring.ToConfig());
                uint seed = (uint)authoring.GetInstanceID() ^ 0x9E3779B1u;
                if (seed == 0u)
                    seed = 1u;
                AddComponent(entity, new NpcMovementState
                {
                    Mode = authoring.Mode,
                    Group = authoring.SeparationGroup,
                    Rng = new Unity.Mathematics.Random(seed)
                });
                AddComponent<NpcAnchorTarget>(entity);
                AddComponent<NpcSeekOverride>(entity);
                AddComponent<NpcOverrideFacing>(entity);
                AddComponent<NpcPendingDodge>(entity);
                AddComponent<NpcPathState>(entity);
                AddBuffer<NpcPathCorner>(entity);
            }
        }
    }
}
