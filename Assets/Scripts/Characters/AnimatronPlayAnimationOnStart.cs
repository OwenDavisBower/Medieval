using ProjectDawn.Animation;
using UnityEngine;

public sealed class AnimatronPlayAnimationOnStart : MonoBehaviour
{
    [SerializeField] string animationName = "Run";

    void Start()
    {
        if (!TryGetComponent(out AnimatronAuthoring animatron) || animatron == null)
            return;

        if (string.IsNullOrWhiteSpace(animationName))
            return;

        if (!animatron.TryFindAnimationIndex(animationName, out var index))
            return;

        animatron.Play(index);
    }
}

