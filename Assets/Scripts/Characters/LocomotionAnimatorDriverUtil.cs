using UnityEngine;

/// <summary>Shared locomotion animator driving logic (player + NPC).</summary>
public static class LocomotionAnimatorDriverUtil
{
    public static readonly int LocomotionParamId = Animator.StringToHash("Locomotion");

    public static void Apply(
        Animator animator,
        Vector3 linearVelocity,
        float maxSpeed,
        float stopSpeedThreshold,
        float animationSpeedScale)
    {
        if (animator == null)
            return;

        Vector3 v = linearVelocity;
        float horizontalSpeed = new Vector3(v.x, 0f, v.z).magnitude;

        animator.SetFloat(LocomotionParamId, horizontalSpeed);

        if (horizontalSpeed < stopSpeedThreshold || maxSpeed < 0.01f)
        {
            animator.speed = 1f;
            return;
        }

        float normalized = Mathf.Clamp01(horizontalSpeed / maxSpeed);
        animator.speed = normalized * animationSpeedScale;
    }
}

