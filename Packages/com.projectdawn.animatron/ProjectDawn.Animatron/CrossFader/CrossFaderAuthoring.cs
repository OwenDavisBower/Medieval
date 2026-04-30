using Unity.Entities;
using UnityEngine;
using ProjectDawn.Animation.Entities;
using ProjectDawn.Animation.Hybrid;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Handles simple crossfading between animations by linearly blending weights over time.
    /// This component provides a basic solution for transitioning between animations,
    /// but for smoother and more physically convincing results, it's recommended to use <see cref="Inertializer"/> instead.
    /// Inertialization better preserves motion momentum and reduces visual popping during abrupt changes.
    /// </summary>
    [AddComponentMenu("Miscellaneous/Cross Fader")]
    [HelpURL("https://lukaschod.github.io/animatron-docs/manual/authoring/cross-fader.html")]
    public class CrossFaderAuthoring : MonoBehaviour
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
                return EntityCrossFader.Duration;
            }

            set
            {
                if (World == null)
                {
                    m_Duration = value;
                    return;
                }
                EntityCrossFader.Duration = value;
            }
        }

        public ref CrossFader EntityCrossFader => 
            ref World.EntityManager.GetComponentDataRW<CrossFader>(m_Entity).ValueRW;

        World World => World.DefaultGameObjectInjectionWorld;

        /// <summary>
        /// Initiates a crossfade transition to the specified animation by blending weights over time.
        /// This provides a simple linear interpolation between the current and target animation.
        /// For smoother transitions that better preserve motion momentum, consider using <see cref="Inertializer"/> instead.
        /// </summary>
        public void CrossFade(AnimationIndex animationIndex)
        {
            EntityCrossFader.CrossFade(animationIndex);
        }

        void Awake()
        {
            var entity = GetComponent<EntityBehaviour>().GetOrCreateEntity();
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            entityManager.AddComponentData(entity, new CrossFader
            {
                Duration = m_Duration,
            });
            m_Entity = entity;
        }

        class Baker : Baker<CrossFaderAuthoring>
        {
            public override void Bake(CrossFaderAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new CrossFader
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
    /// Controls <see cref="Animatron"/> animation cross fading.
    /// </summary>
    public struct CrossFader : IComponentData
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
        /// Previous track animation index.
        /// </summary>
        public AnimationIndex AnimationIndex;

        /// <summary>
        /// Previous track play time.
        /// </summary>
        public float Time;

        /// <summary>
        /// Blending of previous and current track. Ranges from <see cref="Duration"/> to 0.
        /// </summary>
        public float Blend;

        /// <summary>
        /// Indicates whether a transition is currently in progress.
        /// </summary>
        public bool InTransition;

        public static CrossFader Default => new()
        {
            Duration = 0.3f,
        };

        /// <summary>
        /// Crossfades into a new animation using a blending transition.
        /// This is a deferred operation, and subsequent calls within the same frame will overwrite the previous one.
        /// </summary>
        public void CrossFade(AnimationIndex animationIndex)
        {
            RequestedAnimationIndex = animationIndex;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CheckValidity()
        {
            if (Duration <= 0)
                throw new InvalidOperationException("Cross fader duration must be higher than 0!");
        }
    }
}
