using ProjectDawn.Animation;
using UnityEngine;

namespace ProjectDawn.Sample
{
    public class AnimatorPlayAnimation : MonoBehaviour
    {
        public string Name = "Run";
        void Start()
        {
            var animatron = GetComponent<AnimatronAuthoring>();
            animatron.Play(animatron.FindAnimationIndex(Name));
        }
    }
}