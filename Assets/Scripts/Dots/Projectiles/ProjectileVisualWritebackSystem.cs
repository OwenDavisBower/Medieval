using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Projectiles
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ProjectileVisualWritebackSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (tf, companion) in SystemAPI.Query<RefRO<LocalTransform>, ProjectileVisualCompanion>()
                         .WithAll<ProjectileTag>())
            {
                if (companion.Visual == null)
                    continue;

                LocalTransform lt = tf.ValueRO;
                float3 p = lt.Position;
                quaternion r = lt.Rotation;
                float3 s = lt.Scale;
                Transform tr = companion.Visual;
                tr.SetPositionAndRotation(new Vector3(p.x, p.y, p.z),
                    new Quaternion(r.value.x, r.value.y, r.value.z, r.value.w));
                tr.localScale = new Vector3(s.x, s.y, s.z);
            }
        }
    }
}
