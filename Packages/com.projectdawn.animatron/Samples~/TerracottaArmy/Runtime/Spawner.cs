using ProjectDawn.Animation;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace ProjectDawn.Sample
{
    public class Spawner : MonoBehaviour
    {
        public GameObject Prefab;
        public int Count = 50;
        public float Spacing = 1f;

        public TestMode Mode;
        public NormalTest Normal = new NormalTest
        {
            AnimationName = "Run"
        };
        public BlendTest Blend = new BlendTest
        {
            AnimationNameA = "Run",
            AnimationNameB = "Idle",
            Frequency = 0.3f,
            State = 0,
        };

        List<Animator> m_Animators = new();
        List<InertializerAuthoring> m_Inertializers = new();
        List<AnimatronAuthoring> m_Animatrons = new();

        public enum TestMode
        {
            Normal,
            Blend,
        }

        [System.Serializable]
        public class NormalTest
        {
            public string AnimationName;
        }

        [System.Serializable]
        public class BlendTest
        {
            public string AnimationNameA;
            public int AnimationPropertyIndexA { get; set; }
            public AnimationIndex AnimationIndexA { get; set; }
            public string AnimationNameB;
            public int AnimationPropertyIndexB { get; set; }
            public AnimationIndex AnimationIndexB { get; set; }
            public float Frequency;
            public int State;
        }

        void Start()
        {
            var origin = new Vector3(-Count * 0.5f * Spacing, 0, -Count * 0.5f * Spacing);
            for (int i = 0; i < Count; i++)
            {
                for (int j = 0; j < Count; j++)
                {
                    var instance = GameObject.Instantiate(Prefab, new Vector3(i * Spacing, 0, j * Spacing) + origin, Quaternion.identity);
                    if (instance.TryGetComponent<AnimatronAuthoring>(out var animatron))
                        m_Animatrons.Add(animatron);
                    if (instance.TryGetComponent<InertializerAuthoring>(out var inertializer))
                        m_Inertializers.Add(inertializer);
                    if (instance.TryGetComponent<Animator>(out var animator))
                        m_Animators.Add(animator);
                }
            }

            switch (Mode)
            {
                case TestMode.Normal:
                    foreach (var animator in m_Animators)
                        animator.Play(Shader.PropertyToID(Normal.AnimationName));
                    foreach (var animatron in m_Animatrons)
                        animatron.Play(animatron.FindAnimationIndex(Normal.AnimationName));
                    break;

                case TestMode.Blend:
                    Blend.AnimationPropertyIndexA = Animator.StringToHash(Blend.AnimationNameA);
                    Blend.AnimationPropertyIndexB = Animator.StringToHash(Blend.AnimationNameB);


                    if (Prefab.TryGetComponent<AnimatronAuthoring>(out var prefabAnimatron))
                    {
                        Blend.AnimationIndexA = prefabAnimatron.FindAnimationIndex(Blend.AnimationNameA);
                        Blend.AnimationIndexB = prefabAnimatron.FindAnimationIndex(Blend.AnimationNameB);
                    }

                    break;
            }
        }

        void Update()
        {
            switch (Mode)
            {
                case TestMode.Blend:
                    var newState = (int)(Time.time / Blend.Frequency);
                    if (newState == Blend.State)
                        break;
                    Blend.State = newState;

                    var animationPropertyIndex = (newState & 1) == 0 ? Blend.AnimationPropertyIndexA : Blend.AnimationPropertyIndexB;
                    var animationIndex = (newState & 1) == 0 ? Blend.AnimationIndexA : Blend.AnimationIndexB;

                    foreach (var animator in m_Animators)
                        animator.CrossFade(animationPropertyIndex, 0.3f);
                    foreach (var inertializer in m_Inertializers)
                        inertializer.Intertialize(animationIndex);
                    break;
            }
        }

        class Baker : Baker<Spawner>
        {
            public override void Bake(Spawner authoring)
            {
                if (authoring.Prefab == null)
                    throw new System.InvalidOperationException();

                if (!authoring.Prefab.TryGetComponent(out AnimatronAuthoring animatron))
                    throw new System.InvalidOperationException();

                var rig = animatron.Rig;

                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new EntitySpawner
                {
                    Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.None),
                    Count = authoring.Count,
                    Spacing = authoring.Spacing,
                });
                if (authoring.Mode == TestMode.Normal)
                {
                    AddComponent(entity, new EntitySpawner.NormalTest
                    {
                        AnimationIndex = rig.GetOrCreateMotion().Value.FindAnimationIndex(authoring.Normal.AnimationName),
                    });
                }
                else
                {
                    AddComponent(entity, new EntitySpawner.BlendTest
                    {
                        AnimationIndexA = rig.GetOrCreateMotion().Value.FindAnimationIndex(authoring.Blend.AnimationNameA),
                        AnimationIndexB = rig.GetOrCreateMotion().Value.FindAnimationIndex(authoring.Blend.AnimationNameB),
                        Frequency = authoring.Blend.Frequency,
                        State = authoring.Blend.State,
                    });
                }

            }
        }
    }
}

