using ProjectDawn.Animation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace ProjectDawn.Sample
{
    public struct SwingEvent : IEventData
    {
        public float3 Offset;
    }

    public class Swing : MonoBehaviour
    {
        public AnimatronAuthoring Animatron;

        void Start()
        {
            Animatron.Play(Animatron.FindAnimationIndex("Attack"));
        }

        void Update()
        {
            if (!Animatron.TryGetEvent(out SwingEvent e))
                return;

            var visaulEffect = GetComponent<VisualEffect>();
            visaulEffect.SetVector3("Offset", e.Offset);
            visaulEffect.Play();
        }
    }
}