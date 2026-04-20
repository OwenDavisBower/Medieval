using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FollowCam : MonoBehaviour
{
    [SerializeField] Transform target;
    [Tooltip("World-space offset from target (ignores target yaw/pitch/roll).")]
    [SerializeField] Vector3 offset = new Vector3(0f, 2.5f, -6f);
    [SerializeField] float smooth = 8f;
    [SerializeField] float lookAtHeight = 1.2f;

    [Header("Pixel grid snap")]
    [SerializeField]
    [Tooltip("After follow + look-at, snap along camera right/up to match PixelateVolume grid (reduces UV crawl).")]
    bool pixelGridSnapEnabled = true;

    [SerializeField]
    [Tooltip("Distance in front of the camera at which one logical pixel's world size is computed.")]
    float pixelGridSnapReferenceDistance = 10f;

    [SerializeField]
    [Tooltip("Lateral snap pivot when set; otherwise uses Follow Target.")]
    Transform pixelGridSnapPivotOverride;

    [SerializeField]
    [Tooltip("When true, snap only in Play mode and only for Game cameras.")]
    bool pixelGridSnapOnlyInPlayGameCamera = true;

    Camera _camera;

    void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desired = target.position + offset;
        float t = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desired, t);

        Vector3 look = target.position + Vector3.up * lookAtHeight;
        transform.LookAt(look);

        if (pixelGridSnapEnabled)
            ApplyPixelGridSnap();
    }

    void ApplyPixelGridSnap()
    {
        if (pixelGridSnapOnlyInPlayGameCamera && (!Application.isPlaying || _camera.cameraType != CameraType.Game))
            return;

        if (!PixelateGrid.TryGetActiveVolume(out PixelateVolume volume))
            return;

        PixelateGrid.GetScaledRenderSize(_camera, out int scaledW, out int scaledH);
        PixelateGrid.GetLogicalPixelCounts(volume, scaledW, scaledH, out int logicalW, out int logicalH);

        float depth = Mathf.Max(0.01f, pixelGridSnapReferenceDistance);
        float frustumHeight = 2f * depth * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspect = scaledW / (float)scaledH;
        float frustumWidth = frustumHeight * aspect;

        float cellH = frustumHeight / logicalH;
        float cellW = frustumWidth / logicalW;

        Vector3 right = transform.right;
        Vector3 up = transform.up;
        Vector3 forward = transform.forward;

        Vector3 pivot = pixelGridSnapPivotOverride != null ? pixelGridSnapPivotOverride.position : target.position;

        Vector3 pos = transform.position;
        Vector3 delta = pos - pivot;

        float r = Vector3.Dot(delta, right);
        float u = Vector3.Dot(delta, up);
        float f = Vector3.Dot(delta, forward);

        r = Mathf.Round(r / cellW) * cellW;
        u = Mathf.Round(u / cellH) * cellH;

        transform.position = pivot + r * right + u * up + f * forward;
    }
}
