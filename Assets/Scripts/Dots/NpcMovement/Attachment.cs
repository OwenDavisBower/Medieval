using ProjectDawn.Animation;
using Unity.Entities;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Authoring-only: assigns animatron + joint name. The <see cref="AttachmentBaker"/> adds
    /// <see cref="AnimatedJointAttachment"/> so <see cref="AnimatedJointAttachmentSystem"/> can move this entity
    /// each frame (DOTS-spawned followers have no running MonoBehaviour).
    /// </summary>
    public sealed class Attachment : MonoBehaviour
    {
        public AnimatronAuthoring Animatron;
        public string JointName = "";
    }

    public struct AnimatedJointAttachment : IComponentData
    {
        public Entity SourceArmatureEntity;
        public int JointIndex;
    }

    sealed class AttachmentBaker : Baker<Attachment>
    {
        public override void Bake(Attachment authoring)
        {
            if (authoring.Animatron == null || string.IsNullOrWhiteSpace(authoring.JointName))
                return;

            DependsOn(authoring.Animatron);
            if (!authoring.Animatron.TryFindJointIndex(authoring.JointName, out int jointIndex))
            {
                Debug.LogWarning(
                    $"Attachment bake on '{authoring.name}': joint '{authoring.JointName}' not found on rig.",
                    authoring);
                return;
            }

            Entity self = GetEntity(authoring, TransformUsageFlags.Dynamic);
            Entity anim = GetEntity(authoring.Animatron, TransformUsageFlags.Dynamic);
            AddComponent(self, new AnimatedJointAttachment
            {
                SourceArmatureEntity = anim,
                JointIndex = jointIndex
            });
        }
    }
}
