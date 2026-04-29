using UnityEngine;

/// <summary>Animator lookup helpers shared across character/combat systems.</summary>
public static class AnimatorUtil
{
    /// <summary>
    /// Prefer an <see cref="Animator"/> on an active hierarchy branch so inactive LOD/alternate rigs do not steal triggers.
    /// </summary>
    public static Animator ResolvePreferredAnimator(Component root)
    {
        if (root == null)
            return null;

        var animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            var a = animators[i];
            if (a != null && a.gameObject.activeInHierarchy)
                return a;
        }

        return animators.Length > 0 ? animators[0] : null;
    }
}

