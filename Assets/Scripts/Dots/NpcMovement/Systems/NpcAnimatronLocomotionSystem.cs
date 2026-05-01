using ProjectDawn.Animation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// For baked NPCs: drives Animatron idle/walk from <see cref="NpcMovementState"/> on a parent entity.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcIntegrationSystem))]
    [UpdateBefore(typeof(AnimatronSystemGroup))]
    public partial struct NpcAnimatronLocomotionSystem : ISystem
    {
        static readonly FixedString64Bytes k_Idle = "Idle";
        static readonly FixedString64Bytes k_Walking = "Walking";

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcMovementTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // IntegrationJob writes NpcMovementState; main-thread lookups must wait.
            state.Dependency.Complete();

            var parentFromEntity = SystemAPI.GetComponentLookup<Parent>(isReadOnly: true);
            var movementFromEntity = SystemAPI.GetComponentLookup<NpcMovementState>(isReadOnly: true);
            var deadFromEntity = SystemAPI.GetComponentLookup<NpcDeadTag>(isReadOnly: true);
            parentFromEntity.Update(ref state);
            movementFromEntity.Update(ref state);
            deadFromEntity.Update(ref state);

            const float stopThreshold = 0.04f;
            float stopSq = stopThreshold * stopThreshold;
            const float minMaxSpeed = 0.01f;
            const float speedScale = 2f;

            foreach (var (animRw, motionRef, entity) in SystemAPI
                         .Query<RefRW<Animatron>, MotionRef>()
                         .WithEntityAccess())
            {
                if (!TryGetNpcMovementState(entity, parentFromEntity, movementFromEntity, deadFromEntity,
                        out NpcMovementState move))
                    continue;

                if (Time.time < move.ShootGestureSuppressLocomotionUntilUnityTime)
                    continue;

                ref ProjectDawn.Animation.Motion motion = ref motionRef.Value.Value;
                if (!motion.TryFindAnimationIndex(k_Idle, out AnimationIndex idleIdx))
                    continue;
                if (!motion.TryFindAnimationIndex(k_Walking, out AnimationIndex walkIdx))
                    continue;

                float3 h = move.CurrentHorizontalVelocity;
                h.y = 0f;
                float horizSq = math.lengthsq(h);
                bool moving = horizSq >= stopSq && move.EffectiveMoveSpeed >= minMaxSpeed;

                ref Animatron anim = ref animRw.ValueRW;
                if (moving)
                {
                    float horiz = math.sqrt(horizSq);
                    float maxS = math.max(0.05f, move.EffectiveMoveSpeed);
                    anim.Speed = math.saturate(horiz / maxS) * speedScale;
                }
                else
                    anim.Speed = 1f;

                AnimationIndex target = moving ? walkIdx : idleIdx;
                if (anim.AnimationIndex == target)
                    continue;

                if (state.EntityManager.HasComponent<CrossFader>(entity))
                {
                    ref CrossFader cross = ref SystemAPI.GetComponentRW<CrossFader>(entity).ValueRW;
                    cross.CrossFade(target);
                }
                else
                    anim.Play(target);
            }
        }

        static bool TryGetNpcMovementState(
            Entity start,
            ComponentLookup<Parent> parents,
            ComponentLookup<NpcMovementState> movements,
            ComponentLookup<NpcDeadTag> deadTags,
            out NpcMovementState move)
        {
            Entity e = start;
            for (var i = 0; i < 32; i++)
            {
                if (movements.HasComponent(e))
                {
                    if (deadTags.HasComponent(e))
                    {
                        move = default;
                        return false;
                    }

                    move = movements[e];
                    return true;
                }

                if (!parents.HasComponent(e))
                    break;
                e = parents[e].Value;
            }

            move = default;
            return false;
        }
    }
}
