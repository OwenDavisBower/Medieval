using UnityEngine;
using Unity.Entities;
using Unity.Burst;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ProjectDawn.Animation
{
    public struct Culled : IEnableableComponent, IComponentData { }

    public enum CullingMode
    {
        AlwaysAnimate = 0,
        CullCompletely = 2,
    }

    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(AnimatronSystemGroup))]
    public partial class CullingSystem : SystemBase
    {
        Camera[] m_Cameras;
        Plane[] m_Planes;

        [BurstCompile]
        [WithPresent(typeof(Culled))]
        [WithPresent(typeof(MaterialMeshInfo))]
        [WithNone(typeof(Child))]
        unsafe partial struct CullBoundsJob : IJobEntity
        {
            [ReadOnly]
            public NativeArray<Plane> FrustumPlanes;
            [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<Culled> AnimateLookup;
            [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<MaterialMeshInfo> MaterialMeshInfoLookup;

            public void Execute(Entity entity, WorldRenderBounds bounds)
            {
                var itr = (Plane*)FrustumPlanes.GetUnsafeReadOnlyPtr();
                var end = itr + FrustumPlanes.Length;

                while (itr != end)
                {
                    if (TestFrustrumAndBounds(itr, bounds.Value))
                    {
                        AnimateLookup.SetComponentEnabled(entity, false);
                        MaterialMeshInfoLookup.SetComponentEnabled(entity, true);
                        return;
                    }
                    itr += 6;
                }
                AnimateLookup.SetComponentEnabled(entity, true);
                MaterialMeshInfoLookup.SetComponentEnabled(entity, false);
            }
        }

        [BurstCompile]
        [WithPresent(typeof(Culled))]
        [WithNone(typeof(WorldRenderBounds))]
        unsafe partial struct CullHierachyJob : IJobEntity
        {
            [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<Culled> AnimateLookup;

            public void Execute(Entity entity, in DynamicBuffer<Child> children)
            {
                foreach (var child in children)
                {
                    if (!AnimateLookup.HasComponent(child.Value))
                        continue;

                    if (!AnimateLookup.IsComponentEnabled(child.Value))
                    {
                        AnimateLookup.SetComponentEnabled(entity, false);
                        return;
                    }

                }

                AnimateLookup.SetComponentEnabled(entity, true);
            }
        }

        protected override void OnCreate()
        {
            m_Planes= new Plane[6];
        }

        protected override void OnUpdate()
        {
            if (m_Cameras == null || m_Cameras.Length != Camera.allCamerasCount)
                m_Cameras = new Camera[Camera.allCamerasCount];
            Camera.GetAllCameras(m_Cameras);

            // Merge all frustum planes into one NativeArray
            var planes = new NativeList<Plane>(Allocator.TempJob);
            for (int cameraIndex = 0; cameraIndex < Camera.allCamerasCount; cameraIndex++)
            {
                GeometryUtility.CalculateFrustumPlanes(m_Cameras[cameraIndex], m_Planes);
                for (int planeIndex = 0; planeIndex < 6; planeIndex++)
                    planes.Add(m_Planes[planeIndex]);
            }

            Dependency = new CullBoundsJob
            {
                AnimateLookup = SystemAPI.GetComponentLookup<Culled>(),
                MaterialMeshInfoLookup = SystemAPI.GetComponentLookup<MaterialMeshInfo>(),
                FrustumPlanes = planes.AsArray()
            }.ScheduleParallel(Dependency);

            Dependency = new CullHierachyJob
            {
                AnimateLookup = SystemAPI.GetComponentLookup<Culled>(),
            }.ScheduleParallel(Dependency);

            Dependency = planes.Dispose(Dependency);
        }

        unsafe static bool TestFrustrumAndBounds(Plane* planes, AABB aabb)
        {
            float3 center = aabb.Center;
            float3 extents = aabb.Extents;

            for (int i = 0; i < 6; i++)
            {
                Plane plane = planes[i];
                float3 normal = plane.normal;
                float distance = plane.distance;

                float r = math.dot(math.abs(normal), extents);
                float s = math.dot(normal, center) + distance;

                if (s + r < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
