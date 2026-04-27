using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FollowCam : MonoBehaviour
{
    [SerializeField] Transform target;
    [Tooltip("World-space offset from target (ignores target yaw/pitch/roll).")]
    [SerializeField] Vector3 offset = new Vector3(0f, 2.5f, -6f);
    [SerializeField] float smooth = 8f;
    [SerializeField] float lookAtHeight = 1.2f;

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desired = target.position + offset;
        float t = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        Vector3 pos = transform.position;
        pos.x = desired.x;
        pos.y = Mathf.Lerp(pos.y, desired.y, t);
        pos.z = Mathf.Lerp(pos.z, desired.z, t);
        transform.position = pos;

        Vector3 look = target.position + Vector3.up * lookAtHeight;
        transform.LookAt(look);
    }
}
