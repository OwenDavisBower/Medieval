using UnityEngine;

public class FollowCam : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Vector3 offset = new Vector3(0f, 2.5f, -6f);
    [SerializeField] float smooth = 8f;
    [SerializeField] float lookAtHeight = 1.2f;

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desired = target.position + target.TransformDirection(offset);
        float t = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desired, t);

        Vector3 look = target.position + Vector3.up * lookAtHeight;
        transform.LookAt(look);
    }
}
