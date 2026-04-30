using ProjectDawn.Animation.Hybrid;
using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectDawn.Rendering
{
    /// <summary>
    /// RenderMeshArray is a component used for rendering meshes. 
    /// It serves as an alternative to Unity’s existing components: MeshFilter, MeshRenderer, and SkinnedMeshRenderer.
    /// A GameObject with a RenderMeshArray component will render similarly to one using a MeshRenderer and MeshFilter.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Mesh/Render Mesh Array")]
    [HelpURL("https://lukaschod.github.io/animatron-docs/manual/authoring/render-mesh-array.html")]
    public class RenderMeshArrayAuthoring : EntityBehaviour
    {
        [UnityEngine.Serialization.FormerlySerializedAs("Instances")]
        [SerializeField]
        Instance[] m_Instances = Array.Empty<Instance>();

        [UnityEngine.Serialization.FormerlySerializedAs("Filtering")]
        [SerializeField]
        RenderFilterSettings m_Filter = new()
        {
            MotionMode = MotionVectorGenerationMode.Camera,
            ShadowCastingMode = ShadowCastingMode.On,
            RenderingLayerMask = 1,
        };

        [UnityEngine.Serialization.FormerlySerializedAs("LightProbeUsage")]
        [SerializeField]
        LightProbeUsage m_LightProbeUsage = LightProbeUsage.Off;

        [SerializeField]
        AABB m_Bounds;

        public Instance[] Instances => m_Instances;

        /// <summary>
        /// Filtering settings that determine when to draw the entity.
        /// </summary>
        public RenderFilterSettings Filter => m_Filter;

        /// <summary>
        /// Determines what kinds of light probes the entity uses, if any.
        /// </summary>
        /// <remarks>
        /// This value corresponds to <see cref="LightProbeUsage"/>.
        /// </remarks>
        public LightProbeUsage LightProbeUsage => m_LightProbeUsage;

        /// <summary>
        /// The bounding box of the renderer in object space.
        /// </summary>
        public AABB Bounds
        {
            get => m_Bounds;
            set
            {
                if (IsCreated)
                    World.DefaultGameObjectInjectionWorld.EntityManager.SetComponentData(m_Entity, new RenderBounds { Value = value });
                m_Bounds = value;
            }
        }

        public void SetInstances(Instance[] instances)
        {
            if (IsCreated)
            {
                World.DefaultGameObjectInjectionWorld.EntityManager.SetSharedComponentManaged(m_Entity, ConvertInstanceToRenderMeshArray(instances));
            }
            else
            {
                m_Instances = instances;
            }
        }

        public void SetInstance(int index, Instance instance)
        {
            m_Instances[index] = instance;
            if (IsCreated)
                World.DefaultGameObjectInjectionWorld.EntityManager.SetSharedComponentManaged(m_Entity, ConvertInstanceToRenderMeshArray(m_Instances));
        }

        public void RecalculateBounds()
        {
            if (m_Instances.Length == 0)
                return;

            if (m_Instances[0].Mesh == null)
                return;

            var bounds = m_Instances[0].Mesh.bounds;
            for (int i = 1; i < m_Instances.Length; i++)
                bounds.Encapsulate(m_Instances[i].Mesh.bounds);
            m_Bounds = new AABB { Center = bounds.center, Extents = bounds.extents };
        }

        public void SetInstances(
            ReadOnlySpan<UnityObjectRef<Material>> materials, 
            ReadOnlySpan<UnityObjectRef<Mesh>> meshes, 
            ReadOnlySpan<MaterialMeshIndex> indices)
        {
            Debug.Assert(IsCreated);
            World.DefaultGameObjectInjectionWorld.EntityManager.SetSharedComponentManaged(m_Entity, new RenderMeshArray(
                materials,
                meshes,
                indices));
        }

        public void SetSettings(RenderFilterSettings filtering)
        {
            if (IsCreated)
            {
                World.DefaultGameObjectInjectionWorld.EntityManager.SetSharedComponent(m_Entity, Filter.ToFilterSettings(gameObject.layer));
            }
            else
            {
                m_Filter = filtering; 
            }
        }

        protected new void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RenderMeshArrayManager.Add(this);
                return;
            }
#endif
            Create();

            base.OnEnable();
        }

        protected new void OnDisable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RenderMeshArrayManager.Remove(this);
                return;
            }
