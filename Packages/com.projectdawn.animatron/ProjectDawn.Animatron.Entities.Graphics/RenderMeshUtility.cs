using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using static Unity.Rendering.RenderMeshUtility;

namespace ProjectDawn.Rendering
{
    public class RenderMeshUtility : MonoBehaviour
    {
        public static ComponentTypeSet GetComponentSet(RenderMeshDescription renderMeshDescription, List<Material> materials, bool isStatic)
        {
            // Add all components up front using as few calls as possible.
            var componentFlags = EntitiesGraphicsComponentFlags.UseRenderMeshArray;
            componentFlags.AppendMotionAndProbeFlags(renderMeshDescription, isStatic);
            componentFlags.AppendDepthSortedFlag(materials);
            return ComputeComponentTypes(componentFlags);
        }
    }
}