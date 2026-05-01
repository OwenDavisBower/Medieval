using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.NpcMovement
{
    public enum NpcMovementMode : byte
    {
        Orbit = 0,
        MoveTowards = 1,
        WanderAroundTarget = 2
    }

    public enum NpcSeparationGroup : byte
    {
        None = 0,
        Followers = 1,
        Bandits = 2
    }

    public struct NpcMovementTag : IComponentData
    {
    }

    /// <summary>
    /// One-shot initialization marker for loiter state (orbit / wander). A system will consume this tag
    /// to randomize base angle/radius/noise so baked prefab instances don't all loiter identically.
    /// </summary>
    public struct NpcLoiterInitTag : IComponentData
    {
    }

    /// <summary>Static per-entity tuning mirrored from <c>TargetSteeringMotor</c> serialized fields.</summary>
    public struct NpcMovementConfig : IComponentData
    {
        public float MoveSpeed;
        public float MoveSpeedScale;
        public float ArriveThreshold;
        public float Acceleration;
        public float FacingTurnSpeedDegreesPerSecond;
        public float FacingMinHorizontalSpeed;

        public float PostRangedDodgeImpulse;
        public float PostRangedDodgeRetreatRatio;
        public float PostRangedDodgeDelay;
        public float RangedDodgeCooldown;

        public float MinLoiterRadius;
        public float MaxLoiterRadius;
        public float TrailBehindStrength;
        public float MaxTrailOffset;

        public float WanderRadius;
        public float RepickWanderInterval;

        public float TargetSmoothTime;
        public float NoiseFrequency;
        public float AngleWobbleDegrees;
        public float RadiusWobble;

        public byte UseNavMeshWhenAvailable;
        public float NavMeshSampleMaxDistance;
        public float MinCornerAdvanceDistance;

        public float SeparationRadius;
        public float SeparationStrength;

        public float ObstacleProbeRadius;
        public float ObstacleProbeDistance;

        /// <summary>Seconds between repath attempts when the goal has not moved significantly.</summary>
        public float RepathInterval;
        /// <summary>Squared distance the goal must move before forcing an early repath.</summary>
        public float RepathGoalShiftSqr;

        /// <summary>When non-zero, raycast down each frame and align Y to ground (see writeback system).</summary>
        public byte GroundSnapEnabled;
        public float GroundRaycastStartHeight;
        public float GroundRaycastMaxDistance;
        /// <summary>Added to hit point Y (pivot-to-feet offset).</summary>
        public float GroundSnapHeightOffset;
        /// <summary>Vertical SmoothDamp time; 0 or less snaps instantly.</summary>
        public float GroundSnapSmoothTime;
        public int GroundSnapLayerMask;
    }

    /// <summary>Runtime scratch updated every frame by steering/integration systems.</summary>
    public struct NpcMovementState : IComponentData
    {
        public NpcMovementMode Mode;
        public NpcSeparationGroup Group;

        public byte HasSmoothTarget;
        public byte RangedMovementLock;
        public byte DodgeImpulseThisFrame;

        public float BaseAngle;
        public float BaseRadius;
        public float NoiseA;
        public float NoiseB;
        public float NextWanderPickTime;

        public float EffectiveMoveSpeed;
        public float LastDodgeApplyTime;

        public float3 SmoothTarget;
        public float3 SmoothTargetVel;
        public float3 CurrentHorizontalVelocity;

        /// <summary>Separation repulsion accumulated by <c>NpcSeparationSystem</c> and consumed by <c>NpcSteeringSystem</c>.</summary>
        public float3 SeparationAccum;

        /// <summary>Normalized deflection direction from the obstacle probe; zero when no deflection needed.</summary>
        public float3 ObstacleDeflectDir;

        /// <summary>Per-entity RNG used for wander re-picks. Seeded by the entity factory / baker.</summary>
        public Unity.Mathematics.Random Rng;

        /// <summary>Scratch for <see cref="NpcTransformWritebackSystem"/> vertical SmoothDamp.</summary>
        public float GroundSnapYVelocity;

        /// <summary>While &gt; <c>UnityEngine.Time.time</c>, <see cref="NpcAnimatronLocomotionSystem"/> skips idle/walk.</summary>
        public float ShootGestureSuppressLocomotionUntilUnityTime;
    }

    public struct NpcAnchorTarget : IComponentData
    {
        public float3 Position;
        public float3 LinearVelocity;
        public byte HasAnchor;
    }

    /// <summary>
    /// Singleton source for the player/leader world anchor, fed from the GameObject player (main thread).
    /// Followers can copy from this into <see cref="NpcAnchorTarget"/> without any per-NPC companion data.
    /// </summary>
    public struct NpcPlayerAnchor : IComponentData
    {
        public float3 Position;
        public float3 LinearVelocity;
        public byte HasPlayer;
    }

    public struct NpcSeekOverride : IComponentData
    {
        public float3 Position;
        public float SeekHoldDistance;
        public byte HasOverride;
    }

    /// <summary>Written by DOTS combat seek: current hostile ECS target, if any.</summary>
    public struct NpcCombatTarget : IComponentData
    {
        /// <summary>Entity.Null when the current seek goal is not an ECS NPC (e.g. player).</summary>
        public Entity TargetNpcEntity;
        public byte HasCombatTarget;
    }

    public struct NpcOverrideFacing : IComponentData
    {
        public float3 FlatDirection;
        public byte HasOverride;
    }

    public struct NpcPendingDodge : IComponentData
    {
        public float3 ReferencePosition;
        public float FireTime;
        public byte HasPending;
    }

    public struct NpcPathCorner : IBufferElementData
    {
        public float3 Value;
    }

    public struct NpcPathState : IComponentData
    {
        public int CurrentCorner;
        public float LastPathTime;
        public float3 LastPathGoal;
        public byte PathValid;
    }
}
