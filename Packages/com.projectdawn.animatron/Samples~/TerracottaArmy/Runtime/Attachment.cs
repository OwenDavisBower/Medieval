using ProjectDawn.Animation;
using UnityEngine;

namespace ProjectDawn.Sample
{
    public class Attachment : MonoBehaviour
    {
        public AnimatronAuthoring Animatron;
        public string JointName;
        int JointIndex;

        void Start()
        {
            var armature = Animatron.Rig.GetOrCreateArmature();
            JointIndex = armature.Value.FindJointIndex(JointName);
        }

        void LateUpdate()
        {
            var jointPoseWS = Animatron.GetJointWorldTransform(JointIndex);
            transform.position = jointPoseWS.Position;
            transform.rotation = jointPoseWS.Rotation;
        }
    }
}