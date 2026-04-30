using ProjectDawn.Animation;
using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using RigidTransform = ProjectDawn.Animation.RigidTransform;

namespace ProjectDawn.Sample
{
    [RequireComponent(typeof(AnimatronAuthoring))]
    public class VisualizeBlend : MonoBehaviour
    {
        void Start()
        {
            StartCoroutine(PlayAnimations());
            StartCoroutine(DrawHandMotion());
        }

        IEnumerator DrawHandMotion()
        {
            var animatron = GetComponent<AnimatronAuthoring>();
            var armature = animatron.Rig.GetOrCreateArmature();
            var JointIndex = armature.Value.FindJointIndex("RightHand");

            yield return new WaitForSeconds(0.7f);

            var previousPoint = animatron.GetJointWorldTransform(JointIndex).Position;

            float drawDuration = 1.0f;
            float drawFrequency = 0.02f;
            int drawIterations = (int)math.round(drawDuration / drawFrequency);

            for (int i = 0; i < drawIterations; i++)
            {
                var currentPoint = animatron.GetJointWorldTransform(JointIndex).Position;

                Debug.DrawLine(previousPoint, currentPoint, Color.red, 2f);

                previousPoint = currentPoint;

                yield return new WaitForSeconds(drawFrequency);
            }
        }

        IEnumerator PlayAnimations()
        {
            var animatron = GetComponent<AnimatronAuthoring>();

            yield return new WaitForSeconds(0);

            animatron.Play(animatron.FindAnimationIndex("Waving"));

            yield return new WaitForSeconds(1.0f);

            if (TryGetComponent(out InertializerAuthoring inertializer))
                inertializer.Intertialize(animatron.FindAnimationIndex("Idle"));
            if (TryGetComponent(out CrossFaderAuthoring crossFader))
                crossFader.CrossFade(animatron.FindAnimationIndex("Idle"));

            yield return null;
        }
    }
}