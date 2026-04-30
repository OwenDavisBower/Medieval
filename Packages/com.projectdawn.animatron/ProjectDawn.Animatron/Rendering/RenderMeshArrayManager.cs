using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectDawn.Rendering
{
#if UNITY_EDITOR
    /// <summary>
    /// This is special class needed to render mesh render arrays in edit mode where ecs world dont exist.
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    internal static class RenderMeshArrayManager
    {
        //static SkinMatrixBuffer m_SkinMatrixBuffer;
        static List<RenderMeshArrayAuthoring> m_RenderMeshArray;

        static RenderMeshArrayManager()
        {
            m_RenderMeshArray = new List<RenderMeshArrayAuthoring>();
            RenderPipelineManager.beginContextRendering += Render;
        }

        public static void Add(RenderMeshArrayAuthoring instance)
        {
            m_RenderMeshArray.Add(instance);
        }

        public static void Remove(RenderMeshArrayAuthoring instance)
        {
            m_RenderMeshArray.Remove(instance);
        }

        public static void Render(ScriptableRenderContext context, List<Camera> camera)
        {
            if (Application.isPlaying)
                return;
            foreach (var renderMeshArray in m_RenderMeshArray)
            {
                foreach (var instance in renderMeshArray.Instances)
                {
                    if (instance.Mesh == null)
                        continue;

                    if (instance.Material != null)
                    {
                        var renderParams = new RenderParams
                        {
                            material = instance.Material,
                            layer = renderMeshArray.gameObject.layer,
                            lightProbeUsage = renderMeshArray.LightProbeUsage,
                            motionVectorMode = renderMeshArray.Filter.MotionMode,
                            receiveShadows = renderMeshArray.Filter.ReceiveShadows,
                            renderingLayerMask = renderMeshArray.Filter.RenderingLayerMask,
                            shadowCastingMode = renderMeshArray.Filter.ShadowCastingMode,
                        };
                        Graphics.RenderMesh(renderParams, instance.Mesh, instance.SubMesh, renderMeshArray.transform.localToWorldMatrix);
                    }
                    else
                    {
                        // Draw mesh automatically handle null material with error shader
                        Graphics.DrawMesh(instance.Mesh, renderMeshArray.transform.localToWorldMatrix, null, renderMeshArray.gameObject.layer, null, instance.SubMesh);
                    }
                }
            }
        }
    }
#endif
}