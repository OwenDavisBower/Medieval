using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace ProjectDawn.Animation.Editor
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct RenderBoundsSystem : ISystem
    {
        void ISystem.OnUpdate(ref Unity.Entities.SystemState state)
        {
            //foreach (var renderBounds in SystemAPI.Query<WorldRenderBounds>())
            //{
            //    var min = renderBounds.Value.Min;
            //    var max = renderBounds.Value.Max;

            //    // 8 corners of the cube
            //    float3 p000 = new float3(min.x, min.y, min.z);
            //    float3 p001 = new float3(min.x, min.y, max.z);
            //    float3 p010 = new float3(min.x, max.y, min.z);
            //    float3 p011 = new float3(min.x, max.y, max.z);
            //    float3 p100 = new float3(max.x, min.y, min.z);
            //    float3 p101 = new float3(max.x, min.y, max.z);
            //    float3 p110 = new float3(max.x, max.y, min.z);
            //    float3 p111 = new float3(max.x, max.y, max.z);

            //    // Bottom square
            //    Debug.DrawLine(p000, p001, Color.gray);
            //    Debug.DrawLine(p001, p101, Color.gray);
            //    Debug.DrawLine(p101, p100, Color.gray);
            //    Debug.DrawLine(p100, p000, Color.gray);

            //    // Top square
            //    Debug.DrawLine(p010, p011, Color.gray);
            //    Debug.DrawLine(p011, p111, Color.gray);
            //    Debug.DrawLine(p111, p110, Color.gray);
            //    Debug.DrawLine(p110, p010, Color.gray);

            //    // Vertical lines
            //    Debug.DrawLine(p000, p010, Color.gray);
            //    Debug.DrawLine(p001, p011, Color.gray);
            //    Debug.DrawLine(p101, p111, Color.gray);
            //    Debug.DrawLine(p100, p110, Color.gray);
            //}

            foreach (var (localToWorld, renderBounds) in SystemAPI.Query<LocalToWorld, RenderBounds>())
            {
                var min = renderBounds.Value.Min;
                var max = renderBounds.Value.Max;

                // 8 corners of the cube
                float3 p000 = new float3(min.x, min.y, min.z);
                float3 p001 = new float3(min.x, min.y, max.z);
                float3 p010 = new float3(min.x, max.y, min.z);
                float3 p011 = new float3(min.x, max.y, max.z);
                float3 p100 = new float3(max.x, min.y, min.z);
                float3 p101 = new float3(max.x, min.y, max.z);
                float3 p110 = new float3(max.x, max.y, min.z);
                float3 p111 = new float3(max.x, max.y, max.z);

                p000 = localToWorld.Value.TransformPoint(p000);
                p001 = localToWorld.Value.TransformPoint(p001);
                p010 = localToWorld.Value.TransformPoint(p010);
                p011 = localToWorld.Value.TransformPoint(p011);
                p100 = localToWorld.Value.TransformPoint(p100);
                p101 = localToWorld.Value.TransformPoint(p101);
                p110 = localToWorld.Value.TransformPoint(p110);
                p111 = localToWorld.Value.TransformPoint(p111);

                // Bottom square
                Debug.DrawLine(p000, p001, Color.gray);
                Debug.DrawLine(p001, p101, Color.gray);
                Debug.DrawLine(p101, p100, Color.gray);
                Debug.DrawLine(p100, p000, Color.gray);

                // Top square
                Debug.DrawLine(p010, p011, Color.gray);
                Debug.DrawLine(p011, p111, Color.gray);
                Debug.DrawLine(p111, p110, Color.gray);
                Debug.DrawLine(p110, p010, Color.gray);

                // Vertical lines
                Debug.DrawLine(p000, p010, Color.gray);
                Debug.DrawLine(p001, p011, Color.gray);
                Debug.DrawLine(p101, p111, Color.gray);
                Debug.DrawLine(p100, p110, Color.gray);
            }
        }
    }
}