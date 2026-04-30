using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectDawn.Rendering
{
    /// <summary>
    /// Represents settings that control when to render a given entity.
    /// </summary>
    /// <remarks>
    /// For example, you can set the layermask of the entity and also set whether to render an entity in shadow maps or motion passes.
    /// </remarks>
    [System.Serializable]
    public struct RenderFilterSettings
    {
        /// <summary>
        /// The rendering layer the entity is part of.
        /// </summary>
        /// <remarks>
        /// This value corresponds to <see cref="Renderer.renderingLayerMask"/>.
        /// </remarks>
        public uint RenderingLayerMask;

        /// <summary>
        /// Specifies what kinds of motion vectors to generate for the entity, if any.
        /// </summary>
        /// <remarks>
        /// This value corresponds to <see cref="Renderer.motionVectorGenerationMode"/>.
        ///
        /// This value only affects render pipelines that use motion vectors.
        /// </remarks>
        public MotionVectorGenerationMode MotionMode;

        /// <summary>
        /// Specifies how the entity should cast shadows.
        /// </summary>
        /// <remarks>
        /// For entities that Unity converts from GameObjects, this value is the same as the Cast Shadows property of the source
        /// Mesh Renderer component.
        /// For more information, refer to [ShadowCastingMode](https://docs.unity3d.com/ScriptReference/Rendering.ShadowCastingMode.html).
        /// </remarks>
        public ShadowCastingMode ShadowCastingMode;

        /// <summary>
        /// Indicates whether to cast shadows onto the entity.
        /// </summary>
        /// <remarks>
        /// For entities that Unity converts from GameObjects, this value is the same as the Receive Shadows property of the source
        /// Mesh Renderer component.
        /// This value only affects [Progressive Lightmappers](https://docs.unity3d.com/Manual/ProgressiveLightmapper.html).
        /// </remarks>
        public bool ReceiveShadows;

        /// <summary>
        /// Indicates whether the entity is a static shadow caster.
        /// </summary>
        /// <remarks>
        /// This value is important to the BatchRenderGroup.
        /// </remarks>
        public bool StaticShadowCaster;

        public Unity.Entities.Graphics.RenderFilterSettings ToFilterSettings(int layer)
        {
            return new()
            {
                Layer = layer,
                MotionMode = MotionMode,
                ReceiveShadows = ReceiveShadows,
                RenderingLayerMask = RenderingLayerMask,
                ShadowCastingMode = ShadowCastingMode,
                StaticShadowCaster = StaticShadowCaster,
            };
        }
    }
}