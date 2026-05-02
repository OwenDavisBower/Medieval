using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class FollowCam : MonoBehaviour
{
    const float MinYOffset = 0.5f;
    const float MaxYOffset = 70f;

    [SerializeField] Transform target;
    [Tooltip("World-space offset from target (ignores target yaw/pitch/roll).")]
    [SerializeField] Vector3 offset = new Vector3(0f, 2.5f, -6f);
    [SerializeField] float lookAtHeight = 1.2f;

    [Header("Height control")]
    [Tooltip("Screen-pixel pinch span change → world Y offset (tune per project).")]
    [SerializeField] float pinchSensitivity = 0.025f;
    [Tooltip("Mouse scroll units → world Y offset.")]
    [SerializeField] float scrollSensitivity = 0.35f;

    float _lastPinchSpanPixels = -1f;

    void Awake() => ClampYOffset();

    void OnValidate() => ClampYOffset();

    void Update()
    {
        ApplyScrollHeight();
        ApplyPinchHeight();
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        transform.position = target.position + offset;

        Vector3 look = target.position + Vector3.up * lookAtHeight;
        transform.LookAt(look);
    }

    void ApplyScrollHeight()
    {
        var mouse = Mouse.current;
        if (mouse == null)
            return;

        float scrollY = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scrollY) < 0.0001f)
            return;

        offset.y += scrollY * scrollSensitivity;
        ClampYOffset();
    }

    void ApplyPinchHeight()
    {
        var screen = Touchscreen.current;
        if (screen == null)
        {
            _lastPinchSpanPixels = -1f;
            return;
        }

        if (!TryGetTwoPinchPositions(screen, out Vector2 a, out Vector2 b))
        {
            _lastPinchSpanPixels = -1f;
            return;
        }

        float span = Vector2.Distance(a, b);
        if (_lastPinchSpanPixels < 0f)
        {
            _lastPinchSpanPixels = span;
            return;
        }

        float deltaSpan = span - _lastPinchSpanPixels;
        _lastPinchSpanPixels = span;

        offset.y += deltaSpan * pinchSensitivity;
        ClampYOffset();
    }

    static bool TryGetTwoPinchPositions(Touchscreen screen, out Vector2 a, out Vector2 b)
    {
        a = default;
        b = default;
        var touches = screen.touches;
        int count = touches.Count;
        int found = 0;
        for (int i = 0; i < count; i++)
        {
            var t = touches[i];
            if (!t.press.isPressed)
                continue;
            Vector2 pos = t.position.ReadValue();
            if (found == 0)
            {
                a = pos;
                found = 1;
            }
            else
            {
                b = pos;
                return true;
            }
        }

        return false;
    }

    void ClampYOffset()
    {
        offset.y = Mathf.Clamp(offset.y, MinYOffset, MaxYOffset);
    }
}
