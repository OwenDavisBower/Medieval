#nullable enable
using Medieval.VAT;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Medieval.Dots.VAT
{
    public sealed class VatAnimatorAuthoring : MonoBehaviour
    {
        [Header("VAT Atlas")]
        public VatAtlasAsset? atlas;

        [Header("Playback")]
        [Tooltip("If set, selects the first clip whose name matches. Otherwise uses Default Clip Index.")]
        public string defaultClipName = "";

        [Min(0)]
        public int defaultClipIndex = 0;

        [Min(0f)]
        public float speed = 1f;

        private sealed class Baker : Baker<VatAnimatorAuthoring>
        {
            public override void Bake(VatAnimatorAuthoring authoring)
            {
                if (authoring.atlas == null)
                    return;

                var atlas = authoring.atlas;
                var entity = GetEntity(TransformUsageFlags.Renderable);

                var bounds = atlas.bounds;
                AddComponent(entity, new VatAtlasInfo
                {
                    VertexCount = atlas.vertexCount,
                    Fps = atlas.fps,
                    TotalFrames = atlas.totalFrames,
                    BoundsCenter = (float3)bounds.center,
                    BoundsExtents = (float3)bounds.extents
                });

                AddComponent(entity, new RenderBounds
                {
                    Value = new AABB
                    {
                        Center = (float3)bounds.center,
                        Extents = (float3)bounds.extents
                    }
                });

                var clips = AddBuffer<VatClipElement>(entity);
                if (atlas.clips != null)
                {
                    foreach (var c in atlas.clips)
                    {
                        clips.Add(new VatClipElement
                        {
                            StartFrame = c.startFrame,
                            FrameCount = c.frameCount,
                            LengthSeconds = c.length,
                            Loop = (byte)(c.loop ? 1 : 0)
                        });
                    }
                }

                var clipIndex = ResolveDefaultClipIndex(atlas, authoring.defaultClipName, authoring.defaultClipIndex);
                AddComponent(entity, new VatAnimator
                {
                    ClipIndex = clipIndex,
                    Time = 0f,
                    Speed = authoring.speed
                });
                AddComponent(entity, new VatAnimatorRuntime { LastClipIndex = clipIndex });

                // Initialize material property so Entities Graphics has a value immediately.
                AddComponent(entity, new VatFrameProperty { Value = 0f });
            }

            private static int ResolveDefaultClipIndex(VatAtlasAsset atlas, string clipName, int fallbackIndex)
            {
                if (!string.IsNullOrWhiteSpace(clipName) && atlas.clips != null)
                {
                    for (var i = 0; i < atlas.clips.Length; i++)
                    {
                        if (atlas.clips[i].name == clipName)
                            return i;
                    }
                }

                var max = (atlas.clips?.Length ?? 0) - 1;
                if (max < 0)
                    return 0;
                return math.clamp(fallbackIndex, 0, max);
            }
        }
    }
}

