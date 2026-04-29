using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FollowCam : MonoBehaviour
{
    [SerializeField] Transform target;
    [Tooltip("World-space offset from target (ignores target yaw/pitch/roll).")]
    [SerializeField] Vector3 offset = new Vector3(0f, 2.5f, -6f);
    [SerializeField] float lookAtHeight = 1.2f;

    void LateUpdate()
    {
        if (target == null)
            return;

        transform.position = target.position + offset;

        Vector3 look = target.position + Vector3.up * lookAtHeight;
        transform.LookAt(look);
    }
}