namespace ProjectDawn.Sample
{
    public struct EntitySpawner : IComponentData
    {
        public Entity Prefab;
        public int Count;
        public float Spacing;

        public struct NormalTest : IComponentData
        {
            public AnimationIndex AnimationIndex;
        }

        public struct BlendTest : IComponentData
        {
            public AnimationIndex AnimationIndexA;
            public AnimationIndex AnimationIndexB;
            public float Frequency;
            public int State;
        }
    }

    public partial struct NormalTestSystem : ISystem
    {
        void ISystem.OnUpdate(ref SystemState state)
        {
            var random = new Unity.Mathematics.Random(1);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (spawner, test, entity) in SystemAPI.Query<EntitySpawner, EntitySpawner.NormalTest>().WithEntityAccess())
            {
                var Count = spawner.Count;
                var Spacing = spawner.Spacing;
                var origin = new Vector3(-Count * 0.5f * Spacing, 0, -Count * 0.5f * Spacing);
                for (int i = 0; i < Count; i++)
                {
                    for (int j = 0; j < Count; j++)
                    {
                        var instance = ecb.Instantiate(spawner.Prefab);
                        ecb.SetComponent(instance, LocalTransform.FromPosition(new Vector3(i * Spacing, 0, j * Spacing) + origin));
                        ecb.SetComponent(instance, new Animatron
                        {
                            AnimationIndex = test.AnimationIndex,
                            Time = random.NextFloat(0, 0.2f),
                            Speed = 1.0f,
                        });
                    }
                }
                ecb.RemoveComponent<EntitySpawner>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    public partial struct BlendTestSystem : ISystem
    {
        void ISystem.OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (spawner, test, entity) in SystemAPI.Query<EntitySpawner, EntitySpawner.BlendTest>().WithEntityAccess())
            {
                var Count = spawner.Count;
                var Spacing = spawner.Spacing;
                var origin = new Vector3(-Count * 0.5f * Spacing, 0, -Count * 0.5f * Spacing);
                for (int i = 0; i < Count; i++)
                {
                    for (int j = 0; j < Count; j++)
                    {
                        var instance = ecb.Instantiate(spawner.Prefab);
                        ecb.SetComponent(instance, LocalTransform.FromPosition(new Vector3(i * Spacing, 0, j * Spacing) + origin));

                        // This is slightly lazy solution from my end, but I simply add here the same component for next query
                        ecb.AddComponent(instance, test);
                    }
                }
                ecb.RemoveComponent<EntitySpawner>(entity);
            }

            foreach (var (_inertializer, _test, entity) in SystemAPI.Query<RefRW<Inertializer>, RefRW<EntitySpawner.BlendTest>>().WithEntityAccess())
            {
                ref var inertializer = ref _inertializer.ValueRW;
                ref var test = ref _test.ValueRW;
                var newState = (int)(Time.time / test.Frequency);
                if (newState == test.State)
                    break;
                test.State = newState;

                var animationIndex = (newState & 1) == 0 ? test.AnimationIndexA : test.AnimationIndexB;
                inertializer.Intertialize(animationIndex);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}