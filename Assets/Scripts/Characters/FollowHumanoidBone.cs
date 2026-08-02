using UnityEngine;

/// <summary>
/// Keeps this transform aligned to a humanoid bone each frame (Mecanim player weapons).
/// DOTS soldiers use <c>Attachment</c> / Animatron joints instead.
/// </summary>
[DisallowMultipleComponent]
public sealed class FollowHumanoidBone : MonoBehaviour
{
    [SerializeField] HumanBodyBones bone = HumanBodyBones.LeftHand;
    [Tooltip("Leave empty to use the Animator under this object's parents/children.")]
    [SerializeField] Animator animator;
    [Tooltip("Offset in the bone's local space (same units as the bone hierarchy).")]
    [SerializeField] Vector3 localPositionOffset;
    [SerializeField] Vector3 localEulerOffset = new Vector3(-20f, 0f, 107.28f);

    void Awake()
    {
        if (animator != null)
            return;

        animator = GetComponentInParent<Animator>();
        if (animator == null)
            animator = transform.root.GetComponentInChildren<Animator>();
    }

    void LateUpdate()
    {
        if (animator == null)
            return;

        Transform hand = animator.GetBoneTransform(bone);
        if (hand == null)
            return;

        transform.SetPositionAndRotation(
            hand.TransformPoint(localPositionOffset),
            hand.rotation * Quaternion.Euler(localEulerOffset));
    }
}