#endif

            base.OnDisable();
        }

        void OnDrawGizmos()
        {
            // This is needed to draw selection mesh
            if (Event.current.type != EventType.Repaint)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                foreach (var instance in m_Instances)
                    Gizmos.DrawMesh(instance.Mesh, instance.SubMesh);
            }
        }

        void Create()
        {
            if (m_Instances == null)
                return;

            if (World.DefaultGameObjectInjectionWorld == null)
                return;

            m_Entity = GetOrCreateEntity();

            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            var renderMeshArray = ConvertInstanceToRenderMeshArray(m_Instances);

            var renderMeshDescription = new RenderMeshDescription
            {
                FilterSettings = Filter.ToFilterSettings(gameObject.layer),
                LightProbeUsage = LightProbeUsage,
            };

            var materialMeshInfo = MaterialMeshInfo.FromMaterialMeshIndexRange(0, m_Instances.Length);
            materialMeshInfo.Material = -1;
            materialMeshInfo.Mesh = -1;

            Unity.Rendering.RenderMeshUtility.AddComponents(m_Entity, entityManager, renderMeshDescription, renderMeshArray, materialMeshInfo);
            entityManager.SetComponentData(m_Entity, new RenderBounds { Value = m_Bounds });

            entityManager.AddComponentData(m_Entity, new LocalToWorld { Value = transform.localToWorldMatrix });
            entityManager.AddComponentObject(m_Entity, transform);
        }

        static RenderMeshArray ConvertInstanceToRenderMeshArray(Instance[] instances)
        {
            unsafe
            {
                var materials = stackalloc UnityObjectRef<Material>[instances.Length];
                var meshes = stackalloc UnityObjectRef<Mesh>[instances.Length];
                var indices = stackalloc MaterialMeshIndex[instances.Length];
                for (int i = 0; i < instances.Length; i++)
                {
                    materials[i] = instances[i].Material;
                    meshes[i] = instances[i].Mesh;
                    indices[i] = new MaterialMeshIndex
                    {
                        MeshIndex = i,
                        MaterialIndex = i,
                        SubMeshIndex = instances[i].SubMesh,
                    };
                }
                return new RenderMeshArray(
                    new ReadOnlySpan<UnityObjectRef<Material>>(materials, instances.Length),
                    new ReadOnlySpan<UnityObjectRef<Mesh>>(meshes, instances.Length),
                    new ReadOnlySpan<MaterialMeshIndex>(indices, instances.Length));
            }
        }

        [System.Serializable]
        public struct Instance
        {
            public Material Material;
            public Mesh Mesh;
            public int SubMesh;
        }

        class Baker : Baker<RenderMeshArrayAuthoring>
        {
            public unsafe override void Bake(RenderMeshArrayAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Renderable);

                var instances = authoring.m_Instances;

                var renderMeshArray = ConvertInstanceToRenderMeshArray(instances);

                var renderMeshDescription = new RenderMeshDescription
                {
                    FilterSettings = authoring.m_Filter.ToFilterSettings(authoring.gameObject.layer),
                    LightProbeUsage = authoring.m_LightProbeUsage,
                };

                var materialMeshInfo = MaterialMeshInfo.FromMaterialMeshIndexRange(0, instances.Length);
                materialMeshInfo.Material = -1;
                materialMeshInfo.Mesh = -1;

                var components = 
                    RenderMeshUtility.GetComponentSet(renderMeshDescription, renderMeshArray.GetMaterials(materialMeshInfo), authoring.gameObject.isStatic);
                AddComponent(entity, components);

                SetSharedComponent(entity, renderMeshDescription.FilterSettings);
                SetSharedComponentManaged(entity, renderMeshArray);
                SetComponent(entity, materialMeshInfo);
                SetComponent(entity, new RenderBounds { Value = authoring.m_Bounds });
                SetComponent(entity, new LocalToWorld { Value = authoring.transform.localToWorldMatrix });
            }
        }
    }
}