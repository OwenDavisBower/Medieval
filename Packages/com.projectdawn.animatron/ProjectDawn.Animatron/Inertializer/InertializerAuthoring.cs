using Unity.Entities;
using UnityEngine;
using ProjectDawn.Animation.Entities;
using Unity.Mathematics;
using System.Runtime.CompilerServices;
using ProjectDawn.Animation.Hybrid;
using System.Diagnostics;
using System;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Handles animation inertialization, enabling smooth transitions between animations by preserving motion continuity.
    /// Inspired by the technique used in Gears of War, this component reduces visual popping and harsh transitions
    /// when switching from one animation to another.
    /// Based on https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions.
    /// </summary>
    [AddComponentMenu("Miscellaneous/Inertializer")]
    [HelpURL("https://lukaschod.github.io/animatron-docs/manual/authoring/inertializer.html")]
    public class InertializerAuthoring : MonoBehaviour
    {
        [Tooltip("Time in seconds for the transition from the previous animation.")]
        [SerializeField]
        float m_Duration = 0.3f;

        Entity m_Entity;

        /// <summary>
        /// Time in seconds for the transition from the previous animation.
        /// </summary>
        public float Duration
        {
            get
            {
                if (World == null)
                    return m_Duration;
                return EntityInertializer.Duration;
            }

            set
            {
                if (World == null)
                {
                    m_Duration = value;
                    return;
                }
                EntityInertializer.Duration = value;
            }
        }

        public ref Inertializer EntityInertializer =>
            ref World.EntityManager.GetComponentDataRW<Inertializer>(m_Entity).ValueRW;

        World World => World.DefaultGameObjectInjectionWorld;

        /// <summary>
        /// Initiates inertialization to smoothly blend from the previous animation to the specified current animation.
        /// This technique, inspired by Gears of War, helps reduce visual pops or abrupt transitions by preserving motion momentum.
        /// </summary>
        public void Intertialize(AnimationIndex animationIndex)
        {
            EntityInertializer.Intertialize(animationIndex);
        }

        void Awake()
        {
            var entity = GetComponent<EntityBehaviour>().GetOrCreateEntity();
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            entityManager.AddComponentData(entity, new Inertializer
            {
                Duration = m_Duration,
            });

            m_Entity = entity;
        }

        class Baker : Baker<InertializerAuthoring>
        {
            public override void Bake(InertializerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new Inertializer
                {
                    Duration = authoring.m_Duration,
                });
            }
        }
    }
}

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Contains joint position and rotation inertia.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct JointInertia : IBufferElementData
    {
        public Float3Inertia Position;
        public QuaternionInertia Rotation;
    }

    /// <summary>
    /// Controls <see cref="Animatron"/> animation cross fading.
    /// </summary>
    public struct Inertializer : IComponentData
    {
        /// <summary>
        /// Total duration of the transition effect.
        /// Must be greater than 0.
        /// </summary>
        public float Duration;

        /// <summary>
        /// The animation requested to be played using the internalizer.
        /// </summary>
        public AnimationIndex RequestedAnimationIndex;

        /// <summary>
        /// Time elapsed since the last transition.
        /// </summary>
        public float Time;

        /// <summary>
        /// Indicates whether a transition is currently in progress.
        /// </summary>
        public bool InTransition;


        public static Inertializer Default => new()
        {
            Duration = 0.3f,
        };

        /// <summary>
        /// Inertializes into a new animation using a post processing transition.
        /// This is a deferred operation, and subsequent calls within the same frame will overwrite the previous one.
        /// </summary>
        public void Intertialize(AnimationIndex animationIndex)
        {
            RequestedAnimationIndex = animationIndex;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CheckValidity()
        {
            if (Duration <= 0)
                throw new InvalidOperationException("Inertializer duration must be higher than 0!");
        }
    }
}
